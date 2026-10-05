import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// `pnpm dev` proxies /api (and the input WebSocket on /ws) to a running CouchPilot; set COUCHPILOT_HOST to the PC's address.
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../src/CouchPilot/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: true,
    proxy: {
      '/api': process.env.COUCHPILOT_HOST ?? 'http://localhost:8642',
      '/ws': { target: process.env.COUCHPILOT_HOST ?? 'http://localhost:8642', ws: true },
    },
  },
})
