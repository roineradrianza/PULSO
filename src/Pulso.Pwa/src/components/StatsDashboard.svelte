<script>
  import { summary, situations, sectorStats } from '../lib/stores.js';

  // Preferir los totales agregados del servidor (reales aunque el mapa cargue un
  // subconjunto); fallback al cálculo local mientras el resumen no haya llegado.
  $: totalIncidents = $summary?.total_incidents ?? $situations.length;
  $: peopleFound = $summary?.people_found ?? $situations.filter((s) => s.is_person_found).length;
  $: criticalSectors = $summary?.critical_sectors ?? $sectorStats.filter((s) => s.status === 'CRITICAL').length;
</script>

<div class="stats-grid">
  <div class="stat-card">
    <div>
      <div class="stat-value">{totalIncidents}</div>
      <div class="stat-label">Emergencias Reportadas</div>
    </div>
    <div class="stat-icon">⚠️</div>
  </div>
  <div class="stat-card">
    <div>
      <div class="stat-value" style="color: var(--success-green);">{peopleFound}</div>
      <div class="stat-label">Personas Reportadas a Salvo</div>
    </div>
    <div class="stat-icon">✅</div>
  </div>
  <div class="stat-card">
    <div>
      <div class="stat-value" style="color: var(--danger-red);">{criticalSectors}</div>
      <div class="stat-label">Sectores en Alerta Crítica</div>
    </div>
    <div class="stat-icon">🚨</div>
  </div>
</div>
