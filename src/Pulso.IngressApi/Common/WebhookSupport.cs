using System.Diagnostics;
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
    // Inyecta el contexto de traza actual (W3C traceparent del request del webhook)
    // para que el worker, al otro lado de la cola, enlace su procesamiento con la
    // traza que originó el mensaje.
    public static Task EnqueueAsync(IDatabase db, PulsoPayload payload)
    {
        var traceParent = Activity.Current?.Id;
        var toQueue = traceParent is null ? payload : payload with { TraceParent = traceParent };
        var json = JsonSerializer.Serialize(toQueue, PulsoJsonSerializerContext.Default.PulsoPayload);
        return db.ListLeftPushAsync(QueueKey, json);
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
