using System.Globalization;
using Pulso.Shared;

namespace Pulso.IngressApi.Services;

// Filtros opcionales del listado público en WGS84.
public record PublicIncidentFilter(
    string[]? Severities = null,
    string[]? Categories = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null,
    double[]? Bbox = null)
{
    // Construye el filtro desde la query string.
    public static bool TryParse(IQueryCollection query, out PublicIncidentFilter filter, out string? error)
    {
        filter = new PublicIncidentFilter();

        if (!TryParseEnumList(query["severity"], IncidentTaxonomy.Severities, "severity", out var severities, out error) ||
            !TryParseEnumList(query["category"], IncidentTaxonomy.Categories, "category", out var categories, out error) ||
            !TryParseInstant(query["created_from"], "created_from", out var createdFrom, out error) ||
            !TryParseInstant(query["created_to"], "created_to", out var createdTo, out error) ||
            !TryParseBbox(query["bbox"], out var bbox, out error))
        {
            return false;
        }

        filter = new PublicIncidentFilter(severities, categories, createdFrom, createdTo, bbox);
        return true;
    }

    // Lista coma-separada validada contra un vocabulario permitido (case-insensitive).
    private static bool TryParseEnumList(string? raw, string[] allowed, string name, out string[]? values, out string? error)
    {
        values = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToArray();

        var bad = parsed.FirstOrDefault(v => Array.IndexOf(allowed, v) < 0);
        if (bad is not null)
        {
            error = $"{name} inválida: '{bad}'. Valores válidos: {string.Join(", ", allowed)}.";
            return false;
        }

        values = parsed;
        return true;
    }

    // Instante ISO 8601; se asume UTC si no trae offset.
    private static bool TryParseInstant(string? raw, string name, out DateTime? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
        {
            error = $"{name} no es una fecha/hora ISO 8601 válida.";
            return false;
        }

        value = dt;
        return true;
    }

    // bbox = "minLon,minLat,maxLon,maxLat" (4 números, dentro de rango, min <= max).
    private static bool TryParseBbox(string? raw, out double[]? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !parts.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            error = "bbox debe tener el formato 'minLon,minLat,maxLon,maxLat' (4 números).";
            return false;
        }

        var b = parts.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        if (b[0] > b[2] || b[1] > b[3] || b[0] < -180 || b[2] > 180 || b[1] < -90 || b[3] > 90)
        {
            error = "bbox fuera de rango o con un mínimo mayor que el máximo.";
            return false;
        }

        value = b;
        return true;
    }
}
