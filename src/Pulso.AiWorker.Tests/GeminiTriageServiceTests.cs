using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pulso.AiWorker.Models;
using Pulso.AiWorker.Services;
using Pulso.Shared;
using Xunit;

namespace Pulso.AiWorker.Tests;

// Cubre el límite determinístico del pipeline de triage: el parseo/saneamiento de
// TriageResult antes de persistir (GeminiTriageService.SanitizeTaxonomy) y el
// simulador local de respaldo. NO llama a Gemini de verdad (sin red, sin costo, sin
// flakiness) — un golden-dataset que evalúe la CALIDAD de clasificación contra el
// modelo real es una iniciativa aparte, más cara y no determinística.
public class GeminiTriageServiceTests
{
    // Línea base válida: cada test parte de esto y solo cambia lo que quiere probar.
    private static TriageResult ValidResult(
        string severity = "HIGH",
        string category = "SEARCH_AND_RESCUE",
        string? petReportType = null,
        bool? isInappropriateContent = null) =>
        new(
            Severity: severity,
            Category: category,
            Tags: ["landslide"],
            ExtractedAddress: "Sector El Cardonal, La Guaira",
            AffectedPeople: 3,
            Transcription: "",
            Sector: "La Guaira",
            IsPersonFound: false,
            FoundPersonName: null,
            FoundPersonDocument: null,
            PetReportType: petReportType,
            IsInappropriateContent: isInappropriateContent);

    private static GeminiTriageService CreateService(ILlmStructuredClient llmClient, bool withApiKey)
    {
        var settings = withApiKey
            ? new Dictionary<string, string?> { ["GeminiApiKey"] = "test-key", ["GeminiModelName"] = "gemini-3.1-flash-lite" }
            : new Dictionary<string, string?>();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new GeminiTriageService(llmClient, configuration, NullLogger<GeminiTriageService>.Instance);
    }

    [Fact]
    public async Task TriageAsync_GeminiReturnsValidResult_PassesThroughUnchanged()
    {
        var client = new StubLlmClient(ValidResult());
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("hay un derrumbe", null, CancellationToken.None);

        Assert.Equal("HIGH", result.Severity);
        Assert.Equal("SEARCH_AND_RESCUE", result.Category);
        Assert.Equal("gemini", result.TriageProvider);
    }

    // Golden set de valores fuera de vocabulario que Gemini NO debería poder devolver
    // (el responseSchema lo fuerza), pero que SanitizeTaxonomy debe neutralizar igual:
    // defensa en profundidad, no confiar ciegamente en el proveedor externo.
    [Theory]
    [InlineData("URGENTISH")]          // variante inventada/alucinada
    [InlineData("critical")]           // minúsculas (el enum real es en mayúsculas)
    [InlineData("")]                   // vacío
    [InlineData(" CRITICAL")]          // espacio espurio
    public async Task TriageAsync_GeminiReturnsOutOfVocabularySeverity_CoercesToMedium(string badSeverity)
    {
        var client = new StubLlmClient(ValidResult(severity: badSeverity));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte cualquiera", null, CancellationToken.None);

        Assert.Equal("MEDIUM", result.Severity);
        Assert.Contains(result.Severity, IncidentTaxonomy.Severities);
    }

    [Theory]
    [InlineData("ALIEN_INVASION")]
    [InlineData("search_and_rescue")]  // minúsculas
    public async Task TriageAsync_GeminiReturnsOutOfVocabularyCategory_ClearsCategory(string badCategory)
    {
        var client = new StubLlmClient(ValidResult(category: badCategory));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte cualquiera", null, CancellationToken.None);

        Assert.Equal(string.Empty, result.Category);
    }

    [Fact]
    public async Task TriageAsync_EmptyCategory_IsLeftEmpty_NotTreatedAsInvalid()
    {
        // Cadena vacía es la convención de "no determinado" en todo el modelo
        // (Sector, City, etc.) y no debe disparar el log/coerción de "valor inválido".
        var client = new StubLlmClient(ValidResult(category: ""));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte cualquiera", null, CancellationToken.None);

        Assert.Equal(string.Empty, result.Category);
    }

    [Fact]
    public async Task TriageAsync_NoApiKeyConfigured_FallsBackToSimulator_WithTaxonomyValidValues()
    {
        var client = new StubLlmClient(result: null); // no debería ni invocarse
        var service = CreateService(client, withApiKey: false);

        var result = await service.TriageAsync("se vino el cerro, hay gente atrapada", null, CancellationToken.None);

        Assert.Equal("fallback_local", result.TriageProvider);
        Assert.Contains(result.Severity, IncidentTaxonomy.Severities);
        Assert.Contains(result.Category, IncidentTaxonomy.Categories);
    }

    [Fact]
    public async Task TriageAsync_GeminiCallFails_FallsBackToSimulator_WithTaxonomyValidValues()
    {
        var client = new StubLlmClient(result: null); // simula una llamada fallida/vacía
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("posible daño estructural", null, CancellationToken.None);

        Assert.Equal("fallback_local", result.TriageProvider);
        Assert.Contains(result.Severity, IncidentTaxonomy.Severities);
        Assert.Contains(result.Category, IncidentTaxonomy.Categories);
    }

    // Un reporte de mascota nunca es una emergencia real: la severidad se fuerza a LOW
    // sin importar lo que devuelva el modelo (evita que texto emotivo dispare HIGH/CRITICAL).
    [Theory]
    [InlineData("HIGH")]
    [InlineData("CRITICAL")]
    public async Task TriageAsync_PetCategory_ForcesLowSeverity_RegardlessOfModelOutput(string modelSeverity)
    {
        var client = new StubLlmClient(ValidResult(severity: modelSeverity, category: "LOST_FOUND_PET"));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("se me perdió mi perro", null, CancellationToken.None);

        Assert.Equal("LOW", result.Severity);
        Assert.Equal("LOST_FOUND_PET", result.Category);
    }

    [Theory]
    [InlineData("MAYBE")]         // valor inventado
    [InlineData("lost")]          // minúsculas (el enum real es en mayúsculas)
    public async Task TriageAsync_PetReportTypeOutOfVocabulary_IsCleared(string badType)
    {
        var client = new StubLlmClient(ValidResult(category: "LOST_FOUND_PET", petReportType: badType));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte cualquiera", null, CancellationToken.None);

        Assert.Equal(string.Empty, result.PetReportType);
    }

    [Theory]
    [InlineData("LOST")]
    [InlineData("FOUND")]
    public async Task TriageAsync_PetReportTypeValid_PassesThrough(string validType)
    {
        var client = new StubLlmClient(ValidResult(category: "LOST_FOUND_PET", petReportType: validType));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte cualquiera", null, CancellationToken.None);

        Assert.Equal(validType, result.PetReportType);
    }

    [Fact]
    public async Task TriageAsync_IsInappropriateContentTrue_PassesThroughUnchanged()
    {
        // No es parte del saneamiento de taxonomía: solo confirma que SanitizeTaxonomy
        // no la descarta al reconstruir el resultado con "with".
        var client = new StubLlmClient(ValidResult(isInappropriateContent: true));
        var service = CreateService(client, withApiKey: true);

        var result = await service.TriageAsync("reporte con imagen", null, CancellationToken.None);

        Assert.True(result.IsInappropriateContent);
    }

    // Doble de prueba escrito a mano (sin librería de mocking): la interfaz es pequeña
    // y esto evita una dependencia extra solo para devolver un valor fijo.
    private sealed class StubLlmClient(object? result) : ILlmStructuredClient
    {
        public Task<T?> GenerateStructuredAsync<T>(
            string systemInstruction,
            object userPrompt,
            string? modelName,
            CancellationToken cancellationToken) where T : class
            => Task.FromResult(result as T);
    }
}
