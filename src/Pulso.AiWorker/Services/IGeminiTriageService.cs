using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Envía el reporte al modelo de IA de Google (Gemini) y devuelve el resultado
/// estructurado del triaje. Recurre a un simulador local si la API key no está
/// configurada o la llamada remota falla.
/// </summary>
public interface IGeminiTriageService
{
    /// <summary>
    /// Realiza el triaje estructurado del reporte de emergencia.
    /// </summary>
    /// <param name="text">Texto plano del reporte ciudadano (o caption del medio).</param>
    /// <param name="media">Medio adjunto opcional (audio o imagen) a analizar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<TriageResult> TriageAsync(
        string text,
        MediaContent? media,
        CancellationToken cancellationToken);
}
