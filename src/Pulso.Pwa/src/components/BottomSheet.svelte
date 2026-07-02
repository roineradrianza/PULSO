<script>
  import { tick } from 'svelte';

  // Panel deslizante genérico: no sabe qué es un "reporte", solo monta el nodo
  // DOM que le pasan. MapView le entrega el mismo nodo que ya usa el popup de
  // Leaflet en desktop, así que el contenido (foto, comentarios, etc.) no se
  // duplica entre las dos vistas.
  let isOpen = false;
  let panelEl;
  let contentEl;
  let closeBtnEl;

  let dragStartY = 0;
  let dragCurrentY = 0;
  let dragging = false;

  export async function open(node) {
    // contentEl vive dentro del {#if isOpen}, así que todavía no existe hasta
    // que Svelte procese este cambio — sin el tick(), appendChild fallaría en
    // silencio contra un contentEl aún null.
    isOpen = true;
    await tick();
    if (contentEl) {
      contentEl.innerHTML = '';
      contentEl.appendChild(node);
      contentEl.scrollTop = 0;
    }
    requestAnimationFrame(() => closeBtnEl?.focus());
  }

  export function close() {
    isOpen = false;
  }

  function handleKeydown(e) {
    if (e.key === 'Escape' && isOpen) close();
  }

  // Arrastre táctil para cerrar: setPointerCapture hace que el mismo elemento
  // siga recibiendo move/up aunque el dedo se salga de la barra de arrastre.
  function onPointerDown(e) {
    dragging = true;
    dragStartY = e.clientY;
    dragCurrentY = e.clientY;
    e.currentTarget.setPointerCapture(e.pointerId);
  }

  function onPointerMove(e) {
    if (!dragging) return;
    dragCurrentY = e.clientY;
    const delta = Math.max(0, dragCurrentY - dragStartY);
    if (panelEl) panelEl.style.transform = `translateY(${delta}px)`;
  }

  function onPointerUp() {
    if (!dragging) return;
    dragging = false;
    const delta = dragCurrentY - dragStartY;
    if (panelEl) panelEl.style.transform = '';
    if (delta > 100) close();
  }
</script>

<svelte:window on:keydown={handleKeydown} />

{#if isOpen}
  <div class="sheet-backdrop" on:click={close}></div>
  <div class="sheet-panel" bind:this={panelEl} role="dialog" aria-modal="true">
    <div
      class="sheet-handle"
      on:pointerdown={onPointerDown}
      on:pointermove={onPointerMove}
      on:pointerup={onPointerUp}
    >
      <div class="sheet-handle-bar"></div>
    </div>
    <button class="sheet-close" bind:this={closeBtnEl} on:click={close} aria-label="Cerrar">✕</button>
    <div class="sheet-content" bind:this={contentEl}></div>
  </div>
{/if}

<style>
  .sheet-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    z-index: 1998;
  }
  .sheet-panel {
    position: fixed;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: 1999;
    max-height: 75vh;
    display: flex;
    flex-direction: column;
    background: #0f172a;
    border: 1px solid var(--card-border);
    border-bottom: none;
    border-radius: 16px 16px 0 0;
    box-shadow: 0 -10px 30px rgba(0, 0, 0, 0.35);
    animation: sheet-slide-up 0.25s ease-out;
  }
  @keyframes sheet-slide-up {
    from {
      transform: translateY(100%);
    }
    to {
      transform: translateY(0);
    }
  }
  .sheet-handle {
    display: flex;
    justify-content: center;
    padding: 10px 0 4px;
    touch-action: none;
    cursor: grab;
  }
  .sheet-handle-bar {
    width: 40px;
    height: 4px;
    border-radius: 999px;
    background: rgba(255, 255, 255, 0.2);
  }
  .sheet-close {
    position: absolute;
    top: 10px;
    right: 12px;
    background: rgba(255, 255, 255, 0.06);
    border: none;
    color: var(--text-main);
    width: 28px;
    height: 28px;
    border-radius: 50%;
    font-size: 14px;
    line-height: 1;
    cursor: pointer;
  }
  .sheet-content {
    padding: 6px 20px 24px;
    font-family: 'Inter', sans-serif;
  }
</style>
