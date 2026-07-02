using Pulso.Shared;

namespace Pulso.IngressApi.Endpoints;

// Sirve las fotos de reportes (mascotas por ahora) bajo el mismo dominio de la API,
// para que de dónde vengan realmente las fotos sea un detalle de infraestructura,
// no parte del contrato con la PWA.
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/pulso/media/{id}.jpg", async (
            string id, HttpContext http, IHttpClientFactory httpClientFactory, IConfiguration config) =>
        {
            if (!Guid.TryParse(id, out var incidentId))
                return Results.NotFound();

            var supabaseUrl = config["Supabase:Url"];
            var bucketName = config["Supabase:BucketName"];
            if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(bucketName))
                return Results.NotFound();

            var objectKey = IncidentMediaPaths.PetObjectKey(incidentId);
            var sourceUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{bucketName}/{objectKey}";

            var client = httpClientFactory.CreateClient(nameof(MediaEndpoints));
            using var response = await client.GetAsync(sourceUrl, http.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return Results.NotFound();

            var bytes = await response.Content.ReadAsByteArrayAsync(http.RequestAborted);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

            // La foto de un incidente no cambia una vez publicada, así que el navegador
            // puede quedársela cacheada de forma permanente.
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Bytes(bytes, contentType);
        }).RequireRateLimiting("reads");
    }
}
