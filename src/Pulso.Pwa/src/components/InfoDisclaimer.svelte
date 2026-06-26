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
      <h3>¿Cómo funciona este portal y qué hacemos con sus datos?</h3>
    </div>
    <button class="toggle-btn" aria-label="Toggle details">
      {isOpen ? '▲ Ocultar' : '▼ Mostrar'}
    </button>
  </div>

  {#if isOpen}
    <div class="card-content" transition:slide={{ duration: 300 }}>
      <div class="info-section">
        <p class="about-text">
          <strong>PULSO (Plataforma Unificada de Lectura y Seguimiento Offline)</strong> es una cartelera pública digital creada tras el <strong>terremoto ocurrido el 24 de junio de 2026</strong> en Venezuela. Sirve para que cualquier persona informe sobre daños materiales en calles o casas, y avise si localizó a un vecino o familiar. Funciona incluso si no tiene señal o internet en su celular.
        </p>
      </div>
      
      <div class="divider"></div>

      <div class="disclaimer-section">
        <h4 class="disclaimer-title">⚠️ Aviso importante sobre su seguridad y privacidad</h4>
        <ul class="disclaimer-list">
          <li><strong>Seguridad de sus datos:</strong> Sus datos telefónicos, de contacto y ubicación están protegidos y viajan encriptados de forma segura por internet para evitar filtraciones o riesgos.</li>
          <li><strong>Uso correcto:</strong> Por favor, reporte solo información verdadera sobre la emergencia. Queda prohibido enviar reportes falsos o ubicaciones fuera de Venezuela.</li>
          <li><strong>Información pública y voluntaria:</strong> Al enviar un reporte, la información se mostrará en el mapa para que brigadas de voluntarios, familiares y vecinos puedan colaborar. Tenga en cuenta que este es un portal de ayuda ciudadana y no garantiza la llegada de bomberos o autoridades oficiales.</li>
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
