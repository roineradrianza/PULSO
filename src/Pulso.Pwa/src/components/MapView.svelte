<script>
  import { onMount, onDestroy } from 'svelte';
  import L from 'leaflet';
  import 'leaflet/dist/leaflet.css';
  import { situations, mapFocus } from '../lib/stores.js';

  let mapEl;
  let map;
  let markersLayer;
  let unsubSituations;
  let unsubFocus;

  // El color sale de una lista fija (no de datos del usuario): seguro para interpolar.
  function severityColor(sit) {
    if (sit.is_person_found) return 'var(--success-green)';
    switch (sit.severity) {
      case 'CRITICAL': return 'var(--danger-red)';
      case 'HIGH': return 'var(--accent-orange)';
      case 'MEDIUM': return 'var(--warning-amber)';
      default: return 'var(--info-blue)';
    }
  }

  // Popup construido con nodos DOM + textContent: el contenido derivado de reportes
  // (raw_text, sector, nombre) nunca se interpreta como HTML => sin XSS.
  function buildPopup(sit, color) {
    const wrap = document.createElement('div');
    wrap.style.fontFamily = "'Inter', sans-serif";

    const title = document.createElement('h4');
    title.style.cssText = `margin-bottom: 4px; font-family: 'Outfit'; color: ${color};`;
    title.textContent = sit.is_person_found ? '✅ Persona Localizada' : '⚠️ Reporte de Incidente';

    const body = document.createElement('p');
    body.style.cssText = 'font-size: 12px; margin-bottom: 6px;';
    body.textContent = sit.raw_text ?? '';

    const meta = document.createElement('div');
    meta.style.cssText = 'font-size: 11px; color: var(--text-muted);';

    const place = document.createElement('div');
    place.textContent = `Lugar: ${sit.sector || 'Desconocido'}`;

    const date = document.createElement('div');
    date.textContent = `Fecha: ${new Date(sit.created_at).toLocaleString()}`;

    meta.append(place, date);

    if (sit.found_person_name) {
      const name = document.createElement('div');
      name.textContent = `Nombre: ${sit.found_person_name}`;
      meta.append(name);
    }

    wrap.append(title, body, meta);
    return wrap;
  }

  function renderMarkers(list) {
    if (!markersLayer) return;
    markersLayer.clearLayers();

    list.forEach((sit) => {
      if (!sit.latitude || !sit.longitude) return;

      const color = severityColor(sit);
      const icon = L.divIcon({
        html: `<div style="background-color: ${color}; width: 14px; height: 14px; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 10px ${color};"></div>`,
        className: 'custom-marker-icon',
        iconSize: [14, 14],
        iconAnchor: [7, 7]
      });

      L.marker([sit.latitude, sit.longitude], { icon })
        .bindPopup(buildPopup(sit, color))
        .addTo(markersLayer);
    });
  }

  onMount(() => {
    // Centrar por defecto en Caracas, Venezuela.
    map = L.map(mapEl, { center: [10.4806, -66.9036], zoom: 12, zoomControl: true });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 20
    }).addTo(map);

    markersLayer = L.layerGroup().addTo(map);

    unsubSituations = situations.subscribe((list) => renderMarkers(list));
    unsubFocus = mapFocus.subscribe((f) => {
      if (f && map) map.setView([f.lat, f.lng], f.zoom ?? 14);
    });
  });

  onDestroy(() => {
    if (unsubSituations) unsubSituations();
    if (unsubFocus) unsubFocus();
    if (map) map.remove();
  });
</script>

<div class="card" style="padding: 20px;">
  <h2 style="font-size: 18px; font-weight: 700; margin-bottom: 10px;">
    Mapa de Situación en Tiempo Real
  </h2>
  <div class="map-container">
    <div bind:this={mapEl} id="map"></div>
  </div>
</div>
