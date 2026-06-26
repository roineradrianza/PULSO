using Npgsql;
using Pulso.IngressApi.Common;
using Pulso.IngressApi.Models;
using StackExchange.Redis;

namespace Pulso.IngressApi.Endpoints;

// Endpoints del API público de PULSO: ingesta directa y consultas de situación.
public static class PulsoApiEndpoints
{
    public static void MapPulsoApiEndpoints(this WebApplication app)
    {
        // Webhook de ingesta masiva rápida (canal PWA y clientes directos).
        app.MapPost("/api/v1/pulso/ingest", async (PulsoPayload payload, IConnectionMultiplexer redisConn) =>
        {
            if (string.IsNullOrEmpty(payload.MessageId) || string.IsNullOrEmpty(payload.TextBody))
            {
                return Results.BadRequest(new { error = "Propiedades message_id y text_body obligatorias." });
            }

            // Mitigación: límite de tamaño del texto para evitar abuso de memoria/costo de LLM.
            if (payload.TextBody.Length > 10000)
            {
                return Results.BadRequest(new { error = "El cuerpo del mensaje excede el límite de 10,000 caracteres." });
            }

            // Mitigación: descartar geolocalizaciones fuera de Venezuela.
            if (payload.Latitude.HasValue && payload.Longitude.HasValue &&
                WebhookSupport.IsOutsideVenezuela(payload.Latitude.Value, payload.Longitude.Value))
            {
                return Results.BadRequest(new { error = "Las coordenadas geográficas se encuentran fuera del territorio nacional (Venezuela)." });
            }

            var db = redisConn.GetDatabase();

            // Capa B: límite por remitente (si el payload trae teléfono/contacto).
            if (await WebhookSupport.IsSenderRateLimitedAsync(db, payload.Channel, payload.Phone))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            await WebhookSupport.EnqueueAsync(db, payload);

            // Retorno inmediato 200 OK para liberar el webhook del canal.
            return Results.Ok(new { status = "Queued", messageId = payload.MessageId });
        }).RequireRateLimiting("ingest");

        // Situaciones georreferenciadas (payload LIVIANO, sin raw_text).
        // ?since=<ISO8601> -> carga incremental (delta); ?limit=N -> tope de filas; ?date=YYYY-MM-DD -> filtrar por día.
        app.MapGet("/api/v1/pulso/situations", async (HttpRequest request, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
            var list = new List<SituationItem>();

            DateTimeOffset? since = DateTimeOffset.TryParse(request.Query["since"].ToString(), out var s) ? s : null;
            int limit = int.TryParse(request.Query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 2000) : 500;
            var dateStr = request.Query["date"].ToString();
            var (utcStart, utcEnd) = GetUtcDateRange(dateStr);

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT
                        id,
                        ai_category,
                        severity::text,
                        COALESCE(sector, '') as sector,
                        ST_Y(coordinates::geometry) as latitude,
                        ST_X(coordinates::geometry) as longitude,
                        found_person_name,
                        COALESCE(is_hardware_gps, false) as is_hardware_gps,
                        (COALESCE(triage_provider, 'gemini') <> 'gemini') as needs_review,
                        COALESCE(found_person_verified, false) as found_person_verified,
                        created_at
                    FROM public.incidents
                    WHERE status != 'DUPLICATE'
                      AND created_at >= @utcStart AND created_at <= @utcEnd"
                    + (since.HasValue ? " AND created_at > @since" : "")
                    + @"
                    ORDER BY created_at DESC
                    LIMIT @limit";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("utcStart", utcStart);
                cmd.Parameters.AddWithValue("utcEnd", utcEnd);
                if (since.HasValue) cmd.Parameters.AddWithValue("since", since.Value.UtcDateTime);
                cmd.Parameters.AddWithValue("limit", limit);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0).ToString();
                    var category = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var severity = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var sector = reader.GetString(3);
                    double? lat = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                    double? lng = reader.IsDBNull(5) ? null : reader.GetDouble(5);
                    var personName = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var isHardwareGps = !reader.IsDBNull(7) && reader.GetBoolean(7);
                    var needsReview = !reader.IsDBNull(8) && reader.GetBoolean(8);
                    var foundPersonVerified = !reader.IsDBNull(9) && reader.GetBoolean(9);
                    var createdAt = reader.GetDateTime(10);

                    list.Add(new SituationItem(
                        id, category, severity, sector, lat, lng,
                        !string.IsNullOrEmpty(personName), personName,
                        isHardwareGps, needsReview, foundPersonVerified, createdAt));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching situations.");
                return Results.Problem("An error occurred while processing your request.");
            }

            return Results.Ok(list);
        }).RequireRateLimiting("reads");

        // Detalle pesado de un incidente (raw_text), servido bajo demanda al abrir el popup.
        app.MapGet("/api/v1/pulso/situations/{id}", async (string id, IConfiguration config) =>
        {
            if (!Guid.TryParse(id, out var guid))
                return Results.BadRequest(new { error = "id inválido." });

            var connStr = config.GetConnectionString("DefaultConnection");
            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(
                    "SELECT raw_text FROM public.incidents WHERE id = @id AND status != 'DUPLICATE'", conn);
                cmd.Parameters.AddWithValue("id", guid);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                var rawText = reader.IsDBNull(0) ? "" : reader.GetString(0);
                return Results.Ok(new SituationDetail(id, rawText));
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching situation detail.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Totales agregados para las tarjetas del dashboard (independientes del subconjunto cargado).
        app.MapGet("/api/v1/pulso/summary", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT
                        COUNT(*) FILTER (WHERE status != 'DUPLICATE') AS total,
                        COUNT(*) FILTER (WHERE status != 'DUPLICATE' AND found_person_name IS NOT NULL) AS people,
                        (SELECT COUNT(DISTINCT sector) FROM public.incidents
                         WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != '' AND severity = 'CRITICAL') AS critical_sectors
                    FROM public.incidents";

                await using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.Ok(new SituationSummary(0, 0, 0));

                return Results.Ok(new SituationSummary(
                    (int)reader.GetInt64(0),
                    (int)reader.GetInt64(1),
                    (int)reader.GetInt64(2)));
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching summary.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Obtener agregación de estatus consolidado por sector.
        app.MapGet("/api/v1/pulso/locations/stats", async (HttpRequest request, IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
            var dateStr = request.Query["date"].ToString();
            var (utcStart, utcEnd) = GetUtcDateRange(dateStr);
            var list = new List<LocationStat>();

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT
                        COALESCE(sector, 'Desconocido') as sector_name,
                        COUNT(*) as incident_count,
                        CASE
                            WHEN bool_or(severity = 'CRITICAL') THEN 'CRITICAL'
                            WHEN bool_or(severity = 'HIGH') THEN 'HIGH'
                            WHEN bool_or(severity = 'MEDIUM') THEN 'MEDIUM'
                            ELSE 'LOW'
                        END as sector_status,
                        string_agg(found_person_name, ',') filter (where found_person_name is not null) as people_names,
                        AVG(ST_Y(coordinates::geometry)) as latitude,
                        AVG(ST_X(coordinates::geometry)) as longitude
                    FROM public.incidents
                    WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != ''
                      AND created_at >= @utcStart AND created_at <= @utcEnd
                    GROUP BY sector_name";

                await using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("utcStart", utcStart);
                cmd.Parameters.AddWithValue("utcEnd", utcEnd);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var sector = reader.GetString(0);
                    var count = (int)reader.GetInt64(1);
                    var status = reader.GetString(2);
                    var namesRaw = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    double? lat = reader.IsDBNull(4) ? null : reader.GetDouble(4);
                    double? lng = reader.IsDBNull(5) ? null : reader.GetDouble(5);

                    var peopleList = string.IsNullOrEmpty(namesRaw)
                        ? new List<string>()
                        : namesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

                    list.Add(new LocationStat(sector, status, count, peopleList, lat, lng));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching location statistics.");
                return Results.Problem("An error occurred while processing your request.");
            }

            return Results.Ok(list);
        }).RequireRateLimiting("reads");

        // Obtener métricas y analíticas del sistema
        app.MapGet("/api/v1/pulso/metrics", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
            
            var engineStats = new Dictionary<string, int>();
            var channelStats = new Dictionary<string, int>();
            var hourlyDistribution = new List<MetricsHourItem>();
            var peakHours = new List<MetricsHourItem>();

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                // 1. Distribución por Motor
                var engineQuery = @"
                    SELECT COALESCE(triage_provider, 'gemini') as provider, COUNT(*)::integer as count 
                    FROM public.incidents 
                    WHERE status != 'DUPLICATE'
                    GROUP BY provider";
                await using (var cmd = new NpgsqlCommand(engineQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        engineStats[reader.GetString(0)] = reader.GetInt32(1);
                    }
                }

                // 2. Distribución por Canal
                var channelQuery = @"
                    SELECT COALESCE(source_channel, 'unknown') as channel, COUNT(*)::integer as count 
                    FROM public.incidents 
                    WHERE status != 'DUPLICATE'
                    GROUP BY channel";
                await using (var cmd = new NpgsqlCommand(channelQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        channelStats[reader.GetString(0)] = reader.GetInt32(1);
                    }
                }

                // 3. Distribución por Hora (0-23)
                var hourlyQuery = @"
                    SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as hr, COUNT(*)::integer as count 
                    FROM public.incidents 
                    WHERE status != 'DUPLICATE'
                    GROUP BY hr 
                    ORDER BY hr ASC";
                await using (var cmd = new NpgsqlCommand(hourlyQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var hourMap = new Dictionary<int, int>();
                    while (await reader.ReadAsync())
                    {
                        hourMap[reader.GetInt32(0)] = reader.GetInt32(1);
                    }
                    for (int h = 0; h < 24; h++)
                    {
                        hourlyDistribution.Add(new MetricsHourItem(h, hourMap.TryGetValue(h, out var c) ? c : 0));
                    }
                }

                // 4. Horas Pico (Top 3)
                var peakQuery = @"
                    SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as hr, COUNT(*)::integer as count 
                    FROM public.incidents 
                    WHERE status != 'DUPLICATE'
                    GROUP BY hr 
                    ORDER BY count DESC 
                    LIMIT 3";
                await using (var cmd = new NpgsqlCommand(peakQuery, conn))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        peakHours.Add(new MetricsHourItem(reader.GetInt32(0), reader.GetInt32(1)));
                    }
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching system metrics.");
                return Results.Problem("An error occurred while processing your request.");
            }

            return Results.Ok(new MetricsResponse(engineStats, channelStats, hourlyDistribution, peakHours));
        }).RequireRateLimiting("reads");
    }

    private static (DateTime utcStart, DateTime utcEnd) GetUtcDateRange(string? dateStr)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Caracas");
        var nowInVet = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        
        DateTime targetDate;
        if (!DateTime.TryParse(dateStr, out targetDate))
        {
            targetDate = nowInVet.Date;
        }
        
        var localStart = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 23, 59, 59, DateTimeKind.Unspecified);
        
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
    }
}

