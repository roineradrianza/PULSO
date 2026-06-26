-- Soporte de ubicaciones APROXIMADAS por geocodificación.
--
-- Cuando un reporte no trae GPS de hardware, el worker geocodifica la dirección o
-- sector inferidos por la IA y pasa esas coordenadas con p_is_approximate = true.
-- En ese caso:
--   * is_hardware_gps queda en false  -> la UI lo muestra como "Ubicación aproximada".
--   * el incidente NO entra a la deduplicación espacial: un centroide aproximado es
--     impreciso y podría fusionar o separar incidentes incorrectamente.
--
-- Mantiene lo anterior: búsqueda de personas nunca se deduplica y el cast a
-- ::geography para medir el radio en metros.
--
-- Como se agrega un parámetro, eliminamos primero la versión previa de 14
-- argumentos: CREATE OR REPLACE crearía un overload nuevo en vez de reemplazar,
-- y dos firmas con defaults harían ambiguas las llamadas.
DROP FUNCTION IF EXISTS public.process_and_deduplicate_incident(
    varchar, varchar, varchar, text, varchar, severity_level, text[],
    double precision, double precision, text, varchar, varchar, varchar, varchar
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
    p_is_approximate boolean default false
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
        triage_provider
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
        p_triage_provider
    )
    RETURNING id INTO v_new_id;

    RETURN v_new_id;
END;
$$ LANGUAGE plpgsql;
