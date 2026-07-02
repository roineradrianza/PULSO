-- Nombre de la mascota (si el reporte lo menciona), para que el buscador
-- de la PWA pueda encontrar reportes por el nombre del animal.
alter table public.incidents add column if not exists pet_name varchar;
