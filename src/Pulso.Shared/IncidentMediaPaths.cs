namespace Pulso.Shared;

public static class IncidentMediaPaths
{
    // Clave del objeto DENTRO del bucket de Supabase "reports", 
    // bajo el prefijo específico de fotos de mascotas.
    public static string PetObjectKey(Guid incidentId) => $"pets/{incidentId}.jpg";

    // Ruta relativa persistida.
    public static string RelativeUrl(Guid incidentId) => $"/api/v1/pulso/media/{incidentId}.jpg";
}
