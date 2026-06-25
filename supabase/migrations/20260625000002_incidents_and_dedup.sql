-- Tabla principal de incidentes
create table if not exists public.incidents (
    id uuid default gen_random_uuid() primary key,
    external_message_id varchar(255) unique not null, -- Clave de idempotencia
    sender_phone varchar(50) not null,
    source_channel varchar(50) not null, -- 'whatsapp', 'telegram', 'pwa'
    raw_text text not null,
    transcribed_audio text, -- Transcripción de Whisper
    media_file_url text, -- Bucket storage de imágenes
    file_type varchar(50), -- 'image', 'audio', 'location'

    -- Campos de Estructuración de IA (En inglés)
    ai_category varchar(100),
    severity severity_level default 'LOW',
    tags text[],
    parent_incident_id uuid references public.incidents(id) on delete set null, -- Enlace para deduplicación
    status incident_status default 'NEW',

    -- Campos Espaciales (PostGIS)
    coordinates geometry(Point, 4326), -- Coordenadas espaciales estándar WGS 84
    declared_location text, -- Dirección aproximada escrita en lenguaje natural
    is_hardware_gps boolean default false, -- Flag de validación de hardware GPS
    created_at timestamp with time zone default timezone('utc'::text, now()) not null,
    updated_at timestamp with time zone default timezone('utc'::text, now()) not null
);

-- Índices espaciales y relacionales para consultas de alto volumen
create index if not exists incidents_coordinates_spatial_idx on public.incidents using gist(coordinates);
create index if not exists incidents_status_idx on public.incidents(status);
create index if not exists incidents_severity_idx on public.incidents(severity);

-- Habilitar publicación en tiempo real de Supabase si existe
do $$
begin
  if exists (select 1 from pg_publication where pubname = 'supabase_realtime') then
    alter publication supabase_realtime add table public.incidents;
  end if;
end $$;

-- Procedimiento de Deduplicación Espacial en Base de Datos
create or replace function public.process_and_deduplicate_incident(
    p_message_id varchar,
    p_phone varchar,
    p_channel varchar,
    p_raw_text text,
    p_category varchar,
    p_severity severity_level,
    p_tags text[],
    p_latitude double precision,
    p_longitude double precision,
    p_location text
)
returns uuid as $$
declare
    v_coordinates geometry := null;
    v_parent_id uuid := null;
    v_new_id uuid;
    v_status incident_status := 'NEW';
    v_is_gps boolean := false;
begin
    -- 1. Validar el origen de la localización
    if p_latitude is not null and p_longitude is not null then
        v_coordinates := ST_SetSRID(ST_MakePoint(p_longitude, p_latitude), 4326);
        v_is_gps := true;
    elsif p_location is not null and p_location != '' then
        -- Si se provee ubicación escrita pero no hay coordenadas satelitales duras de GPS
        v_coordinates := null;
        v_is_gps := false;
    else
        v_status := 'PENDING_LOCATION';
    end if;

    -- 2. Si hay coordenadas válidas, buscar incidentes activos de la misma categoría (R = 300 metros, T = 4 horas)
    if v_coordinates is not null then
        select id into v_parent_id
        from public.incidents
        where
            ai_category = p_category
            and status != 'DUPLICATE'
            and created_at >= now() - interval '4 hours'
            and ST_DWithin(coordinates, v_coordinates, 300)
        limit 1;

        if v_parent_id is not null then
            v_status := 'DUPLICATE';
        end if;
    end if;

    -- 3. Registrar el incidente unificado en la base de datos
    insert into public.incidents (
        external_message_id,
        sender_phone,
        source_channel,
        raw_text,
        ai_category,
        severity,
        tags,
        coordinates,
        declared_location,
        is_hardware_gps,
        parent_incident_id,
        status
    ) values (
        p_message_id,
        p_phone,
        p_channel,
        p_raw_text,
        p_category,
        p_severity,
        p_tags,
        v_coordinates,
        p_location,
        v_is_gps,
        v_parent_id,
        v_status
    )
    returning id into v_new_id;

    return v_new_id;
end;
$$ language plpgsql;
