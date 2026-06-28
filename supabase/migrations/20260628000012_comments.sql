-- Tabla para comentarios/actualizaciones sobre incidentes reportados
CREATE TABLE IF NOT EXISTS public.comments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    incident_id UUID NOT NULL REFERENCES public.incidents(id) ON DELETE CASCADE,
    raw_text VARCHAR(300) NOT NULL, -- Límite estricto de 300 caracteres
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);

-- Índice para optimizar la carga de comentarios al abrir incidentes
CREATE INDEX IF NOT EXISTS idx_comments_incident_id ON public.comments(incident_id);
