using System.Text.Json.Serialization;

namespace Pulso.IngressApi.Models;

// Estructuras de transferencia de datos del dominio PULSO.



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
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    // Para distinguir "mascota perdida" de "mascota encontrada" en el mapa.
    [property: JsonPropertyName("pet_report_type")] string? PetReportType = null
);

// Detalle pesado de un incidente (texto crudo), servido bajo demanda al abrir el popup.
public record SituationDetail(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("raw_text")] string RawText,
    // Foto del reporte para mostrar en el popup del mapa, si el ciudadano adjuntó una.
    [property: JsonPropertyName("media_file_url")] string? MediaFileUrl = null
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

// ── Open Data API (contrato público y estable, separado de la PWA) ────────────
// Allowlist EXPLÍCITA de campos del incidente para consumo externo. Excluye por
// diseño: sender_phone y media_file_url. El contenido del reporte (raw_text,
// transcribed_audio, declared_location) es público por decisión del proyecto.
public record PublicIncidentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("sector")] string? Sector,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("is_hardware_gps")] bool IsHardwareGps,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("raw_text")] string? RawText,
    [property: JsonPropertyName("transcribed_audio")] string? TranscribedAudio,
    [property: JsonPropertyName("declared_location")] string? DeclaredLocation,
    [property: JsonPropertyName("found_person_name")] string? FoundPersonName,
    [property: JsonPropertyName("affected_person_name")] string? AffectedPersonName,
    [property: JsonPropertyName("found_person_verified")] bool FoundPersonVerified,
    [property: JsonPropertyName("source_channel")] string? SourceChannel,
    // true cuando el reporte se clasificó con el fallback local (sin LLM): la
    // clasificación es menos confiable y conviene confirmarla.
    [property: JsonPropertyName("needs_review")] bool NeedsReview,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

// Metadatos de paginación por cursor opaco (base64url de created_at|id).
public record PaginationInfo(
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore
);

// Envoltura del export record-level (Accept: application/json).
public record PublicIncidentsResponse(
    [property: JsonPropertyName("data")] List<PublicIncidentDto> Data,
    [property: JsonPropertyName("pagination")] PaginationInfo Pagination
);

// Comentarios de un incidente público.
public record PublicCommentsResponse(
    [property: JsonPropertyName("data")] List<CommentDto> Data
);

// ── Variante GeoJSON (Accept: application/geo+json) — RFC 7946 ────────────────
public record GeoJsonGeometry(
    [property: JsonPropertyName("type")] string Type,
    // RFC 7946: el orden es [longitude, latitude].
    [property: JsonPropertyName("coordinates")] double[] Coordinates
);

public record GeoJsonFeature(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("geometry")] GeoJsonGeometry Geometry,
    [property: JsonPropertyName("properties")] PublicIncidentDto Properties
);

public record GeoJsonFeatureCollection(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("features")] List<GeoJsonFeature> Features,
    [property: JsonPropertyName("pagination")] PaginationInfo Pagination
);


