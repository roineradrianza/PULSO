using System.Diagnostics;
using System.Text.Json;
using Pulso.IngressApi.Models;
using Pulso.IngressApi.Serialization;
using Pulso.Shared;
using StackExchange.Redis;

namespace Pulso.IngressApi.Common;

// Utilidades compartidas por los canales de ingesta (PWA, Telegram, WhatsApp).
public static class WebhookSupport
{
    // Cola FIFO de emergencias en Upstash Redis (consumida por el AiWorker).
    public const string QueueKey = "pulso:emergency:messages";

    // Canal pub/sub de "hay trabajo": avisa al worker para que despierte al instante
    // en vez de sondear la cola. Es solo un aviso (best-effort); la cola sigue siendo
    // la fuente de verdad de la entrega.
    public const string WakeChannel = "pulso:queue:wake";

    // Serializa el payload con el contexto source-gen y lo encola.
    // Inyecta el contexto de traza actual (W3C traceparent del request del webhook)
    // para que el worker, al otro lado de la cola, enlace su procesamiento con la
    // traza que originó el mensaje.
    public static async Task EnqueueAsync(IDatabase db, PulsoPayload payload)
    {
        var traceParent = Activity.Current?.Id;
        var toQueue = traceParent is null ? payload : payload with { TraceParent = traceParent };
        var json = JsonSerializer.Serialize(toQueue, PulsoJsonSerializerContext.Default.PulsoPayload);
        await db.ListLeftPushAsync(QueueKey, json);

        // Aviso "hay trabajo" para que el worker despierte al instante. Best-effort: si
        // se pierde, el poll de respaldo del worker drena la cola igual, así que un fallo
        // aquí NO debe afectar al webhook (la entrega ya está garantizada por el LPUSH).
        try
        {
            await db.PublishAsync(RedisChannel.Literal(WakeChannel), RedisValue.EmptyString);
        }
        catch
        {
            // No crítico para la entrega: el worker drenará la cola en su próximo ciclo.
        }
    }

    // --- Límite por remitente (capa B) ---
    // Frena el envenenamiento del mapa y el costo de IA cuando un mismo emisor
    // inunda reportes, SIN importar su IP (clave para webhooks, donde todo el
    // tráfico llega desde las IPs de Telegram/Meta). Cuenta en Redis con ventana
    // por expiración de clave. Límite generoso: no debe suprimir picos legítimos
    // de emergencia, solo el abuso evidente de un remitente.
    public const int SenderMaxReports = 20;
    public static readonly TimeSpan SenderWindow = TimeSpan.FromMinutes(10);

    // Devuelve true si el remitente ya superó su cuota en la ventana actual.
    public static async Task<bool> IsSenderRateLimitedAsync(IDatabase db, string channel, string sender)
    {
        // Sin identificador de remitente (p. ej. ingesta PWA anónima): la capa por IP aplica.
        if (string.IsNullOrEmpty(sender)) return false;

        var key = $"pulso:ratelimit:{channel}:{sender}";
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, SenderWindow);
        return count > SenderMaxReports;
    }
}
