using System.Text.Json.Serialization;

namespace Pulso.IngressApi.Models;

// DTOs del webhook de Telegram (Bot API).

public record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message
);

public record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("from")] TelegramUser? From,
    [property: JsonPropertyName("chat")] TelegramChat Chat,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("caption")] string? Caption,
    [property: JsonPropertyName("voice")] TelegramVoice? Voice,
    [property: JsonPropertyName("photo")] TelegramPhotoSize[]? Photo,
    [property: JsonPropertyName("location")] TelegramLocation? Location
);

public record TelegramUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("username")] string? Username
);

public record TelegramChat(
    [property: JsonPropertyName("id")] long Id
);

public record TelegramVoice(
    [property: JsonPropertyName("file_id")] string FileId,
    [property: JsonPropertyName("mime_type")] string? MimeType
);

public record TelegramPhotoSize(
    [property: JsonPropertyName("file_id")] string FileId,
    [property: JsonPropertyName("file_size")] long? FileSize
);

public record TelegramLocation(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude
);
