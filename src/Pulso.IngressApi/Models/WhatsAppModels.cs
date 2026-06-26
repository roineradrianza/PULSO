using System.Text.Json.Serialization;

namespace Pulso.IngressApi.Models;

// DTOs del webhook de WhatsApp (Meta Cloud API).

public record WhatsAppWebhook(
    [property: JsonPropertyName("object")] string? Object,
    [property: JsonPropertyName("entry")] WhatsAppEntry[]? Entry
);

public record WhatsAppEntry(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("changes")] WhatsAppChange[]? Changes
);

public record WhatsAppChange(
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("value")] WhatsAppValue? Value
);

public record WhatsAppValue(
    [property: JsonPropertyName("messaging_product")] string? MessagingProduct,
    [property: JsonPropertyName("metadata")] WhatsAppMetadata? Metadata,
    [property: JsonPropertyName("messages")] WhatsAppMessage[]? Messages
);

public record WhatsAppMetadata(
    [property: JsonPropertyName("display_phone_number")] string? DisplayPhoneNumber,
    [property: JsonPropertyName("phone_number_id")] string? PhoneNumberId
);

public record WhatsAppMessage(
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("text")] WhatsAppText? Text,
    [property: JsonPropertyName("audio")] WhatsAppMedia? Audio,
    [property: JsonPropertyName("image")] WhatsAppMedia? Image,
    [property: JsonPropertyName("location")] WhatsAppLocation? Location
);

public record WhatsAppText(
    [property: JsonPropertyName("body")] string? Body
);

public record WhatsAppMedia(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("mime_type")] string? MimeType,
    [property: JsonPropertyName("caption")] string? Caption
);

public record WhatsAppLocation(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude
);
