using System.Runtime.CompilerServices;
using Pulso.AiWorker.Models;
using Pulso.Shared;

namespace Pulso.AiWorker.Services;

/// <summary>
/// Auto-registra los enums dinámicos de este modelo al cargar el ensamblado, antes de
/// cualquier llamada al cliente LLM, tomados de la taxonomía compartida.
/// </summary>
internal static class TriageResultSchemaRegistration
{
    // Vocabulario de pet_report_type: no viene de IncidentTaxonomy porque no es una
    // categoría ni una severidad, es una señal propia de LOST_FOUND_PET. Internal
    // (no private) para que GeminiTriageService.SanitizeTaxonomy lo reutilice.
    internal static readonly string[] PetReportTypes = { "LOST", "FOUND" };

    [ModuleInitializer]
    internal static void Register()
    {
        SchemaEnumRegistry.Register<TriageResult>(new Dictionary<string, string[]>
        {
            ["severity"] = IncidentTaxonomy.Severities,
            ["category"] = IncidentTaxonomy.Categories,
            ["pet_report_type"] = PetReportTypes
        });
    }
}
