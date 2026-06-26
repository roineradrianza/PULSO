// Persistencia offline de reportes encolados usando Dexie (wrapper de IndexedDB).
// Reemplaza el manejo manual de IndexedDB con callbacks por una API async/await.
import Dexie from 'dexie';

export const db = new Dexie('pulso_offline_db');

// La clave primaria es message_id (idempotencia del reporte).
db.version(1).stores({
  incidents_queue: 'message_id'
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
