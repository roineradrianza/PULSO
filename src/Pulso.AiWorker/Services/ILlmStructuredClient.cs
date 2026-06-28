namespace Pulso.AiWorker.Services;

/// <summary>
/// Cliente de modelo de lenguaje para SALIDA ESTRUCTURADA (JSON forzado por un esquema).
/// Abstrae el proveedor concreto (hoy Gemini) para que los consumidores —p. ej. el
/// geocodificador por LLM— no acoplen su lógica a una API específica. Cambiar de proveedor
/// de LLM es reemplazar la implementación registrada en DI, sin tocar a los consumidores.
/// </summary>
public interface ILlmStructuredClient
{
    /// <summary>
    /// Genera y devuelve un objeto de tipo <typeparamref name="T"/> a partir de una
    /// instrucción de sistema y un prompt de usuario. Genera el esquema JSON necesario
    /// a partir del tipo en tiempo de ejecución.
    /// </summary>
    Task<T?> GenerateStructuredAsync<T>(
        string systemInstruction,
        object userPrompt,
        string? modelName,
        CancellationToken cancellationToken) where T : class;
}
