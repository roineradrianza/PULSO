using System.Text.Json.Serialization;

namespace Pulso.AiWorker.Models;

public record TriageResult(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("tags")] string[] Tags,
    [property: JsonPropertyName("extracted_address")] string ExtractedAddress,
    [property: JsonPropertyName("affected_people")] int AffectedPeople,
    [property: JsonPropertyName("transcription")] string? Transcription,
    [property: JsonPropertyName("sector")] string? Sector,
    [property: JsonPropertyName("is_person_found")] bool? IsPersonFound,
    [property: JsonPropertyName("found_person_name")] string? FoundPersonName,
    [property: JsonPropertyName("found_person_document")] string? FoundPersonDocument,
    [property: JsonPropertyName("triage_provider")] string TriageProvider = "gemini"
);
