using System.Text.Json.Serialization;

namespace Pulso.IngressApi.Models;

// Estructuras de transferencia de datos del dominio PULSO.

public record PulsoPayload(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("channel")] string Channel, // 'whatsapp', 'telegram', 'pwa'
    [property: JsonPropertyName("text_body")] string TextBody,
    [property: JsonPropertyName("media_url")] string? MediaUrl,
    [property: JsonPropertyName("media_type")] string? MediaType,
    [property: JsonPropertyName("media_file_id")] string? MediaFileId,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    // Contexto de traza (W3C traceparent) inyectado al encolar para enlazar la traza
    // del webhook con el procesamiento del worker a través de la cola Redis.
    [property: JsonPropertyName("traceparent")] string? TraceParent = null
);

// Ítem liviano para el mapa/lista: NO incluye raw_text (se trae bajo demanda).
public record SituationItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("sector")] string Sector,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("is_person_found")] bool IsPersonFound,
    [property: JsonPropertyName("found_person_name")] string? FoundPersonName,
    // Nombre de la persona en peligro (buscada/atrapada/herida), si el reporte lo menciona.
    [property: JsonPropertyName("affected_person_name")] string? AffectedPersonName,
    // Precisión de la ubicación: true = coordenadas GPS de hardware (punto exacto);
    // false = ubicación aproximada (derivada de texto, sin pin en el mapa por ahora).
    [property: JsonPropertyName("is_hardware_gps")] bool IsHardwareGps,
    // true cuando el reporte fue clasificado por el sistema de respaldo (no el motor
    // principal de IA): se muestra como "por confirmar" en la interfaz.
    [property: JsonPropertyName("needs_review")] bool NeedsReview,
    [property: JsonPropertyName("found_person_verified")] bool FoundPersonVerified,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

// Detalle pesado de un incidente (texto crudo), servido bajo demanda al abrir el popup.
public record SituationDetail(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("raw_text")] string RawText
);

// Totales agregados para las tarjetas del dashboard (independientes del subconjunto cargado).
public record SituationSummary(
    [property: JsonPropertyName("total_incidents")] int TotalIncidents,
    [property: JsonPropertyName("people_found")] int PeopleFound,
    [property: JsonPropertyName("critical_sectors")] int CriticalSectors
);

public record LocationStat(
    [property: JsonPropertyName("sector")] string Sector,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("incident_count")] int IncidentCount,
    [property: JsonPropertyName("people_found")] List<string> PeopleFound,
    // Nombres de personas en peligro (buscadas/atrapadas) reportadas en el sector.
    [property: JsonPropertyName("people_searched")] List<string> PeopleSearched,
    // Centroide aproximado del sector para centrar el mapa sin depender de la lista de incidentes.
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude
);

public record MetricsHourItem(
    [property: JsonPropertyName("hour")] int Hour,
    [property: JsonPropertyName("count")] int Count
);

public record MetricsResponse(
    [property: JsonPropertyName("engine_distribution")] Dictionary<string, int> EngineDistribution,
    [property: JsonPropertyName("channel_distribution")] Dictionary<string, int> ChannelDistribution,
    [property: JsonPropertyName("hourly_distribution")] List<MetricsHourItem> HourlyDistribution,
    [property: JsonPropertyName("peak_hours")] List<MetricsHourItem> PeakHours
);

// DTOs para el sistema de comentarios (anónimos por diseño).
public record CommentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("incident_id")] string IncidentId,
    [property: JsonPropertyName("raw_text")] string RawText,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record CreateCommentPayload(
    [property: JsonPropertyName("rawText")] string RawText
);


