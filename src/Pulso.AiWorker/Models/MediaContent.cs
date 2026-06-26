namespace Pulso.AiWorker.Models;

/// <summary>Tipo de medio soportado para análisis por la IA.</summary>
public enum MediaKind
{
    Audio,
    Image
}

/// <summary>
/// Medio resuelto y descargado, listo para enviarse a Gemini como parte
/// inline (Base64 + MIME). El video queda explícitamente excluido por costo.
/// </summary>
public record MediaContent(string Base64Data, string MimeType, MediaKind Kind);
