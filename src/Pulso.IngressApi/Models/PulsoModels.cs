using System.Text.Json.Serialization;

namespace Pulso.IngressApi.Models;

// Estructuras de transferencia de datos del dominio PULSO.

public record PulsoPayload(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("channel")] string Channel, // 'whatsapp', 'telegram', 'pwa'
    [property: JsonPropertyName("text_body")] string TextBody,
    [property: JsonPropertyName("media_url")] string? MediaUrl,
    [property: JsonPropertyName("media_type")] string? MediaType,
    [property: JsonPropertyName("media_file_id")] string? MediaFileId,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude
);

public record SituationItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("raw_text")] string RawText,
    [property: JsonPropertyName("sector")] string Sector,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("is_person_found")] bool IsPersonFound,
    [property: JsonPropertyName("found_person_name")] string? FoundPersonName,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record LocationStat(
    [property: JsonPropertyName("sector")] string Sector,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("incident_count")] int IncidentCount,
    [property: JsonPropertyName("people_found")] List<string> PeopleFound
);
