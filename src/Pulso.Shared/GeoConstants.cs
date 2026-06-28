namespace Pulso.Shared;

/// <summary>
/// Constantes y utilidades geográficas compartidas por toda la plataforma PULSO.
/// </summary>
public static class GeoConstants
{
    // Bounding box aproximada de Venezuela para evitar coordenadas maliciosas o erróneas.
    public const double VenLatMin = 0.0;
    public const double VenLatMax = 16.0;
    public const double VenLngMin = -74.0;
    public const double VenLngMax = -59.0;

    /// <summary>
    /// Determina si una coordenada geográfica está fuera de los límites de Venezuela.
    /// </summary>
    public static bool IsOutsideVenezuela(double lat, double lng)
        => lat < VenLatMin || lat > VenLatMax || lng < VenLngMin || lng > VenLngMax;
}
