using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pulso.IngressApi.Endpoints;
using Pulso.IngressApi.Serialization;
using Pulso.IngressApi.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateSlimBuilder(args);

// Serialización JSON source-gen (compatible con el slim builder / AOT).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, PulsoJsonSerializerContext.Default);
});

// Multiplexor de Upstash Redis.
var redisConnectionString = builder.Configuration.GetConnectionString("UpstashRedis")
    ?? throw new InvalidOperationException("Falta la variable de configuración UpstashRedis.");
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

// Registro de servicios de datos (SOLID - Abstracción de base de datos)
builder.Services.AddSingleton<ISituationRepository, SituationRepository>();

// IP real del cliente: la API corre detrás de Caddy en el MISMO host, así que sin
// esto toda petición se vería como loopback y el rate limit por IP sería inútil.
// Confiamos en X-Forwarded-For SOLO del proxy loopback (Caddy local).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);      // 127.0.0.1
    options.KnownProxies.Add(IPAddress.IPv6Loopback);  // ::1
});

// Rate limiting por IP (capa A). Límites generosos: atrapan abuso evidente sin
// suprimir picos legítimos de reporte durante una emergencia.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };

    // Ingesta directa (canal PWA / clientes directos): escribe y dispara IA.
    options.AddPolicy("ingest", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientIp(httpContext), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        }));

    // Lecturas (situaciones, resumen, sectores, métricas): proteger de scraping/DoS.
    options.AddPolicy("reads", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientIp(httpContext), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        }));

    // Escritura de comentarios (PWA): limitar escrituras por IP para evitar spam.
    options.AddPolicy("writes", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientIp(httpContext), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        }));

    // SSE: conexiones long-lived; limitar las concurrentes por IP.
    options.AddPolicy("sse", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(ClientIp(httpContext), _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 0
        }));
});

// ── Observabilidad (OpenTelemetry → OTLP) ─────────────────────────────────────
// Config-driven: solo EXPORTA si OTEL_EXPORTER_OTLP_ENDPOINT está definido (en prod
// apunta al Aspire Dashboard por red privada). Sin esa variable (p. ej. en local) se
// instrumenta pero no se exporta. El nombre del servicio sale de OTEL_SERVICE_NAME.
var otelServiceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "pulso-api";

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(otelServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRedisInstrumentation()
        .AddNpgsql())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
}

var app = builder.Build();

// Debe ir antes del rate limiter para que la partición use la IP real del cliente.
app.UseForwardedHeaders();
app.UseRateLimiter();

// Partición de rate limit por IP del cliente (ya reescrita por ForwardedHeaders).
static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// La PWA y la API se sirven bajo el MISMO ORIGEN (reverse proxy Caddy en producción,
// proxy de Vite en desarrollo), por lo que CORS no es necesario. Si en el futuro se
// sirve el frontend desde otro origen, reintroducir aquí una política CORS restrictiva.

// Registro de endpoints por área.
app.MapPulsoApiEndpoints();   // ingesta directa + consultas de situación
app.MapTelegramWebhook();     // adaptador de Telegram
app.MapWhatsAppWebhook();     // adaptador de WhatsApp
app.MapStreamEndpoint();      // SSE en tiempo real (señal de incidentes nuevos)

app.Run();
