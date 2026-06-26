<script>
  import { situations, sectorStats } from '../lib/stores.js';

  $: totalIncidents = $situations.length;
  $: peopleFound = $situations.filter((s) => s.is_person_found).length;
  $: criticalSectors = $sectorStats.filter((s) => s.status === 'CRITICAL').length;
</script>

<div class="stats-grid">
  <div class="stat-card">
    <div>
      <div class="stat-value">{totalIncidents}</div>
      <div class="stat-label">Incidentes Registrados</div>
    </div>
    <div class="stat-icon">⚠️</div>
  </div>
  <div class="stat-card">
    <div>
      <div class="stat-value" style="color: var(--success-green);">{peopleFound}</div>
      <div class="stat-label">Personas Localizadas</div>
    </div>
    <div class="stat-icon">✅</div>
  </div>
  <div class="stat-card">
    <div>
      <div class="stat-value" style="color: var(--danger-red);">{criticalSectors}</div>
      <div class="stat-label">Sectores Afectados</div>
    </div>
    <div class="stat-icon">🚨</div>
  </div>
</div>
