using System.Net.Http.Headers;
using System.Text.Json;
using Pulso.AiWorker.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Resuelve y descarga el medio de un reporte (audio o imagen) desde el
/// proveedor correspondiente, con resolución autenticada, protección SSRF/OOM,
/// exclusión de video y compresión de imágenes.
/// </summary>
public sealed class MediaDownloadService : IMediaDownloadService
{
    private const long MaxSizeBytes        = 10 * 1024 * 1024; // 10 MB de descarga
    private const int  MaxImageDimension   = 1568;             // lado mayor tras compresión
    private const int  JpegQuality         = 80;

    /// <summary>
    /// Hosts permitidos para descargar media. Cualquier otro se rechaza (SSRF).
    /// </summary>
    private static readonly string[] AllowedDomains =
    [
        // WhatsApp / Meta (Cloud API)
        "graph.facebook.com",   // metadata de media
        "fbsbx.com",            // lookaside.fbsbx.com (URL temporal de descarga)
        "whatsapp.net",
        "fbcdn.net",
        "fbcdn.com",
        "cdninstagram.com",
        // Telegram
        "telegram.org"          // api.telegram.org (getFile y descarga)
    ];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MediaDownloadService> _logger;

    public MediaDownloadService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MediaDownloadService> logger)
    {
        _httpClient    = httpClientFactory.CreateClient(nameof(MediaDownloadService));
        _configuration = configuration;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task<MediaContent?> ResolveMediaAsync(PulsoPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(payload.MediaType))
            return null;

        // Política: el video no se procesa (costo elevado).
        if (payload.MediaType.StartsWith("video", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Video media skipped by policy (not processed).");
            return null;
        }

        var kind = payload.MediaType.Equals("audio", StringComparison.OrdinalIgnoreCase) ? MediaKind.Audio
                 : payload.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) ? MediaKind.Image
                 : (MediaKind?)null;

        if (kind is null)
        {
            _logger.LogInformation("Unsupported media type '{type}' skipped.", payload.MediaType);
            return null;
        }

        // Resolver URL de descarga (y posible token) según el canal de origen.
        var resolved = payload.Channel switch
        {
            "telegram" => await ResolveTelegramAsync(payload.MediaFileId, cancellationToken),
            "whatsapp" => await ResolveWhatsAppAsync(payload.MediaFileId, cancellationToken),
            _          => ResolveDirect(payload.MediaUrl)
        };

        if (resolved is null)
            return null;

        var bytes = await DownloadAsync(resolved.Url, resolved.AuthBearer, cancellationToken);
        if (bytes is null)
            return null;

        var mime = resolved.MimeType ?? InferMime(kind.Value, resolved.Url);

        // Defensa en profundidad: si el MIME resuelto resulta ser video, descartar.
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Resolved media MIME is video; skipped by policy.");
            return null;
        }

        // Nice-to-have: comprimir/redimensionar imágenes para reducir costo de tokens.
        if (kind.Value == MediaKind.Image)
            (bytes, mime) = CompressImage(bytes, mime);

        return new MediaContent(Convert.ToBase64String(bytes), mime, kind.Value);
    }

    // ── Resolución por proveedor ────────────────────────────────────────────

    private sealed record ResolvedMedia(string Url, string? AuthBearer, string? MimeType);

    /// <summary>Telegram: getFile(file_id) -> file_path -> URL de descarga (token en la ruta).</summary>
    private async Task<ResolvedMedia?> ResolveTelegramAsync(string? fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fileId))
            return null;

        var token = _configuration["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token) || token.Contains("TU_TELEGRAM"))
        {
            _logger.LogWarning("Telegram:BotToken no configurado; no se puede descargar media de Telegram.");
            return null;
        }

        var getFileUrl = $"https://api.telegram.org/bot{token}/getFile?file_id={Uri.EscapeDataString(fileId)}";
        try
        {
            using var resp = await _httpClient.GetAsync(getFileUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram getFile falló: {status}", resp.StatusCode);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<TelegramGetFileResponse>(
                stream, cancellationToken: cancellationToken);

            var filePath = parsed?.Result?.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return null;

            // El token va en la URL: nunca se loguea (solo se loguea el host).
            var downloadUrl = $"https://api.telegram.org/file/bot{token}/{filePath}";
            return new ResolvedMedia(downloadUrl, AuthBearer: null, MimeType: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolviendo media de Telegram.");
            return null;
        }
    }

    /// <summary>WhatsApp: GET graph.facebook.com/{ver}/{media-id} (Bearer) -> URL temporal + MIME.</summary>
    private async Task<ResolvedMedia?> ResolveWhatsAppAsync(string? mediaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(mediaId))
            return null;

        var token = _configuration["WhatsApp:AccessToken"];
        if (string.IsNullOrEmpty(token) || token.Contains("TU_WHATSAPP"))
        {
            _logger.LogWarning("WhatsApp:AccessToken no configurado; no se puede descargar media de WhatsApp.");
            return null;
        }

        var version = _configuration["WhatsApp:GraphApiVersion"] ?? "v21.0";
        var metadataUrl = $"https://graph.facebook.com/{version}/{Uri.EscapeDataString(mediaId)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _httpClient.SendAsync(request, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp media metadata falló: {status}", resp.StatusCode);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            var meta = await JsonSerializer.DeserializeAsync<WhatsAppMediaMetadata>(
                stream, cancellationToken: cancellationToken);

            if (meta is null || string.IsNullOrEmpty(meta.Url))
                return null;

            // La descarga del binario requiere el mismo Bearer token.
            return new ResolvedMedia(meta.Url, AuthBearer: token, MimeType: meta.MimeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolviendo media de WhatsApp.");
            return null;
        }
    }

    /// <summary>Canales con URL directa (ej. PWA): se descarga tal cual.</summary>
    private static ResolvedMedia? ResolveDirect(string? url)
        => string.IsNullOrEmpty(url) ? null : new ResolvedMedia(url, AuthBearer: null, MimeType: null);

    // ── Descarga con SSRF + OOM ──────────────────────────────────────────────

    private async Task<byte[]?> DownloadAsync(string url, string? authBearer, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Blocked media download: Invalid absolute URL or scheme.");
            return null;
        }

        if (!IsHostAllowed(uri.Host))
        {
            _logger.LogWarning("Blocked media download from untrusted host: {host}", uri.Host);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrEmpty(authBearer))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authBearer);

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxSizeBytes)
            {
                _logger.LogWarning(
                    "Rejected media: Content-Length {size} bytes exceeds limit of {max} bytes.",
                    contentLength.Value, MaxSizeBytes);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int bytesRead;
            long totalBytesRead = 0;

            while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > MaxSizeBytes)
                {
                    _logger.LogWarning("Media exceeds max size of {max} bytes during download; aborted.", MaxSizeBytes);
                    return null;
                }
                await ms.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            _logger.LogInformation("Media downloaded successfully from {host}. Size: {size} bytes.", uri.Host, ms.Length);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media. Proceeding with text only.");
            return null;
        }
    }

    // ── Compresión de imágenes (nice-to-have) ────────────────────────────────

    private (byte[] Bytes, string Mime) CompressImage(byte[] original, string fallbackMime)
    {
        try
        {
            using var inputStream = new MemoryStream(original);
            using var image = Image.Load(inputStream);

            // Redimensionar manteniendo proporción si excede el lado mayor permitido.
            if (image.Width > MaxImageDimension || image.Height > MaxImageDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxImageDimension, MaxImageDimension)
                }));
            }

            using var outputStream = new MemoryStream();
            image.Save(outputStream, new JpegEncoder { Quality = JpegQuality });
            var compressed = outputStream.ToArray();

            _logger.LogInformation(
                "Image compressed: {before} bytes -> {after} bytes.", original.Length, compressed.Length);
            return (compressed, "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image compression failed; using original bytes.");
            return (original, fallbackMime);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsHostAllowed(string host)
    {
        foreach (var domain in AllowedDomains)
        {
            if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string InferMime(MediaKind kind, string url)
    {
        if (kind == MediaKind.Image)
        {
            if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase))  return "image/png";
            if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return "image/webp";
            return "image/jpeg";
        }

        // Audio
        if (url.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return "audio/wav";
        if (url.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith(".oga", StringComparison.OrdinalIgnoreCase)) return "audio/ogg";
        if (url.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)) return "audio/aac";
        if (url.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) return "audio/m4a";
        if (url.EndsWith(".amr", StringComparison.OrdinalIgnoreCase)) return "audio/amr";
        return "audio/mpeg";
    }
}
