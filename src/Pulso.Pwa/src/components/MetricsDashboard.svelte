<script>
  import { systemMetrics, online } from '../lib/stores.js';
  import { loadSituationsAndStats } from '../lib/data.js';

  let isExpanded = false;

  function toggleExpand() {
    isExpanded = !isExpanded;
  }

  async function handleRefresh() {
    await loadSituationsAndStats();
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

<div class="metrics-container card">
  <div 
    class="metrics-header" 
    on:click={toggleExpand} 
    on:keydown={(e) => (e.key === 'Enter' || e.key === ' ') && toggleExpand()}
    role="button" 
    tabindex="0"
  >
    <div class="header-title">
      <span class="metrics-pulse"></span>
      <h3>Métricas de Procesamiento y Analíticas</h3>
    </div>
    <div class="header-actions">
      {#if $online}
        <button class="btn-refresh" on:click|stopPropagation={handleRefresh} title="Actualizar métricas">
          🔄
        </button>
      {/if}
      <button class="btn-toggle">
        {isExpanded ? '▲ Colapsar' : '▼ Expandir'}
      </button>
    </div>
  </div>

  {#if isExpanded}
    {#if !$online}
      <div class="offline-warning">
        Las métricas del sistema no están disponibles sin conexión celular.
      </div>
    {:else}
      <div class="metrics-grid">
        <!-- 1. Distribución del Motor de IA vs Contingencia -->
        <div class="metric-section">
          <h4>Distribución del Motor de Triaje</h4>
          <p class="section-desc">Monitorea el uso de Gemini IA frente al motor local de contingencia.</p>
          <div class="engine-bar-container">
            <div class="engine-bar gemini" style="width: {geminiPercent}%">
              <span class="bar-label">Gemini ({geminiPercent}%)</span>
            </div>
            <div class="engine-bar fallback" style="width: {fallbackPercent}%">
              <span class="bar-label">Contingencia ({fallbackPercent}%)</span>
            </div>
          </div>
          <div class="engine-legend">
            <div class="legend-item"><span class="bullet gemini-dot"></span> Gemini: <strong>{engineDist['gemini'] || 0}</strong></div>
            <div class="legend-item"><span class="bullet fallback-dot"></span> Fallback Local: <strong>{engineDist['fallback_local'] || 0}</strong></div>
          </div>
        </div>

        <!-- 2. Distribución de Canales -->
        <div class="metric-section">
          <h4>Distribución por Canales de Origen</h4>
          <p class="section-desc">Medios de recepción de los reportes ciudadanos.</p>
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
              <span class="channel-name">PWA Web</span>
              <span class="channel-count">{channelDist['pwa'] || 0}</span>
            </div>
          </div>
        </div>

        <!-- 3. Horas Pico -->
        <div class="metric-section full-width">
          <h4>Horas Pico de Mayor Uso (Top 3)</h4>
          <div class="peaks-container">
            {#each peakHours as peak, idx}
              <div class="peak-item">
                <span class="peak-rank">#{idx + 1}</span>
                <span class="peak-time">{peak.hour.toString().padStart(2, '0')}:00 hs</span>
                <span class="peak-count">{peak.count} reportes</span>
              </div>
            {:else}
              <div class="no-data">Sin reportes registrados hoy</div>
            {/each}
          </div>
        </div>

        <!-- 4. Gráfico de Distribución Horaria -->
        <div class="metric-section full-width">
          <h4>Distribución de Reportes por Hora (0-23hs)</h4>
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
                  <span class="chart-label">{item.hour}</span>
                </div>
              {/each}
            </div>
          </div>
        </div>
      </div>
    {/if}
  {/if}
</div>

<style>
  .metrics-container {
    background: var(--card-bg);
    border: 1px solid var(--card-border);
    border-radius: 16px;
    padding: 20px;
    margin-bottom: 20px;
    box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.3);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    transition: all 0.3s ease;
  }

  .metrics-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    cursor: pointer;
    user-select: none;
  }

  .header-title {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .metrics-pulse {
    width: 10px;
    height: 10px;
    background-color: var(--accent-orange);
    border-radius: 50%;
    display: inline-block;
    box-shadow: 0 0 8px var(--accent-orange);
  }

  .header-actions {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .btn-refresh {
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid var(--card-border);
    padding: 6px 10px;
    border-radius: 8px;
    color: var(--text-main);
    cursor: pointer;
    font-size: 14px;
    transition: background 0.2s;
  }

  .btn-refresh:hover {
    background: rgba(255, 255, 255, 0.1);
  }

  .btn-toggle {
    background: none;
    border: none;
    color: var(--text-muted);
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    padding: 4px 8px;
    border-radius: 6px;
  }

  .btn-toggle:hover {
    color: var(--text-main);
    background: rgba(255, 255, 255, 0.05);
  }

  .offline-warning {
    margin-top: 15px;
    padding: 12px;
    background: rgba(239, 68, 68, 0.1);
    border: 1px solid rgba(239, 68, 68, 0.2);
    border-radius: 8px;
    color: var(--danger-red);
    font-size: 13px;
    text-align: center;
  }

  .metrics-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 24px;
    margin-top: 20px;
    padding-top: 20px;
    border-top: 1px solid var(--card-border);
  }

  @media (max-width: 768px) {
    .metrics-grid {
      grid-template-columns: 1fr;
    }
  }

  .metric-section {
    display: flex;
    flex-direction: column;
    gap: 12px;
    background: rgba(255, 255, 255, 0.02);
    border: 1px solid rgba(255, 255, 255, 0.03);
    border-radius: 12px;
    padding: 16px;
  }

  .full-width {
    grid-column: 1 / -1;
  }

  h4 {
    font-size: 15px;
    font-weight: 700;
    color: var(--text-main);
    font-family: 'Outfit', sans-serif;
  }

  .section-desc {
    font-size: 12px;
    color: var(--text-muted);
    margin-top: -6px;
  }

  /* Progress Bar styling */
  .engine-bar-container {
    height: 24px;
    width: 100%;
    background: rgba(255, 255, 255, 0.05);
    border-radius: 12px;
    display: flex;
    overflow: hidden;
    margin-top: 8px;
    border: 1px solid var(--card-border);
  }

  .engine-bar {
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 11px;
    font-weight: 700;
    color: white;
    transition: width 0.5s ease;
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
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    padding: 0 6px;
  }

  .engine-legend {
    display: flex;
    gap: 16px;
    font-size: 12px;
    margin-top: 4px;
  }

  .legend-item {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .bullet {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }

  .gemini-dot { background-color: #3b82f6; }
  .fallback-dot { background-color: #f59e0b; }

  /* Channel Grid */
  .channels-grid {
    display: flex;
    flex-direction: column;
    gap: 10px;
    margin-top: 6px;
  }

  .channel-pill {
    display: flex;
    align-items: center;
    padding: 10px 14px;
    border-radius: 10px;
    background: rgba(255, 255, 255, 0.04);
    border: 1px solid var(--card-border);
  }

  .channel-icon {
    font-size: 16px;
    margin-right: 10px;
    width: 24px;
    text-align: center;
  }

  .channel-name {
    flex-grow: 1;
    font-size: 13px;
    font-weight: 500;
  }

  .channel-count {
    font-weight: 700;
    font-size: 14px;
    padding: 2px 8px;
    border-radius: 6px;
    background: rgba(255, 255, 255, 0.08);
  }

  /* Peaks Container */
  .peaks-container {
    display: flex;
    gap: 16px;
    justify-content: space-around;
  }

  @media (max-width: 580px) {
    .peaks-container {
      flex-direction: column;
      gap: 10px;
    }
  }

  .peak-item {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 14px;
    border-radius: 12px;
    background: rgba(255, 123, 0, 0.05);
    border: 1px solid rgba(255, 123, 0, 0.15);
    text-align: center;
  }

  .peak-rank {
    font-size: 11px;
    font-weight: 800;
    color: var(--accent-orange);
    text-transform: uppercase;
    margin-bottom: 2px;
  }

  .peak-time {
    font-size: 16px;
    font-weight: 700;
    color: var(--text-main);
  }

  .peak-count {
    font-size: 12px;
    color: var(--text-muted);
    margin-top: 4px;
  }

  .no-data {
    color: var(--text-muted);
    font-size: 13px;
    text-align: center;
    padding: 10px;
    width: 100%;
  }

  /* Chart Layout */
  .chart-container {
    display: flex;
    height: 180px;
    gap: 12px;
    margin-top: 10px;
    padding: 10px 0;
  }

  .y-axis {
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    font-size: 10px;
    color: var(--text-muted);
    text-align: right;
    width: 25px;
    padding-bottom: 18px;
  }

  .chart-bars {
    flex-grow: 1;
    display: flex;
    justify-content: space-between;
    align-items: flex-end;
    height: 100%;
    border-bottom: 1px solid var(--card-border);
    padding-bottom: 4px;
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
    max-width: 16px;
    background: linear-gradient(180deg, var(--accent-orange) 0%, rgba(255, 123, 0, 0.2) 100%);
    border-radius: 4px 4px 0 0;
    transition: height 0.4s ease, background-color 0.2s;
    cursor: pointer;
    min-height: 2px;
  }

  .chart-bar:hover {
    background: linear-gradient(180deg, #ff9500 0%, rgba(255, 149, 0, 0.4) 100%);
    box-shadow: 0 0 10px rgba(255, 123, 0, 0.4);
  }

  .chart-label {
    font-size: 9px;
    color: var(--text-muted);
    margin-top: 6px;
    position: absolute;
    bottom: -16px;
  }

  /* Tooltip logic on hover */
  .bar-tooltip {
    display: none;
    position: absolute;
    top: -24px;
    background: #111827;
    border: 1px solid var(--card-border);
    color: white;
    font-size: 10px;
    font-weight: 700;
    padding: 2px 6px;
    border-radius: 4px;
    white-space: nowrap;
    pointer-events: none;
    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.2);
    z-index: 10;
  }

  .chart-bar:hover .bar-tooltip {
    display: block;
  }
</style>
