import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'

const backend = process.env.NOPDS_BACKEND ?? 'http://127.0.0.1:5249'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['favicon.ico', 'favicon.svg', 'apple-touch-icon-180x180.png', 'mask-icon.svg', 'robots.txt', 'browserconfig.xml'],
      manifest: {
        id: '/',
        name: '.NET OPDS by CHDS',
        short_name: '.NET OPDS',
        description: 'Your e-book library: browse, search, read and sync over OPDS.',
        lang: 'en',
        dir: 'ltr',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        display_override: ['window-controls-overlay', 'standalone'],
        orientation: 'any',
        theme_color: '#8f4f17',
        background_color: '#f6f1e8',
        categories: ['books', 'education', 'entertainment'],
        icons: [
          { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          { src: 'maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
        shortcuts: [
          { name: 'Search', short_name: 'Search', url: '/search', icons: [{ src: 'pwa-192x192.png', sizes: '192x192' }] },
          { name: 'My bookshelf', short_name: 'Shelf', url: '/shelf', icons: [{ src: 'pwa-192x192.png', sizes: '192x192' }] },
          { name: 'New books', short_name: 'New', url: '/books?sort=added', icons: [{ src: 'pwa-192x192.png', sizes: '192x192' }] },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        maximumFileSizeToCacheInBytes: 4 * 1024 * 1024,
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/opds/, /^\/kosync/, /^\/hubs/, /^\/health/, /^\/openapi/],
        runtimeCaching: [
          {
            // Covers and thumbnails: stable URLs, cache first.
            urlPattern: ({ url }) => /^\/opds\/(t\/[^/]+\/)?(thumb|cover)\/\d+/.test(url.pathname),
            handler: 'CacheFirst',
            options: { cacheName: 'covers', expiration: { maxEntries: 3000, maxAgeSeconds: 60 * 60 * 24 * 30 }, cacheableResponse: { statuses: [200] } },
          },
          {
            // Opened books stay available for offline reading.
            urlPattern: ({ url }) => /^\/api\/v1\/books\/\d+\/content$/.test(url.pathname),
            handler: 'CacheFirst',
            options: { cacheName: 'books', expiration: { maxEntries: 30 }, cacheableResponse: { statuses: [200] } },
          },
          {
            urlPattern: ({ url, request }) => url.pathname.startsWith('/api/v1/') && !url.pathname.startsWith('/api/v1/auth') && request.method === 'GET',
            handler: 'NetworkFirst',
            options: { cacheName: 'api', networkTimeoutSeconds: 5, expiration: { maxEntries: 500, maxAgeSeconds: 60 * 60 * 24 * 7 }, cacheableResponse: { statuses: [200] } },
          },
        ],
      },
      devOptions: { enabled: false },
    }),
  ],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 1500,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': backend,
      '/opds': backend,
      '/kosync': backend,
      '/health': backend,
      '/hubs': { target: backend, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
  },
})
