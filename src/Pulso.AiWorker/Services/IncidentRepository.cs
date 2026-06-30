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
        bool isApproximate,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling process_and_deduplicate_incident function in Postgres/PostGIS...");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT public.process_and_deduplicate_incident(" +
            "@message_id, @phone, @channel, @raw_text, @category, @severity::severity_level, " +
            "@tags, @latitude, @longitude, @location, @sector, @found_person_name, @found_person_document, @triage_provider, @is_approximate, @affected_person_name, @city)",
            conn);

        cmd.Parameters.AddWithValue("message_id",          payload.MessageId);
        cmd.Parameters.AddWithValue("phone",               payload.Phone);
        cmd.Parameters.AddWithValue("channel",             payload.Channel);
        cmd.Parameters.AddWithValue("raw_text",            rawText);
        cmd.Parameters.AddWithValue("category",            string.IsNullOrEmpty(triage.Category) ? DBNull.Value : triage.Category);
        cmd.Parameters.AddWithValue("severity",            triage.Severity);
        cmd.Parameters.AddWithValue("tags",                triage.Tags);
        cmd.Parameters.AddWithValue("latitude",            latitude.HasValue  ? (object)latitude.Value  : DBNull.Value);
        cmd.Parameters.AddWithValue("longitude",           longitude.HasValue ? (object)longitude.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("location",            string.IsNullOrEmpty(triage.ExtractedAddress) ? DBNull.Value : triage.ExtractedAddress);
        cmd.Parameters.AddWithValue("sector",              string.IsNullOrEmpty(triage.Sector)           ? DBNull.Value : triage.Sector);
        cmd.Parameters.AddWithValue("found_person_name",   string.IsNullOrEmpty(triage.FoundPersonName)  ? DBNull.Value : triage.FoundPersonName);
        cmd.Parameters.AddWithValue("found_person_document", string.IsNullOrEmpty(triage.FoundPersonDocument) ? DBNull.Value : triage.FoundPersonDocument);
        cmd.Parameters.AddWithValue("triage_provider",     string.IsNullOrEmpty(triage.TriageProvider)   ? "gemini" : triage.TriageProvider);
        cmd.Parameters.AddWithValue("is_approximate",      isApproximate);
        cmd.Parameters.AddWithValue("affected_person_name", string.IsNullOrEmpty(triage.AffectedPersonName) ? DBNull.Value : triage.AffectedPersonName);
        cmd.Parameters.AddWithValue("city",                string.IsNullOrEmpty(triage.City)             ? DBNull.Value : triage.City);

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

    /// <inheritdoc/>
    public async Task<Guid?> TryAttachLocationToRecentAsync(
        string channel,
        string phone,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Selecciona el reporte más reciente del remitente que aún NO tiene GPS exacto
        // (is_hardware_gps = false cubre tanto los sin coordenadas como los aproximados/
        // geocodificados) dentro de una ventana de 2 h, y le adjunta el GPS de hardware,
        // mejorando una ubicación aproximada a exacta si aplica. Atómico: SELECT + UPDATE.
        // created_at se refresca a now() a propósito: el reporte recién se vuelve
        // mapeable al obtener su ubicación, y así entra en el delta del SSE
        // (que filtra por created_at > watermark) y el pin aparece en tiempo real.
        await using var cmd = new NpgsqlCommand(@"
            UPDATE public.incidents
            SET coordinates     = ST_SetSRID(ST_MakePoint(@lng, @lat), 4326),
                is_hardware_gps = true,
                status          = CASE WHEN status = 'PENDING_LOCATION' THEN 'NEW'::incident_status ELSE status END,
                created_at      = now(),
                updated_at      = now()
            WHERE id = (
                SELECT id FROM public.incidents
                WHERE sender_phone   = @phone
                  AND source_channel = @channel
                  AND is_hardware_gps = false
                  AND status != 'DUPLICATE'
                  AND created_at >= now() - interval '2 hours'
                ORDER BY created_at DESC
                LIMIT 1
            )
            RETURNING id", conn);

        cmd.Parameters.AddWithValue("lng",     longitude);
        cmd.Parameters.AddWithValue("lat",     latitude);
        cmd.Parameters.AddWithValue("phone",   phone);
        cmd.Parameters.AddWithValue("channel", channel);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid guid ? guid : null;
    }

    /// <inheritdoc/>
    public async Task UpsertPendingLocationAsync(
        string channel,
        string phone,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Una fila por remitente: si comparte su ubicación de nuevo antes de describir,
        // la más reciente reemplaza a la anterior y se reinicia la ventana (created_at).
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.pending_locations (source_channel, sender_phone, latitude, longitude, created_at)
            VALUES (@channel, @phone, @lat, @lng, now())
            ON CONFLICT (source_channel, sender_phone)
            DO UPDATE SET latitude = excluded.latitude,
                          longitude = excluded.longitude,
                          created_at = now()", conn);

        cmd.Parameters.AddWithValue("channel", channel);
        cmd.Parameters.AddWithValue("phone",   phone);
        cmd.Parameters.AddWithValue("lat",     latitude);
        cmd.Parameters.AddWithValue("lng",     longitude);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(double Latitude, double Longitude)?> TryConsumePendingLocationAsync(
        string channel,
        string phone,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Atómico: borra y devuelve la ubicación pendiente del remitente si sigue
        // vigente (< 2 h). Las pendientes más viejas se ignoran (y se limpian aquí).
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM public.pending_locations
            WHERE source_channel = @channel
              AND sender_phone   = @phone
              AND created_at >= now() - interval '2 hours'
            RETURNING latitude, longitude", conn);

        cmd.Parameters.AddWithValue("channel", channel);
        cmd.Parameters.AddWithValue("phone",   phone);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var lat = reader.GetDouble(0);
            var lng = reader.GetDouble(1);
            return (lat, lng);
        }
        return null;
    }
}
