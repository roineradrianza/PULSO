-- Objetivo: evitar que la deduplicación espacial colapse personas distintas.
--
-- Contexto: en escenarios de búsqueda y rescate, dos personas desaparecidas en
-- el mismo derrumbe (misma categoría, dentro de 300 m y 4 h) podrían fusionarse
-- en un solo incidente, haciendo "desaparecer" a una de ellas del mapa. Para
-- vidas humanas preferimos un falso duplicado visible que una persona perdida.
--
-- Cambios respecto a la versión anterior (0005):
--   1. La deduplicación NO se aplica cuando el incidente es de búsqueda de
--      personas: categoría 'SEARCH_AND_RESCUE' o cuando trae nombre de persona
--      encontrada. Esos reportes se insertan siempre como NEW.
--   2. Se RESTAURA el cast a ::geography en ST_DWithin (la 0005 lo perdió por
--      accidente). Sin el cast, 300 se interpreta en grados y no en metros, lo
--      que fusiona prácticamente todo. IMPORTANTE: mantener este cast en futuras
--      recreaciones de la función.
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
    p_triage_provider varchar default 'gemini'
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
        v_is_gps := true;
    ELSIF p_location is not null AND p_location != '' THEN
        v_coordinates := null;
        v_is_gps := false;
    ELSE
        v_status := 'PENDING_LOCATION';
    END IF;

    -- Un reporte se considera "de persona" si es búsqueda y rescate o trae el
    -- nombre de una persona encontrada. Estos NUNCA se deduplican.
    v_is_person_report := p_category = 'SEARCH_AND_RESCUE'
        OR (p_found_person_name is not null AND p_found_person_name != '');

    -- 2. Deduplicación espacial SOLO para incidentes que no son de personas.
    --    Mismo sector/categoría, R = 300 metros, T = 4 horas.
    --    Se castea a geography para que ST_DWithin calcule la distancia en metros.
    IF v_coordinates is not null AND NOT v_is_person_report THEN
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
