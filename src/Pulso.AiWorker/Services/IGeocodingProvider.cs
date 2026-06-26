using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Proveedor concreto de geocodificación (Nominatim, Google, etc.).
/// El orquestador (<see cref="IGeocodingService"/>) prueba los proveedores
/// registrados EN ORDEN: el primero que resuelve gana, y los siguientes actúan
/// como reintento/fallback. Para añadir un proveedor nuevo basta implementar esta
/// interfaz y registrarlo después del actual en el contenedor de DI.
/// </summary>
public interface IGeocodingProvider
{
    /// <summary>Nombre corto del proveedor (p. ej. "nominatim"), usado en logs y trazas.</summary>
    string Name { get; }

    /// <summary>
    /// Geocodifica la consulta de texto. Devuelve coordenadas o null si no resuelve.
    /// No debe lanzar por errores transitorios: ante fallo, retornar null para que el
    /// orquestador intente el siguiente proveedor.
    /// </summary>
    Task<GeoResult?> GeocodeAsync(string query, CancellationToken cancellationToken);
}
