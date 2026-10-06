import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

// `pnpm dev` proxies /api (and the live WebSocket on /ws) to a running CouchPilot; set COUCHPILOT_HOST to the PC's address.
export default defineConfig({
  plugins: [
    vue(),
    // The service worker keeps the app itself on the phone, so it opens at once, and says
    // «Offline» rather than showing Chrome's error page when the PC is off. It only registers
    // over HTTPS (or on localhost). The manifest stays a plain file in public/; main.ts registers it.
    VitePWA({
      injectRegister: false,
      manifest: false,
      workbox: {
        // A new version takes over at once: main.ts then reloads the page onto it, so the phone
        // never keeps running an app older than the extension it talks to.
        skipWaiting: true,
        clientsClaim: true,
        globPatterns: ['**/*.{js,css,html,svg,png,woff2,webmanifest}'],
        // Everything under /api is the PC's, live; only the game art and capture thumbnails below are kept.
        navigateFallbackDenylist: [/^\/api\//, /^\/ws/],
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            // Covers, backgrounds and icons: shown from the phone straight away, refreshed behind.
            urlPattern: ({ url, sameOrigin }) => sameOrigin && /^\/api\/games\/[^/]+\/(cover|background|icon)$/.test(url.pathname),
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'game-art',
              // Only real images: art Playnite links on the web arrives as an opaque redirect,
              // which costs megabytes of quota apiece for nothing we can show offline.
              cacheableResponse: { statuses: [200] },
              expiration: { maxEntries: 600, purgeOnQuotaError: true },
            },
          },
          {
            // Capture thumbnails: their URL carries the file's version, so a kept one is never stale.
            urlPattern: ({ url, sameOrigin }) => sameOrigin && /^\/api\/captures\/[^/]+\/thumb$/.test(url.pathname),
            handler: 'CacheFirst',
            options: {
              cacheName: 'capture-thumbnails',
              cacheableResponse: { statuses: [200] },
              expiration: { maxEntries: 1500, purgeOnQuotaError: true },
            },
          },
        ],
      },
    }),
  ],
  build: {
    outDir: '../src/CouchPilot/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: true,
    // secure: false so an https:// COUCHPILOT_HOST signed by your own CA works without telling Node about the CA.
    proxy: {
      '/api': { target: process.env.COUCHPILOT_HOST ?? 'http://localhost:8642', secure: false },
      '/ws': { target: process.env.COUCHPILOT_HOST ?? 'http://localhost:8642', ws: true, secure: false },
    },
  },
})
