<script>
  import { sectorStats, situations, mapFocus, showToast } from '../lib/stores.js';

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
</script>

<div class="card" style="padding: 20px;">
  <div class="sector-search-container">
    <h2 style="font-size: 18px; font-weight: 700;">Situación y Personas Localizadas por Sector</h2>
    <input
      type="text"
      class="sector-search-input"
      placeholder="Buscar sector o persona (ej. Altamira, Frank)..."
      bind:value={query}
    />
  </div>

  <div class="sectors-list">
    {#if filtered.length === 0}
      <p style="color: var(--text-muted); font-size: 13px; text-align: center; margin-top: 20px;">
        No se encontraron sectores coincidentes.
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
                <span class="people-label">Localizados ({stat.people_found.length}):</span>
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
            <span style="font-size: 10px; font-weight: bold; color: var(--text-muted);">{stat.status}</span>
          </div>
        </div>
      {/each}
    {/if}
  </div>
</div>
