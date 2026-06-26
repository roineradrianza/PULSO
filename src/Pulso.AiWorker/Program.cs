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

// Cliente dedicado a Nominatim: su política de uso EXIGE un User-Agent identificable.
builder.Services.AddHttpClient("nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PulsoAid/1.0 (+https://pulsoaid.org; contacto: roineradrianzap@gmail.com)");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ── Servicios de dominio ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IGeminiTriageService,    GeminiTriageService>();
builder.Services.AddSingleton<IMediaDownloadService,   MediaDownloadService>();
builder.Services.AddSingleton<IIncidentRepository,     IncidentRepository>();
builder.Services.AddSingleton<IOutboundMessageService, OutboundMessageService>();

// Geocodificación: el ORDEN de registro define la cadena de fallback.
// Hoy solo Nominatim; cuando se agregue Google, registrarlo AQUÍ debajo para que
// actúe como reintento cuando Nominatim no resuelva.
builder.Services.AddSingleton<IGeocodingProvider, NominatimGeocodingProvider>();
// builder.Services.AddSingleton<IGeocodingProvider, GoogleGeocodingProvider>();
builder.Services.AddSingleton<IGeocodingService, GeocodingService>();

// ── Worker en segundo plano ───────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
