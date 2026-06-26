using Pulso.AiWorker;
using Pulso.AiWorker.Services;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// ── Redis (Upstash) ───────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("UpstashRedis")
    ?? throw new InvalidOperationException("Falta la variable de configuración UpstashRedis.");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString));

// ── HttpClient factory ────────────────────────────────────────────────────────
// Usar IHttpClientFactory en lugar de un HttpClient estático permite al runtime
// rotar los HttpMessageHandlers y evitar la saturación de sockets.
builder.Services.AddHttpClient();

// ── Servicios de dominio ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IGeminiTriageService,    GeminiTriageService>();
builder.Services.AddSingleton<IMediaDownloadService,   MediaDownloadService>();
builder.Services.AddSingleton<IIncidentRepository,     IncidentRepository>();
builder.Services.AddSingleton<IOutboundMessageService, OutboundMessageService>();

// ── Worker en segundo plano ───────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
