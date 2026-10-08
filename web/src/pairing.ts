import { computed, onScopeDispose, ref } from 'vue'
import { api, unpaired, type ApiError } from './api'
import { t } from './i18n'

// Pairing this phone with the PC: ask for a PIN (the PC puts it on its screen for two minutes),
// type it, and the PC answers with a cookie that stands for this phone from then on. The
// pairing screen drives this; `unpaired` in api.ts says when it has to show.
//
// Browsers drop cookies on their own: storage cleanup, a home-screen app with a cookie jar of its
// own. So the phone also keeps the token in localStorage, and when the PC says «unpaired» it first
// shows it back (POST /api/pair/resume) to get the cookie again; the PIN only if that fails too.

export type PairingStep = 'resuming' | 'start' | 'requesting' | 'pin' | 'checking'

const NAME_KEY = 'couchpilot-device-name'
const TOKEN_KEY = 'couchpilot-device-token'

function readToken() {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

function keepToken(token: string | null) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {}
}

// At start: a phone paired before it kept a copy of its token gets one now. Pairing off, or
// the PC unreachable, leaves whatever was kept alone.
export async function keepPairing() {
  try {
    const r = await api.pairing()
    if (r.token) keepToken(r.token)
  } catch {}
}

// When the token was last shown: a browser that won't keep the cookie at all would otherwise
// bounce between the app and this screen forever.
let resumedAt = 0
const canResume = () => !!readToken() && Date.now() - resumedAt > 30000

// Back in with the kept token, if there is one and the PC still knows it.
async function resume() {
  const token = readToken()
  if (!token) return false
  resumedAt = Date.now()
  try {
    await api.resume(token)
    return true
  } catch (e) {
    // Only a PC that answered «I don't know it» makes the copy worthless; a network hiccup doesn't.
    if ((e as ApiError).code === 'forgotten') keepToken(null)
    return false
  }
}

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
  const step = ref<PairingStep>(canResume() ? 'resuming' : 'start')
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
      const device = await api.pair(pin.value, name.value.trim())
      keepToken(device.token)
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

  if (step.value === 'resuming') {
    resume().then((ok) => {
      if (ok) unpaired.value = false
      step.value = 'start'
    })
  }

  return { step, name, pin, error, attemptsLeft, secondsLeft, expired, requestPin, submit, reset }
}
