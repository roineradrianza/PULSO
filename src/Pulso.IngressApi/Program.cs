using Pulso.IngressApi.Endpoints;
using Pulso.IngressApi.Serialization;
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

var app = builder.Build();

// La PWA y la API se sirven bajo el MISMO ORIGEN (reverse proxy Caddy en producción,
// proxy de Vite en desarrollo), por lo que CORS no es necesario. Si en el futuro se
// sirve el frontend desde otro origen, reintroducir aquí una política CORS restrictiva.

// Registro de endpoints por área.
app.MapPulsoApiEndpoints();   // ingesta directa + consultas de situación
app.MapTelegramWebhook();     // adaptador de Telegram
app.MapWhatsAppWebhook();     // adaptador de WhatsApp
app.MapStreamEndpoint();      // SSE en tiempo real (señal de incidentes nuevos)

app.Run();
