// Cliente HTTP de la API de Ingesta/Consulta (Pulso.IngressApi).
// Por defecto usa rutas RELATIVAS (mismo origen): en producción el reverse proxy
// (Caddy) enruta `/api/*` al backend, y en desarrollo lo hace el proxy de Vite.
// Esto elimina la necesidad de CORS. Se puede sobreescribir con VITE_API_BASE_URL.
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

export async function fetchSituations() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/situations`);
  if (!res.ok) throw new Error('No se pudieron cargar las situaciones.');
  return res.json();
}

export async function fetchSectorStats() {
  const res = await fetch(`${API_BASE_URL}/api/v1/pulso/locations/stats`);
  if (!res.ok) throw new Error('No se pudieron cargar las estadísticas por sector.');
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
