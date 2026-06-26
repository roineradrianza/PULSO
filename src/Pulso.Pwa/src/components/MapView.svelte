<script>
  import { onMount, onDestroy } from 'svelte';
  import L from 'leaflet';
  import 'leaflet/dist/leaflet.css';
  import 'leaflet.markercluster';
  import 'leaflet.markercluster/dist/MarkerCluster.css';
  import { situations, mapFocus } from '../lib/stores.js';
  import { fetchSituationDetail } from '../lib/api.js';

  let mapEl;
  let map;
  let clusterGroup;
  let unsubSituations;
  let unsubFocus;

  // Estado de diff: id -> { marker, sig } para actualizar sin reconstruir todo.
  const markersById = new Map();
  // Caché de detalle (raw_text) ya descargado, por id.
  const detailCache = new Map();

  const SEVERITY_RANK = { LOW: 0, MEDIUM: 1, HIGH: 2, CRITICAL: 3 };
  const RANK_COLOR = ['var(--info-blue)', 'var(--warning-amber)', 'var(--accent-orange)', 'var(--danger-red)'];

  function severityColor(sit) {
    if (sit.is_person_found) {
      return sit.found_person_verified ? 'var(--success-green)' : '#7ea085';
    }
    return RANK_COLOR[SEVERITY_RANK[sit.severity] ?? 0];
  }

  // Firma de lo que afecta el render del marcador (para detectar cambios).
  function signature(sit) {
    return `${sit.latitude},${sit.longitude},${sit.severity},${sit.is_person_found},${sit.found_person_verified},${sit.is_hardware_gps},${sit.needs_review},${sit.affected_person_name ?? ''}`;
  }

  // Chip de precisión de ubicación, en lenguaje simple para cualquier persona.
  // Verde = el pin está en el punto real (GPS); ámbar = ubicación aproximada.
  function buildConfidenceChip(sit) {
    const exact = sit.is_hardware_gps;
    const chip = document.createElement('div');
    chip.style.cssText =
      'display: inline-flex; align-items: center; gap: 6px; font-size: 11px; ' +
      'font-weight: 600; padding: 3px 9px; border-radius: 999px; margin-bottom: 8px; ' +
      'background: rgba(255,255,255,0.06);';

    const dot = document.createElement('span');
    dot.style.cssText =
      `width: 8px; height: 8px; border-radius: 50%; flex: none; ` +
      `background: ${exact ? 'var(--success-green)' : 'var(--warning-amber)'};`;

    const label = document.createElement('span');
    label.textContent = exact ? 'Ubicación exacta' : 'Ubicación aproximada';

    chip.append(dot, label);

    if (sit.needs_review) {
      const note = document.createElement('span');
      note.style.cssText = 'color: var(--text-muted); font-weight: 500;';
      note.textContent = '· Reporte por confirmar';
      chip.append(note);
    }
    return chip;
  }

  function buildIcon(sit) {
    const color = severityColor(sit);
    return L.divIcon({
      html: `<div style="background-color: ${color}; width: 14px; height: 14px; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 10px ${color};"></div>`,
      className: 'custom-marker-icon',
      iconSize: [14, 14],
      iconAnchor: [7, 7]
    });
  }

  // Popup construido con nodos DOM + textContent (sin XSS). El texto pesado
  // (raw_text) se carga BAJO DEMANDA al abrir, con estado de carga.
  function buildPopup(sit) {
    const color = severityColor(sit);
    const wrap = document.createElement('div');
    wrap.style.fontFamily = "'Inter', sans-serif";

    const title = document.createElement('h4');
    title.style.cssText = `margin-bottom: 4px; font-family: 'Outfit'; color: ${color};`;
    title.textContent = sit.is_person_found ? '🟡 Persona reportada a salvo' : '⚠️ Reporte de Incidente';

    let verificationBadge = null;
    let unverifiedNote = null;
    if (sit.is_person_found) {
      verificationBadge = document.createElement('div');
      verificationBadge.style.cssText =
        'display: inline-flex; align-items: center; gap: 4px; font-size: 11px; ' +
        'font-weight: 700; padding: 2px 8px; border-radius: 4px; margin-bottom: 8px; margin-right: 6px;';
      
      if (sit.found_person_verified) {
        verificationBadge.style.background = 'rgba(76, 175, 80, 0.15)';
        verificationBadge.style.color = 'var(--success-green)';
        verificationBadge.textContent = '✓ Confirmado';
      } else {
        verificationBadge.style.background = 'rgba(126, 160, 133, 0.15)';
        verificationBadge.style.color = '#7ea085';
        verificationBadge.textContent = 'Sin verificar';

        unverifiedNote = document.createElement('div');
        unverifiedNote.style.cssText =
          'font-size: 11px; color: var(--text-muted); font-style: italic; ' +
          'margin-top: 4px; margin-bottom: 8px; border-left: 2px solid #7ea085; padding-left: 6px;';
        unverifiedNote.textContent = 'Aviso ciudadano sin confirmación oficial. Se recomienda verificar la información directamente en el lugar.';
      }
    }

    const confidence = buildConfidenceChip(sit);

    const body = document.createElement('p');
    body.style.cssText = 'font-size: 12px; margin-bottom: 6px;';

    const meta = document.createElement('div');
    meta.style.cssText = 'font-size: 11px; color: var(--text-muted);';
    const place = document.createElement('div');
    place.textContent = `Lugar: ${sit.sector || 'Desconocido'}`;
    const date = document.createElement('div');
    date.textContent = `Fecha: ${new Date(sit.created_at).toLocaleString('es-VE', { timeZone: 'America/Caracas' })}`;
    meta.append(place, date);
    if (sit.found_person_name) {
      const name = document.createElement('div');
      name.textContent = `Nombre: ${sit.found_person_name}`;
      meta.append(name);
    }
    if (sit.affected_person_name) {
      const searched = document.createElement('div');
      searched.style.cssText = 'color: var(--accent-orange); font-weight: 600;';
      searched.textContent = `Persona buscada: ${sit.affected_person_name}`;
      meta.append(searched);
    }

    wrap.append(title);
    if (verificationBadge) {
      wrap.append(verificationBadge);
    }
    wrap.append(confidence);
    if (unverifiedNote) {
      wrap.append(unverifiedNote);
    }
    wrap.append(body, meta);

    // Detalle lazy: usar caché o descargar.
    const cached = detailCache.get(sit.id);
    if (cached !== undefined) {
      body.textContent = cached || '(sin detalle)';
    } else {
      body.textContent = 'Cargando detalle…';
      fetchSituationDetail(sit.id)
        .then((d) => {
          const text = d?.raw_text ?? '';
          detailCache.set(sit.id, text);
          body.textContent = text || '(sin detalle)';
        })
        .catch(() => {
          body.textContent = '(no se pudo cargar el detalle)';
        });
    }

    return wrap;
  }

  function makeMarker(sit) {
    const marker = L.marker([sit.latitude, sit.longitude], { icon: buildIcon(sit) });
    // bindPopup con función => contenido (y descarga lazy) solo al abrir.
    marker.bindPopup(() => buildPopup(sit));
    // Severidad accesible para colorear el cluster.
    marker.pulsoSeverity = sit.severity;
    return marker;
  }

  // Diff incremental: agrega nuevos, actualiza cambiados, elimina ausentes.
  function syncMarkers(list) {
    if (!clusterGroup) return;

    const seen = new Set();
    const toAdd = [];

    for (const sit of list) {
      if (!sit.latitude || !sit.longitude) continue;
      seen.add(sit.id);
      const sig = signature(sit);
      const existing = markersById.get(sit.id);

      if (!existing) {
        const marker = makeMarker(sit);
        markersById.set(sit.id, { marker, sig });
        toAdd.push(marker);
      } else if (existing.sig !== sig) {
        // Cambió posición/severidad: actualizar en sitio.
        existing.marker.setLatLng([sit.latitude, sit.longitude]);
        existing.marker.setIcon(buildIcon(sit));
        existing.marker.pulsoSeverity = sit.severity;
        existing.marker.bindPopup(() => buildPopup(sit));
        existing.sig = sig;
        detailCache.delete(sit.id);
      }
    }

    // Eliminar marcadores cuyos incidentes ya no están en el conjunto.
    const toRemove = [];
    for (const [id, entry] of markersById) {
      if (!seen.has(id)) {
        toRemove.push(entry.marker);
        markersById.delete(id);
      }
    }

    if (toRemove.length) clusterGroup.removeLayers(toRemove);
    if (toAdd.length) clusterGroup.addLayers(toAdd);
  }

  // Ícono del cluster: color = severidad más alta que contiene + conteo.
  function clusterIcon(cluster) {
    let rank = 0;
    for (const m of cluster.getAllChildMarkers()) {
      const r = SEVERITY_RANK[m.pulsoSeverity] ?? 0;
      if (r > rank) rank = r;
    }
    const color = RANK_COLOR[rank];
    const count = cluster.getChildCount();
    return L.divIcon({
      html: `<div style="background-color: ${color}; color: #fff; width: 36px; height: 36px; display: flex; align-items: center; justify-content: center; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 12px ${color}; font-family: 'Outfit', sans-serif; font-weight: 700; font-size: 13px;">${count}</div>`,
      className: 'pulso-cluster-icon',
      iconSize: [36, 36]
    });
  }

  onMount(() => {
    map = L.map(mapEl, { center: [10.4806, -66.9036], zoom: 12, zoomControl: true });

    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>',
      subdomains: 'abcd',
      maxZoom: 20
    }).addTo(map);

    clusterGroup = L.markerClusterGroup({
      chunkedLoading: true,
      iconCreateFunction: clusterIcon
    });
    map.addLayer(clusterGroup);

    unsubSituations = situations.subscribe((list) => syncMarkers(list));
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
