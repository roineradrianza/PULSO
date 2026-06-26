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
      showToast(`Sincronización exitosa: ${successCount} reportes transmitidos.`);
      setTimeout(loadSituationsAndStats, 3000);
    } else {
      showToast(`Sincronizados ${successCount} reportes, ${failCount} fallaron.`, true);
    }
  }
</script>

{#if $pendingCount > 0}
  <div class="card sync-card">
    <div class="sync-info">
      <div class="sync-title">Reportes guardados localmente</div>
      <div class="sync-count">{$pendingCount} reportes encolados listos para transmitir</div>
    </div>
    <button
      class="btn-primary"
      style="padding: 10px 16px; font-size: 13px;"
      on:click={syncQueue}
      disabled={syncing}
    >
      {#if syncing}
        <span class="spinner"></span> Sincronizando...
      {:else}
        Sincronizar
      {/if}
    </button>
  </div>
{/if}
