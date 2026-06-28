using System.Text.Json.Serialization;
using Pulso.IngressApi.Models;
using Pulso.Shared;

namespace Pulso.IngressApi.Serialization;

// Contexto de serialización JSON source-gen (compatible con el slim builder / AOT).
[JsonSerializable(typeof(PulsoPayload))]
[JsonSerializable(typeof(SituationItem))]
[JsonSerializable(typeof(List<SituationItem>))]
[JsonSerializable(typeof(SituationDetail))]
[JsonSerializable(typeof(SituationSummary))]
[JsonSerializable(typeof(LocationStat))]
[JsonSerializable(typeof(List<LocationStat>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(TelegramUpdate))]
[JsonSerializable(typeof(WhatsAppWebhook))]
[JsonSerializable(typeof(MetricsResponse))]
[JsonSerializable(typeof(MetricsHourItem))]
[JsonSerializable(typeof(List<MetricsHourItem>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(CommentDto))]
[JsonSerializable(typeof(List<CommentDto>))]
[JsonSerializable(typeof(CreateCommentPayload))]
internal partial class PulsoJsonSerializerContext : JsonSerializerContext
{
}
