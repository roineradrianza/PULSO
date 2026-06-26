<script>
  import { onMount, onDestroy } from 'svelte';
  import Header from './components/Header.svelte';
  import InfoDisclaimer from './components/InfoDisclaimer.svelte';
  import SyncCard from './components/SyncCard.svelte';
  import StatsDashboard from './components/StatsDashboard.svelte';
  import MetricsDashboard from './components/MetricsDashboard.svelte';
  import MapView from './components/MapView.svelte';
  import SectorsList from './components/SectorsList.svelte';
  import ReportForm from './components/ReportForm.svelte';
  import Toast from './components/Toast.svelte';
  import { online, pendingCount, showToast } from './lib/stores.js';
  import { loadSituationsAndStats } from './lib/data.js';
  import { countQueued } from './lib/db.js';

  let pollTimer;
  let currentView = 'main';

  function updateViewFromHash() {
    currentView = window.location.hash === '#/metrics' ? 'metrics' : 'main';
  }

  async function refreshPending() {
    pendingCount.set(await countQueued());
  }

  function handleOnline() {
    online.set(true);
    showToast('Conexión restablecida. Cargando datos y listo para sincronizar.');
    loadSituationsAndStats();
  }

  function handleOffline() {
    online.set(false);
    showToast('Sin conexión celular. Operando en modo offline.', true);
  }

  onMount(() => {
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    window.addEventListener('hashchange', updateViewFromHash);

    updateViewFromHash();
    loadSituationsAndStats();
    refreshPending();

    // Polling ligero para actualizar el mapa cada 15s si hay red.
    pollTimer = setInterval(() => {
      if (navigator.onLine) loadSituationsAndStats();
    }, 15000);
  });

  onDestroy(() => {
    clearInterval(pollTimer);
    window.removeEventListener('online', handleOnline);
    window.removeEventListener('offline', handleOffline);
    window.removeEventListener('hashchange', updateViewFromHash);
  });
</script>

<div class="container">
  {#if currentView === 'metrics'}
    <MetricsDashboard />
  {:else}
    <Header />
    <InfoDisclaimer />
    <SyncCard on:synced={refreshPending} />
    <StatsDashboard />

    <div class="workspace-grid">
      <MapView />
      <SectorsList />
    </div>

    <ReportForm on:queued={refreshPending} />
  {/if}
</div>

<Toast />
