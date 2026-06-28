using System.Text;
using System.Text.Json;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Implementación de <see cref="ILlmStructuredClient"/> sobre Google Gemini (Structured
/// Outputs: responseMimeType=application/json + responseSchema). Encapsula la URL, la
/// autenticación (x-goog-api-key), la resolución del modelo y el parseo de la respuesta,
/// para que los consumidores solo aporten prompt y esquema.
/// </summary>
public sealed class GeminiStructuredClient : ILlmStructuredClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiStructuredClient> _logger;

    public GeminiStructuredClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiStructuredClient> logger)
    {
        _httpClient    = httpClientFactory.CreateClient(nameof(GeminiStructuredClient));
        _configuration = configuration;
        _logger        = logger;
    }

    public async Task<string?> GenerateJsonAsync(
        string systemInstruction,
        object userPrompt,
        object responseSchema,
        string? modelName,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["GeminiApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("TU_API_KEY") || apiKey == "placeholder")
        {
            _logger.LogWarning("GeminiApiKey no configurado; el cliente LLM se omite.");
            return null;
        }

        var model      = modelName ?? _configuration["GeminiModelName"] ?? "gemini-2.0-flash";
        var apiVersion = model.Contains("1.5") ? "v1" : "v1beta";
        var url        = $"https://generativelanguage.googleapis.com/{apiVersion}/models/{model}:generateContent";

        object parts;
        if (userPrompt is string textPrompt)
        {
            parts = new[] { new { text = textPrompt } };
        }
        else if (userPrompt is System.Collections.IEnumerable enumerableParts)
        {
            var list = new List<object>();
            foreach (var part in enumerableParts)
            {
                list.Add(part);
            }
            parts = list.ToArray();
        }
        else
        {
            parts = new[] { new { text = userPrompt.ToString() } };
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents          = new[] { new { parts } },
            generationConfig  = new { responseMimeType = "application/json", responseSchema }
        };

        var jsonRequest = JsonSerializer.Serialize(
            body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Cliente LLM (Gemini): respondió {status} con modelo '{model}' — {err}.",
                    response.StatusCode, model, err);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cliente LLM (Gemini): fallo en la generación estructurada.");
            return null;
        }
    }
}
