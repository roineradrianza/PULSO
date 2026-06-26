// Captura de geolocalización por hardware (chip GPS de alta precisión).
export function captureHardwareLocation() {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) {
      reject(new Error('Tu navegador no soporta geolocalización.'));
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (position) => resolve(position.coords),
      (error) => reject(error),
      {
        enableHighAccuracy: true, // Forzar uso de chip GPS de alta precisión
        timeout: 15000,           // Esperar máximo 15 segundos
        maximumAge: 0             // No utilizar coordenadas en caché
      }
    );
  });
}
