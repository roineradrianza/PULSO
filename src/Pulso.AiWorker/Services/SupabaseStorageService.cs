using System.Net.Http.Headers;
using Pulso.Shared;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Sube imágenes al bucket público de Supabase Storage, vía su API REST
/// autenticada con la service role key.
/// </summary>
public sealed class SupabaseStorageService : IMediaStorageService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SupabaseStorageService> logger)
    {
        _httpClient    = httpClientFactory.CreateClient(nameof(SupabaseStorageService));
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task<string?> UploadAsync(byte[] bytes, string mimeType, Guid incidentId, CancellationToken cancellationToken)
    {
        var supabaseUrl = _configuration["Supabase:Url"];
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
        var bucketName = _configuration["Supabase:BucketName"];

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(serviceRoleKey) || string.IsNullOrEmpty(bucketName))
        {
            _logger.LogWarning("Supabase Storage no configurado; se omite la subida de la foto.");
            return null;
        }

        var objectKey = IncidentMediaPaths.ObjectKey(incidentId);
        var uploadUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{bucketName}/{objectKey}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
            // Reintentos sobrescriben limpio en vez de fallar por conflicto de llave.
            request.Headers.Add("x-upsert", "true");

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            request.Content = content;

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Supabase Storage: respondió {status} al subir la foto del incidente {id} — {err}.",
                    response.StatusCode, incidentId, err);
                return null;
            }

            _logger.LogInformation("Foto subida a Supabase Storage para el incidente {id}.", incidentId);
            return IncidentMediaPaths.RelativeUrl(incidentId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error subiendo la foto del incidente {id} a Supabase Storage.", incidentId);
            return null;
        }
    }
}
