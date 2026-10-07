import { computed, onScopeDispose, ref } from 'vue'
import { api, unpaired, type ApiError } from './api'
import { t } from './i18n'

// Pairing this phone with the PC: ask for a PIN (the PC puts it on its screen for two minutes),
// type it, and the PC answers with a cookie that stands for this phone from then on. The
// pairing screen drives this; `unpaired` in api.ts says when it has to show.

export type PairingStep = 'start' | 'requesting' | 'pin' | 'checking'

const NAME_KEY = 'couchpilot-device-name'

// «Pixel 8 · Chrome», «iPhone · Safari», «Telefono»: what the PC shows while asking, and lists
// in its settings afterwards. The person can change it before pairing.
export function guessDeviceName() {
  const ua = navigator.userAgent
  let device = t.app.pairing.phone
  const android = /Android [\d.]+; ([^;)]+?)(?: Build|\))/.exec(ua)
  if (android && !/^(K|Mobile)$/.test(android[1].trim())) device = android[1].trim()
  else if (/iPhone/.test(ua)) device = 'iPhone'
  else if (/iPad/.test(ua)) device = 'iPad'
  else if (/Android/.test(ua)) device = 'Android'
  else if (/Windows/.test(ua)) device = t.app.pairing.windowsPc
  else if (/Macintosh/.test(ua)) device = 'Mac'
  const browser = /EdgA?\//.test(ua) ? 'Edge' : /SamsungBrowser/.test(ua) ? 'Samsung Internet' : /Firefox/.test(ua) ? 'Firefox' : /Chrome|CriOS/.test(ua) ? 'Chrome' : /Safari/.test(ua) ? 'Safari' : null
  return browser ? `${device} · ${browser}` : device
}

export function usePairing() {
  const step = ref<PairingStep>('start')
  const name = ref(readName())
  const pin = ref('')
  const error = ref<string | null>(null)
  const attemptsLeft = ref<number | null>(null)
  const deadline = ref<number | null>(null)
  const now = ref(Date.now())
  const timer = window.setInterval(() => (now.value = Date.now()), 500)
  onScopeDispose(() => clearInterval(timer))

  const secondsLeft = computed(() => (deadline.value === null ? 0 : Math.max(0, Math.ceil((deadline.value - now.value) / 1000))))
  // The PIN expired while the boxes were up: back to the start, saying so.
  const expired = computed(() => step.value === 'pin' && deadline.value !== null && now.value > deadline.value)

  function readName() {
    try {
      return localStorage.getItem(NAME_KEY) || guessDeviceName()
    } catch {
      return guessDeviceName()
    }
  }

  function rememberName() {
    try {
      localStorage.setItem(NAME_KEY, name.value.trim())
    } catch {}
  }

  async function requestPin() {
    if (step.value === 'requesting' || step.value === 'checking') return
    error.value = null
    attemptsLeft.value = null
    pin.value = ''
    step.value = 'requesting'
    rememberName()
    try {
      const r = await api.requestPin(name.value.trim())
      deadline.value = Date.now() + r.seconds * 1000
      step.value = 'pin'
    } catch (e) {
      error.value = (e as Error).message
      step.value = 'start'
    }
  }

  // Six digits in: ask the PC. Right, `unpaired` goes false and the app takes over again.
  async function submit() {
    if (step.value !== 'pin' || pin.value.length !== 6) return
    step.value = 'checking'
    error.value = null
    try {
      await api.pair(pin.value, name.value.trim())
      unpaired.value = false
      step.value = 'start'
      pin.value = ''
      deadline.value = null
    } catch (e) {
      const code = (e as ApiError).code
      error.value = (e as Error).message
      pin.value = ''
      if (code === 'wrong-pin') {
        attemptsLeft.value = (e as ApiError).attemptsLeft
        step.value = 'pin'
      } else {
        // Expired, refused at the PC, or pairing turned off meanwhile.
        step.value = 'start'
        deadline.value = null
      }
    }
  }

  function reset() {
    step.value = 'start'
    pin.value = ''
    deadline.value = null
    error.value = null
  }

  return { step, name, pin, error, attemptsLeft, secondsLeft, expired, requestPin, submit, reset }
}
