// Cliente HTTP de la API de Ingesta/Consulta (Pulso.IngressApi).
// Por defecto usa rutas RELATIVAS (mismo origen): en producción el reverse proxy
// (Caddy) enruta `/api/*` al backend, y en desarrollo lo hace el proxy de Vite.
// Esto elimina la necesidad de CORS. Se puede sobreescribir con VITE_API_BASE_URL.
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

// Situaciones livianas (sin raw_text). `since` (ISO) trae solo el delta; `limit` topa filas.
export async function fetchSituations({ since = null, limit = 500, date = null } = {}) {
  const params = new URLSearchParams();
  if (since) params.set('since', since);
  if (limit) params.set('limit', String(limit));
  if (date) params.set('date', date);
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations?${params.toString()}`);
  if (!res.ok) throw new Error('No se pudieron cargar las situaciones.');
  return res.json();
}

// Detalle pesado (raw_text) de un incidente, bajo demanda al abrir el popup.
export async function fetchSituationDetail(id) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations/${encodeURIComponent(id)}`);
  if (!res.ok) throw new Error('No se pudo cargar el detalle del incidente.');
  return res.json();
}

// Totales agregados para las tarjetas del dashboard.
export async function fetchSummary() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/summary`);
  if (!res.ok) throw new Error('No se pudo cargar el resumen.');
  return res.json();
}

export async function fetchSectorStats(date = null) {
  const url = date ? `${API_BASE_URL}/api/v1/pulso/locations/stats?date=${encodeURIComponent(date)}` : `${API_BASE_URL}/api/v1/pulso/locations/stats`;
  const res = await fetch(url);
  if (!res.ok) throw new Error('No se pudieron cargar las estadísticas por sector.');
  return res.json();
}

export async function fetchSystemMetrics() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/metrics`);
  if (!res.ok) throw new Error('No se pudieron cargar las métricas del sistema.');
  return res.json();
}


export async function sendToApi(incident) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/ingest`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(incident)
  });
  if (!res.ok) throw new Error('Fallo la ingesta en la API.');
  return res.json();
}
