<script>
  import { reportFilter, situations } from '../lib/stores.js';

  const MODES = [
    { id: 'all', label: 'Todos' },
    { id: 'incidents', label: 'Incidentes' },
    { id: 'pets', label: '🐾 Mascotas' }
  ];

  // Aclara qué queda oculto en cada modo: en una emergencia, no ver un reporte
  // debe ser una decisión visible del usuario, nunca una sorpresa.
  const HINTS = {
    all: 'Mostrando todos los reportes.',
    incidents: 'Solo incidentes: los reportes de mascotas quedan ocultos.',
    pets: 'Solo mascotas perdidas y encontradas.'
  };

  $: petTotal = $situations.filter((s) => s.category === 'LOST_FOUND_PET').length;
  $: counts = {
    all: $situations.length,
    incidents: $situations.length - petTotal,
    pets: petTotal
  };
</script>

<div class="filter-bar">
  <div class="segmented" role="tablist" aria-label="Tipo de reporte">
    {#each MODES as m (m.id)}
      <button
        class="{m.id}"
        class:active={$reportFilter === m.id}
        role="tab"
        aria-selected={$reportFilter === m.id}
        on:click={() => reportFilter.set(m.id)}
      >
        {m.label} <span class="count">{counts[m.id]}</span>
      </button>
    {/each}
  </div>
  <div class="filter-hint">{HINTS[$reportFilter]}</div>
</div>

<style>
  .filter-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    flex-wrap: wrap;
    width: 100%;
  }

  .segmented {
    display: inline-flex;
    background: rgba(0, 0, 0, 0.35);
    border: 1px solid var(--card-border);
    border-radius: 12px;
    padding: 4px;
    gap: 4px;
  }

  .segmented button {
    border: none;
    cursor: pointer;
    background: transparent;
    color: var(--text-muted);
    font-family: 'Outfit', sans-serif;
    font-weight: 600;
    font-size: 13px;
    padding: 8px 18px;
    border-radius: 9px;
    display: flex;
    align-items: center;
    gap: 7px;
    transition: all 0.2s ease;
  }

  .segmented button:hover {
    color: var(--text-main);
  }

  .count {
    font-size: 11px;
    font-weight: 700;
    background: rgba(255, 255, 255, 0.08);
    padding: 1px 7px;
    border-radius: 20px;
  }

  .segmented button.active {
    color: white;
  }

  .segmented button.active .count {
    background: rgba(0, 0, 0, 0.25);
    color: #fff;
  }

  /* Color activo semántico: dice qué se está viendo sin leer la etiqueta.
     "Todos" en neutro (es el estado sin filtro), "Incidentes" con el naranja
     de alerta y "Mascotas" con el dorado de sus pines en el mapa. */
  .segmented button.active.all {
    background: linear-gradient(135deg, #5b6779 0%, #414c5e 100%);
    box-shadow: 0 4px 15px rgba(91, 103, 121, 0.35);
  }

  .segmented button.active.incidents {
    background: linear-gradient(135deg, var(--accent-orange) 0%, #e65c00 100%);
    box-shadow: 0 4px 15px var(--accent-glow);
  }

  .segmented button.active.pets {
    background: linear-gradient(135deg, #d9a86b 0%, #b07f43 100%);
    box-shadow: 0 4px 15px rgba(201, 151, 90, 0.4);
  }

  .filter-hint {
    font-size: 12px;
    color: var(--text-muted);
  }

  /* En pantallas angostas la botonera ocupa todo el ancho y apila etiqueta y
     contador para conservar un área táctil cómoda. */
  @media (max-width: 850px) {
    .segmented {
      display: flex;
      width: 100%;
      border-radius: 14px;
    }

    .segmented button {
      flex: 1;
      flex-direction: column;
      gap: 3px;
      padding: 12px 4px;
      border-radius: 11px;
    }

    .count {
      font-size: 10px;
    }

    .filter-hint {
      width: 100%;
      text-align: center;
      font-size: 11px;
    }
  }
</style>
