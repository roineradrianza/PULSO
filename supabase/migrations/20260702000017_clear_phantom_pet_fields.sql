-- Los campos de mascota solo tienen sentido en reportes LOST_FOUND_PET. El esquema
-- estructurado del triaje podía forzar un pet_report_type en reportes humanos
-- (el enum no admitía "no aplica"); esto limpia esos registros fantasma.
update public.incidents
set pet_report_type = null, pet_name = null
where ai_category is distinct from 'LOST_FOUND_PET'
  and (pet_report_type is not null or pet_name is not null);

-- "Sin nombre" es ausencia de dato: normalizar cadenas vacías a NULL para que los
-- agregados por sector no dependan de limpiar cadenas en cada consumidor.
update public.incidents
set pet_name = null
where pet_name = '';
