namespace Pulso.AiWorker.Models;

/// <summary>
/// Coordenadas APROXIMADAS obtenidas al geocodificar una dirección/sector en texto.
/// Nunca representan GPS de hardware: el incidente se marca como ubicación aproximada.
/// </summary>
public sealed record GeoResult(double Latitude, double Longitude, string Provider);
