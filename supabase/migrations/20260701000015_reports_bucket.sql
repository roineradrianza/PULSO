-- Bucket público único para fotos de reportes
insert into storage.buckets (id, name, public)
values ('reports', 'reports', true)
on conflict (id) do nothing;
