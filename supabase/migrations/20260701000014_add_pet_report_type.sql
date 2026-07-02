-- Señal perdida/encontrada para reportes de mascotas (categoría LOST_FOUND_PET).
-- Nullable: solo se completa cuando el triaje detecta un reporte de mascota.
alter table public.incidents add column if not exists pet_report_type varchar(10);
