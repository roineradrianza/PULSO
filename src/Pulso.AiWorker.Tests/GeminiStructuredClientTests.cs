using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pulso.AiWorker.Models;
using Pulso.AiWorker.Services;
using Xunit;

namespace Pulso.AiWorker.Tests;

// Cubre el límite que GeminiTriageServiceTests deja fuera a propósito: el parseo real
// de la respuesta HTTP de Gemini (usageMetadata, finishReason, content.parts[0].text)
// y que eso efectivamente llegue como atributos gen_ai.* al span. Usa un
// HttpMessageHandler de prueba, así que sigue sin red, costo ni flakiness.
public class GeminiStructuredClientTests
{
    private static GeminiStructuredClient CreateClient(HttpMessageHandler handler, string model = "gemini-3.1-flash-lite")
    {
        var settings = new Dictionary<string, string?> { ["GeminiApiKey"] = "test-key", ["GeminiModelName"] = model };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var httpClient = new HttpClient(handler);
        return new GeminiStructuredClient(new FakeHttpClientFactory(httpClient), configuration, NullLogger<GeminiStructuredClient>.Instance);
    }

    private static Activity? CaptureActivity(Func<Task> action)
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Worker.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured = activity
        };
        ActivitySource.AddActivityListener(listener);

        action().GetAwaiter().GetResult();
        return captured;
    }

    private static string BuildGeminiResponseBody(object structuredPayload, string finishReason, int inputTokens, int outputTokens)
    {
        var innerJson = JsonSerializer.Serialize(structuredPayload);
        return JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new { parts = new[] { new { text = innerJson } } },
                    finishReason
                }
            },
            usageMetadata = new { promptTokenCount = inputTokens, candidatesTokenCount = outputTokens }
        });
    }

    [Fact]
    public async Task GenerateStructuredAsync_ParsesRealGeminiResponseShape_AndRecordsGenAiAttributes()
    {
        var responseBody = BuildGeminiResponseBody(
            new
            {
                severity = "HIGH",
                category = "SEARCH_AND_RESCUE",
                tags = new[] { "landslide" },
                extracted_address = "Sector El Cardonal, La Guaira",
                affected_people = 3,
                transcription = "",
                sector = "La Guaira",
                is_person_found = false,
                found_person_name = "",
                found_person_document = "",
                affected_person_name = "",
                city = "La Guaira",
                description = "Posible derrumbe con personas afectadas.",
                is_actionable_report = true,
                triage_provider = "gemini"
            },
            finishReason: "STOP",
            inputTokens: 128,
            outputTokens: 42);

        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, responseBody));

        TriageResult? result = null;
        var activity = CaptureActivity(async () =>
            result = await client.GenerateStructuredAsync<TriageResult>("system", "hay un derrumbe", "gemini-3.1-flash-lite", CancellationToken.None));

        Assert.NotNull(result);
        Assert.Equal("HIGH", result!.Severity);
        Assert.Equal("SEARCH_AND_RESCUE", result.Category);
        Assert.Equal(3, result.AffectedPeople);

        Assert.NotNull(activity);
        Assert.Equal("gemini", activity!.GetTagItem("gen_ai.system"));
        Assert.Equal("gemini-3.1-flash-lite", activity.GetTagItem("gen_ai.request.model"));
        Assert.Equal(128, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(42, activity.GetTagItem("gen_ai.usage.output_tokens"));
        Assert.Equal(new[] { "STOP" }, activity.GetTagItem("gen_ai.response.finish_reasons"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task GenerateStructuredAsync_HttpErrorResponse_RecordsErrorStatus_AndReturnsNull()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, """{"error":"boom"}"""));

        TriageResult? result = null;
        var activity = CaptureActivity(async () =>
            result = await client.GenerateStructuredAsync<TriageResult>("system", "hay un derrumbe", "gemini-3.1-flash-lite", CancellationToken.None));

        Assert.Null(result);
        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.Equal("HTTP 500", activity.StatusDescription);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
