using StackExchange.Redis;

namespace Pulso.IngressApi.Endpoints;

// Server-Sent Events: notifica a los navegadores conectados cuando hay incidentes
// nuevos. Solo transmite una SEÑAL (no datos con PII); el cliente reacciona pidiendo
// el delta por el endpoint saneado /situations?since=. Alimentado por Redis pub/sub
// desde el AiWorker. Mismo origen (Caddy), sin secretos en el frontend.
public static class StreamEndpoints
{
    // Canal Redis donde el worker publica al guardar un incidente.
    private static readonly RedisChannel IncidentsChannel = RedisChannel.Literal("pulso:incidents:events");

    public static void MapStreamEndpoint(this WebApplication app)
    {
        app.MapGet("/api/v1/pulso/stream", async (HttpContext ctx, IConnectionMultiplexer redis, CancellationToken ct) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("X-Accel-Buffering", "no"); // evita buffering del proxy

            var subscriber = redis.GetSubscriber();
            var queue = await subscriber.SubscribeAsync(IncidentsChannel);

            await ctx.Response.WriteAsync(": connected\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    heartbeat.CancelAfter(TimeSpan.FromSeconds(25));

                    try
                    {
                        var message = await queue.ReadAsync(heartbeat.Token);
                        await ctx.Response.WriteAsync($"event: incident\ndata: {message.Message}\n\n", ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Latido para mantener viva la conexión a través de proxies.
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                    }

                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Cliente desconectado: salida limpia.
            }
            finally
            {
                await queue.UnsubscribeAsync();
            }
        }).RequireRateLimiting("sse");
    }
}
