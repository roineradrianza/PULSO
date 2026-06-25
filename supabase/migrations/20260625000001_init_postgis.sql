-- Habilitar la extensión espacial PostGIS
create extension if not exists postgis;

-- Tipos enumerados para categorización y estados de PULSO
create type severity_level as enum ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
create type incident_status as enum ('NEW', 'PROCESSING', 'ASSIGNED', 'RESOLVED', 'DUPLICATE', 'PENDING_LOCATION');
