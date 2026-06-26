using System.Text.Json;

namespace Pulso.AiWorker.Infrastructure;

/// <summary>
/// Utilidades estáticas para enmascarar PII en logs.
/// Ninguna información sensible (teléfono, coordenadas) debe
/// aparecer en texto claro en los registros de la aplicación.
/// </summary>
public static class PiiMasking
{
    /// <summary>
    /// Enmascara un número de teléfono dejando visibles solo los primeros
    /// dígitos y los últimos cuatro.  Ej: +58412123**** o ****
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return string.Empty;
        if (phone.Length <= 4) return "****";
        return phone.Substring(0, Math.Min(phone.Length - 4, 8)) + "****" + phone.Substring(phone.Length - 4);
    }

    /// <summary>
    /// Serializa el JSON del payload enmascarando los campos sensibles
    /// antes de emitirlos al log.
    /// </summary>
    public static string MaskPayloadJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("phone"))
                {
                    dict[prop.Name] = MaskPhone(prop.Value.GetString());
                }
                else if (prop.NameEquals("latitude") || prop.NameEquals("longitude"))
                {
                    dict[prop.Name] = "[REDACTED]";
                }
                else
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True   => true,
                        JsonValueKind.False  => false,
                        JsonValueKind.Null   => null,
                        _                   => prop.Value.ToString()
                    };
                }
            }
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return "[Invalid Payload - Masking Failed]";
        }
    }
}
