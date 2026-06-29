using System.Reflection;
using System.Text;
using Pulso.IngressApi.Models;
using Pulso.IngressApi.Serialization;
using Pulso.IngressApi.Services;

namespace Pulso.IngressApi.Endpoints;

// Open Data API: contrato público, abierto y versionado para extracción masiva de
// reportes por terceros.
public static class PublicDataEndpoints
{
    private const int DefaultLimit = 1000;
    private const int MaxLimit = 5000;
    private const string GeoJsonMediaType = "application/geo+json";

    public static void MapPublicDataEndpoints(this WebApplication app)
    {
        // Export record-level. Negocia JSON (default) o GeoJSON vía cabecera Accept.
        app.MapGet("/api/v1/public/incidents", async (HttpContext http, IPublicDataRepository repo) =>
        {
            AllowAnyOrigin(http);

            var sinceRaw = http.Request.Query["since"].ToString();
            DateTime? cursorTime = null;
            Guid? cursorId = null;
            if (!string.IsNullOrEmpty(sinceRaw))
            {
                if (!TryDecodeCursor(sinceRaw, out cursorTime, out cursorId))
                    return Results.BadRequest(new { error = "El parámetro 'since' no es un cursor válido." });
            }

            var limit = int.TryParse(http.Request.Query["limit"].ToString(), out var l)
                ? Math.Clamp(l, 1, MaxLimit)
                : DefaultLimit;

            try
            {
                var items = await repo.GetPublicIncidentsAsync(cursorTime, cursorId, limit);

                // has_more: si llenamos el límite, asumimos que puede haber más páginas.
                var hasMore = items.Count == limit;
                var nextCursor = hasMore && items.Count > 0
                    ? EncodeCursor(items[^1].CreatedAt, items[^1].Id)
                    : null;

                http.Response.Headers.CacheControl = "public, max-age=30";
                // La respuesta varía según Accept (JSON vs GeoJSON): sin esto una caché
                // compartida podría servir la representación equivocada para el mismo URL.
                http.Response.Headers.Vary = "Accept";
                if (nextCursor != null)
                {
                    var nextUrl = $"{BaseUrl(http)}/api/v1/public/incidents?since={Uri.EscapeDataString(nextCursor)}&limit={limit}";
                    http.Response.Headers.Link = $"<{nextUrl}>; rel=\"next\"";
                }

                var pagination = new PaginationInfo(limit, items.Count, nextCursor, hasMore);

                if (WantsGeoJson(http))
                {
                    var features = items
                        .Where(i => i.Latitude.HasValue && i.Longitude.HasValue)
                        .Select(i => new GeoJsonFeature(
                            "Feature",
                            new GeoJsonGeometry("Point", new[] { i.Longitude!.Value, i.Latitude!.Value }),
                            i))
                        .ToList();

                    var fc = new GeoJsonFeatureCollection("FeatureCollection", features, pagination);
                    return Results.Json(fc, PulsoJsonSerializerContext.Default.GeoJsonFeatureCollection, GeoJsonMediaType);
                }

                var response = new PublicIncidentsResponse(items, pagination);
                return Results.Json(response, PulsoJsonSerializerContext.Default.PublicIncidentsResponse);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error occurred while fetching public incidents.");
                return Results.Problem("An error occurred while processing your request.");
            }
        }).RequireRateLimiting("public");

        // Especificación OpenAPI 3.1
        app.MapGet("/api/v1/public/openapi.yaml", (HttpContext http) =>
        {
            AllowAnyOrigin(http);
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(OpenApiSpec.Value, "application/yaml");
        }).RequireRateLimiting("public");

        // Documentación interactiva apuntando al YAML anterior.
        app.MapGet("/api/v1/public/docs", (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(DocsHtml, "text/html");
        }).RequireRateLimiting("public");

        // Preflight CORS para consumidores de navegador en otro origen. Sin esto, un
        // fetch cross-origin que dispare OPTIONS (headers no-safelisted) sería bloqueado.
        app.MapMethods("/api/v1/public/{*path}", new[] { "OPTIONS" }, (HttpContext http) =>
        {
            http.Response.Headers.AccessControlAllowOrigin = "*";
            http.Response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
            http.Response.Headers.AccessControlAllowHeaders = "*";
            http.Response.Headers.AccessControlMaxAge = "86400";
            return Results.NoContent();
        });
    }

    private static bool WantsGeoJson(HttpContext http)
    {
        var accept = http.Request.Headers.Accept.ToString();
        return accept.Contains("geo+json", StringComparison.OrdinalIgnoreCase);
    }

    // El API es de solo lectura, sin credenciales: CORS abierto para consumo desde navegador.
    private static void AllowAnyOrigin(HttpContext http)
    {
        http.Response.Headers.AccessControlAllowOrigin = "*";
        // Sin esto, el navegador oculta la cabecera Link al JS cross-origin y no podría
        // paginar siguiendo rel="next" (solo le quedaría el next_cursor del body).
        http.Response.Headers.AccessControlExposeHeaders = "Link";
    }

    private static string BaseUrl(HttpContext http)
        => $"{http.Request.Scheme}://{http.Request.Host}";

    // Cursor opaco = base64url(created_at "O" | id). El cliente solo lo reenvía.
    private static string EncodeCursor(DateTime createdAt, string id)
    {
        var raw = $"{createdAt.ToUniversalTime():O}|{id}";
        return Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string cursor, out DateTime? createdAt, out Guid? id)
    {
        createdAt = null;
        id = null;
        try
        {
            var raw = Encoding.UTF8.GetString(Base64UrlDecode(cursor));
            var sep = raw.IndexOf('|');
            if (sep <= 0) return false;

            if (!DateTime.TryParse(raw[..sep], null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return false;
            if (!Guid.TryParse(raw[(sep + 1)..], out var guid))
                return false;

            createdAt = dt.ToUniversalTime();
            id = guid;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    // El YAML se embebe como recurso para no depender del middleware de archivos estáticos.
    private static readonly Lazy<string> OpenApiSpec = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("v1.openapi.yaml", StringComparison.OrdinalIgnoreCase));
        if (name == null) return "openapi: 3.1.0\ninfo:\n  title: PULSO Open Data API\n  version: '1'\n";
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    // Swagger UI (vía CDN, versión mayor fijada): a diferencia de Scalar, muestra un
    // selector de media type en la respuesta, así se ven los ejemplos de application/json
    // y application/geo+json, y trae "Try it out" para probar el endpoint.
    private const string DocsHtml = """
        <!doctype html>
        <html>
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>PULSO Open Data API</title>
            <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css" />
          </head>
          <body>
            <div id="swagger-ui"></div>
            <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
            <script>
              window.ui = SwaggerUIBundle({
                url: '/api/v1/public/openapi.yaml',
                dom_id: '#swagger-ui'
              });
            </script>
          </body>
        </html>
        """;
}
