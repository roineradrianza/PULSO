<script>
  import { createEventDispatcher } from 'svelte';
  import { pendingCount, showToast } from '../lib/stores.js';
  import { getQueuedIncidents, removeQueuedIncident } from '../lib/db.js';
  import { sendToApi } from '../lib/api.js';
  import { loadSituationsAndStats } from '../lib/data.js';

  const dispatch = createEventDispatcher();
  let syncing = false;

  async function syncQueue() {
    const list = await getQueuedIncidents();
    if (list.length === 0) return;

    syncing = true;
    let successCount = 0;
    let failCount = 0;

    for (const incident of list) {
      try {
        await sendToApi(incident);
        await removeQueuedIncident(incident.message_id);
        successCount++;
      } catch (error) {
        console.error('Error al sincronizar reporte:', incident.message_id, error);
        failCount++;
      }
    }

    syncing = false;
    dispatch('synced');

    if (failCount === 0) {
      showToast(`¡Listo! Se enviaron con éxito ${successCount} reportes.`);
      setTimeout(loadSituationsAndStats, 3000);
    } else {
      showToast(`Se enviaron ${successCount} reportes, pero ${failCount} fallaron. Intente de nuevo en una zona con mejor señal.`, true);
    }
  }
</script>

{#if $pendingCount > 0}
  <div class="card sync-card">
    <div class="sync-info">
      <div class="sync-title">Reportes guardados en su teléfono</div>
      <div class="sync-count">
        Tiene {$pendingCount} reporte{$pendingCount > 1 ? 's' : ''} que no se pudo{$pendingCount > 1 ? 'n' : ''} enviar por falta de señal. Toque el botón para enviarlos ahora.
      </div>
    </div>
    <button
      class="btn-primary"
      style="padding: 10px 16px; font-size: 13px;"
      on:click={syncQueue}
      disabled={syncing}
    >
      {#if syncing}
        <span class="spinner"></span> Enviando...
      {:else}
        Enviar reportes pendientes
      {/if}
    </button>
  </div>
{/if}
