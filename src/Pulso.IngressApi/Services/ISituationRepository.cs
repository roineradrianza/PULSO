using Pulso.IngressApi.Models;

namespace Pulso.IngressApi.Services;

public interface ISituationRepository
{
    Task<List<SituationItem>> GetSituationsAsync(DateTimeOffset? since, int limit, string? dateStr);
    Task<SituationDetail?> GetSituationDetailAsync(Guid id);
    Task<List<CommentDto>?> GetCommentsAsync(Guid incidentId);
    Task<CommentDto?> CreateCommentAsync(Guid incidentId, string rawText);
    Task<SituationSummary> GetSituationSummaryAsync();
    Task<List<LocationStat>> GetLocationStatsAsync(string? dateStr);
    Task<MetricsResponse> GetSystemMetricsAsync();
}
