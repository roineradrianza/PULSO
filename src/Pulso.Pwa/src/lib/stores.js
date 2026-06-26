// Estado global reactivo de la aplicación (Svelte stores).
import { writable } from 'svelte/store';

// Estado de conexión de red.
export const online = writable(typeof navigator !== 'undefined' ? navigator.onLine : true);

// Datos cargados desde la API.
export const situations = writable([]);
export const sectorStats = writable([]);
export const systemMetrics = writable(null);

// Cantidad de reportes en la cola offline.
export const pendingCount = writable(0);

// Solicitud de enfoque del mapa en unas coordenadas { lat, lng, zoom }.
export const mapFocus = writable(null);

// --- Notificaciones (toast) ---
export const toast = writable({ message: '', isError: false, show: false });

let toastTimer;
export function showToast(message, isError = false) {
  clearTimeout(toastTimer);
  toast.set({ message, isError, show: true });
  toastTimer = setTimeout(() => {
    toast.update((t) => ({ ...t, show: false }));
  }, 4000);
}
