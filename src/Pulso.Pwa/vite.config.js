import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { VitePWA } from 'vite-plugin-pwa';

// Configuración Vite para el portal PULSO.
// - Svelte: UI por componentes con auto-escape (mitiga XSS de raíz).
// - vite-plugin-pwa (Workbox): service worker versionado + precaching + actualización automática.
export default defineConfig({
  // En desarrollo, redirigir /api al backend para servir todo desde el mismo origen
  // (http://localhost:5173) y así no necesitar CORS. Ajustable con VITE_API_TARGET.
  server: {
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5152',
        changeOrigin: true
      }
    }
  },
  plugins: [
    svelte(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'PULSO - Plataforma Unificada de Lectura y Seguimiento Offline',
        short_name: 'PULSO',
        description: 'Reporte y seguimiento de emergencias en tiempo real, con soporte offline.',
        lang: 'es',
        theme_color: '#0b0f19',
        background_color: '#0b0f19',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: 'icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: 'icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: 'icons/icon-512-maskable.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      },
      workbox: {
        // Precachear el app shell (todo lo que sale del build)
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff,woff2}'],
        // Cachear los azulejos del mapa en tiempo de ejecución para resiliencia offline
        runtimeCaching: [
          {
            urlPattern: ({ url }) => url.hostname.endsWith('basemaps.cartocdn.com'),
            handler: 'CacheFirst',
            options: {
              cacheName: 'pulso-map-tiles',
              expiration: { maxEntries: 600, maxAgeSeconds: 60 * 60 * 24 * 30 },
              cacheableResponse: { statuses: [0, 200] }
            }
          }
        ]
      },
      devOptions: {
        // Permite probar el SW también en `npm run dev`
        enabled: false
      }
    })
  ]
});
