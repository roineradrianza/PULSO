using System.Text.Json.Serialization;

namespace Pulso.AiWorker.Models;

// Respuesta de Telegram getFile (Bot API).
public record TelegramGetFileResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] TelegramFileInfo? Result
);

public record TelegramFileInfo(
    [property: JsonPropertyName("file_path")] string? FilePath,
    [property: JsonPropertyName("file_size")] long? FileSize
);

// Metadata de media de WhatsApp (Graph API): URL temporal de descarga + MIME.
public record WhatsAppMediaMetadata(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("mime_type")] string? MimeType
);
