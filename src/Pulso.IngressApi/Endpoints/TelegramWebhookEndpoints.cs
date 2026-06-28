using System.Security.Cryptography;
using System.Text;
using Pulso.IngressApi.Common;
using Pulso.IngressApi.Models;
using Pulso.Shared;
using StackExchange.Redis;

namespace Pulso.IngressApi.Endpoints;

// Adaptador del webhook de Telegram (Bot API): normaliza un Update a PulsoPayload y lo encola.
public static class TelegramWebhookEndpoints
{
    public static void MapTelegramWebhook(this WebApplication app)
    {
        app.MapPost("/api/v1/webhooks/telegram", async (TelegramUpdate update, HttpRequest request, IConnectionMultiplexer redisConn, IConfiguration config) =>
        {
            // 1. Autenticar con el secret token de setWebhook (header X-Telegram-Bot-Api-Secret-Token).
            var expectedSecret = config["Telegram:SecretToken"];
            if (string.IsNullOrEmpty(expectedSecret) || expectedSecret == "placeholder")
            {
                app.Logger.LogError("Telegram:SecretToken no configurado. Webhook rechazado por seguridad (Fail-Closed).");
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var provided = Encoding.UTF8.GetBytes(request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString());
            var expected = Encoding.UTF8.GetBytes(expectedSecret);
            if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var msg = update.Message;
            if (msg is null)
            {
                // Update sin mensaje (edición, callback, etc.): ack para evitar reintentos.
                return Results.Ok();
            }

            // 2. Normalizar a PulsoPayload.
            string textBody = msg.Text ?? msg.Caption ?? string.Empty;
            string? mediaType = null;
            string? mediaFileId = null;

            if (msg.Voice is not null)
            {
                // La descarga autenticada (getFile + token) queda para la tarea de media.
                mediaType = "audio";
                mediaFileId = msg.Voice.FileId;
                if (string.IsNullOrEmpty(textBody)) textBody = "[Nota de voz recibida - pendiente de transcripción]";
            }
            else if (msg.Photo is { Length: > 0 })
            {
                // Telegram entrega varias resoluciones; tomar la de mayor tamaño.
                var largest = msg.Photo.OrderByDescending(p => p.FileSize ?? 0).First();
                mediaType = "image";
                mediaFileId = largest.FileId;
                if (string.IsNullOrEmpty(textBody)) textBody = "[Imagen recibida]";
            }

            // Coordenadas: se encolan tal cual. El worker es la autoridad sobre el
            // bounding box de Venezuela (las sanea antes de persistir) y, si la
            // ubicación queda fuera, le responde al ciudadano explicándolo. Anularlas
            // aquí descartaría el mensaje en silencio (sin respuesta posible).
            double? lat = msg.Location?.Latitude;
            double? lng = msg.Location?.Longitude;

            // Nada accionable (ej. sticker, contacto): ack y salir.
            if (string.IsNullOrEmpty(textBody) && !lat.HasValue)
            {
                return Results.Ok();
            }

            var payload = new PulsoPayload(
                MessageId: $"tg-{msg.Chat.Id}-{msg.MessageId}",   // clave de idempotencia única y estable
                Phone: msg.Chat.Id.ToString(),                    // Telegram no provee teléfono; usar chat id como contacto
                Channel: "telegram",
                TextBody: textBody,
                MediaUrl: null,                                   // sin URL: la descarga autenticada es tarea de media
                MediaType: mediaType,
                MediaFileId: mediaFileId,
                Latitude: lat,
                Longitude: lng
            );

            var db = redisConn.GetDatabase();

            // Capa B: límite por remitente (chat id). Si excede, se descarta el mensaje
            // pero SIEMPRE se responde 200 para que Telegram no reintente ni deshabilite el webhook.
            if (await WebhookSupport.IsSenderRateLimitedAsync(db, "telegram", payload.Phone))
            {
                app.Logger.LogWarning("Remitente de Telegram excedió el límite; se descarta el mensaje. chat={chat}", msg.Chat.Id);
                return Results.Ok();
            }

            await WebhookSupport.EnqueueAsync(db, payload);

            return Results.Ok();
        });
    }
}
