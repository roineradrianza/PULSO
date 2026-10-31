<script>
  import { onMount, onDestroy } from 'svelte';
  import L from 'leaflet';
  import 'leaflet/dist/leaflet.css';
  import 'leaflet.markercluster';
  import 'leaflet.markercluster/dist/MarkerCluster.css';
  import { situations, sectorStats, mapFocus, isMobile, showToast, reportFilter } from '../lib/stores.js';
  import { fetchSituationDetail, fetchComments, sendComment } from '../lib/api.js';
  import BottomSheet from './BottomSheet.svelte';

  let mapEl;
  let cardEl;
  let map;
  let clusterGroup;
  let sheet;
  let mobile = false;
  let unsubSituations;
  let unsubFocus;
  let unsubSectors;
  let unsubMobile;
  let unsubFilter;
  // Último snapshot de situaciones: permite re-proyectar los pines al cambiar
  // el filtro sin esperar una nueva emisión del store.
  let lastList = [];
  let filterMode = 'all';
  // El mapa se auto-centra UNA sola vez en el sector más crítico al cargar. Pasa a true
  // tras ese primer enfoque, o si el usuario interactúa/clica un sector antes, para no
  // arrancarle el mapa en los polls siguientes.
  let autoFocused = false;

  // Estado de diff: id -> { marker, sig } para actualizar sin reconstruir todo.
  const markersById = new Map();
  // Caché de detalle ya descargado, por id: { text, mediaUrl }.
  const detailCache = new Map();

  const SEVERITY_RANK = { LOW: 0, MEDIUM: 1, HIGH: 2, CRITICAL: 3 };
  const RANK_COLOR = ['var(--info-blue)', 'var(--warning-amber)', 'var(--accent-orange)', 'var(--danger-red)'];
  const PET_COLOR = 'var(--pet-gold)';

  function isPetReport(sit) {
    return sit.category === 'LOST_FOUND_PET';
  }

  // Visibilidad de un reporte según el filtro global por tipo.
  function matchesFilter(sit) {
    if (filterMode === 'pets') return isPetReport(sit);
    if (filterMode === 'incidents') return !isPetReport(sit);
    return true;
  }

  function severityColor(sit) {
    if (isPetReport(sit)) return PET_COLOR;
    if (sit.is_person_found) {
      return sit.found_person_verified ? 'var(--success-green)' : '#7ea085';
    }
    return RANK_COLOR[SEVERITY_RANK[sit.severity] ?? 0];
  }

  // Firma de lo que afecta el render del marcador (para detectar cambios).
  function signature(sit) {
    return `${sit.latitude},${sit.longitude},${sit.severity},${sit.is_person_found},${sit.found_person_verified},${sit.is_hardware_gps},${sit.needs_review},${sit.affected_person_name ?? ''},${sit.category ?? ''},${sit.pet_report_type ?? ''}`;
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
    if (isPetReport(sit)) {
      return L.divIcon({
        html: `<div class="interactive-marker-dot" style="background-color: ${color}; width: 20px; height: 20px; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 10px ${color}; display: flex; align-items: center; justify-content: center; font-size: 11px; transition: transform 0.15s ease-out;">🐾</div>`,
        className: 'custom-marker-icon',
        iconSize: [20, 20],
        iconAnchor: [10, 10]
      });
    }
    return L.divIcon({
      html: `<div class="interactive-marker-dot" style="background-color: ${color}; width: 14px; height: 14px; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 10px ${color}; transition: transform 0.15s ease-out;"></div>`,
      className: 'custom-marker-icon',
      iconSize: [14, 14],
      iconAnchor: [7, 7]
    });
  }

  // Overlay de pantalla completa para ver la foto del reporte a tamaño real.
  // Se agrega directo al body (no al popup/sheet) para quedar por encima de
  // ambos sin importar cuál esté abierto.
  function openLightbox(url) {
    const backdrop = document.createElement('div');
    backdrop.className = 'pulso-lightbox-backdrop';

    const img = document.createElement('img');
    img.src = url;
    img.alt = 'Foto ampliada del reporte';
    img.className = 'pulso-lightbox-img';

    const closeBtn = document.createElement('button');
    closeBtn.className = 'pulso-lightbox-close';
    closeBtn.textContent = '✕';
    closeBtn.setAttribute('aria-label', 'Cerrar');

    function close() {
      backdrop.remove();
      document.removeEventListener('keydown', onKeydown);
    }
    function onKeydown(e) {
      if (e.key === 'Escape') close();
    }

    backdrop.addEventListener('click', (e) => {
      if (e.target === backdrop) close();
    });
    closeBtn.addEventListener('click', close);
    document.addEventListener('keydown', onKeydown);

    backdrop.append(img, closeBtn);
    document.body.appendChild(backdrop);
  }

  // Popup construido con nodos DOM + textContent (sin XSS). El texto pesado
  // (raw_text) se carga BAJO DEMANDA al abrir, con estado de carga.
  function buildPopup(sit) {
    const color = severityColor(sit);
    const wrap = document.createElement('div');
    wrap.style.fontFamily = "'Inter', sans-serif";

    const title = document.createElement('h4');
    title.style.cssText = `margin-bottom: 4px; font-family: 'Outfit'; color: ${color};`;
    if (isPetReport(sit)) {
      title.textContent = sit.pet_report_type === 'LOST' ? '🐾 Mascota perdida' : '🐾 Mascota encontrada';
    } else {
      title.textContent = sit.is_person_found ? '🟡 Persona reportada a salvo' : '⚠️ Reporte de Incidente';
    }

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

    // Foto del reporte (si tiene): oculta hasta que se resuelve el detalle, cachea
    // junto con el texto para no repetir la descarga al reabrir el popup. El
    // skeleton se muestra mientras la imagen carga (no solo mientras se resuelve
    // el detalle: la URL puede tardar en traer los bytes aunque el detalle ya llegó).
    const photoSkeleton = document.createElement('div');
    photoSkeleton.className = 'photo-skeleton';
    photoSkeleton.style.display = 'none';

    const photo = document.createElement('img');
    photo.alt = 'Foto del reporte';
    photo.style.cssText =
      'width: 100%; max-height: 140px; object-fit: cover; border-radius: 6px; ' +
      'margin-bottom: 6px; display: none;';

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
    date.textContent = `Fecha: ${new Date(sit.created_at).toLocaleString('es-VE', {
      timeZone: 'America/Caracas',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    })}`;
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
    if (sit.pet_name) {
      const petName = document.createElement('div');
      petName.style.cssText = `color: ${PET_COLOR}; font-weight: 600;`;
      petName.textContent = `Nombre: ${sit.pet_name}`;
      meta.append(petName);
    }

    wrap.append(title);
    if (verificationBadge) {
      wrap.append(verificationBadge);
    }
    wrap.append(confidence);
    if (unverifiedNote) {
      wrap.append(unverifiedNote);
    }
    wrap.append(photoSkeleton, photo, body, meta);

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
          day: 'numeric',
          month: 'short',
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

    function applyDetail(text, mediaUrl) {
      body.textContent = text || '(sin detalle)';
      if (mediaUrl) {
        photoSkeleton.style.display = 'block';
        photo.onload = () => {
          photoSkeleton.style.display = 'none';
          photo.style.display = 'block';
        };
        // Si la foto falla, se retira el skeleton sin mostrar un ícono de error:
        // el resto del popup (texto, ubicación) sigue siendo útil sin ella.
        photo.onerror = () => {
          photoSkeleton.style.display = 'none';
        };
        photo.style.cursor = 'zoom-in';
        photo.onclick = () => openLightbox(mediaUrl);
        photo.src = mediaUrl;
      }
    }

    // Detalle lazy: usar caché o descargar.
    const cached = detailCache.get(sit.id);
    if (cached !== undefined) {
      applyDetail(cached.text, cached.mediaUrl);
    } else {
      body.textContent = 'Cargando detalle…';
      fetchSituationDetail(sit.id)
        .then((d) => {
          const text = d?.raw_text ?? '';
          const mediaUrl = d?.media_file_url ?? null;
          detailCache.set(sit.id, { text, mediaUrl });
          applyDetail(text, mediaUrl);
        })
        .catch(() => {
          body.textContent = '(no se pudo cargar el detalle)';
        });
    }

    return wrap;
  }

  // En móvil el detalle se abre en el bottom sheet (más espacio para foto/
  // comentarios); en desktop sigue siendo el popup chico de Leaflet. Ambos
  // reutilizan el mismo buildPopup(sit), solo cambia dónde se monta el nodo.
  function bindMarkerInteraction(marker, sit) {
    marker.unbindPopup();
    marker.off('click');
    if (mobile) {
      marker.on('click', () => sheet.open(buildPopup(sit)));
    } else {
      marker.bindPopup(() => buildPopup(sit), { minWidth: 260, maxWidth: 320, maxHeight: 260 });
    }
  }

  function makeMarker(sit) {
    const marker = L.marker([sit.latitude, sit.longitude], { icon: buildIcon(sit) });
    bindMarkerInteraction(marker, sit);
    // Severidad y tipo accesibles para colorear el cluster.
    marker.pulsoSeverity = sit.severity;
    marker.pulsoIsPet = isPetReport(sit);
    return marker;
  }

  // Diff incremental: agrega nuevos, actualiza cambiados, elimina ausentes.
  function syncMarkers(list) {
    if (!clusterGroup) return;

    const seen = new Set();
    const toAdd = [];

    for (const sit of list.filter(matchesFilter)) {
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
        existing.marker.pulsoIsPet = isPetReport(sit);
        bindMarkerInteraction(existing.marker, sit);
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

  // Encuadra el mapa al conjunto de reportes visibles con el filtro activo,
  // en vez de centrarlo en uno solo (elegir "el primero" sería arbitrario).
  // En móvil además trae el mapa a la vista, que puede estar fuera de pantalla.
  function fitToFiltered() {
    const pts = lastList
      .filter((s) => s.latitude && s.longitude && matchesFilter(s))
      .map((s) => [s.latitude, s.longitude]);
    if (pts.length === 0) return;
    autoFocused = true; // el usuario pidió este encuadre: cancela el auto-enfoque inicial
    map.flyToBounds(L.latLngBounds(pts).pad(0.2), { maxZoom: 15 });
    if (mobile && cardEl) cardEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  // Ícono del cluster: color = severidad más alta que contiene + conteo.
  // Si el cluster solo agrupa mascotas hereda su dorado, para que el color
  // de severidad no sugiera una emergencia humana donde no la hay.
  function clusterIcon(cluster) {
    const children = cluster.getAllChildMarkers();
    let color;
    if (children.every((m) => m.pulsoIsPet)) {
      color = PET_COLOR;
    } else {
      let rank = 0;
      for (const m of children) {
        const r = SEVERITY_RANK[m.pulsoSeverity] ?? 0;
        if (r > rank) rank = r;
      }
      color = RANK_COLOR[rank];
    }
    const count = cluster.getChildCount();
    return L.divIcon({
      html: `<div style="background-color: ${color}; color: #fff; width: 36px; height: 36px; display: flex; align-items: center; justify-content: center; border: 2px solid #ffffff; border-radius: 50%; box-shadow: 0 0 12px ${color}; font-family: 'Outfit', sans-serif; font-weight: 700; font-size: 13px;">${count}</div>`,
      className: 'pulso-cluster-icon',
      iconSize: [36, 36]
    });
  }

  onMount(() => {
    map = L.map(mapEl, { center: [10.4806, -66.9036], zoom: 12, zoomControl: true });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution:
        '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      subdomains: 'abc',
      maxZoom: 19
    }).addTo(map);

    clusterGroup = L.markerClusterGroup({
      chunkedLoading: true,
      iconCreateFunction: clusterIcon
    });
    map.addLayer(clusterGroup);

    // Cualquier gesto del usuario sobre el mapa cancela el auto-enfoque inicial.
    // (mousedown/wheel/touchstart son gestos reales; no se disparan por setView programático.)
    ['mousedown', 'wheel', 'touchstart'].forEach((ev) =>
      mapEl.addEventListener(ev, () => { autoFocused = true; }, { once: true, passive: true })
    );

    unsubMobile = isMobile.subscribe((v) => { mobile = v; });
    unsubSituations = situations.subscribe((list) => {
      lastList = list;
      syncMarkers(list);
    });

    // Al cambiar el filtro se re-proyectan los pines y el mapa vuela a
    // encuadrarlos: el usuario ve de inmediato dónde está lo que pidió.
    unsubFilter = reportFilter.subscribe((mode) => {
      if (mode === filterMode) return;
      filterMode = mode;
      syncMarkers(lastList);
      fitToFiltered();
    });
    unsubFocus = mapFocus.subscribe((f) => {
      if (f && map) {
        autoFocused = true; // un enfoque manual (clic en un sector) cancela el auto-enfoque
        map.setView([f.lat, f.lng], f.zoom ?? 14);
      }
    });

    // Auto-enfoque: la primera vez que llegan sectores (vienen ordenados por criticidad
    // desde el backend), centra el mapa en el primero que tenga coordenadas. Solo una vez.
    unsubSectors = sectorStats.subscribe((list) => {
      if (autoFocused || !map || !list || list.length === 0) return;
      const top = list.find((s) => s.latitude != null && s.longitude != null);
      if (top) {
        autoFocused = true;
        map.setView([top.latitude, top.longitude], 13);
      }
    });
  });

  onDestroy(() => {
    if (unsubSituations) unsubSituations();
    if (unsubFocus) unsubFocus();
    if (unsubSectors) unsubSectors();
    if (unsubMobile) unsubMobile();
    if (unsubFilter) unsubFilter();
    if (map) map.remove();
  });
</script>

<div class="card" style="padding: 20px;" bind:this={cardEl}>
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

<BottomSheet bind:this={sheet} />

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
  :global(#map) {
    background-color: var(--bg-color);
  }
  :global(.leaflet-tile-pane) {
    filter: invert(100%) hue-rotate(180deg) saturate(60%) brightness(92%) contrast(90%);
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
  :global(.leaflet-popup-content, .sheet-content) {
    overflow-y: auto !important;
    padding-right: 6px !important;
    box-sizing: border-box;
  }
  :global(.leaflet-popup-content::-webkit-scrollbar, .sheet-content::-webkit-scrollbar) {
    width: 4px;
  }
  :global(.leaflet-popup-content::-webkit-scrollbar-track, .sheet-content::-webkit-scrollbar-track) {
    background: rgba(255, 255, 255, 0.02);
    border-radius: 4px;
  }
  :global(.leaflet-popup-content::-webkit-scrollbar-thumb, .sheet-content::-webkit-scrollbar-thumb) {
    background: rgba(255, 255, 255, 0.15);
    border-radius: 4px;
  }
  :global(.leaflet-popup-content::-webkit-scrollbar-thumb:hover, .sheet-content::-webkit-scrollbar-thumb:hover) {
    background: rgba(255, 255, 255, 0.3);
  }
  :global(.photo-skeleton) {
    width: 100%;
    height: 140px;
    border-radius: 6px;
    margin-bottom: 6px;
    background: linear-gradient(
      90deg,
      rgba(255, 255, 255, 0.04) 25%,
      rgba(255, 255, 255, 0.09) 37%,
      rgba(255, 255, 255, 0.04) 63%
    );
    background-size: 400% 100%;
    animation: pulso-skeleton-shimmer 1.4s ease infinite;
  }
  @keyframes pulso-skeleton-shimmer {
    0% {
      background-position: 100% 50%;
    }
    100% {
      background-position: 0 50%;
    }
  }
  :global(.pulso-lightbox-backdrop) {
    position: fixed;
    inset: 0;
    z-index: 2500;
    background: rgba(0, 0, 0, 0.85);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 24px;
    box-sizing: border-box;
  }
  :global(.pulso-lightbox-img) {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
    border-radius: 6px;
  }
  :global(.pulso-lightbox-close) {
    position: fixed;
    top: 16px;
    right: 16px;
    background: rgba(255, 255, 255, 0.1);
    border: none;
    color: #fff;
    width: 36px;
    height: 36px;
    border-radius: 50%;
    font-size: 16px;
    line-height: 1;
    cursor: pointer;
  }
</style>
