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

            await WebhookSupport.EnqueueAsync(redisConn.GetDatabase(), payload);

            // Retorno inmediato 200 OK para liberar el webhook del canal.
            return Results.Ok(new { status = "Queued", messageId = payload.MessageId });
        });

        // Obtener situaciones activas georreferenciadas.
        app.MapGet("/api/v1/pulso/situations", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
            var list = new List<SituationItem>();

            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var query = @"
                    SELECT
                        id,
                        ai_category,
                        severity::text,
                        raw_text,
                        COALESCE(sector, '') as sector,
                        ST_Y(coordinates::geometry) as latitude,
                        ST_X(coordinates::geometry) as longitude,
                        found_person_name,
                        created_at
                    FROM public.incidents
                    WHERE status != 'DUPLICATE'
                    ORDER BY created_at DESC";

                await using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0).ToString();
                    var category = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var severity = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var rawText = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var sector = reader.GetString(4);
                    double? lat = reader.IsDBNull(5) ? null : reader.GetDouble(5);
                    double? lng = reader.IsDBNull(6) ? null : reader.GetDouble(6);
                    var personName = reader.IsDBNull(7) ? null : reader.GetString(7);
                    var createdAt = reader.GetDateTime(8);

                    list.Add(new SituationItem(
                        id,
                        category,
                        severity,
                        rawText,
                        sector,
                        lat,
                        lng,
                        !string.IsNullOrEmpty(personName),
                        personName,
                        createdAt
                    ));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching situations.");
                return Results.Problem("An error occurred while processing your request.");
            }

            return Results.Ok(list);
        });

        // Obtener agregación de estatus consolidado por sector.
        app.MapGet("/api/v1/pulso/locations/stats", async (IConfiguration config) =>
        {
            var connStr = config.GetConnectionString("DefaultConnection");
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
                        string_agg(found_person_name, ',') filter (where found_person_name is not null) as people_names
                    FROM public.incidents
                    WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != ''
                    GROUP BY sector_name";

                await using var cmd = new NpgsqlCommand(query, conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var sector = reader.GetString(0);
                    var count = (int)reader.GetInt64(1);
                    var status = reader.GetString(2);
                    var namesRaw = reader.IsDBNull(3) ? "" : reader.GetString(3);

                    var peopleList = string.IsNullOrEmpty(namesRaw)
                        ? new List<string>()
                        : namesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

                    list.Add(new LocationStat(sector, status, count, peopleList));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching location statistics.");
                return Results.Problem("An error occurred while processing your request.");
            }

            return Results.Ok(list);
        });
    }
}
