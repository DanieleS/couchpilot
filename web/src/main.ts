import { createApp } from 'vue'
import App from './App.vue'
import './styles/global.css'
import './styles/overlays.css'

createApp(App).mount('#app')

// The service worker (see vite.config.ts). When an update of the extension brings a new one,
// it takes over as soon as it's installed: start again on the new version. The very first
// one taking over is not an update.
if ('serviceWorker' in navigator && import.meta.env.PROD) {
  let updating = !!navigator.serviceWorker.controller
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (updating) location.reload()
    updating = true
  })
  window.addEventListener('load', () => navigator.serviceWorker.register('/sw.js').catch(() => {}))
}
