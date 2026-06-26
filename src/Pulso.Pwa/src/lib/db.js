// Persistencia offline de reportes encolados usando Dexie (wrapper de IndexedDB).
// Reemplaza el manejo manual de IndexedDB con callbacks por una API async/await.
import Dexie from 'dexie';

export const db = new Dexie('pulso_offline_db');

// v1: cola de reportes offline (clave: message_id).
db.version(1).stores({
  incidents_queue: 'message_id'
});

// v2: snapshot del panorama para ver el mapa sin conexión (clave: key).
db.version(2).stores({
  incidents_queue: 'message_id',
  snapshot: 'key'
});

export function saveIncidentLocal(incident) {
  return db.incidents_queue.put(incident);
}

export function getQueuedIncidents() {
  return db.incidents_queue.toArray();
}

export function removeQueuedIncident(messageId) {
  return db.incidents_queue.delete(messageId);
}

export function countQueued() {
  return db.incidents_queue.count();
}

// --- Snapshot offline del panorama (situaciones + sectores + resumen) ---
const SNAPSHOT_KEY = 'latest';

export function saveSnapshot(snapshot) {
  return db.snapshot.put({ key: SNAPSHOT_KEY, ...snapshot, savedAt: Date.now() });
}

export function getSnapshot() {
  return db.snapshot.get(SNAPSHOT_KEY);
}
