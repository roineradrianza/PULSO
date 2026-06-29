using Dapper;
using Npgsql;
using Pulso.IngressApi.Models;

namespace Pulso.IngressApi.Services;

public sealed class SituationRepository : ISituationRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SituationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // --- Private Db Mapping Records (AOT-safe, aligned to raw SQL column names) ---
    private record DbSituationItem(
        Guid id,
        string? ai_category,
        string? severity,
        string? sector,
        string? city,
        double? latitude,
        double? longitude,
        string? found_person_name,
        bool is_hardware_gps,
        bool needs_review,
        bool found_person_verified,
        DateTime created_at,
        string? affected_person_name);

    private record DbCommentDto(Guid id, Guid incident_id, string raw_text, DateTime created_at);
    private record DbCommentResult(Guid id, DateTime created_at);
    private record DbSituationSummary(long total, long people, long critical_sectors);
    
    private record DbLocationStat(
        string sector_name,
        string? city_name,
        long incident_count,
        string sector_status,
        string? people_names,
        string? searched_names,
        double latitude,
        double longitude);

    private record DbEngineStat(string provider, int count);
    private record DbChannelStat(string channel, int count);
    private record DbMetricHour(int hr, int count);

    public async Task<List<SituationItem>> GetSituationsAsync(DateTimeOffset? since, int limit, string? dateStr)
    {
        var hasDate = !string.IsNullOrEmpty(dateStr);
        await using var conn = await _dataSource.OpenConnectionAsync();

        var query = @"
            SELECT
                id,
                ai_category,
                severity::text,
                COALESCE(sector, '') as sector,
                city,
                ST_Y(coordinates::geometry) as latitude,
                ST_X(coordinates::geometry) as longitude,
                found_person_name,
                COALESCE(is_hardware_gps, false) as is_hardware_gps,
                (COALESCE(triage_provider, 'gemini') <> 'gemini') as needs_review,
                COALESCE(found_person_verified, false) as found_person_verified,
                created_at,
                affected_person_name
            FROM public.incidents
            WHERE status != 'DUPLICATE'"
            + (hasDate ? " AND created_at >= @utcStart AND created_at <= @utcEnd" : "")
            + (since.HasValue ? " AND created_at > @since" : "")
            + @"
            ORDER BY created_at DESC
            LIMIT @limit";

        object? parameters;
        if (hasDate)
        {
            var (utcStart, utcEnd) = GetUtcDateRange(dateStr);
            parameters = new { utcStart, utcEnd, since = since?.UtcDateTime, limit };
        }
        else
        {
            parameters = new { since = since?.UtcDateTime, limit };
        }

        var situations = await conn.QueryAsync<DbSituationItem>(query, parameters);
        return situations.Select(row => new SituationItem(
            row.id.ToString(),
            row.ai_category ?? "",
            row.severity ?? "",
            row.sector ?? "",
            row.city,
            row.latitude,
            row.longitude,
            !string.IsNullOrEmpty(row.found_person_name),
            row.found_person_name,
            row.affected_person_name,
            row.is_hardware_gps,
            row.needs_review,
            row.found_person_verified,
            row.created_at)).ToList();
    }

    public async Task<SituationDetail?> GetSituationDetailAsync(Guid id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var rawText = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT raw_text FROM public.incidents WHERE id = @id AND status != 'DUPLICATE'",
            new { id });

        if (rawText == null) return null;
        return new SituationDetail(id.ToString(), rawText);
    }

    public async Task<List<CommentDto>?> GetCommentsAsync(Guid incidentId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        // Validar existencia del incidente
        var exists = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM public.incidents WHERE id = @id", new { id = incidentId });
        if (exists == null)
            return null;

        var comments = await conn.QueryAsync<DbCommentDto>(
            @"SELECT 
                id, 
                incident_id, 
                raw_text, 
                created_at 
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

    public async Task<CommentDto?> CreateCommentAsync(Guid incidentId, string rawText)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        // Validar existencia del incidente
        var exists = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM public.incidents WHERE id = @id", new { id = incidentId });
        if (exists == null)
            return null;

        var result = await conn.QuerySingleAsync<DbCommentResult>(
            "INSERT INTO public.comments (incident_id, raw_text) VALUES (@incident_id, @raw_text) RETURNING id, created_at",
            new { incident_id = incidentId, raw_text = rawText });

        return new CommentDto(
            result.id.ToString(),
            incidentId.ToString(),
            rawText,
            result.created_at
        );
    }

    public async Task<SituationSummary> GetSituationSummaryAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var query = @"
            SELECT
                COUNT(*) FILTER (WHERE status != 'DUPLICATE') AS total,
                COUNT(*) FILTER (WHERE status != 'DUPLICATE' AND found_person_name IS NOT NULL) AS people,
                (SELECT COUNT(DISTINCT sector) FROM public.incidents
                 WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != '' AND severity = 'CRITICAL') AS critical_sectors
            FROM public.incidents";

        var result = await conn.QuerySingleAsync<DbSituationSummary>(query);
        return new SituationSummary(
            (int)result.total,
            (int)result.people,
            (int)result.critical_sectors);
    }

    public async Task<List<LocationStat>> GetLocationStatsAsync(string? dateStr)
    {
        var hasDate = !string.IsNullOrEmpty(dateStr);
        await using var conn = await _dataSource.OpenConnectionAsync();

        var query = @"
            SELECT
                COALESCE(sector, 'Desconocido') as sector_name,
                COALESCE(city, '') as city_name,
                COUNT(*) as incident_count,
                CASE
                    WHEN bool_or(severity = 'CRITICAL') THEN 'CRITICAL'
                    WHEN bool_or(severity = 'HIGH') THEN 'HIGH'
                    WHEN bool_or(severity = 'MEDIUM') THEN 'MEDIUM'
                    ELSE 'LOW'
                END as sector_status,
                string_agg(found_person_name, ',') filter (where found_person_name is not null) as people_names,
                string_agg(affected_person_name, ',') filter (where affected_person_name is not null) as searched_names,
                AVG(ST_Y(coordinates::geometry)) as latitude,
                AVG(ST_X(coordinates::geometry)) as longitude
            FROM public.incidents
            WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != ''"
            + (hasDate ? " AND created_at >= @utcStart AND created_at <= @utcEnd" : "")
            + @"
            GROUP BY sector_name, city_name
            ORDER BY
                CASE
                    WHEN bool_or(severity = 'CRITICAL') THEN 0
                    WHEN bool_or(severity = 'HIGH') THEN 1
                    WHEN bool_or(severity = 'MEDIUM') THEN 2
                    ELSE 3
                END,
                COUNT(*) DESC";

        object? parameters = null;
        if (hasDate)
        {
            var (utcStart, utcEnd) = GetUtcDateRange(dateStr);
            parameters = new { utcStart, utcEnd };
        }

        var stats = await conn.QueryAsync<DbLocationStat>(query, parameters);
        return stats.Select(row =>
        {
            var peopleList = string.IsNullOrEmpty(row.people_names)
                ? new List<string>()
                : row.people_names.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

            var searchedList = string.IsNullOrEmpty(row.searched_names)
                ? new List<string>()
                : row.searched_names.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

            var city = string.IsNullOrEmpty(row.city_name) ? null : row.city_name;
            return new LocationStat(row.sector_name, city, row.sector_status, (int)row.incident_count, peopleList, searchedList, row.latitude, row.longitude);
        }).ToList();
    }

    public async Task<MetricsResponse> GetSystemMetricsAsync()
    {
        var engineStats = new Dictionary<string, int>();
        var channelStats = new Dictionary<string, int>();
        var hourlyDistribution = new List<MetricsHourItem>();
        var peakHours = new List<MetricsHourItem>();

        await using var conn = await _dataSource.OpenConnectionAsync();

        // 1. Distribución por Motor
        var engineQuery = @"
            SELECT COALESCE(triage_provider, 'gemini') as provider, COUNT(*)::integer as count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY provider";
        var engines = await conn.QueryAsync<DbEngineStat>(engineQuery);
        foreach (var row in engines)
        {
            engineStats[row.provider] = row.count;
        }

        // 2. Distribución por Canal
        var channelQuery = @"
            SELECT COALESCE(source_channel, 'unknown') as channel, COUNT(*)::integer as count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY channel";
        var channels = await conn.QueryAsync<DbChannelStat>(channelQuery);
        foreach (var row in channels)
        {
            channelStats[row.channel] = row.count;
        }

        // 3. Distribución por Hora (0-23)
        var hourlyQuery = @"
            SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as hr, COUNT(*)::integer as count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY hr 
            ORDER BY hr ASC";
        var hourly = await conn.QueryAsync<DbMetricHour>(hourlyQuery);
        var hourMap = hourly.ToDictionary(row => row.hr, row => row.count);
        for (int h = 0; h < 24; h++)
        {
            hourlyDistribution.Add(new MetricsHourItem(h, hourMap.TryGetValue(h, out var c) ? c : 0));
        }

        // 4. Horas Pico (Top 3)
        var peakQuery = @"
            SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as hr, COUNT(*)::integer as count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY hr 
            ORDER BY count DESC 
            LIMIT 3";
        var peaks = await conn.QueryAsync<DbMetricHour>(peakQuery);
        foreach (var row in peaks)
        {
            peakHours.Add(new MetricsHourItem(row.hr, row.count));
        }

        return new MetricsResponse(engineStats, channelStats, hourlyDistribution, peakHours);
    }

    private static (DateTime utcStart, DateTime utcEnd) GetUtcDateRange(string? dateStr)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Caracas");
        var nowInVet = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        
        DateTime targetDate;
        if (!DateTime.TryParse(dateStr, out targetDate))
        {
            targetDate = nowInVet.Date;
        }
        
        var localStart = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 23, 59, 59, DateTimeKind.Unspecified);
        
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
    }
}
