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
        CancellationToken cancellationToken);

    /// <summary>
    /// Actualiza la columna <c>transcribed_audio</c> del incidente indicado.
    /// </summary>
    Task SaveTranscriptionAsync(
        Guid incidentId,
        string transcription,
        CancellationToken cancellationToken);
}
