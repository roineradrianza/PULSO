using Pulso.IngressApi.Common;
using Pulso.IngressApi.Models;
using Pulso.IngressApi.Services;
using Pulso.Shared;
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
                GeoConstants.IsOutsideVenezuela(payload.Latitude.Value, payload.Longitude.Value))
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
        app.MapGet("/api/v1/pulso/situations", async (HttpRequest request, ISituationRepository repo) =>
        {
            DateTimeOffset? since = DateTimeOffset.TryParse(request.Query["since"].ToString(), out var s) ? s : null;
            int limit = int.TryParse(request.Query["limit"].ToString(), out var l) ? Math.Clamp(l, 1, 2000) : 500;
            var dateStr = request.Query["date"].ToString();

            try
            {
                var list = await repo.GetSituationsAsync(since, limit, dateStr);
                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching situations.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Detalle pesado de un incidente (raw_text), servido bajo demanda al abrir el popup.
        app.MapGet("/api/v1/pulso/situations/{id}", async (string id, ISituationRepository repo) =>
        {
            if (!Guid.TryParse(id, out var guid))
                return Results.BadRequest(new { error = "id inválido." });

            try
            {
                var detail = await repo.GetSituationDetailAsync(guid);
                if (detail == null)
                    return Results.NotFound();

                return Results.Ok(detail);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching situation detail.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Obtener comentarios de un incidente (anónimos por diseño)
        app.MapGet("/api/v1/pulso/situations/{id}/comments", async (string id, ISituationRepository repo) =>
        {
            if (!Guid.TryParse(id, out var guid))
                return Results.BadRequest(new { error = "id inválido." });

            try
            {
                var list = await repo.GetCommentsAsync(guid);
                if (list == null)
                    return Results.NotFound(new { error = "Incidente no encontrado." });

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching comments.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Agregar un comentario a un incidente (anónimo, máximo 300 caracteres)
        app.MapPost("/api/v1/pulso/situations/{id}/comments", async (string id, CreateCommentPayload payload, ISituationRepository repo) =>
        {
            if (!Guid.TryParse(id, out var guid))
                return Results.BadRequest(new { error = "id inválido." });

            if (payload == null || string.IsNullOrWhiteSpace(payload.RawText))
                return Results.BadRequest(new { error = "El comentario no puede estar vacío." });

            var trimmedText = payload.RawText.Trim();
            if (trimmedText.Length > 300)
                return Results.BadRequest(new { error = "El comentario excede el límite de 300 caracteres." });

            try
            {
                var newComment = await repo.CreateCommentAsync(guid, trimmedText);
                if (newComment == null)
                    return Results.NotFound(new { error = "Incidente no encontrado." });

                return Results.Created($"/api/v1/pulso/situations/{id}/comments/{newComment.Id}", newComment);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while creating comment.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("writes");

        // Totales agregados para las tarjetas del dashboard (independientes del subconjunto cargado).
        app.MapGet("/api/v1/pulso/summary", async (ISituationRepository repo) =>
        {
            try
            {
                var summary = await repo.GetSituationSummaryAsync();
                return Results.Ok(summary);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching summary.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Obtener agregación de estatus consolidado por sector.
        app.MapGet("/api/v1/pulso/locations/stats", async (HttpRequest request, ISituationRepository repo) =>
        {
            var dateStr = request.Query["date"].ToString();
            try
            {
                var stats = await repo.GetLocationStatsAsync(dateStr);
                return Results.Ok(stats);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching location statistics.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");

        // Obtener métricas y analíticas del sistema
        app.MapGet("/api/v1/pulso/metrics", async (ISituationRepository repo) =>
        {
            try
            {
                var metrics = await repo.GetSystemMetricsAsync();
                return Results.Ok(metrics);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching system metrics.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("reads");
    }
}
