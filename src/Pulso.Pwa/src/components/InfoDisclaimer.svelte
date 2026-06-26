<script>
  import { slide } from 'svelte/transition';
  let isOpen = true;

  function toggle() {
    isOpen = !isOpen;
  }
</script>

<div class="info-disclaimer-card card">
  <div class="card-header" on:click={toggle} role="button" tabindex="0" on:keydown={(e) => e.key === 'Enter' && toggle()}>
    <div class="header-title">
      <span class="info-icon">ℹ️</span>
      <h3>Sobre PULSO y Uso de Datos</h3>
    </div>
    <button class="toggle-btn" aria-label="Toggle details">
      {isOpen ? '▲ Ocultar' : '▼ Mostrar'}
    </button>
  </div>

  {#if isOpen}
    <div class="card-content" transition:slide={{ duration: 300 }}>
      <div class="info-section">
        <p class="about-text">
          <strong>PULSO</strong> es un portal ciudadano de visibilidad y mapeo de reportes activado con motivo del <strong>terremoto ocurrido el 24 de junio de 2026</strong> en Venezuela. Permite registrar daños, incidentes y personas localizadas a nivel nacional, funcionando en línea y en zonas sin cobertura celular (offline-first) para ayudar a centralizar información de interés público.
        </p>
      </div>
      
      <div class="divider"></div>

      <div class="disclaimer-section">
        <h4 class="disclaimer-title">⚠️ Descargo de Responsabilidad y Privacidad (PII)</h4>
        <ul class="disclaimer-list">
          <li><strong>Protección de Datos:</strong> La información personal ingresada (nombres, cédulas, números telefónicos y geolocalización) es altamente sensible. Se procesa de forma segura mediante conexiones cifradas (TLS/SSL) y almacenamiento protegido para evitar riesgos físicos o persecuciones.</li>
          <li><strong>Uso Responsable:</strong> Esta plataforma se destina exclusivamente a coordinar labores de búsqueda, salvamento y ayuda. Queda estrictamente prohibido ingresar reportes falsos, información malintencionada o coordenadas fuera del territorio nacional.</li>
          <li><strong>Carácter de la Información:</strong> Al enviar un reporte, usted acepta que esta información estará disponible de manera pública y abierta para cualquier persona o entidad interesada en colaborar (organizaciones humanitarias, voluntarios o familiares). No existe garantía alguna de que las brigadas de auxilio oficiales vayan a recibir o actuar directamente sobre estos datos.</li>
        </ul>
      </div>
    </div>
  {/if}
</div>

<style>
  .info-disclaimer-card {
    border-left: 4px solid var(--info-blue);
    background: rgba(20, 30, 55, 0.45);
    padding: 16px 20px;
    margin-bottom: 8px;
    transition: all 0.3s ease;
  }

  .card-header {
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

  .header-title h3 {
    margin: 0;
    font-size: 16px;
    font-weight: 700;
    color: var(--text-main);
  }

  .info-icon {
    font-size: 18px;
  }

  .toggle-btn {
    background: none;
    border: none;
    color: var(--text-muted);
    font-size: 12px;
    padding: 4px 8px;
    cursor: pointer;
    font-weight: 600;
  }

  .toggle-btn:hover {
    color: var(--text-main);
  }

  .card-content {
    margin-top: 14px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .about-text {
    font-size: 13.5px;
    line-height: 1.5;
    color: var(--text-main);
  }

  .divider {
    height: 1px;
    background: var(--card-border);
    margin: 4px 0;
  }

  .disclaimer-section {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  .disclaimer-title {
    font-size: 13px;
    font-weight: 700;
    color: var(--warning-amber);
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .disclaimer-list {
    margin: 0;
    padding-left: 18px;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .disclaimer-list li {
    font-size: 12.5px;
    line-height: 1.45;
    color: var(--text-muted);
  }

  .disclaimer-list li strong {
    color: var(--text-main);
  }
</style>
