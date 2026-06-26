<script>
  import { createEventDispatcher } from 'svelte';
  import { showToast } from '../lib/stores.js';
  import { saveIncidentLocal } from '../lib/db.js';
  import { sendToApi } from '../lib/api.js';
  import { captureHardwareLocation } from '../lib/geo.js';
  import { loadSituationsAndStats } from '../lib/data.js';

  const dispatch = createEventDispatcher();

  let phone = '584120000000';
  let declaredLocation = '';
  let reportDetails = '';

  let lat = null;
  let lng = null;
  let accuracy = null;
  let gpsStatus = '🔴 Ubicación no obtenida';
  let gpsStatusColor = 'var(--danger-red)';
  let gpsLoading = false;

  async function captureGps() {
    gpsStatus = '🟡 Buscando ubicación por satélite...';
    gpsStatusColor = 'var(--warning-amber)';
    gpsLoading = true;
    try {
      const coords = await captureHardwareLocation();
      lat = coords.latitude;
      lng = coords.longitude;
      accuracy = coords.accuracy;
      gpsStatus = '🟢 Ubicación detectada con éxito';
      gpsStatusColor = 'var(--success-green)';
      showToast('Ubicación capturada con éxito.');
    } catch (error) {
      gpsStatus = '🔴 No se pudo obtener la ubicación';
      gpsStatusColor = 'var(--danger-red)';
      showToast(`Error al leer GPS: ${error.message}`, true);
    } finally {
      gpsLoading = false;
    }
  }

  function resetForm() {
    declaredLocation = '';
    reportDetails = '';
    lat = null;
    lng = null;
    accuracy = null;
    gpsStatus = '🔴 Ubicación no obtenida';
    gpsStatusColor = 'var(--danger-red)';
  }

  async function submit() {
    const uuid = 'pwa-' + Date.now() + '-' + Math.random().toString(36).slice(2, 11);
    const combinedTextBody = `[Dirección Escrita]: ${declaredLocation}\n[Detalle de Situación]: ${reportDetails}`;

    const incident = {
      message_id: uuid,
      phone,
      channel: 'pwa',
      text_body: combinedTextBody,
      media_url: null,
      media_type: null,
      latitude: lat,
      longitude: lng
    };

    if (navigator.onLine) {
      try {
        showToast('Transmitiendo reporte...');
        await sendToApi(incident);
        showToast('Reporte enviado exitosamente.');
        resetForm();
        setTimeout(loadSituationsAndStats, 3000);
      } catch (error) {
        // Distinguir el tipo de fallo: no mentir al usuario ni reintentar en vano.
        const status = error?.status;

        if (status === 400) {
          // Datos inválidos: encolar fallaría siempre. Conservar el formulario para corregir.
          showToast('No pudimos enviar el reporte. Revisa los datos e inténtalo de nuevo.', true);
          return;
        }

        // Resto de casos: guardar local para que el reporte NUNCA se pierda.
        console.warn('Fallo el envío directo, guardando offline:', error);
        await saveIncidentLocal(incident);
        dispatch('queued');

        if (status === 429) {
          showToast('Estamos recibiendo muchos reportes ahora mismo. Guardamos el tuyo y lo reenviaremos en unos minutos.', true);
        } else if (status === 502 || status === 503 || status === 504) {
          showToast('El servidor no está disponible por un momento. Tu reporte quedó guardado y se enviará al restablecerse.', true);
        } else if (status === undefined) {
          // fetch lanzó sin respuesta: sin conexión / servidor inalcanzable.
          showToast('Sin conexión. Tu reporte quedó guardado en tu teléfono.', true);
        } else {
          showToast('Ocurrió un error en el servidor. Guardamos tu reporte para reintentar.', true);
        }
        resetForm();
      }
    } else {
      await saveIncidentLocal(incident);
      dispatch('queued');
      showToast('Portal offline. Reporte encolado localmente.');
      resetForm();
    }
  }
</script>

<div class="card" style="max-width: 600px; margin: 0 auto; width: 100%;">
  <h2 style="font-size: 20px; font-weight: 700;">Crear un Reporte de Ayuda</h2>
  <p style="color: var(--text-muted); font-size: 13px; margin-top: -8px;">
    Cualquier persona puede informar sobre una emergencia. Si no tiene señal o internet, el reporte se guardará en su teléfono de forma segura.
  </p>

  <form on:submit|preventDefault={submit}>
    <div class="form-group" style="margin-bottom: 12px;">
      <label for="phone">Su número de teléfono</label>
      <input id="phone" type="tel" placeholder="Ejemplo: 0412-1234567" required bind:value={phone} />
    </div>

    <div class="form-group" style="margin-bottom: 12px;">
      <label for="declared-location">¿Dónde ocurrió? (Dirección o puntos de referencia)</label>
      <input
        id="declared-location"
        type="text"
        placeholder="Ejemplo: Avenida Francisco de Miranda, frente al Metro de Altamira"
        required
        bind:value={declaredLocation}
      />
    </div>

    <div class="form-group" style="margin-bottom: 12px;">
      <label for="text-body">¿Qué pasó o a quién encontró?</label>
      <textarea
        id="text-body"
        rows="4"
        placeholder="Describa la situación: si hay daños materiales, escapes de agua o gas, heridos, o escriba el nombre completo y la cédula de la persona que encontró a salvo."
        required
        bind:value={reportDetails}
      ></textarea>
    </div>

    <div class="form-group" style="margin-bottom: 16px;">
      <label for="btn-gps">Ubicación por satélite (GPS)</label>
      <div class="gps-display" style="margin-top: 4px; margin-bottom: 8px; padding: 12px; background: rgba(255,255,255,0.03); border-radius: 8px; border: 1px solid var(--card-border);">
        <div style="font-weight: 600; display: flex; align-items: center; gap: 8px;">
          <span>Estado:</span>
          <span class="gps-status" style="color: {gpsStatusColor}; font-weight: 700;">{gpsStatus}</span>
        </div>
        {#if lat !== null && lng !== null}
          <div style="font-size: 12px; color: var(--text-muted); margin-top: 8px; display: flex; align-items: center; gap: 12px; line-height: 1;">
            <span style="display: flex; align-items: center; gap: 6px;">
              <span style="width: 8px; height: 8px; border-radius: 50%; background-color: var(--success-green); display: inline-block;"></span>
              Ubicación obtenida
            </span>
            <span style="color: rgba(255, 255, 255, 0.15);">|</span>
            <span>Precisión: {accuracy !== null ? accuracy.toFixed(0) + ' metros' : '-'}</span>
          </div>
        {/if}
      </div>
      <button id="btn-gps" type="button" class="btn-secondary" style="padding: 10px;" on:click={captureGps} disabled={gpsLoading}>
        Obtener mi ubicación actual
      </button>
    </div>

    <button type="submit" class="btn-primary" style="width: 100%;">Enviar reporte de emergencia</button>
  </form>
</div>
