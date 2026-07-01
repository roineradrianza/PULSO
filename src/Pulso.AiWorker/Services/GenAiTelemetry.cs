using System.Diagnostics;

namespace Pulso.AiWorker.Services;

// Telemetría GenAI compartida (OpenTelemetry semantic conventions, aún experimentales)
// para cualquier cliente LLM: una sola fuente de verdad para el span/ActivitySource y
// las claves gen_ai.*, para que agregar un proveedor nuevo no duplique este código.
internal static class GenAiTelemetry
{
    private static readonly ActivitySource ActivitySource = new("Pulso.AiWorker");

    public static Activity? StartCall(string system, string model)
    {
        var activity = ActivitySource.StartActivity("llm-generate-structured");
        activity?.SetTag("gen_ai.system", system);
        activity?.SetTag("gen_ai.request.model", model);
        return activity;
    }

    public static void RecordUsage(Activity? activity, int? inputTokens, int? outputTokens, string? finishReason)
    {
        if (inputTokens.HasValue)
            activity?.SetTag("gen_ai.usage.input_tokens", inputTokens.Value);
        if (outputTokens.HasValue)
            activity?.SetTag("gen_ai.usage.output_tokens", outputTokens.Value);
        if (finishReason is not null)
            activity?.SetTag("gen_ai.response.finish_reasons", new[] { finishReason });
    }

    public static void RecordError(Activity? activity, string description)
        => activity?.SetStatus(ActivityStatusCode.Error, description);
}
