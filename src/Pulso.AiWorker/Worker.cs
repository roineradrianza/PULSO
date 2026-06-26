using System.Diagnostics;
using System.Text.Json;
using Pulso.AiWorker.Infrastructure;
using Pulso.AiWorker.Models;
using Pulso.AiWorker.Services;
using StackExchange.Redis;

namespace Pulso.AiWorker;

/// <summary>
/// Servicio en segundo plano que consume la cola Redis de mensajes de
/// emergencia, orquesta el triaje con IA y persiste el incidente en la base
/// de datos PostGIS.  Toda la lógica de dominio está delegada a servicios
/// inyectados; este clase actúa únicamente como orquestador.
/// </summary>
public class Worker : BackgroundService
{
    // Fuente de spans de negocio del worker (un span por reporte procesado).
    public const string ActivitySourceName = "Pulso.AiWorker";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    // Mensajes de confirmación hacia el ciudadano (best-effort, por el canal de origen).
    private const string AckReceivedMessage =
        "✅ Recibimos tu reporte. Lo estamos procesando…";
    private const string LocationAttachedMessage =
        "📍 Ubicación recibida. Tu reporte está completo. Gracias por ayudar.";
    private const string OrphanLocationMessage =
        "Recibimos tu ubicación, pero no encontramos un reporte reciente para asociarla. " +
        "Por favor envía primero la descripción del incidente.";

    // Límite geográfico de Venezuela (bounding box).
    // Coordenadas fuera de este rectángulo se descartan para evitar el
    // envenenamiento del mapa con ubicaciones falsas de otros países.
    private const double VenLatMin  =   0.0;
    private const double VenLatMax  =  16.0;
    private const double VenLngMin  = -74.0;
    private const double VenLngMax  = -59.0;

    private readonly ILogger<Worker>          _logger;
    private readonly IConnectionMultiplexer   _redis;
    private readonly IGeminiTriageService     _geminiTriage;
    private readonly IMediaDownloadService    _mediaDownload;
    private readonly IIncidentRepository      _incidentRepo;
    private readonly IOutboundMessageService  _outbound;
    private readonly IGeocodingService        _geocoding;

    public Worker(
        ILogger<Worker>          logger,
        IConnectionMultiplexer   redis,
        IGeminiTriageService     geminiTriage,
        IMediaDownloadService    mediaDownload,
        IIncidentRepository      incidentRepo,
        IOutboundMessageService  outbound,
        IGeocodingService        geocoding)
    {
        _logger        = logger;
        _redis         = redis;
        _geminiTriage  = geminiTriage;
        _mediaDownload = mediaDownload;
        _incidentRepo  = incidentRepo;
        _outbound      = outbound;
        _geocoding     = geocoding;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PULSO AI Worker started. Listening to queue...");

        var db       = _redis.GetDatabase();
        var queueKey = "pulso:emergency:messages";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var rawPayload = await db.ListRightPopAsync(queueKey);
                if (rawPayload.IsNullOrEmpty)
                {
                    // Cola vacía — esperar antes de reintentar para no saturar CPU
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                _logger.LogInformation(
                    "Message received for processing: {payload}",
                    PiiMasking.MaskPayloadJson(rawPayload.ToString()));

                await ProcessMessageAsync(rawPayload.ToString(), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error processing message from the queue.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task ProcessMessageAsync(string jsonPayload, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PulsoPayload>(jsonPayload);
        if (payload == null)
        {
            _logger.LogWarning("Null or invalid payload received. Skipping.");
            return;
        }

        // Span de negocio que agrupa todo el procesamiento del reporte y lo ENLAZA con
        // la traza del webhook que lo originó (el contexto viaja en payload.TraceParent
        // a través de la cola Redis). Los spans hijos (Gemini, Nominatim, Redis, Npgsql)
        // se anidan bajo este.
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(payload.TraceParent) &&
            ActivityContext.TryParse(payload.TraceParent, null, out var parsed))
        {
            parentContext = parsed;
        }

        using var activity = ActivitySource.StartActivity(
            "process-incident", ActivityKind.Consumer, parentContext);
        activity?.SetTag("pulso.channel", payload.Channel);
        activity?.SetTag("messaging.system", "redis");

        // 0. ¿Es una respuesta de ubicación? (coordenadas sin texto, p. ej. el usuario
        //    compartió su ubicación tras pedírsela el bot). En ese caso NO creamos un
        //    incidente vacío: adjuntamos las coordenadas al reporte previo del remitente.
        if (IsLocationReply(payload))
        {
            activity?.SetTag("pulso.operation", "attach-location");
            var (replyLat, replyLng) = SanitizeCoordinates(payload.Latitude, payload.Longitude);
            if (replyLat.HasValue && replyLng.HasValue)
            {
                var attachedId = await _incidentRepo.TryAttachLocationToRecentAsync(
                    payload.Channel, payload.Phone, replyLat.Value, replyLng.Value, cancellationToken);

                if (attachedId.HasValue)
                {
                    _logger.LogInformation(
                        "Location attached to the sender's previous report {id}.", attachedId.Value);
                    await PublishIncidentSignalAsync(attachedId.Value);
                    await _outbound.SendTextAsync(payload, LocationAttachedMessage, cancellationToken);
                }
                else
                {
                    // Sin reporte previo asociado: una ubicación sola no es accionable.
                    _logger.LogInformation("Location received with no pending report to attach to; ignored.");
                    await _outbound.SendTextAsync(payload, OrphanLocationMessage, cancellationToken);
                }
            }
            return;
        }

        // Acuse inmediato: el ciudadano sabe que su reporte llegó y se está procesando.
        await _outbound.SendTextAsync(payload, AckReceivedMessage, cancellationToken);

        // 1. Resolver y descargar media (audio o imagen; el video se descarta por política)
        var media = await _mediaDownload.ResolveMediaAsync(payload, cancellationToken);

        // 2. Triaje con IA
        _logger.LogInformation("Sending report to AI model for categorization and structured triage...");
        var triage = await _geminiTriage.TriageAsync(payload.TextBody, media, cancellationToken);

        // Sustituir el texto con la transcripción si el modelo la produjo
        var rawText = !string.IsNullOrEmpty(triage.Transcription)
            ? $"[Transcripción de audio]: {triage.Transcription}"
            : payload.TextBody;

        // 3. Validar coordenadas dentro del bounding box de Venezuela
        var (latitude, longitude) = SanitizeCoordinates(payload.Latitude, payload.Longitude);
        var isApproximate = false;

        // 4. Plan B: sin GPS de hardware, geocodificar la dirección/sector inferidos por la IA
        //    para obtener coordenadas APROXIMADAS (preferir la dirección sobre el sector).
        if (!latitude.HasValue || !longitude.HasValue)
        {
            var geoQuery = !string.IsNullOrWhiteSpace(triage.ExtractedAddress)
                ? triage.ExtractedAddress
                : triage.Sector;

            if (!string.IsNullOrWhiteSpace(geoQuery))
            {
                var geo = await _geocoding.GeocodeAsync(geoQuery!, cancellationToken);
                if (geo is not null)
                {
                    latitude      = geo.Latitude;
                    longitude     = geo.Longitude;
                    isApproximate = true;
                    _logger.LogInformation(
                        "Approximate location resolved via geocoding ({provider}).", geo.Provider);
                }
            }
        }

        // 5. Fallback conversacional si AÚN no hay ubicación: pedirla por el canal de origen.
        if (!latitude.HasValue && !longitude.HasValue)
        {
            _logger.LogWarning(
                "Could not resolve location via GPS, text or geocoding. Requesting location from citizen...");
            await _outbound.SendLocationRequestAsync(payload, cancellationToken);
        }

        // 6. Persistir incidente (isApproximate => is_hardware_gps = false y sin deduplicación espacial)
        var incidentId = await _incidentRepo.SaveIncidentAsync(
            payload, triage, rawText, latitude, longitude, isApproximate, cancellationToken);

        // 7. Guardar transcripción si aplica
        if (!string.IsNullOrEmpty(triage.Transcription) && incidentId.HasValue)
        {
            await _incidentRepo.SaveTranscriptionAsync(
                incidentId.Value, triage.Transcription, cancellationToken);
        }

        // 8. Confirmación final al ciudadano si el reporte quedó con ubicación (GPS o
        //    geocodificada). Si no la tiene, el paso 5 ya respondió pidiendo la ubicación,
        //    y la confirmación llegará cuando la comparta (rama attach-location).
        if (incidentId.HasValue && latitude.HasValue && longitude.HasValue)
        {
            var place = string.IsNullOrWhiteSpace(triage.Sector) ? "" : $" en {triage.Sector}";
            await _outbound.SendTextAsync(payload,
                $"📍 Tu reporte quedó registrado{place}. Gracias por ayudar a tu comunidad.",
                cancellationToken);
        }

        // 9. Notificar a los clientes conectados (SSE) vía Redis pub/sub. Solo una
        //    señal con el id; el cliente pedirá el delta por el endpoint saneado.
        if (incidentId.HasValue)
        {
            await PublishIncidentSignalAsync(incidentId.Value);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Una "respuesta de ubicación": trae coordenadas pero no texto (el usuario
    /// compartió su ubicación, normalmente tras pedírsela el bot).
    /// </summary>
    private static bool IsLocationReply(PulsoPayload payload)
        => payload.Latitude.HasValue
           && payload.Longitude.HasValue
           && string.IsNullOrWhiteSpace(payload.TextBody);

    /// <summary>
    /// Publica en Redis pub/sub la señal de incidente (creado o actualizado) para que
    /// el SSE notifique a los clientes. Solo el id; sin PII. No crítico.
    /// </summary>
    private async Task PublishIncidentSignalAsync(Guid incidentId)
    {
        try
        {
            await _redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal("pulso:incidents:events"), incidentId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo publicar la señal de incidente (no crítico).");
        }
    }

    /// <summary>
    /// Descarta coordenadas fuera del bounding box de Venezuela para evitar el
    /// envenenamiento del mapa con ubicaciones de otros países.
    /// </summary>
    private (double? Latitude, double? Longitude) SanitizeCoordinates(double? lat, double? lng)
    {
        if (!lat.HasValue || !lng.HasValue)
            return (null, null);

        if (lat.Value < VenLatMin || lat.Value > VenLatMax ||
            lng.Value < VenLngMin || lng.Value > VenLngMax)
        {
            _logger.LogWarning(
                "Discarded incoming coordinates outside Venezuela boundary: Lat {lat}, Lng {lng}",
                lat.Value, lng.Value);
            return (null, null);
        }

        return (lat, lng);
    }
}
