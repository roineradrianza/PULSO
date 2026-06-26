<script>
  import { sectorStats, situations, mapFocus, showToast, selectedDate } from '../lib/stores.js';

  let query = '';

  const laGuairaSectors = ['Maiquetía', 'La Guaira', 'Macuto', 'Caraballeda', 'Naiguatá', 'Catia La Mar'];

  function getSectorLabel(sector) {
    if (laGuairaSectors.includes(sector)) {
      return `La Guaira, ${sector}`;
    }
    return `Caracas, ${sector}`;
  }

  $: filtered = $sectorStats.filter((stat) => {
    const label = getSectorLabel(stat.sector).toLowerCase();
    const queryLower = query.toLowerCase().trim();
    return label.includes(queryLower) ||
      stat.people_found.some((p) => p.toLowerCase().includes(queryLower));
  });

  function focusSector(stat) {
    // Preferir el centroide del servidor (no depende del subconjunto cargado en el mapa).
    let lat = stat.latitude;
    let lng = stat.longitude;

    // Fallback: buscar un punto del sector en la lista en memoria.
    if (lat == null || lng == null) {
      const point = $situations.find(
        (s) => s.sector === stat.sector && s.latitude && s.longitude
      );
      if (point) {
        lat = point.latitude;
        lng = point.longitude;
      }
    }

    if (lat != null && lng != null) {
      mapFocus.set({ lat, lng, zoom: 14 });
      showToast(`Enfocando mapa en sector ${stat.sector}`);
    } else {
      showToast(`El sector ${stat.sector} no tiene coordenadas registradas.`, true);
    }
  }

  function translateStatus(status) {
    switch (status) {
      case 'CRITICAL': return 'CRÍTICO';
      case 'HIGH': return 'ALTO';
      case 'MEDIUM': return 'MEDIO';
      case 'LOW': return 'BAJO';
      default: return status;
    }
  }
</script>

<div class="card" style="padding: 20px;">
  <div class="sector-search-container">
    <h2 style="font-size: 18px; font-weight: 700;">Lista de Sectores y Personas Encontradas</h2>
    
    <div style="display: flex; gap: 10px; flex-wrap: wrap; align-items: center; width: 100%;">
      <input
        type="text"
        class="sector-search-input"
        placeholder="Escriba un sector o nombre (ej: Altamira, Pedro)..."
        bind:value={query}
        style="flex: 1; min-width: 180px;"
      />
      <div style="display: flex; align-items: center; gap: 8px; flex-shrink: 0;">
        <span style="font-size: 11px; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;">Día:</span>
        <input 
          type="date" 
          bind:value={$selectedDate}
          class="custom-datepicker"
          style="background: rgba(0, 0, 0, 0.3); border: 1px solid var(--card-border); border-radius: 8px; color: var(--text-main); padding: 10px 14px; font-size: 14px; outline: none; font-family: 'Inter', sans-serif; cursor: pointer; transition: border-color 0.2s ease;"
          on:focus={(e) => e.target.style.borderColor = 'var(--accent-orange)'}
          on:blur={(e) => e.target.style.borderColor = 'var(--card-border)'}
          on:click={(e) => e.target.showPicker()}
        />
      </div>
    </div>
  </div>

  <div class="sectors-list">
    {#if filtered.length === 0}
      <p style="color: var(--text-muted); font-size: 13px; text-align: center; margin-top: 20px;">
        No encontramos ningún sector o persona con ese nombre.
      </p>
    {:else}
      {#each filtered as stat (stat.sector)}
        <div
          class="sector-card"
          role="button"
          tabindex="0"
          on:click={() => focusSector(stat)}
          on:keydown={(e) => e.key === 'Enter' && focusSector(stat)}
        >
          <div>
            <div class="sector-name">
              <span class="status-dot {stat.status.toLowerCase()}"></span>
              {getSectorLabel(stat.sector)}
            </div>
            {#if stat.people_found.length > 0}
              <div class="sector-people-container">
                <span class="people-label">Reportados a salvo · sin verificar ({stat.people_found.length}):</span>
                <div class="people-badges">
                  {#each stat.people_found as person}
                    <span class="person-badge" title={person}>{person}</span>
                  {/each}
                </div>
              </div>
            {/if}
          </div>
          <div class="sector-meta">
            <span class="sector-count">{stat.incident_count} reportes</span>
            <span style="font-size: 10px; font-weight: bold; color: var(--text-muted);">{translateStatus(stat.status)}</span>
          </div>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .custom-datepicker::-webkit-calendar-picker-indicator {
    filter: invert(1);
    cursor: pointer;
  }
</style>
