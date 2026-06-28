<script>
  import { onMount, onDestroy } from 'svelte';
  import L from 'leaflet';
  import 'leaflet/dist/leaflet.css';
  import 'leaflet.markercluster';
  import 'leaflet.markercluster/dist/MarkerCluster.css';
  import { situations, mapFocus, showToast } from '../lib/stores.js';
  import { fetchSituationDetail, fetchComments, sendComment } from '../lib/api.js';

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
      html: `<div class="interactive-marker-dot" style="background-color: ${color}; width: 14px; height: 14px; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 10px ${color}; transition: transform 0.15s ease-out;"></div>`,
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
    const placeLabel = sit.city
      ? `${sit.city}, ${sit.sector || 'Desconocido'}`
      : (sit.sector || 'Desconocido');
    place.textContent = `Lugar: ${placeLabel}`;
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

    // --- SECCIÓN DE COMENTARIOS (ANÓNIMOS Y ONLINE-ONLY) ---
    const commentsSection = document.createElement('div');
    commentsSection.style.cssText = 'margin-top: 10px; border-top: 1px solid var(--card-border); padding-top: 10px;';

    const commentsTitle = document.createElement('h5');
    commentsTitle.style.cssText = 'margin: 0 0 6px 0; font-family: "Outfit"; font-size: 13px; color: var(--text-main); font-weight: 700;';
    commentsTitle.textContent = 'Actualizaciones';

    const commentsList = document.createElement('div');
    commentsList.style.cssText = 'max-height: 100px; overflow-y: auto; margin-bottom: 8px; font-size: 11px; display: flex; flex-direction: column; gap: 5px; padding-right: 4px;';
    commentsList.textContent = 'Cargando comentarios…';

    commentsSection.append(commentsTitle, commentsList);

    let commentsData = [];

    function renderComments() {
      commentsList.innerHTML = '';
      if (commentsData.length === 0) {
        const empty = document.createElement('div');
        empty.style.cssText = 'color: var(--text-muted); font-style: italic;';
        empty.textContent = 'Sin comentarios aún.';
        commentsList.appendChild(empty);
        return;
      }

      for (const c of commentsData) {
        const item = document.createElement('div');
        item.style.cssText = 'background: rgba(255,255,255,0.02); border-radius: 4px; padding: 4px 6px; border-left: 2px solid var(--info-blue);';

        const itemMeta = document.createElement('div');
        itemMeta.style.cssText = 'display: flex; justify-content: space-between; color: var(--text-muted); font-size: 9px; margin-bottom: 2px;';

        const name = document.createElement('span');
        name.style.fontWeight = '700';
        name.textContent = 'Anónimo';

        const time = document.createElement('span');
        time.textContent = new Date(c.created_at).toLocaleString('es-VE', {
          timeZone: 'America/Caracas',
          day: '2-digit',
          month: '2-digit',
          hour: '2-digit',
          minute: '2-digit',
          hour12: true
        });

        itemMeta.append(name, time);

        const text = document.createElement('div');
        text.style.cssText = 'color: var(--text-main); word-break: break-word; line-height: 1.3;';
        text.textContent = c.raw_text;

        item.append(itemMeta, text);
        commentsList.appendChild(item);
      }
      commentsList.scrollTop = commentsList.scrollHeight;
    }

    if (!navigator.onLine) {
      commentsList.style.color = 'var(--text-muted)';
      commentsList.textContent = 'Requiere conexión activa para ver o enviar comentarios.';
    } else {
      fetchComments(sit.id)
        .then((list) => {
          commentsData = list;
          renderComments();
        })
        .catch(() => {
          commentsList.style.color = 'var(--danger-red)';
          commentsList.textContent = 'Error al cargar comentarios.';
        });
    }

    const form = document.createElement('form');
    form.style.cssText = 'display: flex; flex-direction: column; gap: 4px;';

    const textarea = document.createElement('textarea');
    textarea.placeholder = 'Escribe una actualización (máx 300 caracteres)...';
    textarea.maxLength = 300;
    textarea.required = true;
    textarea.style.cssText =
      'width: 100%; height: 38px; resize: none; background: rgba(0,0,0,0.25); ' +
      'border: 1px solid var(--card-border); border-radius: 4px; color: var(--text-main); ' +
      'padding: 4px 6px; font-size: 11px; font-family: inherit; box-sizing: border-box;';

    const btnRow = document.createElement('div');
    btnRow.style.cssText = 'display: flex; justify-content: space-between; align-items: center;';

    const charCount = document.createElement('span');
    charCount.style.cssText = 'font-size: 9px; color: var(--text-muted);';
    charCount.textContent = '0 / 300';

    textarea.addEventListener('input', () => {
      charCount.textContent = `${textarea.value.length} / 300`;
    });

    const submitBtn = document.createElement('button');
    submitBtn.type = 'submit';
    submitBtn.textContent = 'Enviar';
    submitBtn.style.cssText =
      'background: var(--info-blue); border: none; color: #fff; font-family: "Outfit"; ' +
      'font-size: 10px; font-weight: 700; padding: 3px 10px; border-radius: 4px; cursor: pointer; ' +
      'transition: background 0.15s;';

    submitBtn.addEventListener('mouseenter', () => submitBtn.style.background = '#2c85e6');
    submitBtn.addEventListener('mouseleave', () => submitBtn.style.background = 'var(--info-blue)');

    btnRow.append(charCount, submitBtn);
    form.append(textarea, btnRow);

    if (!navigator.onLine) {
      textarea.disabled = true;
      submitBtn.disabled = true;
      submitBtn.style.background = 'rgba(255,255,255,0.05)';
      submitBtn.style.color = 'var(--text-muted)';
      submitBtn.style.cursor = 'not-allowed';
    }

    form.addEventListener('submit', (e) => {
      e.preventDefault();
      const text = textarea.value.trim();
      if (!text) return;

      submitBtn.disabled = true;
      submitBtn.textContent = 'Enviando…';

      sendComment(sit.id, text)
        .then((newComment) => {
          commentsData.push(newComment);
          renderComments();
          textarea.value = '';
          charCount.textContent = '0 / 300';
          submitBtn.disabled = false;
          submitBtn.textContent = 'Enviar';
        })
        .catch((err) => {
          console.error(err);
          showToast(err.message || 'No se pudo enviar el comentario.', true);
          submitBtn.disabled = false;
          submitBtn.textContent = 'Enviar';
        });
    });

    commentsSection.appendChild(form);
    wrap.append(commentsSection);

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
    marker.bindPopup(() => buildPopup(sit), { minWidth: 260, maxWidth: 320 });
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
        existing.marker.bindPopup(() => buildPopup(sit), { minWidth: 260, maxWidth: 320 });
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
  <div class="map-info-panel">
    <span class="info-pin">📌</span>
    <p class="info-text">
      Toca un punto en el mapa o un sector en el listado para ver los detalles del reporte.
    </p>
  </div>
  <div class="map-container">
    <div bind:this={mapEl} id="map"></div>
  </div>
</div>

<style>
  .map-info-panel {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 10px 14px;
    background: rgba(30, 41, 59, 0.45);
    border-left: 3px solid var(--info-blue);
    border-radius: 6px;
    margin-bottom: 14px;
  }
  .info-pin {
    font-size: 16px;
    flex-shrink: 0;
  }
  .info-text {
    font-size: 12.5px;
    line-height: 1.4;
    color: var(--text-main);
    margin: 0;
    font-weight: 500;
  }
  :global(.custom-marker-icon) {
    cursor: pointer !important;
  }
  :global(.custom-marker-icon:hover .interactive-marker-dot) {
    transform: scale(1.35);
    box-shadow: 0 0 15px rgba(255, 255, 255, 0.8) !important;
  }
  :global(.pulso-cluster-icon) {
    cursor: pointer !important;
  }
  :global(.pulso-cluster-icon:hover > div) {
    transform: scale(1.1);
    transition: transform 0.15s ease-out;
  }
</style>
