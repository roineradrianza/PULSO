using System.Text.Json.Serialization;
using Pulso.IngressApi.Models;

namespace Pulso.IngressApi.Serialization;

// Contexto de serialización JSON source-gen (compatible con el slim builder / AOT).
[JsonSerializable(typeof(PulsoPayload))]
[JsonSerializable(typeof(SituationItem))]
[JsonSerializable(typeof(List<SituationItem>))]
[JsonSerializable(typeof(LocationStat))]
[JsonSerializable(typeof(List<LocationStat>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(TelegramUpdate))]
[JsonSerializable(typeof(WhatsAppWebhook))]
internal partial class PulsoJsonSerializerContext : JsonSerializerContext
{
}
