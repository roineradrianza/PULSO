-- Agregar columnas de sector e información de personas encontradas a public.incidents
alter table public.incidents add column if not exists sector varchar(100);
alter table public.incidents add column if not exists found_person_name varchar(255);
alter table public.incidents add column if not exists found_person_document varchar(50);

-- Crear índices para búsquedas optimizadas
create index if not exists incidents_sector_idx on public.incidents(sector);
create index if not exists incidents_found_person_document_idx on public.incidents(found_person_document);

-- Recrear el procedimiento almacenado con los nuevos parámetros
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
    p_location text,
    p_sector varchar,
    p_found_person_name varchar default null,
    p_found_person_document varchar default null
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
        v_coordinates := null;
        v_is_gps := false;
    else
        v_status := 'PENDING_LOCATION';
    end if;

    -- 2. Si hay coordenadas válidas, buscar incidentes activos en el mismo sector y categoría (R = 300 metros, T = 4 horas)
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
        status,
        sector,
        found_person_name,
        found_person_document
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
        v_status,
        p_sector,
        p_found_person_name,
        p_found_person_document
    )
    returning id into v_new_id;

    return v_new_id;
end;
$$ language plpgsql;
