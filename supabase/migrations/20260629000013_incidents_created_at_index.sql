-- Índice compuesto para la paginación por cursor del Open Data API.
-- El endpoint público /api/v1/public/incidents ordena por (created_at, id) y filtra
-- con la comparación de tupla (created_at, id) > (@cursor). Sin este índice cada
-- página re-ordena la tabla completa; con él Postgres salta directo a la posición
-- del cursor y lee N filas en orden.
CREATE INDEX IF NOT EXISTS idx_incidents_created_at_id
    ON public.incidents (created_at, id);
