// Estado global reactivo de la aplicación (Svelte stores).
import { writable } from 'svelte/store';

// Estado de conexión de red.
export const online = writable(typeof navigator !== 'undefined' ? navigator.onLine : true);

// Calcular la fecha de hoy en America/Caracas para la inicialización
function getTodayInVet() {
  const d = new Date();
  const formatter = new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'America/Caracas',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  });
  return formatter.format(d);
}

// Filtro de fecha seleccionado
export const selectedDate = writable(getTodayInVet());

// Datos cargados desde la API.
export const situations = writable([]);
export const sectorStats = writable([]);
export const systemMetrics = writable(null);

// Totales agregados del servidor (independientes del subconjunto cargado en el mapa).
export const summary = writable(null);

// Cantidad de reportes en la cola offline.
export const pendingCount = writable(0);

// Solicitud de enfoque del mapa en unas coordenadas { lat, lng, zoom }.
export const mapFocus = writable(null);

// --- Notificaciones (toast) ---
// type: 'success' (verde) | 'error' (rojo) | 'info' (azul, avisos neutrales).
export const toast = writable({ message: '', type: 'success', show: false });

let toastTimer;
function emitToast(message, type) {
  clearTimeout(toastTimer);
  toast.set({ message, type, show: true });
  toastTimer = setTimeout(() => {
    toast.update((t) => ({ ...t, show: false }));
  }, 4000);
}

// Compatibilidad: showToast(msg) = éxito; showToast(msg, true) = error.
export function showToast(message, isError = false) {
  emitToast(message, isError ? 'error' : 'success');
}

// Aviso neutral (ni éxito ni error): p. ej. "Reconectando con el servidor…".
export function showInfo(message) {
  emitToast(message, 'info');
}
