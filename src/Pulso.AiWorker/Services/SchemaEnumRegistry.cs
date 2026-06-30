namespace Pulso.AiWorker.Services;

/// <summary>
/// Registro de vocabularios controlados (enum) que un tipo de salida estructurada declara
/// para sus propiedades.
/// </summary>
public static class SchemaEnumRegistry
{
    // Poblado únicamente por [ModuleInitializer] al cargar el ensamblado. Seguro para lectura concurrente.
    private static readonly Dictionary<Type, IReadOnlyDictionary<string, string[]>> OverridesByType = new();

    /// <summary>
    /// Registra, para <typeparamref name="T"/>, qué valores de enum forzar por propiedad
    /// JSON. La clave debe coincidir con el <c>[JsonPropertyName]</c> del DTO.
    /// </summary>
    public static void Register<T>(IReadOnlyDictionary<string, string[]> overridesByJsonProperty)
        => OverridesByType[typeof(T)] = overridesByJsonProperty;

    // Público (no internal): este worker es un ejecutable, no una librería publicada, así
    // que no hay superficie de API que proteger, y permite testear el registro sin
    // InternalsVisibleTo.
    public static IReadOnlyDictionary<string, string[]>? GetOverrides(Type type)
        => OverridesByType.TryGetValue(type, out var overrides) ? overrides : null;
}
