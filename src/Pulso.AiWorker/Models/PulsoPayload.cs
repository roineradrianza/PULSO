using System.Text.Json.Serialization;

namespace Pulso.AiWorker.Models;

public record PulsoPayload(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("text_body")] string TextBody,
    [property: JsonPropertyName("media_url")] string? MediaUrl,
    [property: JsonPropertyName("media_type")] string? MediaType,
    [property: JsonPropertyName("media_file_id")] string? MediaFileId,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    // Contexto de traza (W3C traceparent) inyectado por el productor para enlazar
    // la traza del webhook con el procesamiento del worker a través de la cola.
    [property: JsonPropertyName("traceparent")] string? TraceParent = null
);
