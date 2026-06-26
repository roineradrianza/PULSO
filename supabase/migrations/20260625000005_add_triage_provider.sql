-- Migración para soportar métricas del motor de triaje
ALTER TABLE public.incidents ADD COLUMN IF NOT EXISTS triage_provider VARCHAR(50) DEFAULT 'gemini';

-- Recreamos la función process_and_deduplicate_incident con soporte para p_triage_provider
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

    -- 2. Si hay coordenadas válidas, buscar incidentes activos en el mismo sector y categoría (R = 300 metros, T = 4 horas)
    IF v_coordinates is not null THEN
        SELECT id INTO v_parent_id
        FROM public.incidents
        WHERE
            ai_category = p_category
            and status != 'DUPLICATE'
            and created_at >= now() - interval '4 hours'
            and ST_DWithin(coordinates, v_coordinates, 300)
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
