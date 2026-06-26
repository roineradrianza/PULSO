// Orquestación de carga de datos de situación, sectores, resumen y métricas.
// Estrategia: carga inicial completa, luego cargas incrementales (delta) que solo
// traen incidentes nuevos desde el watermark. Persiste un snapshot en IndexedDB
// para ver el panorama sin conexión.
import { fetchSituations, fetchSectorStats, fetchSummary, fetchSystemMetrics } from './api.js';
import { situations, sectorStats, summary, systemMetrics, showToast } from './stores.js';
import { saveSnapshot, getSnapshot } from './db.js';

let watermark = null;          // mayor created_at visto (ISO 8601)
const byId = new Map();        // id -> situación (estado fusionado en memoria)
let lastStats = [];
let lastSummary = null;
let currentQueryDate = null;   // fecha de consulta activa (formato YYYY-MM-DD o null)

// Indicador de degradación del servidor para las cargas en segundo plano.
// Mantenemos en pantalla lo ya cargado (cache) y avisamos UNA sola vez al fallar
// (429/502/…), sin spamear en cada reintento (poll de 60s + reconexión SSE).
let serverDegraded = false;
function noteServerIssue() {
  if (!serverDegraded) {
    serverDegraded = true;
    showToast('Reconectando con el servidor…');
  }
}
function noteServerOk() {
  serverDegraded = false;
}

function publish() {
  // Ordenar por created_at DESC para consistencia con el backend.
  const merged = Array.from(byId.values()).sort((a, b) => (a.created_at < b.created_at ? 1 : -1));
  situations.set(merged);
  return merged;
}

function track(item) {
  if (!watermark || item.created_at > watermark) watermark = item.created_at;
}

// Refresca los agregados livianos (sectores, resumen, métricas) en paralelo.
async function refreshAggregates(date = null) {
  const [stats, summ, metrics] = await Promise.all([
    fetchSectorStats(date),
    fetchSummary(),
    fetchSystemMetrics()
  ]);
  lastStats = stats;
  lastSummary = summ;
  sectorStats.set(stats);
  summary.set(summ);
  systemMetrics.set(metrics);
}

// Guarda el panorama actual para consulta offline (best-effort, no bloquea).
function persistSnapshot(merged) {
  saveSnapshot({ situations: merged, sectorStats: lastStats, summary: lastSummary }).catch(() => {});
}

// Carga inicial completa: reemplaza el estado en memoria.
export async function loadInitial(date = null) {
  if (!navigator.onLine) return 0;
  try {
    currentQueryDate = date;
    const [sits] = await Promise.all([
      fetchSituations({ since: null, limit: 500, date }),
      refreshAggregates(date)
    ]);

    byId.clear();
    watermark = null;
    for (const it of sits) {
      byId.set(it.id, it);
      track(it);
    }
    persistSnapshot(publish());
    noteServerOk();
    return sits.length;
  } catch (err) {
    console.error('Error en la carga inicial de datos:', err);
    noteServerIssue();
    return 0;
  }
}

// Carga incremental: solo el delta de incidentes + refresco de agregados.
// Devuelve la cantidad de incidentes NUEVOS para el feedback al usuario.
export async function loadDelta() {
  if (!navigator.onLine) return 0;
  try {
    const [delta] = await Promise.all([
      fetchSituations({ since: watermark, limit: 500, date: currentQueryDate }),
      refreshAggregates(currentQueryDate)
    ]);

    let newCount = 0;
    for (const it of delta) {
      if (!byId.has(it.id)) newCount++;
      byId.set(it.id, it);
      track(it);
    }
    if (delta.length > 0) {
      persistSnapshot(publish());
    } else {
      // Sin incidentes nuevos, pero los agregados pueden haber cambiado: re-guardar liviano.
      persistSnapshot(Array.from(byId.values()));
    }
    noteServerOk();
    return newCount;
  } catch (err) {
    console.error('Error en la carga incremental de datos:', err);
    noteServerIssue();
    return 0;
  }
}

// Carga el último snapshot guardado para mostrar el mapa sin conexión.
// Devuelve el timestamp (ms) en que se guardó, o null si no hay snapshot.
export async function loadFromCache() {
  try {
    const snap = await getSnapshot();
    if (!snap) return null;

    byId.clear();
    watermark = null;
    for (const it of snap.situations ?? []) {
      byId.set(it.id, it);
      track(it);
    }
    publish();
    if (snap.sectorStats) {
      lastStats = snap.sectorStats;
      sectorStats.set(snap.sectorStats);
    }
    if (snap.summary) {
      lastSummary = snap.summary;
      summary.set(snap.summary);
    }
    return snap.savedAt ?? null;
  } catch (err) {
    console.error('Error al cargar el snapshot offline:', err);
    return null;
  }
}

// Alias de compatibilidad para llamadores existentes (SyncCard, ReportForm).
export { loadDelta as loadSituationsAndStats };
