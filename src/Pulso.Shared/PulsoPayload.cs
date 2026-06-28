using System.Text.Json.Serialization;

namespace Pulso.Shared;

/// <summary>
/// DTO unificado que representa el mensaje de emergencia ingresado al sistema.
/// Se usa para comunicar el API de ingesta con el Worker a través de la cola de mensajería.
/// </summary>
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
    [property: JsonPropertyName("traceparent")] string? TraceParent = null
);
