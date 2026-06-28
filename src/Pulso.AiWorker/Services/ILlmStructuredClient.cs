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
    /// Genera una respuesta JSON que cumple <paramref name="responseSchema"/> a partir de una
    /// instrucción de sistema y un prompt de usuario. <paramref name="modelName"/> es opcional:
    /// si es null, el cliente usa su modelo por defecto. Devuelve el texto JSON crudo del modelo,
    /// o null ante fallo. No lanza por errores transitorios: el llamador degrada con gracia.
    /// </summary>
    Task<string?> GenerateJsonAsync(
        string systemInstruction,
        string userPrompt,
        object responseSchema,
        string? modelName,
        CancellationToken cancellationToken);
}
