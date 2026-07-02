namespace Pulso.AiWorker.Services;

/// <summary>
/// Sube media aprobada (ya pasó moderación) a un almacenamiento persistente. Genérica
/// a propósito: nada aquí es específico de mascotas, por si más adelante se usa para
/// fotos de otros tipos de incidente.
/// </summary>
public interface IMediaStorageService
{
    /// <summary>
    /// Sube <paramref name="bytes"/> asociados al incidente <paramref name="incidentId"/>.
    /// Nunca lanza: en fallo (config ausente, error HTTP, excepción de red) devuelve
    /// null y el incidente se guarda igual, solo sin foto.
    /// </summary>
    Task<string?> UploadAsync(byte[] bytes, string mimeType, Guid incidentId, CancellationToken cancellationToken);
}
