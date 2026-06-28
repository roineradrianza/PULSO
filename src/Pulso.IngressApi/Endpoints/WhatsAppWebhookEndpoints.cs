using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulso.IngressApi.Common;
using Pulso.IngressApi.Models;
using Pulso.IngressApi.Serialization;
using Pulso.Shared;
using StackExchange.Redis;

namespace Pulso.IngressApi.Endpoints;

// Adaptador del webhook de WhatsApp (Meta Cloud API): verificación, firma HMAC y normalización a PulsoPayload.
public static class WhatsAppWebhookEndpoints
{
    public static void MapWhatsAppWebhook(this WebApplication app)
    {
        // Verificación: Meta envía un GET que validamos y respondemos con hub.challenge.
        app.MapGet("/api/v1/webhooks/whatsapp", (HttpRequest request, IConfiguration config) =>
        {
            var mode = request.Query["hub.mode"].ToString();
            var token = request.Query["hub.verify_token"].ToString();
            var challenge = request.Query["hub.challenge"].ToString();
            var verifyToken = config["WhatsApp:VerifyToken"];

            if (mode == "subscribe" && 
                !string.IsNullOrEmpty(verifyToken) && 
                verifyToken != "placeholder" && 
                token == verifyToken)
            {
                // Eco del challenge como texto plano (200) para completar la verificación.
                return Results.Text(challenge);
            }
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        // Eventos: valida la firma HMAC, normaliza los mensajes a PulsoPayload y los encola.
        app.MapPost("/api/v1/webhooks/whatsapp", async (HttpRequest request, IConnectionMultiplexer redisConn, IConfiguration config) =>
        {
            // Leer el cuerpo CRUDO (bytes) — requerido para validar la firma HMAC sin re-codificar.
            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                await request.Body.CopyToAsync(ms);
                rawBytes = ms.ToArray();
            }

            // 1. Validar X-Hub-Signature-256 (HMAC-SHA256 del cuerpo crudo con el App Secret).
            var appSecret = config["WhatsApp:AppSecret"];
            if (string.IsNullOrEmpty(appSecret) || appSecret == "placeholder")
            {
                app.Logger.LogError("WhatsApp:AppSecret no configurado. Webhook rechazado por seguridad (Fail-Closed).");
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            if (!VerifyWhatsAppSignature(rawBytes, request.Headers["X-Hub-Signature-256"].ToString(), appSecret))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            WhatsAppWebhook? hook;
            try
            {
                hook = JsonSerializer.Deserialize(rawBytes, PulsoJsonSerializerContext.Default.WhatsAppWebhook);
            }
            catch
            {
                return Results.Ok(); // cuerpo no parseable: ack para no provocar reintentos
            }

            var db = redisConn.GetDatabase();

            foreach (var entry in hook?.Entry ?? Array.Empty<WhatsAppEntry>())
            {
                foreach (var change in entry.Changes ?? Array.Empty<WhatsAppChange>())
                {
                    // value.messages solo está presente en mensajes entrantes; los 'statuses' (entregado/leído) se ignoran.
                    var messages = change.Value?.Messages;
                    if (messages is null) continue;

                    foreach (var m in messages)
                    {
                        if (string.IsNullOrEmpty(m.Id) || string.IsNullOrEmpty(m.From)) continue;

                        string textBody = m.Text?.Body ?? m.Image?.Caption ?? string.Empty;
                        string? mediaType = null;
                        string? mediaFileId = null;

                        if (m.Audio is not null)
                        {
                            // Nota de voz: la descarga (Graph API + token, URL que expira en 5 min) es tarea de media.
                            mediaType = "audio";
                            mediaFileId = m.Audio.Id;
                            if (string.IsNullOrEmpty(textBody)) textBody = "[Nota de voz recibida - pendiente de transcripción]";
                        }
                        else if (m.Image is not null)
                        {
                            mediaType = "image";
                            mediaFileId = m.Image.Id;
                            if (string.IsNullOrEmpty(textBody)) textBody = "[Imagen recibida]";
                        }

                        // Coordenadas: se encolan tal cual. El worker es la autoridad sobre el
                        // bounding box de Venezuela (las sanea antes de persistir) y, si la
                        // ubicación queda fuera, le responde al ciudadano. Anularlas aquí
                        // descartaría el mensaje en silencio (sin respuesta posible).
                        double? lat = m.Location?.Latitude;
                        double? lng = m.Location?.Longitude;

                        if (string.IsNullOrEmpty(textBody) && !lat.HasValue) continue;

                        var payload = new PulsoPayload(
                            MessageId: m.Id,            // wamid... globalmente único (idempotencia)
                            Phone: m.From,             // wa_id / número del remitente
                            Channel: "whatsapp",
                            TextBody: textBody,
                            MediaUrl: null,            // descarga autenticada vía Graph API = tarea de media
                            MediaType: mediaType,
                            MediaFileId: mediaFileId,
                            Latitude: lat,
                            Longitude: lng
                        );

                        // Capa B: límite por remitente (wa_id). Si excede, se descarta el mensaje;
                        // el webhook igual responde 200 al final para no provocar reintentos de Meta.
                        if (await WebhookSupport.IsSenderRateLimitedAsync(db, "whatsapp", payload.Phone))
                        {
                            app.Logger.LogWarning("Remitente de WhatsApp excedió el límite; se descarta el mensaje. from={from}", m.From);
                            continue;
                        }

                        await WebhookSupport.EnqueueAsync(db, payload);
                    }
                }
            }

            return Results.Ok();
        });
    }

    // Valida la firma HMAC-SHA256 del webhook (header X-Hub-Signature-256: "sha256=<hex>").
    private static bool VerifyWhatsAppSignature(byte[] body, string signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) ||
            !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedHex = signatureHeader["sha256=".Length..];
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedHex = Convert.ToHexStringLower(hmac.ComputeHash(body));

        var a = Encoding.UTF8.GetBytes(computedHex);
        var b = Encoding.UTF8.GetBytes(providedHex);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
