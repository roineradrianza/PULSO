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
    // Nombre de la persona EN PELIGRO (atrapada, desaparecida, herida o buscada). Es
    // independiente de found_person_name, que es para la persona reportada a SALVO.
    [property: JsonPropertyName("affected_person_name")] string? AffectedPersonName = null,
    [property: JsonPropertyName("city")] string? City = null,
    // Resumen objetivo de lo reportado o de lo observado en la imagen/audio. Se usa
    // como texto del incidente cuando el ciudadano no escribió nada (solo media).
    [property: JsonPropertyName("description")] string? Description = null,
    // false si el mensaje NO es un reporte real de emergencia (saludo, pregunta,
    // prueba, spam). En ese caso el worker pide aclaración y no crea incidente.
    [property: JsonPropertyName("is_actionable_report")] bool? IsActionableReport = null,
    [property: JsonPropertyName("triage_provider")] string TriageProvider = "gemini"
);
