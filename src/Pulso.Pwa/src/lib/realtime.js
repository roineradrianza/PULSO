// Cliente de tiempo real vía SSE. El servidor solo emite una SEÑAL ("incident")
// cuando hay un incidente nuevo; nosotros respondemos pidiendo el delta saneado.
// Reconexión automática y debounce para ráfagas. El polling de respaldo cubre los
// casos en que el SSE esté bloqueado por la red.
import { API_BASE_URL } from './api.js';
import { loadDelta } from './data.js';

let eventSource = null;
let reconnectTimer = null;
let debounceTimer = null;
let onNewCb = null;

function triggerDelta() {
  clearTimeout(debounceTimer);
  debounceTimer = setTimeout(async () => {
    const newCount = await loadDelta();
    if (newCount > 0 && onNewCb) onNewCb(newCount);
  }, 600);
}

function connect() {
  if (typeof EventSource === 'undefined') return;
  try {
    eventSource = new EventSource(`${API_BASE_URL}/api/v1/pulso/stream`);
    eventSource.addEventListener('incident', triggerDelta);
    eventSource.onerror = () => {
      // El navegador reintenta solo, pero forzamos un ciclo limpio con backoff.
      if (eventSource) {
        eventSource.close();
        eventSource = null;
      }
      clearTimeout(reconnectTimer);
      reconnectTimer = setTimeout(connect, 5000);
    };
  } catch {
    // Ignorar: el polling de respaldo mantiene los datos frescos.
  }
}

export function startRealtime(onNew) {
  onNewCb = onNew;
  connect();
}

export function stopRealtime() {
  clearTimeout(reconnectTimer);
  clearTimeout(debounceTimer);
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }
}
