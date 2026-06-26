<script>
  import { sectorStats, situations, mapFocus, showToast } from '../lib/stores.js';

  let query = '';

  $: filtered = $sectorStats.filter((stat) =>
    stat.sector.toLowerCase().includes(query.toLowerCase().trim())
  );

  function focusSector(stat) {
    const points = $situations.filter(
      (s) => s.sector === stat.sector && s.latitude && s.longitude
    );

    if (points.length > 0) {
      mapFocus.set({ lat: points[0].latitude, lng: points[0].longitude, zoom: 14 });
      showToast(`Enfocando mapa en sector ${stat.sector}`);
    } else {
      showToast(`El sector ${stat.sector} no tiene coordenadas registradas.`, true);
    }
  }
</script>

<div class="card" style="padding: 20px;">
  <div class="sector-search-container">
    <h2 style="font-size: 18px; font-weight: 700;">Estado de la Situación por Sector</h2>
    <input
      type="text"
      class="sector-search-input"
      placeholder="Buscar zona (ej. Altamira, Petare)..."
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
              {stat.sector}
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
