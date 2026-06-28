using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Pulso.AiWorker.Models;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Implementación de <see cref="ILlmStructuredClient"/> sobre Google Gemini (Structured
/// Outputs: responseMimeType=application/json + responseSchema). Encapsula la URL, la
/// autenticación (x-goog-api-key), la resolución del modelo y el parseo de la respuesta,
/// para que los consumidores solo aporten prompt y el tipo genérico C#.
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

    public async Task<T?> GenerateStructuredAsync<T>(
        string systemInstruction,
        object userPrompt,
        string? modelName,
        CancellationToken cancellationToken) where T : class
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

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Configuración para inyectar descripciones de los atributos [Description] en el JSON Schema
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TransformSchemaNode = (context, schema) =>
            {
                var attributeProvider = context.PropertyInfo is not null 
                    ? context.PropertyInfo.AttributeProvider 
                    : context.TypeInfo.Type;

                var descriptionAttr = attributeProvider?
                    .GetCustomAttributes(inherit: true)
                    .OfType<DescriptionAttribute>()
                    .FirstOrDefault();

                if (descriptionAttr != null && schema is JsonObject jObj)
                {
                    jObj["description"] = descriptionAttr.Description;
                }

                return schema;
            }
        };

        var responseSchema = options.GetJsonSchemaAsNode(typeof(T), exporterOptions);

        // Inyectar restricciones de enumerados (enum) en tiempo de ejecución para TriageResult
        if (typeof(T) == typeof(TriageResult))
        {
            var properties = responseSchema?["properties"]?.AsObject();
            if (properties != null)
            {
                if (properties.TryGetPropertyValue("severity", out var severityNode) && severityNode is JsonObject severityObj)
                {
                    severityObj["enum"] = new JsonArray { "LOW", "MEDIUM", "HIGH", "CRITICAL" };
                }
                if (properties.TryGetPropertyValue("category", out var categoryNode) && categoryNode is JsonObject categoryObj)
                {
                    categoryObj["enum"] = new JsonArray { "SEARCH_AND_RESCUE", "FIRE_HAZARD", "MEDICAL_EMERGENCY", "WATER_FOOD_SHORTAGE", "INFRASTRUCTURE_DAMAGE" };
                }
            }
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents          = new[] { new { parts } },
            generationConfig  = new { responseMimeType = "application/json", responseSchema }
        };

        var jsonRequest = JsonSerializer.Serialize(body, options);

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

            var candidateText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrEmpty(candidateText))
                return null;

            return JsonSerializer.Deserialize<T>(candidateText, options);
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
