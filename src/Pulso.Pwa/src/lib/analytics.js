// Analítica web de PULSO: tráfico/uso (Umami) + rendimiento (Core Web Vitals).
//
// Privacidad primero: Umami no usa cookies ni recolecta PII; solo agregados de uso.
// Es CONFIG-DRIVEN: si no están definidas VITE_UMAMI_SRC y VITE_UMAMI_WEBSITE_ID,
// todo queda en no-op (no se carga script ni se envía nada). Así dev y los entornos
// sin analítica configurada no tocan ninguna red de terceros.
import { onCLS, onINP, onLCP, onTTFB, onFCP } from 'web-vitals';

const UMAMI_SRC = import.meta.env.VITE_UMAMI_SRC ?? '';
const UMAMI_WEBSITE_ID = import.meta.env.VITE_UMAMI_WEBSITE_ID ?? '';

const enabled = Boolean(UMAMI_SRC && UMAMI_WEBSITE_ID);

// Inyecta el script de Umami (self-hosted). data-auto-track captura la primera vista;
// las navegaciones por hash (#/metrics) las registramos a mano en trackView().
function injectUmami() {
  const script = document.createElement('script');
  script.async = true;
  script.defer = true;
  script.src = UMAMI_SRC;
  script.setAttribute('data-website-id', UMAMI_WEBSITE_ID);
  // Sin auto-track: el SPA usa hash routing y lo manejamos nosotros para no perder
  // ni duplicar vistas. Disparamos la vista inicial al cargar el tracker.
  script.setAttribute('data-auto-track', 'false');
  document.head.appendChild(script);
}

// Reporta una vista de página. `name` es opcional (ruta legible, p. ej. "/mapa").
export function trackView(name) {
  if (!enabled || typeof window === 'undefined' || !window.umami) return;
  try {
    // umami.track() sin args envía la URL actual; con string envía una ruta explícita.
    if (name) window.umami.track((props) => ({ ...props, url: name }));
    else window.umami.track();
  } catch {
    // La analítica nunca debe romper la app.
  }
}

// Reporta un evento de uso (p. ej. instalación de la PWA).
export function trackEvent(eventName, data) {
  if (!enabled || typeof window === 'undefined' || !window.umami) return;
  try {
    window.umami.track(eventName, data);
  } catch {
    // No crítico.
  }
}

// Envía cada métrica de Core Web Vitals como evento de Umami. Los valores llegan de
// forma asíncrona (LCP al cargar, INP/CLS en interacción/ocultar la página).
function reportWebVitals() {
  const send = ({ name, value, rating }) =>
    trackEvent('web-vitals', { metric: name, value: Math.round(value), rating });

  onLCP(send);
  onINP(send);
  onCLS(send);
  onTTFB(send);
  onFCP(send);
}

// Arranca la analítica: carga Umami, registra Web Vitals y la instalación de la PWA.
// Idempotente y seguro de llamar siempre (no hace nada si no está configurada).
export function initAnalytics() {
  if (!enabled || typeof window === 'undefined') return;

  injectUmami();

  // Vista inicial: el tracker puede no estar listo al instante, reintentamos brevemente.
  let tries = 0;
  const fireInitial = () => {
    if (window.umami) {
      trackView();
    } else if (tries++ < 20) {
      setTimeout(fireInitial, 250);
    }
  };
  fireInitial();

  // Adopción: cuántos instalan la PWA.
  window.addEventListener('appinstalled', () => trackEvent('pwa_instalada'));

  reportWebVitals();
}
