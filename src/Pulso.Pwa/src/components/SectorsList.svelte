<script>
  import { sectorStats, situations, mapFocus, showToast, selectedDate, reportFilter } from '../lib/stores.js';

  let query = '';

  const laGuairaSectors = ['Maiquetía', 'La Guaira', 'Macuto', 'Caraballeda', 'Naiguatá', 'Catia La Mar'];

  // El título nombra lo que se está viendo, en lenguaje llano para cualquier persona.
  const TITLES = {
    all: 'Reportes por sector',
    incidents: 'Incidentes por sector',
    pets: '🐾 Mascotas perdidas y encontradas por sector'
  };

  function getSectorLabel(stat) {
    // Si la IA infirió la ciudad, usarla (plataforma nacional: no asumir Caracas).
    if (stat.city) {
      return `${stat.city}, ${stat.sector}`;
    }
    // Respaldo para reportes sin ciudad (datos previos): heurística de La Guaira; si no, Caracas.
    if (laGuairaSectors.includes(stat.sector)) {
      return `La Guaira, ${stat.sector}`;
    }
    return `Caracas, ${stat.sector}`;
  }

  // Insensible a acentos (buscar "jose" debe encontrar "José").
  function normalize(str) {
    return str.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  }

  // pets_count del servidor incluye también mascotas sin nombre (los arreglos de
  // nombres solo alimentan los badges). El respaldo por longitud cubre datos
  // cacheados por versiones previas del API que aún no traían el contador.
  function petCount(stat) {
    return stat.pets_count ?? ((stat.pets_found?.length ?? 0) + (stat.pets_lost?.length ?? 0));
  }

  $: filtered = $sectorStats.filter((stat) => {
    if ($reportFilter === 'pets' && petCount(stat) === 0) return false;

    const q = normalize(query.trim());
    if (!q) return true;
    if (normalize(getSectorLabel(stat)).includes(q)) return true;

    // La búsqueda solo considera nombres visibles en el modo activo: coincidir
    // por uno que la tarjeta no muestra sería desconcertante.
    const matches = (names) => (names ?? []).some((n) => normalize(n).includes(q));
    if ($reportFilter !== 'pets' && (matches(stat.people_found) || matches(stat.people_searched))) return true;
    if ($reportFilter !== 'incidents' && (matches(stat.pets_found) || matches(stat.pets_lost))) return true;
    return false;
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
    <h2 style="font-size: 18px; font-weight: 700;">{TITLES[$reportFilter]}</h2>

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
        {#if $reportFilter === 'pets' && !query.trim()}
          No hay reportes de mascotas por ahora.
        {:else if $reportFilter === 'pets'}
          No encontramos mascotas ni sectores con ese nombre.
        {:else}
          No encontramos ningún sector o persona con ese nombre.
        {/if}
      </p>
    {:else}
      {#each filtered as stat (`${stat.sector}|${stat.city ?? ''}`)}
        <div
          class="sector-card"
          role="button"
          tabindex="0"
          on:click={() => focusSector(stat)}
          on:keydown={(e) => e.key === 'Enter' && focusSector(stat)}
        >
          <div>
            <div class="sector-name">
              <span class="status-dot {$reportFilter === 'pets' ? 'pets' : stat.status.toLowerCase()}"></span>
              {getSectorLabel(stat)}
            </div>
            {#if $reportFilter !== 'pets'}
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
              {#if (stat.people_searched ?? []).length > 0}
                <div class="sector-people-container">
                  <span class="people-label searched-label">Personas buscadas ({stat.people_searched.length}):</span>
                  <div class="people-badges">
                    {#each stat.people_searched as person}
                      <span class="person-badge searched" title={person}>{person}</span>
                    {/each}
                  </div>
                </div>
              {/if}
            {/if}
            {#if $reportFilter !== 'incidents'}
              {#if (stat.pets_found ?? []).length > 0}
                <div class="sector-people-container">
                  <span class="people-label pets-label">Mascotas encontradas ({stat.pets_found.length}):</span>
                  <div class="people-badges">
                    {#each stat.pets_found as pet}
                      <span class="person-badge pet" title={pet}>🐾 {pet}</span>
                    {/each}
                  </div>
                </div>
              {/if}
              {#if (stat.pets_lost ?? []).length > 0}
                <div class="sector-people-container">
                  <span class="people-label pets-label">Mascotas perdidas ({stat.pets_lost.length}):</span>
                  <div class="people-badges">
                    {#each stat.pets_lost as pet}
                      <span class="person-badge pet lost" title={pet}>🐾 {pet}</span>
                    {/each}
                  </div>
                </div>
              {/if}
            {/if}
          </div>
          <div class="sector-meta">
            {#if $reportFilter === 'pets'}
              <span class="sector-count pets">🐾 {petCount(stat)} {petCount(stat) === 1 ? 'mascota' : 'mascotas'}</span>
            {:else if $reportFilter === 'incidents'}
              <span class="sector-count">{Math.max(stat.incident_count - petCount(stat), 0)} reportes</span>
            {:else}
              <span class="sector-count">{stat.incident_count} reportes</span>
            {/if}
            {#if $reportFilter !== 'pets'}
              <!-- La severidad agrega TODOS los incidentes del sector; en la vista
                   de mascotas sería ruido, por eso solo se muestra fuera de ella. -->
              <span style="font-size: 10px; font-weight: bold; color: var(--text-muted);">{translateStatus(stat.status)}</span>
            {/if}
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
  /* Persona en peligro (buscada/atrapada): se distingue del "a salvo" con tono ámbar. */
  .person-badge.searched {
    background: rgba(245, 158, 11, 0.15);
    color: var(--accent-orange);
    border: 1px solid rgba(245, 158, 11, 0.35);
  }
  .people-label.searched-label {
    color: var(--warning-amber);
  }
  /* Mascotas en dorado (el mismo de sus pines en el mapa) para no confundirlas
     con los estados verde/ámbar de personas. */
  .person-badge.pet {
    background: rgba(201, 151, 90, 0.14);
    color: var(--pet-gold);
    border: 1px solid rgba(201, 151, 90, 0.35);
  }
  /* Mascota perdida: ámbar de alerta, igual que una persona buscada. */
  .person-badge.pet.lost {
    background: rgba(245, 158, 11, 0.12);
    color: var(--warning-amber);
    border-color: rgba(245, 158, 11, 0.3);
  }
  .people-label.pets-label {
    color: var(--pet-gold);
  }
  /* En la vista de mascotas el punto de severidad pasa a un neutro dorado:
     ese semáforo describe incidentes humanos, no mascotas. */
  .status-dot.pets {
    background: var(--pet-gold);
    box-shadow: 0 0 8px rgba(201, 151, 90, 0.5);
  }
  .sector-count.pets {
    background: rgba(201, 151, 90, 0.15);
    color: var(--pet-gold);
  }
</style>
