using Npgsql;
using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Persiste incidentes de emergencia en la base de datos PostGIS de Supabase.
/// Encapsula la lógica de deduplicación espacial delegada al procedimiento
/// almacenado <c>process_and_deduplicate_incident</c>.
/// </summary>
public sealed class IncidentRepository : IIncidentRepository
{
    private readonly string _connectionString;
    private readonly ILogger<IncidentRepository> _logger;

    public IncidentRepository(IConfiguration configuration, ILogger<IncidentRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta la variable de configuración DefaultConnection.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid?> SaveIncidentAsync(
        PulsoPayload payload,
        TriageResult triage,
        string rawText,
        double? latitude,
        double? longitude,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling process_and_deduplicate_incident function in Postgres/PostGIS...");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT public.process_and_deduplicate_incident(" +
            "@message_id, @phone, @channel, @raw_text, @category, @severity::severity_level, " +
            "@tags, @latitude, @longitude, @location, @sector, @found_person_name, @found_person_document)",
            conn);

        cmd.Parameters.AddWithValue("message_id",          payload.MessageId);
        cmd.Parameters.AddWithValue("phone",               payload.Phone);
        cmd.Parameters.AddWithValue("channel",             payload.Channel);
        cmd.Parameters.AddWithValue("raw_text",            rawText);
        cmd.Parameters.AddWithValue("category",            triage.Category);
        cmd.Parameters.AddWithValue("severity",            triage.Severity);
        cmd.Parameters.AddWithValue("tags",                triage.Tags);
        cmd.Parameters.AddWithValue("latitude",            latitude.HasValue  ? (object)latitude.Value  : DBNull.Value);
        cmd.Parameters.AddWithValue("longitude",           longitude.HasValue ? (object)longitude.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("location",            string.IsNullOrEmpty(triage.ExtractedAddress) ? DBNull.Value : triage.ExtractedAddress);
        cmd.Parameters.AddWithValue("sector",              string.IsNullOrEmpty(triage.Sector)           ? DBNull.Value : triage.Sector);
        cmd.Parameters.AddWithValue("found_person_name",   string.IsNullOrEmpty(triage.FoundPersonName)  ? DBNull.Value : triage.FoundPersonName);
        cmd.Parameters.AddWithValue("found_person_document", string.IsNullOrEmpty(triage.FoundPersonDocument) ? DBNull.Value : triage.FoundPersonDocument);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);

        if (result is Guid guid)
        {
            _logger.LogInformation("Incident successfully processed in database. Generated/associated ID: {id}", guid);
            return guid;
        }

        _logger.LogWarning("process_and_deduplicate_incident returned a non-GUID result: {result}", result);
        return null;
    }

    /// <inheritdoc/>
    public async Task SaveTranscriptionAsync(
        Guid incidentId,
        string transcription,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving transcription in transcribed_audio column for incident {id}...", incidentId);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "UPDATE public.incidents SET transcribed_audio = @transcribed WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("transcribed", transcription);
        cmd.Parameters.AddWithValue("id",          incidentId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
