<script>
  import { systemMetrics, online } from '../lib/stores.js';
  import { loadSituationsAndStats } from '../lib/data.js';

  async function handleRefresh() {
    await loadSituationsAndStats();
  }

  function goBack() {
    window.location.hash = '';
  }

  // Reactive variables computed from systemMetrics store
  $: metrics = $systemMetrics;
  $: engineDist = metrics?.engine_distribution || {};
  $: channelDist = metrics?.channel_distribution || {};
  $: hourlyDist = metrics?.hourly_distribution || [];
  $: peakHours = metrics?.peak_hours || [];

  // Compute percentages for engine distribution
  $: totalEngineProcessed = Object.values(engineDist).reduce((a, b) => a + b, 0) || 1;
  $: geminiPercent = Math.round(((engineDist['gemini'] || 0) / totalEngineProcessed) * 100);
  $: fallbackPercent = Math.round(((engineDist['fallback_local'] || 0) / totalEngineProcessed) * 100);

  // Helper for max count in hourly distribution to scale the bars
  $: maxHourlyCount = Math.max(...hourlyDist.map(h => h.count), 1);
</script>

<div class="metrics-page">
  <div class="metrics-nav">
    <button class="btn-back" on:click={goBack}>
      ← Volver al Mapa
    </button>
    <div class="nav-title">
      <span class="metrics-pulse"></span>
      <h2>Consola de Métricas y Analíticas del Sistema</h2>
    </div>
    <div>
      {#if $online}
        <button class="btn-refresh" on:click={handleRefresh} title="Actualizar métricas">
          🔄 Actualizar
        </button>
      {/if}
    </div>
  </div>

  {#if !$online}
    <div class="offline-warning">
      Las métricas del sistema no están disponibles sin conexión celular. Por favor, conéctese a internet para ver los datos de analíticas en tiempo real.
    </div>
  {:else if !metrics}
    <div class="loading-state">
      Cargando métricas del sistema...
    </div>
  {:else}
    <div class="metrics-grid">
      <!-- 1. Distribución del Motor de IA vs Contingencia -->
      <div class="metric-section card">
        <div class="section-header">
          <span class="section-icon">🤖</span>
          <h4>Distribución del Motor de Triaje</h4>
        </div>
        <p class="section-desc">Monitoreo de carga entre el motor principal de Inteligencia Artificial (Gemini) y el motor local de contingencia.</p>
        
        <div class="engine-bar-container">
          <div class="engine-bar gemini" style="width: {geminiPercent}%">
            <span class="bar-label">Gemini ({geminiPercent}%)</span>
          </div>
          <div class="engine-bar fallback" style="width: {fallbackPercent}%">
            <span class="bar-label">Contingencia ({fallbackPercent}%)</span>
          </div>
        </div>

        <div class="engine-legend">
          <div class="legend-card gemini-card">
            <div class="legend-header">
              <span class="bullet gemini-dot"></span>
              <span>Motor IA Gemini</span>
            </div>
            <div class="legend-value">{engineDist['gemini'] || 0} <span class="legend-unit">reportes</span></div>
          </div>
          <div class="legend-card fallback-card">
            <div class="legend-header">
              <span class="bullet fallback-dot"></span>
              <span>Contingencia Local</span>
            </div>
            <div class="legend-value">{engineDist['fallback_local'] || 0} <span class="legend-unit">reportes</span></div>
          </div>
        </div>
      </div>

      <!-- 2. Distribución de Canales -->
      <div class="metric-section card">
        <div class="section-header">
          <span class="section-icon">📡</span>
          <h4>Distribución por Canales de Ingesta</h4>
        </div>
        <p class="section-desc">Identificación de los canales y medios por los cuales los ciudadanos envían sus reportes de emergencia.</p>
        
        <div class="channels-grid">
          <div class="channel-pill whatsapp">
            <span class="channel-icon">💬</span>
            <span class="channel-name">WhatsApp</span>
            <span class="channel-count">{channelDist['whatsapp'] || 0}</span>
          </div>
          <div class="channel-pill telegram">
            <span class="channel-icon">✈️</span>
            <span class="channel-name">Telegram</span>
            <span class="channel-count">{channelDist['telegram'] || 0}</span>
          </div>
          <div class="channel-pill pwa">
            <span class="channel-icon">📱</span>
            <span class="channel-name">PWA Web (Directo)</span>
            <span class="channel-count">{channelDist['pwa'] || 0}</span>
          </div>
        </div>
      </div>

      <!-- 3. Horas Pico -->
      <div class="metric-section card full-width">
        <div class="section-header">
          <span class="section-icon">⚡</span>
          <h4>Horas Pico de Mayor Congestión (Top 3)</h4>
        </div>
        <p class="section-desc">Las tres horas del día que registran la mayor cantidad de reportes acumulados.</p>
        
        <div class="peaks-container">
          {#each peakHours as peak, idx}
            <div class="peak-item">
              <span class="peak-rank">TOP {idx + 1}</span>
              <span class="peak-time">{peak.hour.toString().padStart(2, '0')}:00 hs</span>
              <span class="peak-count">{peak.count} reportes recibidos</span>
            </div>
          {:else}
            <div class="no-data">Sin suficientes datos de reportes registrados para calcular picos.</div>
          {/each}
        </div>
      </div>

      <!-- 4. Gráfico de Distribución Horaria -->
      <div class="metric-section card full-width">
        <div class="section-header">
          <span class="section-icon">📊</span>
          <h4>Distribución Temporal de Reportes (0 - 23hs)</h4>
        </div>
        <p class="section-desc">Volumen de reportes recibidos por cada hora del día. Pase el cursor sobre las barras para ver detalles.</p>
        
        <div class="chart-container">
          <div class="y-axis">
            <span>{maxHourlyCount}</span>
            <span>{Math.round(maxHourlyCount / 2)}</span>
            <span>0</span>
          </div>
          <div class="chart-bars">
            {#each hourlyDist as item}
              <div class="chart-bar-wrapper">
                <div 
                  class="chart-bar" 
                  style="height: {(item.count / maxHourlyCount) * 100}%"
                  title="Hora {item.hour.toString().padStart(2, '0')}:00 hs - {item.count} reportes"
                >
                  {#if item.count > 0}
                    <span class="bar-tooltip">{item.count}</span>
                  {/if}
                </div>
                <span class="chart-label">{item.hour}h</span>
              </div>
            {/each}
          </div>
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .metrics-page {
    width: 100%;
    margin-top: 10px;
    display: flex;
    flex-direction: column;
    gap: 20px;
    animation: fadeIn 0.4s ease-out;
  }

  @keyframes fadeIn {
    from { opacity: 0; transform: translateY(10px); }
    to { opacity: 1; transform: translateY(0); }
  }

  .metrics-nav {
    display: flex;
    justify-content: space-between;
    align-items: center;
    background: var(--card-bg);
    border: 1px solid var(--card-border);
    border-radius: 12px;
    padding: 14px 20px;
    backdrop-filter: blur(8px);
    gap: 16px;
  }

  @media (max-width: 768px) {
    .metrics-nav {
      flex-direction: column;
      align-items: stretch;
      text-align: center;
    }
  }

  .nav-title {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 10px;
  }

  .nav-title h2 {
    font-size: 20px;
    font-weight: 700;
    color: var(--text-main);
    font-family: 'Outfit', sans-serif;
  }

  .metrics-pulse {
    width: 12px;
    height: 12px;
    background-color: var(--accent-orange);
    border-radius: 50%;
    display: inline-block;
    box-shadow: 0 0 10px var(--accent-orange);
    animation: pulse-ring 1.5s infinite;
  }

  .btn-back {
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid var(--card-border);
    padding: 8px 16px;
    border-radius: 8px;
    color: var(--text-main);
    font-weight: 600;
    cursor: pointer;
    font-size: 13px;
    transition: all 0.2s;
  }

  .btn-back:hover {
    background: rgba(255, 255, 255, 0.15);
    border-color: rgba(255, 255, 255, 0.25);
  }

  .btn-refresh {
    background: linear-gradient(135deg, var(--accent-orange) 0%, #ff9500 100%);
    border: none;
    padding: 8px 16px;
    border-radius: 8px;
    color: white;
    font-weight: 600;
    cursor: pointer;
    font-size: 13px;
    box-shadow: 0 4px 12px var(--accent-glow);
    transition: all 0.2s;
  }

  .btn-refresh:hover {
    transform: translateY(-1px);
    box-shadow: 0 6px 16px var(--accent-glow);
  }

  .offline-warning {
    padding: 24px;
    background: rgba(239, 68, 68, 0.1);
    border: 1px solid rgba(239, 68, 68, 0.2);
    border-radius: 16px;
    color: var(--danger-red);
    font-size: 15px;
    text-align: center;
    backdrop-filter: blur(8px);
  }

  .loading-state {
    padding: 40px;
    text-align: center;
    color: var(--text-muted);
    font-size: 16px;
  }

  .metrics-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 24px;
  }

  @media (max-width: 900px) {
    .metrics-grid {
      grid-template-columns: 1fr;
    }
  }

  .metric-section {
    display: flex;
    flex-direction: column;
    gap: 16px;
  }

  .full-width {
    grid-column: 1 / -1;
  }

  .section-header {
    display: flex;
    align-items: center;
    gap: 10px;
    border-bottom: 1px solid var(--card-border);
    padding-bottom: 12px;
  }

  .section-icon {
    font-size: 20px;
  }

  h4 {
    font-size: 17px;
    font-weight: 700;
    color: var(--text-main);
    font-family: 'Outfit', sans-serif;
  }

  .section-desc {
    font-size: 13px;
    color: var(--text-muted);
    line-height: 1.4;
  }

  /* Engine Bar Container */
  .engine-bar-container {
    height: 32px;
    width: 100%;
    background: rgba(255, 255, 255, 0.05);
    border-radius: 16px;
    display: flex;
    overflow: hidden;
    margin: 8px 0;
    border: 1px solid var(--card-border);
  }

  .engine-bar {
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 12px;
    font-weight: 700;
    color: white;
    transition: width 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  }

  .engine-bar.gemini {
    background: linear-gradient(90deg, #3b82f6, #6366f1);
    box-shadow: inset 0 0 10px rgba(99, 102, 241, 0.5);
  }

  .engine-bar.fallback {
    background: linear-gradient(90deg, #f59e0b, #ef4444);
    box-shadow: inset 0 0 10px rgba(239, 68, 68, 0.5);
  }

  .bar-label {
    padding: 0 12px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .engine-legend {
    display: flex;
    gap: 16px;
    margin-top: 8px;
  }

  @media (max-width: 480px) {
    .engine-legend {
      flex-direction: column;
    }
  }

  .legend-card {
    flex: 1;
    padding: 12px;
    border-radius: 10px;
    border: 1px solid var(--card-border);
  }

  .gemini-card {
    background: rgba(59, 130, 246, 0.04);
  }

  .fallback-card {
    background: rgba(245, 158, 11, 0.04);
  }

  .legend-header {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    color: var(--text-muted);
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 6px;
  }

  .bullet {
    width: 10px;
    height: 10px;
    border-radius: 50%;
  }

  .gemini-dot {
    background-color: #3b82f6;
    box-shadow: 0 0 8px #3b82f6;
  }

  .fallback-dot {
    background-color: #f59e0b;
    box-shadow: 0 0 8px #f59e0b;
  }

  .legend-value {
    font-size: 24px;
    font-weight: 800;
    color: var(--text-main);
    font-family: 'Outfit', sans-serif;
  }

  .legend-unit {
    font-size: 12px;
    font-weight: 500;
    color: var(--text-muted);
  }

  /* Channel Grid */
  .channels-grid {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .channel-pill {
    display: flex;
    align-items: center;
    padding: 14px 18px;
    border-radius: 12px;
    background: rgba(255, 255, 255, 0.03);
    border: 1px solid var(--card-border);
    transition: transform 0.2s, background-color 0.2s;
  }

  .channel-pill:hover {
    transform: translateX(4px);
    background: rgba(255, 255, 255, 0.05);
  }

  .channel-icon {
    font-size: 20px;
    margin-right: 14px;
    width: 28px;
    text-align: center;
  }

  .channel-name {
    flex-grow: 1;
    font-size: 14px;
    font-weight: 600;
  }

  .channel-count {
    font-weight: 800;
    font-size: 16px;
    padding: 4px 12px;
    border-radius: 8px;
    background: rgba(255, 255, 255, 0.08);
    font-family: 'Outfit', sans-serif;
  }

  /* Peaks Container */
  .peaks-container {
    display: flex;
    gap: 20px;
  }

  @media (max-width: 640px) {
    .peaks-container {
      flex-direction: column;
      gap: 12px;
    }
  }

  .peak-item {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 20px;
    border-radius: 14px;
    background: rgba(255, 123, 0, 0.04);
    border: 1px solid rgba(255, 123, 0, 0.15);
    text-align: center;
    transition: transform 0.2s;
  }

  .peak-item:hover {
    transform: translateY(-2px);
  }

  .peak-rank {
    font-size: 12px;
    font-weight: 800;
    color: var(--accent-orange);
    text-transform: uppercase;
    letter-spacing: 1px;
    margin-bottom: 4px;
  }

  .peak-time {
    font-size: 22px;
    font-weight: 800;
    color: var(--text-main);
    font-family: 'Outfit', sans-serif;
  }

  .peak-count {
    font-size: 13px;
    color: var(--text-muted);
    margin-top: 6px;
    font-weight: 500;
  }

  /* Chart Layout */
  .chart-container {
    display: flex;
    height: 220px;
    gap: 16px;
    padding: 10px 0 20px 0;
  }

  .y-axis {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    font-size: 11px;
    color: var(--text-muted);
    text-align: right;
    width: 25px;
    padding-bottom: 24px;
  }

  .chart-bars {
    flex-grow: 1;
    display: flex;
    justify-content: space-between;
    align-items: flex-end;
    height: 100%;
    border-bottom: 1px solid var(--card-border);
    padding-bottom: 6px;
  }

  .chart-bar-wrapper {
    flex-grow: 1;
    height: 100%;
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
    align-items: center;
    position: relative;
  }

  .chart-bar {
    width: 60%;
    max-width: 20px;
    background: linear-gradient(180deg, var(--accent-orange) 0%, rgba(255, 123, 0, 0.15) 100%);
    border-radius: 6px 6px 0 0;
    transition: height 0.6s cubic-bezier(0.4, 0, 0.2, 1);
    cursor: pointer;
    min-height: 2px;
  }

  .chart-bar:hover {
    background: linear-gradient(180deg, #ff9500 0%, rgba(255, 149, 0, 0.3) 100%);
    box-shadow: 0 0 12px rgba(255, 123, 0, 0.5);
  }

  .chart-label {
    font-size: 10px;
    color: var(--text-muted);
    margin-top: 8px;
    position: absolute;
    bottom: -20px;
    font-weight: 500;
  }

  /* Tooltip logic on hover */
  .bar-tooltip {
    display: none;
    position: absolute;
    top: -28px;
    background: #111827;
    border: 1px solid var(--card-border);
    color: white;
    font-size: 11px;
    font-weight: 700;
    padding: 3px 8px;
    border-radius: 6px;
    white-space: nowrap;
    pointer-events: none;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
    z-index: 10;
  }

  .chart-bar:hover .bar-tooltip {
    display: block;
  }
</style>
