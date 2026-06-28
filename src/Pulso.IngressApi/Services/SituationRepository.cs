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

    // --- Private Db Mapping Records (AOT-safe, eliminates DLR dynamic runtime binder) ---
    private record DbSituationItem(
        Guid Id,
        string? AiCategory,
        string? Severity,
        string? Sector,
        string? City,
        double? Latitude,
        double? Longitude,
        string? FoundPersonName,
        bool IsHardwareGps,
        bool NeedsReview,
        bool FoundPersonVerified,
        DateTime CreatedAt,
        string? AffectedPersonName);

    private record DbCommentDto(Guid Id, Guid IncidentId, string RawText, DateTime CreatedAt);
    private record DbCommentResult(Guid Id, DateTime CreatedAt);
    private record DbSituationSummary(int Total, int People, int CriticalSectors);
    
    private record DbLocationStat(
        string SectorName,
        string? CityName,
        int IncidentCount,
        string SectorStatus,
        string? PeopleNames,
        string? SearchedNames,
        double? Latitude,
        double? Longitude);

    private record DbEngineStat(string Provider, int Count);
    private record DbChannelStat(string Channel, int Count);
    private record DbMetricHour(int Hr, int Count);

    public async Task<List<SituationItem>> GetSituationsAsync(DateTimeOffset? since, int limit, string? dateStr)
    {
        var hasDate = !string.IsNullOrEmpty(dateStr);
        await using var conn = await _dataSource.OpenConnectionAsync();

        var query = @"
            SELECT
                id as Id,
                ai_category as AiCategory,
                severity::text as Severity,
                COALESCE(sector, '') as Sector,
                city as City,
                ST_Y(coordinates::geometry) as Latitude,
                ST_X(coordinates::geometry) as Longitude,
                found_person_name as FoundPersonName,
                COALESCE(is_hardware_gps, false) as IsHardwareGps,
                (COALESCE(triage_provider, 'gemini') <> 'gemini') as NeedsReview,
                COALESCE(found_person_verified, false) as FoundPersonVerified,
                created_at as CreatedAt,
                affected_person_name as AffectedPersonName
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
            row.Id.ToString(),
            row.AiCategory ?? "",
            row.Severity ?? "",
            row.Sector ?? "",
            row.City,
            row.Latitude,
            row.Longitude,
            !string.IsNullOrEmpty(row.FoundPersonName),
            row.FoundPersonName,
            row.AffectedPersonName,
            row.IsHardwareGps,
            row.NeedsReview,
            row.FoundPersonVerified,
            row.CreatedAt)).ToList();
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
                id as Id, 
                incident_id as IncidentId, 
                raw_text as RawText, 
                created_at as CreatedAt 
             FROM public.comments 
             WHERE incident_id = @id 
             ORDER BY created_at ASC",
            new { id = incidentId });

        return comments.Select(c => new CommentDto(
            c.Id.ToString(),
            c.IncidentId.ToString(),
            c.RawText,
            c.CreatedAt)).ToList();
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
            result.Id.ToString(),
            incidentId.ToString(),
            rawText,
            result.CreatedAt
        );
    }

    public async Task<SituationSummary> GetSituationSummaryAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var query = @"
            SELECT
                COUNT(*) FILTER (WHERE status != 'DUPLICATE') AS Total,
                COUNT(*) FILTER (WHERE status != 'DUPLICATE' AND found_person_name IS NOT NULL) AS People,
                (SELECT COUNT(DISTINCT sector) FROM public.incidents
                 WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != '' AND severity = 'CRITICAL') AS CriticalSectors
            FROM public.incidents";

        var result = await conn.QuerySingleAsync<DbSituationSummary>(query);
        return new SituationSummary(
            result.Total,
            result.People,
            result.CriticalSectors);
    }

    public async Task<List<LocationStat>> GetLocationStatsAsync(string? dateStr)
    {
        var hasDate = !string.IsNullOrEmpty(dateStr);
        await using var conn = await _dataSource.OpenConnectionAsync();

        var query = @"
            SELECT
                COALESCE(sector, 'Desconocido') as SectorName,
                COALESCE(city, '') as CityName,
                COUNT(*) as IncidentCount,
                CASE
                    WHEN bool_or(severity = 'CRITICAL') THEN 'CRITICAL'
                    WHEN bool_or(severity = 'HIGH') THEN 'HIGH'
                    WHEN bool_or(severity = 'MEDIUM') THEN 'MEDIUM'
                    ELSE 'LOW'
                END as SectorStatus,
                string_agg(found_person_name, ',') filter (where found_person_name is not null) as PeopleNames,
                string_agg(affected_person_name, ',') filter (where affected_person_name is not null) as SearchedNames,
                AVG(ST_Y(coordinates::geometry)) as Latitude,
                AVG(ST_X(coordinates::geometry)) as Longitude
            FROM public.incidents
            WHERE status != 'DUPLICATE' AND sector IS NOT NULL AND sector != ''"
            + (hasDate ? " AND created_at >= @utcStart AND created_at <= @utcEnd" : "")
            + @"
            GROUP BY sector_name, city_name";

        object? parameters = null;
        if (hasDate)
        {
            var (utcStart, utcEnd) = GetUtcDateRange(dateStr);
            parameters = new { utcStart, utcEnd };
        }

        var stats = await conn.QueryAsync<DbLocationStat>(query, parameters);
        return stats.Select(row =>
        {
            var peopleList = string.IsNullOrEmpty(row.PeopleNames)
                ? new List<string>()
                : row.PeopleNames.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

            var searchedList = string.IsNullOrEmpty(row.SearchedNames)
                ? new List<string>()
                : row.SearchedNames.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

            var city = string.IsNullOrEmpty(row.CityName) ? null : row.CityName;
            return new LocationStat(row.SectorName, city, row.SectorStatus, row.IncidentCount, peopleList, searchedList, row.Latitude, row.Longitude);
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
            SELECT COALESCE(triage_provider, 'gemini') as Provider, COUNT(*)::integer as Count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY Provider";
        var engines = await conn.QueryAsync<DbEngineStat>(engineQuery);
        foreach (var row in engines)
        {
            engineStats[row.Provider] = row.Count;
        }

        // 2. Distribución por Canal
        var channelQuery = @"
            SELECT COALESCE(source_channel, 'unknown') as Channel, COUNT(*)::integer as Count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY Channel";
        var channels = await conn.QueryAsync<DbChannelStat>(channelQuery);
        foreach (var row in channels)
        {
            channelStats[row.Channel] = row.Count;
        }

        // 3. Distribución por Hora (0-23)
        var hourlyQuery = @"
            SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as Hr, COUNT(*)::integer as Count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY Hr 
            ORDER BY Hr ASC";
        var hourly = await conn.QueryAsync<DbMetricHour>(hourlyQuery);
        var hourMap = hourly.ToDictionary(row => row.Hr, row => row.Count);
        for (int h = 0; h < 24; h++)
        {
            hourlyDistribution.Add(new MetricsHourItem(h, hourMap.TryGetValue(h, out var c) ? c : 0));
        }

        // 4. Horas Pico (Top 3)
        var peakQuery = @"
            SELECT (EXTRACT(HOUR FROM created_at AT TIME ZONE 'America/Caracas'))::integer as Hr, COUNT(*)::integer as Count 
            FROM public.incidents 
            WHERE status != 'DUPLICATE'
            GROUP BY Hr 
            ORDER BY Count DESC 
            LIMIT 3";
        var peaks = await conn.QueryAsync<DbMetricHour>(peakQuery);
        foreach (var row in peaks)
        {
            peakHours.Add(new MetricsHourItem(row.Hr, row.Count));
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
