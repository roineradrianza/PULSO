namespace Pulso.Shared;

public static class IncidentMediaPaths
{
    // Clave del objeto DENTRO del bucket de Supabase.
    public static string ObjectKey(Guid incidentId) => $"{incidentId}.jpg";

    // Ruta relativa persistida.
    public static string RelativeUrl(Guid incidentId) => $"/api/v1/pulso/media/{incidentId}.jpg";
}
