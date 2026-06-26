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
  import { loadInitial, loadDelta, loadFromCache } from './lib/data.js';
  import { startRealtime, stopRealtime } from './lib/realtime.js';
  import { countQueued } from './lib/db.js';

  let pollTimer;
  let currentView = 'main';

  function updateViewFromHash() {
    currentView = window.location.hash === '#/metrics' ? 'metrics' : 'main';
  }

  async function refreshPending() {
    pendingCount.set(await countQueued());
  }

  function notifyNew(newCount) {
    showToast(newCount === 1 ? '1 nuevo reporte recibido' : `${newCount} nuevos reportes recibidos`);
  }

  function relativeTime(ms) {
    const mins = Math.round((Date.now() - ms) / 60000);
    if (mins < 1) return 'hace un momento';
    if (mins < 60) return `hace ${mins} min`;
    return `hace ${Math.round(mins / 60)} h`;
  }

  function handleOnline() {
    online.set(true);
    showToast('Conexión restablecida. Cargando datos y listo para sincronizar.');
    loadInitial();
  }

  function handleOffline() {
    online.set(false);
    showToast('Sin conexión celular. Operando en modo offline.', true);
  }

  // Polling de respaldo (por si el SSE está bloqueado por la red).
  async function pollDelta() {
    if (!navigator.onLine) return;
    const newCount = await loadDelta();
    if (newCount > 0) notifyNew(newCount);
  }

  onMount(async () => {
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    window.addEventListener('hashchange', updateViewFromHash);

    updateViewFromHash();
    refreshPending();

    // Mostrar de inmediato el último panorama guardado (incluso sin red).
    const savedAt = await loadFromCache();
    if (!navigator.onLine && savedAt) {
      showToast(`Mostrando últimos datos guardados (${relativeTime(savedAt)}).`, true);
    }

    if (navigator.onLine) loadInitial();

    // Tiempo real vía SSE (dispara delta al instante); el polling queda como respaldo.
    startRealtime(notifyNew);
    pollTimer = setInterval(pollDelta, 60000);
  });

  onDestroy(() => {
    clearInterval(pollTimer);
    stopRealtime();
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
