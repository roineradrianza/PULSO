-- Nombre de la persona EN PELIGRO (buscada, atrapada, desaparecida o herida).
--
-- Hasta ahora solo guardábamos found_person_name: el nombre de quien fue reportado
-- a SALVO. Pero un reporte de búsqueda y rescate ("hay una persona atrapada llamada
-- María Alejandra") nombra a la persona en peligro, no a una persona encontrada. Ese
-- nombre quedaba solo en raw_text/transcribed_audio y no aparecía en ninguna vista
-- estructurada (lista de sectores), aunque para una plataforma de rescate es tan
-- crítico como saber quién está a salvo.
--
-- affected_person_name captura ese nombre. Es independiente y simétrico a
-- found_person_name: una persona "buscada" no es una persona "a salvo".
alter table public.incidents
    add column if not exists affected_person_name varchar;

-- Recrear process_and_deduplicate_incident agregando p_affected_person_name.
-- Como cambia la firma, eliminamos primero la versión previa de 15 argumentos
-- (CREATE OR REPLACE crearía un overload nuevo en vez de reemplazar).
DROP FUNCTION IF EXISTS public.process_and_deduplicate_incident(
    varchar, varchar, varchar, text, varchar, severity_level, text[],
    double precision, double precision, text, varchar, varchar, varchar, varchar, boolean
);

CREATE OR REPLACE FUNCTION public.process_and_deduplicate_incident(
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
    p_found_person_document varchar default null,
    p_triage_provider varchar default 'gemini',
    p_is_approximate boolean default false,
    p_affected_person_name varchar default null
)
RETURNS uuid as $$
DECLARE
    v_coordinates geometry := null;
    v_parent_id uuid := null;
    v_new_id uuid;
    v_status incident_status := 'NEW';
    v_is_gps boolean := false;
    v_is_person_report boolean := false;
BEGIN
    -- 1. Validar el origen de la localización
    IF p_latitude is not null AND p_longitude is not null THEN
        v_coordinates := ST_SetSRID(ST_MakePoint(p_longitude, p_latitude), 4326);
        -- Solo es GPS de hardware si las coordenadas NO provienen de geocodificación.
        v_is_gps := NOT p_is_approximate;
    ELSIF p_location is not null AND p_location != '' THEN
        v_coordinates := null;
        v_is_gps := false;
    ELSE
        v_status := 'PENDING_LOCATION';
    END IF;

    -- Reporte "de persona": búsqueda y rescate o con nombre de persona encontrada.
    v_is_person_report := p_category = 'SEARCH_AND_RESCUE'
        OR (p_found_person_name is not null AND p_found_person_name != '');

    -- 2. Deduplicación espacial: solo para puntos EXACTOS que no son de personas.
    --    Las ubicaciones aproximadas (geocodificadas) se excluyen.
    IF v_coordinates is not null AND NOT v_is_person_report AND NOT p_is_approximate THEN
        SELECT id INTO v_parent_id
        FROM public.incidents
        WHERE
            ai_category = p_category
            and status != 'DUPLICATE'
            and created_at >= now() - interval '4 hours'
            and ST_DWithin(coordinates::geography, v_coordinates::geography, 300)
        limit 1;

        IF v_parent_id is not null THEN
            v_status := 'DUPLICATE';
        END IF;
    END IF;

    -- 3. Registrar el incidente unificado en la base de datos
    INSERT INTO public.incidents (
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
        found_person_document,
        triage_provider,
        affected_person_name
    ) VALUES (
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
        p_found_person_document,
        p_triage_provider,
        p_affected_person_name
    )
    RETURNING id INTO v_new_id;

    RETURN v_new_id;
END;
$$ LANGUAGE plpgsql;
