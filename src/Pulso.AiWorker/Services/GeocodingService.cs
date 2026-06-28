using System.Globalization;
using Pulso.AiWorker.Models;
using StackExchange.Redis;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Orquestador de geocodificación. Recorre los proveedores registrados EN ORDEN
/// (cadena de fallback), cachea los resultados en Redis (incluida la caché negativa
/// para no reconsultar direcciones que no resuelven) y descarta puntos fuera de
/// Venezuela. Centralizar caché y validación aquí mantiene simples a los proveedores.
/// </summary>
public sealed class GeocodingService : IGeocodingService
{


    private const string CachePrefix = "pulso:geocode:";
    private const string MissSentinel = "MISS";
    private static readonly TimeSpan HitTtl  = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissTtl = TimeSpan.FromHours(6); // reintentar más tarde si fue fallo transitorio

    private readonly IReadOnlyList<IGeocodingProvider> _providers;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<GeocodingService> _logger;

    public GeocodingService(
        IEnumerable<IGeocodingProvider> providers,
        IConnectionMultiplexer redis,
        ILogger<GeocodingService> logger)
    {
        _providers = providers.ToList();
        _redis = redis;
        _logger = logger;
    }

    public async Task<GeoResult?> GeocodeAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0) return null;

        var db = _redis.GetDatabase();
        var cacheKey = CachePrefix + normalized.ToLowerInvariant();

        // 1. Caché (incluye caché negativa).
        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            var raw = cached.ToString();
            if (raw == MissSentinel) return null;
            var parsed = TryParseCache(raw);
            if (parsed is not null) return parsed;
        }

        // 2. Cadena de proveedores: el primero que resuelve dentro de Venezuela gana.
        foreach (var provider in _providers)
        {
            var result = await provider.GeocodeAsync(normalized, cancellationToken);
            if (result is null) continue;

            if (!IsInsideVenezuela(result.Latitude, result.Longitude))
            {
                _logger.LogWarning("Geocodificación de {provider} cayó fuera de Venezuela; se descarta.", provider.Name);
                continue;
            }

            await db.StringSetAsync(cacheKey, FormatCache(result), HitTtl);
            return result;
        }

        // 3. Nadie resolvió: cachear el fallo para no reconsultar de inmediato.
        await db.StringSetAsync(cacheKey, MissSentinel, MissTtl);
        return null;
    }

    private static bool IsInsideVenezuela(double lat, double lng)
        => !GeoConstants.IsOutsideVenezuela(lat, lng);

    private static string FormatCache(GeoResult r)
        => string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}", r.Latitude, r.Longitude, r.Provider);

    private static GeoResult? TryParseCache(string raw)
    {
        var parts = raw.Split(';');
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            return new GeoResult(lat, lng, parts.Length >= 3 ? parts[2] : "cache");
        }
        return null;
    }
}
