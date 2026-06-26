// Orquestación de carga de datos de situación y estadísticas.
import { fetchSituations, fetchSectorStats, fetchSystemMetrics } from './api.js';
import { situations, sectorStats, systemMetrics } from './stores.js';

export async function loadSituationsAndStats() {
  if (!navigator.onLine) return;

  try {
    // Fetch paralelo para velocidad.
    const [sit, stats, metrics] = await Promise.all([
      fetchSituations(),
      fetchSectorStats(),
      fetchSystemMetrics()
    ]);
    situations.set(sit);
    sectorStats.set(stats);
    systemMetrics.set(metrics);
  } catch (err) {
    console.error('Error al cargar datos de situación y métricas:', err);
  }
}
