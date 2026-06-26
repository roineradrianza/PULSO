# PULSO PWA

Portal Público de Situación de Emergencias — PWA offline-first.

## Stack
- **Vite** — build, dev server, tree-shaking.
- **Svelte** — UI por componentes con **auto-escape** (mitiga XSS de raíz).
- **Dexie** — cola offline sobre IndexedDB (`async/await`).
- **vite-plugin-pwa (Workbox)** — service worker versionado, precaching y actualización automática.
- **Leaflet** + **@fontsource** — mapa y fuentes **self-hosted** (sin depender de CDNs externos, mejor resiliencia offline).

## Requisitos
- Node.js 18+ y [pnpm](https://pnpm.io) 9+ (`corepack enable` o `npm i -g pnpm`).

## Scripts
```bash
pnpm install      # instalar dependencias
pnpm dev          # servidor de desarrollo (http://localhost:5173)
pnpm build        # build de producción -> dist/
pnpm preview      # previsualizar el build de producción
```

> El gestor de paquetes está fijado con `packageManager` en `package.json`.
> Con Corepack (`corepack enable`) se usará automáticamente la versión correcta de pnpm.

## Configuración y mismo origen
La app llama a la API con **rutas relativas** (`/api/...`), sirviéndose todo bajo el
**mismo origen** — así no se necesita CORS:

- **Desarrollo:** el proxy de Vite reenvía `/api` al backend (`http://localhost:5152`
  por defecto, configurable con `VITE_API_TARGET`).
- **Producción:** el reverse proxy [Caddy](../../Caddyfile) sirve la PWA y enruta
  `/api` al `Pulso.IngressApi` bajo un solo dominio con HTTPS/HTTP3 automáticos.

Solo define `VITE_API_BASE_URL` si necesitas apuntar a una API en **otro** origen
(ver `.env.example`).

## Iconos del manifest (pendiente)
El `manifest` referencia iconos en `public/icons/` que **debes agregar** como PNG:
- `public/icons/icon-192.png` (192×192)
- `public/icons/icon-512.png` (512×512)
- `public/icons/icon-512-maskable.png` (512×512, maskable)

Sin ellos la app funciona, pero la instalación PWA no mostrará icono.

## Estructura
```
src/
  main.js                # bootstrap (CSS global + fuentes)
  app.css                # estilos globales
  App.svelte             # layout principal + ciclo de vida (red, polling)
  lib/
    api.js               # cliente HTTP de la API
    db.js                # cola offline (Dexie)
    data.js              # carga de situaciones/estadísticas
    geo.js               # captura GPS
    stores.js            # estado reactivo + toasts
  components/
    Header.svelte
    SyncCard.svelte
    StatsDashboard.svelte
    MapView.svelte       # Leaflet; popups con DOM/textContent (sin XSS)
    SectorsList.svelte
    ReportForm.svelte
    Toast.svelte
```
