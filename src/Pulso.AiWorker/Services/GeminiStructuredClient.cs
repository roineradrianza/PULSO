using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Implementación de <see cref="ILlmStructuredClient"/> sobre Google Gemini (Structured
/// Outputs: responseMimeType=application/json + responseSchema). Encapsula la URL, la
/// autenticación (x-goog-api-key), la resolución del modelo y el parseo de la respuesta,
/// para que los consumidores solo aporten prompt y el tipo genérico C#.
/// </summary>
public sealed class GeminiStructuredClient : ILlmStructuredClient
{
    // Única fuente de verdad del modelo por defecto: evita que el literal se duplique
    // (y diverja) entre este cliente y sus consumidores (p. ej. GeminiTriageService).
    internal const string DefaultModelName = "gemini-3.1-flash-lite";

    // Bloqueo nativo de Gemini para las categorías de daño más graves, aplicado a
    // cualquier llamada (texto o imagen). Es un respaldo tosco: la señal principal de
    // moderación es el campo is_inappropriate_content del propio responseSchema.
    private static readonly object[] SafetySettings =
    {
        new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
        new { category = "HARM_CATEGORY_DANGEROUS_CONTENT",  threshold = "BLOCK_MEDIUM_AND_ABOVE" },
        new { category = "HARM_CATEGORY_HARASSMENT",         threshold = "BLOCK_MEDIUM_AND_ABOVE" },
        new { category = "HARM_CATEGORY_HATE_SPEECH",        threshold = "BLOCK_MEDIUM_AND_ABOVE" }
    };

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

        var model      = modelName ?? _configuration["GeminiModelName"] ?? DefaultModelName;
        var apiVersion = model.Contains("1.5") ? "v1" : "v1beta";
        var url        = $"https://generativelanguage.googleapis.com/{apiVersion}/models/{model}:generateContent";

        using var activity = GenAiTelemetry.StartCall("gemini", model);

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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        // Configuración para inyectar descripciones y normalizar tipos para Gemini
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TransformSchemaNode = (context, schema) =>
            {
                if (schema is JsonObject jObj)
                {
                    // Normalizar el tipo a un string único y en mayúsculas compatible con Gemini (Proto field no repetido)
                    if (jObj.TryGetPropertyValue("type", out var typeNode))
                    {
                        if (typeNode is JsonArray typeArray)
                        {
                            var primaryType = typeArray
                                .Select(n => n?.GetValue<string>())
                                .FirstOrDefault(t => t != "null" && t != null);

                            if (primaryType != null)
                            {
                                jObj["type"] = JsonValue.Create(primaryType.ToUpperInvariant());
                            }
                        }
                        else if (typeNode is JsonValue typeVal && typeVal.TryGetValue<string>(out var singleType))
                        {
                            jObj["type"] = JsonValue.Create(singleType.ToUpperInvariant());
                        }
                    }

                    // Inyectar descripciones de los atributos [Description]
                    var attributeProvider = context.PropertyInfo is not null 
                        ? context.PropertyInfo.AttributeProvider 
                        : context.TypeInfo.Type;

                    var descriptionAttr = attributeProvider?
                        .GetCustomAttributes(inherit: true)
                        .OfType<DescriptionAttribute>()
                        .FirstOrDefault();

                    if (descriptionAttr != null)
                    {
                        jObj["description"] = descriptionAttr.Description;
                    }
                }

                return schema;
            }
        };

        var responseSchema = options.GetJsonSchemaAsNode(typeof(T), exporterOptions);

        // Inyectar enums dinámicos declarados por el propio tipo T
        var enumOverrides = SchemaEnumRegistry.GetOverrides(typeof(T));
        if (enumOverrides != null)
        {
            var properties = responseSchema?["properties"]?.AsObject();
            if (properties != null)
            {
                foreach (var (propertyName, allowedValues) in enumOverrides)
                {
                    if (properties.TryGetPropertyValue(propertyName, out var propNode) && propNode is JsonObject propObj)
                    {
                        propObj["enum"] = new JsonArray(allowedValues.Select(v => (JsonNode)v).ToArray());
                    }
                }
            }
        }

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents          = new[] { new { parts } },
            generationConfig  = new { responseMimeType = "application/json", responseSchema },
            safetySettings = SafetySettings
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
                GenAiTelemetry.RecordError(activity, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            int? inputTokens = null, outputTokens = null;
            if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var pt)) inputTokens = pt.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ct)) outputTokens = ct.GetInt32();
            }

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                _logger.LogWarning("Cliente LLM (Gemini): la respuesta no incluyó candidates (posible bloqueo de seguridad).");
                GenAiTelemetry.RecordError(activity, "blocked: no candidates");
                return null;
            }

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
            GenAiTelemetry.RecordUsage(activity, inputTokens, outputTokens, finishReason);

            if (finishReason == "SAFETY")
            {
                _logger.LogWarning("Cliente LLM (Gemini): respuesta bloqueada por safetySettings (finishReason=SAFETY).");
                GenAiTelemetry.RecordError(activity, "blocked: SAFETY");
                return null;
            }

            var candidateText = candidate
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrEmpty(candidateText))
            {
                GenAiTelemetry.RecordError(activity, "Empty candidate text");
                return null;
            }

            return JsonSerializer.Deserialize<T>(candidateText, options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cliente LLM (Gemini): fallo en la generación estructurada.");
            GenAiTelemetry.RecordError(activity, ex.Message);
            return null;
        }
    }
}
