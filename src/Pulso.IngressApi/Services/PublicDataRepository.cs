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

    public async Task<List<PublicIncidentDto>> GetPublicIncidentsAsync(DateTime? cursorTime, Guid? cursorId, int limit, PublicIncidentFilter filter)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        // WHERE dinámico: condiciones AND-combinables. La comparación de tupla
        // (created_at, id) > (@t, @id) garantiza que dos filas con el mismo timestamp
        // no se salten ni dupliquen al paginar.
        var where = new List<string> { "status != 'DUPLICATE'" };
        var p = new DynamicParameters();
        p.Add("limit", limit);

        if (cursorTime.HasValue && cursorId.HasValue)
        {
            where.Add("(created_at, id) > (@cursorTime, @cursorId)");
            p.Add("cursorTime", cursorTime.Value);
            p.Add("cursorId", cursorId.Value);
        }
        if (filter.Severities is { Length: > 0 })
        {
            where.Add("severity::text = ANY(@severities)");
            p.Add("severities", filter.Severities);
        }
        if (filter.Categories is { Length: > 0 })
        {
            where.Add("ai_category::text = ANY(@categories)");
            p.Add("categories", filter.Categories);
        }
        if (filter.CreatedFrom.HasValue)
        {
            where.Add("created_at >= @createdFrom");
            p.Add("createdFrom", filter.CreatedFrom.Value);
        }
        if (filter.CreatedTo.HasValue)
        {
            where.Add("created_at <= @createdTo");
            p.Add("createdTo", filter.CreatedTo.Value);
        }
        // Filtro espacial por bounding box (usa el índice GiST). Excluye implícitamente
        // los registros sin coordenadas (coordinates && envelope es null para ellos).
        if (filter.Bbox is { Length: 4 })
        {
            where.Add("coordinates && ST_MakeEnvelope(@minLon, @minLat, @maxLon, @maxLat, 4326)");
            p.Add("minLon", filter.Bbox[0]);
            p.Add("minLat", filter.Bbox[1]);
            p.Add("maxLon", filter.Bbox[2]);
            p.Add("maxLat", filter.Bbox[3]);
        }

        var query = $@"
            SELECT {SelectColumns}
            FROM public.incidents
            WHERE {string.Join(" AND ", where)}
            ORDER BY created_at ASC, id ASC
            LIMIT @limit";

        var rows = await conn.QueryAsync<DbPublicIncident>(query, p);
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

    private record DbComment(Guid id, Guid incident_id, string raw_text, DateTime created_at);

    public async Task<List<CommentDto>?> GetPublicIncidentCommentsAsync(Guid incidentId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        var exists = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM public.incidents WHERE id = @id AND status != 'DUPLICATE'", new { id = incidentId });
        if (exists == null)
            return null;

        var comments = await conn.QueryAsync<DbComment>(
            @"SELECT id, incident_id, raw_text, created_at
              FROM public.comments
              WHERE incident_id = @id
              ORDER BY created_at ASC",
            new { id = incidentId });

        return comments.Select(c => new CommentDto(
            c.id.ToString(),
            c.incident_id.ToString(),
            c.raw_text,
            c.created_at)).ToList();
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
