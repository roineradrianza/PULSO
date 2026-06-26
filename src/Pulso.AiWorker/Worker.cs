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

        // 8. Notificar a los clientes conectados (SSE) vía Redis pub/sub. Solo una
        //    señal con el id; el cliente pedirá el delta por el endpoint saneado.
        if (incidentId.HasValue)
        {
            try
            {
                await _redis.GetSubscriber().PublishAsync(
                    RedisChannel.Literal("pulso:incidents:events"), incidentId.Value.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo publicar la señal de incidente nuevo (no crítico).");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
