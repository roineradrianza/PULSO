using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Pulso.AiWorker.Models;

public record TriageResult(
    [property: JsonPropertyName("severity")]
    [property: Description("Nivel de gravedad del reporte de emergencia (LOW, MEDIUM, HIGH, CRITICAL).")]
    string Severity,

    [property: JsonPropertyName("category")]
    [property: Description("Categoría del reporte de emergencia (SEARCH_AND_RESCUE, FIRE_HAZARD, MEDICAL_EMERGENCY, WATER_FOOD_SHORTAGE, INFRASTRUCTURE_DAMAGE).")]
    string Category,

    [property: JsonPropertyName("tags")]
    [property: Description("Lista de palabras clave relevantes identificadas en el reporte.")]
    string[] Tags,

    [property: JsonPropertyName("extracted_address")]
    [property: Description("Calles, avenidas, urbanizaciones o estados mencionados.")]
    string ExtractedAddress,

    [property: JsonPropertyName("affected_people")]
    [property: Description("Cantidad aproximada de heridos o atrapados.")]
    int AffectedPeople,

    [property: JsonPropertyName("transcription")]
    [property: Description("Transcripción completa si es audio, de lo contrario cadena vacía.")]
    string? Transcription,

    [property: JsonPropertyName("sector")]
    [property: Description("Nombre normalizado del sector, urbanización o barrio (ej. Altamira, Petare, Catia, La Isabelica). Si no se menciona, cadena vacía.")]
    string? Sector,

    [property: JsonPropertyName("is_person_found")]
    [property: Description("Establecer en true si el reporte indica que una persona perdida o afectada fue encontrada o está a salvo.")]
    bool? IsPersonFound,

    [property: JsonPropertyName("found_person_name")]
    [property: Description("Nombre completo de la persona encontrada (si aplica, de lo contrario cadena vacía).")]
    string? FoundPersonName,

    [property: JsonPropertyName("found_person_document")]
    [property: Description("Número de cédula o documento de la persona encontrada (si aplica, solo dígitos, de lo contrario cadena vacía).")]
    string? FoundPersonDocument,

    // Nombre de la persona EN PELIGRO (atrapada, desaparecida, herida o buscada). Es
    // independiente de found_person_name, que es para la persona reportada a SALVO.
    [property: JsonPropertyName("affected_person_name")]
    [property: Description("Nombre completo de la persona EN PELIGRO: atrapada, desaparecida, herida o que se está buscando (NO la que está a salvo). Ej.: 'hay una persona atrapada llamada María Alejandra' -> 'María Alejandra'. Si no se menciona ningún nombre así, cadena vacía.")]
    string? AffectedPersonName = null,

    [property: JsonPropertyName("city")]
    [property: Description("Ciudad o municipio del incidente (ej. Caracas, Valencia, Maracaibo, San Cristóbal, Barquisimeto). PLATAFORMA NACIONAL: no asumas Caracas; infiere la ciudad del texto o del sector mencionado. Si no se puede determinar, cadena vacía.")]
    string? City = null,

    // Resumen objetivo de lo reportado o de lo observado en la imagen/audio. Se usa
    // como texto del incidente cuando el ciudadano no escribió nada (solo media).
    [property: JsonPropertyName("description")]
    [property: Description("Resumen objetivo y breve (1-2 frases, en español) de lo que reporta el ciudadano o de lo que se observa en la imagen (daños, derrumbes, incendios, inundaciones, heridos). Útil como descripción del incidente cuando no hay texto escrito.")]
    string? Description = null,

    // false si el mensaje NO es un reporte real de emergencia (saludo, pregunta,
    // prueba, spam). En ese caso el worker pide aclaración y no crea incidente.
    [property: JsonPropertyName("is_actionable_report")]
    [property: Description("true si el mensaje describe CUALQUIER reporte concreto, aunque sea breve: daños, incendios, inundaciones o derrumbes; PERSONAS (desaparecidas, atrapadas, heridas, encontradas, a salvo o avistadas/identificadas en un lugar); o necesidades (agua, comida, medicinas, insumos). false SOLO si es un saludo, una pregunta general, una prueba o spam, sin ningún hecho ni lugar concreto.")]
    bool? IsActionableReport = null,

    [property: JsonPropertyName("triage_provider")]
    [property: Description("El motor de triaje utilizado.")]
    string TriageProvider = "gemini"
);
