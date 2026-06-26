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
  let gpsStatus = 'Sin datos';
  let gpsStatusColor = 'var(--accent-orange)';
  let gpsLoading = false;

  async function captureGps() {
    gpsStatus = 'Obteniendo señal...';
    gpsStatusColor = 'var(--warning-amber)';
    gpsLoading = true;
    try {
      const coords = await captureHardwareLocation();
      lat = coords.latitude;
      lng = coords.longitude;
      accuracy = coords.accuracy;
      gpsStatus = 'Señal GPS Activa';
      gpsStatusColor = 'var(--success-green)';
      showToast('Ubicación capturada con éxito.');
    } catch (error) {
      gpsStatus = 'Error de GPS';
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
    gpsStatus = 'Sin datos';
    gpsStatusColor = 'var(--accent-orange)';
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
        console.warn('Fallo el envío directo, guardando offline:', error);
        await saveIncidentLocal(incident);
        dispatch('queued');
        showToast('Problema de red. Guardado en cola local.', true);
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
  <h2 style="font-size: 20px; font-weight: 700;">Informar Situación o Persona Localizada</h2>
  <p style="color: var(--text-muted); font-size: 13px; margin-top: -8px;">
    Cualquier ciudadano común puede reportar. Si no tienes conexión, tu reporte se encolará y podrás
    sincronizarlo después.
  </p>

  <form on:submit|preventDefault={submit}>
    <div class="form-group" style="margin-bottom: 12px;">
      <label for="phone">Tu Teléfono de Contacto</label>
      <input id="phone" type="tel" placeholder="Ej: 584121234567" required bind:value={phone} />
    </div>

    <div class="form-group" style="margin-bottom: 12px;">
      <label for="declared-location">Dirección escrita / Referencias del Lugar</label>
      <input
        id="declared-location"
        type="text"
        placeholder="Ej. Avenida Luis Roche de Altamira, frente a la plaza"
        required
        bind:value={declaredLocation}
      />
    </div>

    <div class="form-group" style="margin-bottom: 12px;">
      <label for="text-body">¿Qué está ocurriendo o a quién localizaste?</label>
      <textarea
        id="text-body"
        rows="4"
        placeholder="Describe los daños, escapes de gas, o detalla si localizaste a alguien (escribe su Nombre y Cédula si los tienes)..."
        required
        bind:value={reportDetails}
      ></textarea>
    </div>

    <div class="form-group" style="margin-bottom: 16px;">
      <label for="btn-gps">Ubicación de Hardware (GPS)</label>
      <div class="gps-display" style="margin-top: 4px; margin-bottom: 8px;">
        <div>Estado GPS: <span class="gps-status" style="color: {gpsStatusColor};">{gpsStatus}</span></div>
        <div>Latitud: <span>{lat !== null ? lat.toFixed(6) : '-'}</span></div>
        <div>Longitud: <span>{lng !== null ? lng.toFixed(6) : '-'}</span></div>
        <div>Precisión: <span>{accuracy !== null ? accuracy.toFixed(1) + ' m' : '-'}</span></div>
      </div>
      <button id="btn-gps" type="button" class="btn-secondary" style="padding: 10px;" on:click={captureGps} disabled={gpsLoading}>
        Capturar Ubicación Satelital
      </button>
    </div>

    <button type="submit" class="btn-primary" style="width: 100%;">Enviar Reporte a la Red</button>
  </form>
</div>
