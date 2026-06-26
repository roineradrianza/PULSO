using System.Text.Json;
using Pulso.IngressApi.Models;
using Pulso.IngressApi.Serialization;
using StackExchange.Redis;

namespace Pulso.IngressApi.Common;

// Utilidades compartidas por los canales de ingesta (PWA, Telegram, WhatsApp).
public static class WebhookSupport
{
    // Cola FIFO de emergencias en Upstash Redis (consumida por el AiWorker).
    public const string QueueKey = "pulso:emergency:messages";

    // Caja delimitadora aproximada del territorio de Venezuela.
    // Se usa para descartar coordenadas falsas/maliciosas fuera del país.
    public static bool IsOutsideVenezuela(double lat, double lng)
        => lat < 0.0 || lat > 16.0 || lng < -74.0 || lng > -59.0;

    // Serializa el payload con el contexto source-gen y lo encola.
    public static Task EnqueueAsync(IDatabase db, PulsoPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, PulsoJsonSerializerContext.Default.PulsoPayload);
        return db.ListLeftPushAsync(QueueKey, json);
    }
}
