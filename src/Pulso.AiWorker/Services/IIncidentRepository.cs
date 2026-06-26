using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Persiste el incidente triado en la base de datos PostGIS de Supabase y
/// gestiona operaciones secundarias como guardar transcripciones de audio.
/// </summary>
public interface IIncidentRepository
{
    /// <summary>
    /// Llama a <c>process_and_deduplicate_incident</c> en Postgres y retorna
    /// el UUID del incidente creado o deduplicado.
    /// </summary>
    Task<Guid?> SaveIncidentAsync(
        PulsoPayload payload,
        TriageResult triage,
        string rawText,
        double? latitude,
        double? longitude,
        bool isApproximate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Actualiza la columna <c>transcribed_audio</c> del incidente indicado.
    /// </summary>
    Task SaveTranscriptionAsync(
        Guid incidentId,
        string transcription,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adjunta coordenadas GPS de hardware al reporte MÁS RECIENTE del mismo
    /// remitente que aún no tiene ubicación (dentro de una ventana de tiempo).
    /// Sirve para correlacionar una "ubicación suelta" enviada como respuesta a la
    /// solicitud del bot con el reporte original, en vez de crear un incidente vacío.
    /// Devuelve el id del incidente actualizado, o null si no había ninguno pendiente.
    /// </summary>
    Task<Guid?> TryAttachLocationToRecentAsync(
        string channel,
        string phone,
        double latitude,
        double longitude,
        CancellationToken cancellationToken);

    /// <summary>
    /// Guarda (upsert) la ubicación que un remitente compartió ANTES de describir
    /// su incidente, para usarla cuando luego llegue la descripción (flujo
    /// ubicación → descripción). Una fila por remitente: la más reciente reemplaza
    /// a la anterior.
    /// </summary>
    Task UpsertPendingLocationAsync(
        string channel,
        string phone,
        double latitude,
        double longitude,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consume (lee y borra) la ubicación pendiente del remitente si existe dentro
    /// de la ventana de 2 h. Devuelve las coordenadas exactas a usar para el
    /// incidente, o null si no hay ninguna pendiente vigente. Atómico: DELETE … RETURNING.
    /// </summary>
    Task<(double Latitude, double Longitude)?> TryConsumePendingLocationAsync(
        string channel,
        string phone,
        CancellationToken cancellationToken);
}
