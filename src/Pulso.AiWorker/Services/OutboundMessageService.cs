using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pulso.AiWorker.Infrastructure;
using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Implementa el envío de mensajes salientes hacia el ciudadano usando las APIs
/// de cada canal (Telegram Bot API, WhatsApp Cloud API). Las respuestas se
/// envían dentro de la ventana de servicio (24h en WhatsApp), por lo que el
/// texto libre está permitido.
/// </summary>
public sealed class OutboundMessageService : IOutboundMessageService
{
    private const string LocationRequestMessage =
        "Hemos recibido tu reporte de emergencia en PULSO, pero no pudimos ubicar tu posición. " +
        "Por favor toca el ícono de adjuntar (+) en el chat y selecciona 'Ubicación' para " +
        "enviarnos tus coordenadas GPS exactas.";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboundMessageService> _logger;

    public OutboundMessageService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OutboundMessageService> logger)
    {
        _httpClient    = httpClientFactory.CreateClient(nameof(OutboundMessageService));
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public Task SendLocationRequestAsync(PulsoPayload payload, CancellationToken cancellationToken)
        => SendTextAsync(payload.Channel, payload.Phone, LocationRequestMessage, cancellationToken);

    /// <inheritdoc/>
    public Task SendTextAsync(PulsoPayload payload, string message, CancellationToken cancellationToken)
        => SendTextAsync(payload.Channel, payload.Phone, message, cancellationToken);

    private async Task SendTextAsync(string channel, string recipient, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(recipient))
            return;

        try
        {
            switch (channel)
            {
                case "telegram":
                    await SendTelegramAsync(recipient, text, cancellationToken);
                    break;
                case "whatsapp":
                    await SendWhatsAppAsync(recipient, text, cancellationToken);
                    break;
                default:
                    // PWA u otros canales sin vía de respuesta saliente del servidor.
                    _logger.LogInformation("No outbound channel for '{channel}'; skipping reply.", channel);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Best-effort: un fallo al responder no debe interrumpir el procesamiento del incidente.
            _logger.LogError(ex, "Failed to send outbound message via {channel}.", channel);
        }
    }

    private async Task SendTelegramAsync(string chatId, string text, CancellationToken cancellationToken)
    {
        var token = _configuration["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token) || token.Contains("TU_TELEGRAM"))
        {
            _logger.LogWarning("Telegram:BotToken no configurado; no se puede responder por Telegram.");
            return;
        }

        // chat_id admite entero o cadena; nuestros reportes traen un id numérico.
        object body = long.TryParse(chatId, out var numericId)
            ? new { chat_id = numericId, text }
            : new { chat_id = chatId, text };

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("Telegram sendMessage falló: {status}", resp.StatusCode);
        else
            _logger.LogInformation("Mensaje saliente enviado por Telegram a {recipient}.", PiiMasking.MaskPhone(chatId));
    }

    private async Task SendWhatsAppAsync(string recipient, string text, CancellationToken cancellationToken)
    {
        var token         = _configuration["WhatsApp:AccessToken"];
        var phoneNumberId = _configuration["WhatsApp:PhoneNumberId"];

        if (string.IsNullOrEmpty(token) || token.Contains("TU_WHATSAPP") ||
            string.IsNullOrEmpty(phoneNumberId) || phoneNumberId.Contains("TU_WHATSAPP"))
        {
            _logger.LogWarning("WhatsApp:AccessToken/PhoneNumberId no configurados; no se puede responder por WhatsApp.");
            return;
        }

        var version = _configuration["WhatsApp:GraphApiVersion"] ?? "v21.0";
        var url     = $"https://graph.facebook.com/{version}/{phoneNumberId}/messages";

        var body = new
        {
            messaging_product = "whatsapp",
            recipient_type    = "individual",
            to                = recipient,
            type              = "text",
            text              = new { body = text }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(request, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("WhatsApp send message falló: {status} — {err}", resp.StatusCode, err);
        }
        else
        {
            _logger.LogInformation("Mensaje saliente enviado por WhatsApp a {recipient}.", PiiMasking.MaskPhone(recipient));
        }
    }
}
