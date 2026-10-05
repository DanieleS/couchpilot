import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// `pnpm dev` proxies /api (and the live WebSocket on /ws) to a running CouchPilot; set COUCHPILOT_HOST to the PC's address.
export default defineConfig({
  plugins: [vue()],
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
