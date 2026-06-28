// Cliente HTTP de la API de Ingesta/Consulta (Pulso.IngressApi).
// Por defecto usa rutas RELATIVAS (mismo origen): en producción el reverse proxy
// (Caddy) enruta `/api/*` al backend, y en desarrollo lo hace el proxy de Vite.
// Esto elimina la necesidad de CORS. Se puede sobreescribir con VITE_API_BASE_URL.
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

// Error de API que conserva el código de estado HTTP (y Retry-After si viene), para
// que la UI pueda distinguir 429 (límite), 502/503/504 (servidor caído), 400 (datos
// inválidos), etc., y mostrar el mensaje correcto. Un fallo de red de fetch (sin
// respuesta) NO produce un ApiError: se propaga como TypeError, que la UI trata como
// "sin conexión".
export class ApiError extends Error {
  constructor(status, message, retryAfter = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.retryAfter = retryAfter;
  }
}

function ensureOk(res, fallbackMsg) {
  if (res.ok) return res;
  const retryAfterRaw = res.headers.get('Retry-After');
  const retryAfter = retryAfterRaw ? Number(retryAfterRaw) : null;
  throw new ApiError(res.status, fallbackMsg, Number.isFinite(retryAfter) ? retryAfter : null);
}

// Situaciones livianas (sin raw_text). `since` (ISO) trae solo el delta; `limit` topa filas.
export async function fetchSituations({ since = null, limit = 500, date = null } = {}) {
  const params = new URLSearchParams();
  if (since) params.set('since', since);
  if (limit) params.set('limit', String(limit));
  if (date) params.set('date', date);
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations?${params.toString()}`);
  ensureOk(res, 'No se pudieron cargar las situaciones.');
  return res.json();
}

// Detalle pesado (raw_text) de un incidente, bajo demanda al abrir el popup.
export async function fetchSituationDetail(id) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations/${encodeURIComponent(id)}`);
  ensureOk(res, 'No se pudo cargar el detalle del incidente.');
  return res.json();
}

// Totales agregados para las tarjetas del dashboard.
export async function fetchSummary() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/summary`);
  ensureOk(res, 'No se pudo cargar el resumen.');
  return res.json();
}

export async function fetchSectorStats(date = null) {
  const url = date ? `${API_BASE_URL}/api/v1/pulso/locations/stats?date=${encodeURIComponent(date)}` : `${API_BASE_URL}/api/v1/pulso/locations/stats`;
  const res = await fetch(url);
  ensureOk(res, 'No se pudieron cargar las estadísticas por sector.');
  return res.json();
}

export async function fetchSystemMetrics() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/metrics`);
  ensureOk(res, 'No se pudieron cargar las métricas del sistema.');
  return res.json();
}

export async function sendToApi(incident) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/ingest`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(incident)
  });
  if (!res.ok) {
    // Conservar el motivo específico del servidor (p. ej. coordenadas fuera de
    // Venezuela, texto muy largo) para poder mostrárselo al usuario en un 400.
    let serverMsg = null;
    try {
      const body = await res.json();
      serverMsg = typeof body?.error === 'string' ? body.error : null;
    } catch {
      // Sin cuerpo JSON (p. ej. 429 de rate limit): se usa el mensaje genérico.
    }
    const retryAfterRaw = res.headers.get('Retry-After');
    const retryAfter = retryAfterRaw ? Number(retryAfterRaw) : null;
    throw new ApiError(
      res.status,
      serverMsg ?? 'Falló la ingesta en la API.',
      Number.isFinite(retryAfter) ? retryAfter : null
    );
  }
  return res.json();
}

export async function fetchComments(incidentId) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations/${encodeURIComponent(incidentId)}/comments`);
  ensureOk(res, 'No se pudieron cargar los comentarios.');
  return res.json();
}

export async function sendComment(incidentId, rawText) {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations/${encodeURIComponent(incidentId)}/comments`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ rawText })
  });
  ensureOk(res, 'No se pudo enviar el comentario.');
  return res.json();
}

