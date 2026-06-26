using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Servicio de geocodificación de alto nivel que usa el worker. Coordina la cadena
/// de proveedores (con fallback), la caché y la validación geográfica.
/// </summary>
public interface IGeocodingService
{
    /// <summary>
    /// Devuelve coordenadas APROXIMADAS para una dirección o sector en texto, o null
    /// si no se pudo resolver con confianza (o cae fuera de Venezuela).
    /// </summary>
    Task<GeoResult?> GeocodeAsync(string query, CancellationToken cancellationToken);
}
