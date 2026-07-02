using Pulso.AiWorker.Models;
using Pulso.AiWorker.Services;
using Pulso.Shared;
using Xunit;

namespace Pulso.AiWorker.Tests;

// Verifica que el auto-registro de enums dinámicos haya corrido antes de cualquier consulta.
public class SchemaEnumRegistryTests
{
    [Fact]
    public void TriageResult_IsAutoRegistered_WithSeverityAndCategoryEnums()
    {
        var overrides = SchemaEnumRegistry.GetOverrides(typeof(TriageResult));

        Assert.NotNull(overrides);
        Assert.Equal(IncidentTaxonomy.Severities, overrides!["severity"]);
        Assert.Equal(IncidentTaxonomy.Categories, overrides["category"]);
        Assert.Equal(new[] { "LOST", "FOUND" }, overrides["pet_report_type"]);
    }

    [Fact]
    public void UnregisteredType_ReturnsNull()
    {
        var overrides = SchemaEnumRegistry.GetOverrides(typeof(string));

        Assert.Null(overrides);
    }
}
