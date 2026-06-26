using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Envía mensajes salientes al ciudadano por el canal de origen del reporte
/// (Telegram, WhatsApp). Usado para el fallback conversacional cuando no se
/// pudo resolver la ubicación del incidente.
/// </summary>
public interface IOutboundMessageService
{
    /// <summary>
    /// Solicita al ciudadano que comparta su ubicación GPS, respondiendo por el
    /// mismo canal por el que llegó el reporte. Es best-effort: los errores se
    /// registran sin interrumpir el procesamiento del incidente.
    /// </summary>
    Task SendLocationRequestAsync(PulsoPayload payload, CancellationToken cancellationToken);
}
