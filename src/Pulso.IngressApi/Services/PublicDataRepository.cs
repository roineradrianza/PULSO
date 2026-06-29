using Dapper;
using Npgsql;
using Pulso.IngressApi.Models;

namespace Pulso.IngressApi.Services;

public sealed class PublicDataRepository : IPublicDataRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PublicDataRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // Mapeo AOT-safe alineado a los nombres crudos de columna del SELECT.
    private record DbPublicIncident(
        Guid id,
        string? ai_category,
        string? severity,
        string? sector,
        string? city,
        double? latitude,
        double? longitude,
        bool is_hardware_gps,
        string? status,
        string? raw_text,
        string? transcribed_audio,
        string? declared_location,
        string? found_person_name,
        string? affected_person_name,
        bool found_person_verified,
        string? source_channel,
        bool needs_review,
        DateTime created_at,
        DateTime updated_at);

    // Allowlist explícita de columnas (NUNCA SELECT *). sender_phone y media_file_url
    // quedan excluidos por diseño. Compartido por el listado y el detalle por id.
    private const string SelectColumns = @"
        id,
        ai_category,
        severity::text AS severity,
        COALESCE(sector, '') AS sector,
        city,
        ST_Y(coordinates::geometry) AS latitude,
        ST_X(coordinates::geometry) AS longitude,
        COALESCE(is_hardware_gps, false) AS is_hardware_gps,
        status::text AS status,
        raw_text,
        transcribed_audio,
        declared_location,
        found_person_name,
        affected_person_name,
        COALESCE(found_person_verified, false) AS found_person_verified,
        source_channel,
        (COALESCE(triage_provider, 'gemini') <> 'gemini') AS needs_review,
        created_at,
        updated_at";

    public async Task<List<PublicIncidentDto>> GetPublicIncidentsAsync(DateTime? cursorTime, Guid? cursorId, int limit)
    {
        var hasCursor = cursorTime.HasValue && cursorId.HasValue;
        await using var conn = await _dataSource.OpenConnectionAsync();

        // La comparación de tupla (created_at, id) > (@t, @id) garantiza que dos filas
        // con el mismo timestamp no se salten ni dupliquen.
        var query = $@"
            SELECT {SelectColumns}
            FROM public.incidents
            WHERE status != 'DUPLICATE'"
            + (hasCursor ? " AND (created_at, id) > (@cursorTime, @cursorId)" : "")
            + @"
            ORDER BY created_at ASC, id ASC
            LIMIT @limit";

        object parameters = hasCursor
            ? new { cursorTime = cursorTime!.Value, cursorId = cursorId!.Value, limit }
            : new { limit };

        var rows = await conn.QueryAsync<DbPublicIncident>(query, parameters);
        return rows.Select(Map).ToList();
    }

    public async Task<PublicIncidentDto?> GetPublicIncidentByIdAsync(Guid id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var query = $@"
            SELECT {SelectColumns}
            FROM public.incidents
            WHERE id = @id AND status != 'DUPLICATE'
            LIMIT 1";

        var row = await conn.QueryFirstOrDefaultAsync<DbPublicIncident>(query, new { id });
        return row is null ? null : Map(row);
    }

    private static PublicIncidentDto Map(DbPublicIncident r) => new(
        r.id.ToString(),
        r.ai_category,
        r.severity,
        string.IsNullOrEmpty(r.sector) ? null : r.sector,
        r.city,
        r.latitude,
        r.longitude,
        r.is_hardware_gps,
        r.status,
        r.raw_text,
        r.transcribed_audio,
        r.declared_location,
        r.found_person_name,
        r.affected_person_name,
        r.found_person_verified,
        r.source_channel,
        r.needs_review,
        r.created_at,
        r.updated_at);
}
