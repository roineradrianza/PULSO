// Orquestación de carga de datos de situación y estadísticas.
import { fetchSituations, fetchSectorStats } from './api.js';
import { situations, sectorStats } from './stores.js';

export async function loadSituationsAndStats() {
  if (!navigator.onLine) return;

  try {
    // Fetch paralelo para velocidad.
    const [sit, stats] = await Promise.all([fetchSituations(), fetchSectorStats()]);
    situations.set(sit);
    sectorStats.set(stats);
  } catch (err) {
    console.error('Error al cargar datos de situación:', err);
  }
}
