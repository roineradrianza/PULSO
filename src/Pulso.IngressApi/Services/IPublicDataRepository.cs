using Pulso.IngressApi.Models;

namespace Pulso.IngressApi.Services;

// Acceso de datos del Open Data API.
public interface IPublicDataRepository
{
    // Paginación incremental por cursor compuesto (created_at, id) en orden ascendente.
    // cursorTime/cursorId nulos => primera página (registros más antiguos primero).
    Task<List<PublicIncidentDto>> GetPublicIncidentsAsync(DateTime? cursorTime, Guid? cursorId, int limit, PublicIncidentFilter filter);

    // Detalle de un incidente público por id. null si no existe o es un DUPLICATE.
    Task<PublicIncidentDto?> GetPublicIncidentByIdAsync(Guid id);

    // Comentarios de un incidente público, en orden cronológico. null si el incidente
    // no existe o es un DUPLICATE (mismo criterio de visibilidad que el detalle).
    Task<List<CommentDto>?> GetPublicIncidentCommentsAsync(Guid incidentId);
}
