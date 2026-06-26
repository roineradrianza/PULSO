using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Resuelve y descarga el medio asociado a un reporte aplicando:
/// <list type="bullet">
///   <item>Resolución autenticada por canal (Telegram getFile, WhatsApp Graph API).</item>
///   <item>SSRF — solo hosts incluidos en la lista permitida.</item>
///   <item>OOM — corta la descarga si supera el tamaño máximo.</item>
///   <item>Política — el video se descarta (no se procesa por costo).</item>
///   <item>Compresión — las imágenes se redimensionan/recomprimen.</item>
/// </list>
/// </summary>
public interface IMediaDownloadService
{
    /// <summary>
    /// Resuelve el medio del <paramref name="payload"/> (según canal y referencia)
    /// y lo devuelve como <see cref="MediaContent"/> en Base64. Retorna <c>null</c>
    /// si no hay medio procesable, es video, el host no está permitido, falta el
    /// token del proveedor o se supera el tamaño máximo.
    /// </summary>
    Task<MediaContent?> ResolveMediaAsync(PulsoPayload payload, CancellationToken cancellationToken);
}
