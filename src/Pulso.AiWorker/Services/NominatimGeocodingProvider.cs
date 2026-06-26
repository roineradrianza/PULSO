using System.Globalization;
using System.Text.Json;
using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Geocodificación con Nominatim (OpenStreetMap, gratis). Restringe la búsqueda a
/// Venezuela (countrycodes=ve) y respeta la política de uso: User-Agent identificable
/// (configurado en el HttpClient con nombre "nominatim") y un máximo de 1 req/seg.
/// </summary>
public sealed class NominatimGeocodingProvider : IGeocodingProvider
{
    public string Name => "nominatim";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NominatimGeocodingProvider> _logger;

    // Política de Nominatim: máximo 1 petición por segundo. Como el worker procesa la
    // cola en serie, basta un candado simple con un intervalo mínimo entre llamadas.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public NominatimGeocodingProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<NominatimGeocodingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GeoResult?> GeocodeAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var sinceLast = DateTimeOffset.UtcNow - _lastCall;
                if (sinceLast < MinInterval)
                    await Task.Delay(MinInterval - sinceLast, cancellationToken);

                var client = _httpClientFactory.CreateClient("nominatim");
                var url = $"search?q={Uri.EscapeDataString(query)}&format=jsonv2&countrycodes=ve&limit=1";
                using var response = await client.GetAsync(url, cancellationToken);
                _lastCall = DateTimeOffset.UtcNow;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Nominatim respondió {status} para la consulta de geocodificación.", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;

                var first = doc.RootElement[0];
                if (!first.TryGetProperty("lat", out var latEl) || !first.TryGetProperty("lon", out var lonEl))
                    return null;

                if (double.TryParse(latEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(lonEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    return new GeoResult(lat, lon, Name);
                }

                return null;
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al geocodificar con Nominatim; se intentará el siguiente proveedor si existe.");
            return null;
        }
    }
}
