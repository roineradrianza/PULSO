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
  import { online, pendingCount, showToast, selectedDate } from './lib/stores.js';
  import { loadInitial, loadDelta, loadFromCache } from './lib/data.js';
  import { startRealtime, stopRealtime } from './lib/realtime.js';
  import { countQueued } from './lib/db.js';
  import { initAnalytics, trackView } from './lib/analytics.js';

  let pollTimer;
  let currentView = 'main';

  // Carga reactiva de datos al cambiar la fecha seleccionada o al recuperar red.
  // announce: es una acción explícita del usuario, así que mostramos el motivo si falla (429/502/…).
  $: if ($selectedDate && $online) {
    loadInitial($selectedDate, { announce: true });
  }

  function updateViewFromHash() {
    currentView = window.location.hash === '#/metrics' ? 'metrics' : 'main';
    // Registrar la vista para analítica (ruta legible en vez del hash crudo).
    trackView(currentView === 'metrics' ? '/metricas' : '/');
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
    // Analítica web (tráfico/uso + Web Vitals). No-op si no está configurada.
    initAnalytics();

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
    <SyncCard on:synced={refreshPending} />
    <StatsDashboard />

    <div class="workspace-grid">
      <MapView />
      <SectorsList />
    </div>

    <div class="workspace-grid">
      <ReportForm on:queued={refreshPending} />
      <InfoDisclaimer />
    </div>
  {/if}

  <footer class="app-footer">
    <div class="footer-content">
      <div class="footer-left">
        <strong>PULSO (Plataforma Unificada de Lectura y Seguimiento Offline)</strong>
        <p>Iniciativa comunitaria y sin fines de lucro para el reporte y análisis de emergencias.</p>
      </div>
      <div class="footer-right">
        <span>Desarrollado y mantenido de forma voluntaria.</span>
        <span>Para soporte, reportes de fallos o colaborar: <a href="mailto:pulso@roineradrianza.com">pulso@roineradrianza.com</a></span>

      </div>
    </div>
    <div class="footer-bottom">
      &copy; {new Date().getFullYear()} PULSO. Código abierto bajo licencia <a href="https://www.apache.org/licenses/LICENSE-2.0" target="_blank" rel="noopener noreferrer" style="color: inherit; text-decoration: underline;">Apache 2.0</a>.
    </div>
  </footer>
</div>

<Toast />

<style>
  .app-footer {
    margin-top: 48px;
    padding-top: 24px;
    padding-bottom: 24px;
    border-top: 1px solid var(--card-border);
    color: var(--text-muted);
    font-size: 12px;
  }
  .footer-content {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    flex-wrap: wrap;
    gap: 24px;
    margin-bottom: 16px;
  }
  .footer-left, .footer-right {
    flex: 1;
    min-width: 280px;
  }
  .footer-left strong {
    color: var(--text-main);
    display: block;
    margin-bottom: 6px;
    font-size: 13px;
  }
  .footer-left p {
    margin: 0;
    line-height: 1.5;
  }
  .footer-right {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .footer-right a {
    color: var(--info-blue, #3b82f6);
    text-decoration: none;
    transition: color 0.2s ease;
  }
  .footer-right a:hover {
    color: var(--success-green, #10b981);
    text-decoration: underline;
  }

  .footer-bottom {
    text-align: center;
    font-size: 11px;
    color: rgba(255, 255, 255, 0.25);
    margin-top: 16px;
    padding-top: 16px;
    border-top: 1px solid rgba(255, 255, 255, 0.03);
  }
</style>
