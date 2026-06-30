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
    [ModuleInitializer]
    internal static void Register()
    {
        SchemaEnumRegistry.Register<TriageResult>(new Dictionary<string, string[]>
        {
            ["severity"] = IncidentTaxonomy.Severities,
            ["category"] = IncidentTaxonomy.Categories
        });
    }
}
