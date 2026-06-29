namespace Pulso.Shared;

/// <summary>
/// Vocabulario controlado de los incidentes PULSO: única fuente de verdad para las
/// categorías y severidades. Agregar un valor aquí lo propaga a la clasificación y a la validación.
/// </summary>
public static class IncidentTaxonomy
{
    /// <summary>Categorías de emergencia.</summary>
    public static readonly string[] Categories =
    {
        "SEARCH_AND_RESCUE",
        "FIRE_HAZARD",
        "MEDICAL_EMERGENCY",
        "WATER_FOOD_SHORTAGE",
        "INFRASTRUCTURE_DAMAGE"
    };

    /// <summary>Niveles de severidad (orden ascendente de gravedad).</summary>
    public static readonly string[] Severities =
    {
        "LOW",
        "MEDIUM",
        "HIGH",
        "CRITICAL"
    };
}
