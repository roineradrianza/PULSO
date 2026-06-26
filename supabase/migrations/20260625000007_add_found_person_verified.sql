-- Estado de verificación de los reportes de "persona a salvo".
--
-- Todo reporte ciudadano entra como NO verificado (false): es un aporte sin
-- confirmar, no una confirmación oficial. Un operador de confianza puede
-- marcarlo true (incluso manualmente desde el panel de Supabase durante la
-- emergencia) para que la interfaz lo muestre como "Confirmado". La IA NUNCA
-- marca un reporte como verificado.
--
-- Invariante: un reporte de "a salvo" es su propio incidente y no cierra ni
-- resuelve automáticamente ningún caso (no se asigna 'RESOLVED' en ningún flujo).
--
-- Solo ADD COLUMN: no se recrea la función process_and_deduplicate_incident.
alter table public.incidents
    add column if not exists found_person_verified boolean not null default false;
