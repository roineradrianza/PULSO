-- Buffer de "ubicación pendiente" para soportar el flujo ubicación → descripción.
--
-- El flujo natural del bot es: el ciudadano describe el incidente y luego comparte
-- su ubicación (que se adjunta al incidente con TryAttachLocationToRecentAsync).
-- Pero algunos usuarios comparten su ubicación PRIMERO, antes de escribir nada.
-- En ese momento no existe ningún incidente al cual adjuntarla.
--
-- En vez de descartar esa ubicación (perdiéndola y forzando al usuario a enviarla
-- de nuevo), la guardamos aquí, asociada al remitente. Cuando luego llega la
-- descripción sin GPS propio, el worker consume esta ubicación y la usa como
-- coordenada EXACTA del incidente. Así el orden de los mensajes no importa.
--
-- Es un buffer efímero, no parte del modelo de incidentes: una fila por remitente
-- (upsert), consumida y borrada al crearse el incidente, y expirada por ventana
-- de tiempo (2 h) en el worker. Vive en DB (no en Redis) para sobrevivir reinicios.
create table if not exists public.pending_locations (
    source_channel text        not null,
    sender_phone   text        not null,
    latitude       double precision not null,
    longitude      double precision not null,
    created_at     timestamptz not null default now(),
    primary key (source_channel, sender_phone)
);

-- Índice para purgar/consultar por antigüedad (ventana de 2 h).
create index if not exists idx_pending_locations_created_at
    on public.pending_locations (created_at);
