import { ref, shallowRef } from 'vue'
import { createMockLink } from './mock'
import { decode, encodeSubscribe, PROTOCOL_VERSION, Subscribe, type ServerMessage } from './protocol'
import type { Status } from './types'

const useMock = import.meta.env.VITE_MOCK === '1'

export type LinkState = 'connecting' | 'open' | 'off'

// The phone's one connection to the PC (/ws, specified in docs/protocol.md): the PC pushes its
// state down it, the touchpad and the mixer send their commands up it. Open while the page is
// on screen, closed in the background, and it reconnects by itself.

// What a socket needs to be: a real WebSocket, or the mock's stand-in.
export type Link = {
  send(bytes: Uint8Array): void
  close(): void
}

const link = ref<LinkState>('off')
const status = shallowRef<Status | null>(null)
const audioListeners = new Set<(m: ServerMessage) => void>()
let socket: Link | null = null
let open = false
let retries = 0
let retryTimer = 0
let offlineTimer = 0
let started = false

// A blip shouldn't flash «Offline»: the PC counts as gone only after a few seconds without a link.
const OFFLINE_AFTER_MS = 3000

function receive(m: ServerMessage) {
  switch (m.op) {
    case 'hello':
      if (m.version !== PROTOCOL_VERSION) reloadForNewVersion()
      break
    case 'status':
      status.value = m.status
      break
    case 'audioState':
    case 'audioVolumes':
    case 'audioLevels':
      for (const listener of audioListeners) listener(m)
      break
  }
}

// The PC speaks another version: this page is a stale copy of the app, the one it serves now
// is the right one. Not more than once a minute, in case reloading doesn't fix it.
function reloadForNewVersion() {
  try {
    const last = Number(sessionStorage.getItem('couchpilot-reload') ?? 0)
    if (Date.now() - last < 60000) return
    sessionStorage.setItem('couchpilot-reload', String(Date.now()))
  } catch {}
  location.reload()
}

function opened() {
  open = true
  retries = 0
  link.value = 'open'
  clearTimeout(offlineTimer)
  // A fresh connection knows nothing about what this page wants.
  if (audioListeners.size > 0) socket?.send(encodeSubscribe(Subscribe.audio))
}

function closed(which: Link) {
  if (socket !== which) return
  socket = null
  open = false
  link.value = wanted() ? 'connecting' : 'off'
  clearTimeout(offlineTimer)
  offlineTimer = window.setTimeout(() => (status.value = null), OFFLINE_AFTER_MS)
  clearTimeout(retryTimer)
  if (wanted()) retryTimer = window.setTimeout(connect, Math.min(5000, 400 * 2 ** retries++))
}

const wanted = () => document.visibilityState === 'visible'

function connect() {
  if (socket || !wanted()) return
  link.value = 'connecting'
  if (useMock) {
    const mock = createMockLink(receive, () => closed(mock))
    socket = mock
    opened()
    return
  }
  const ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`)
  ws.binaryType = 'arraybuffer'
  const wrapped: Link = { send: (b) => ws.send(b), close: () => ws.close() }
  socket = wrapped
  ws.onopen = opened
  ws.onclose = () => closed(wrapped)
  ws.onmessage = (e) => {
    if (!(e.data instanceof ArrayBuffer)) return
    try {
      receive(decode(e.data))
    } catch (error) {
      console.warn('CouchPilot: messaggio non valido dal PC', error)
    }
  }
}

function sync() {
  if (wanted()) return connect()
  clearTimeout(retryTimer)
  const s = socket
  socket = null
  open = false
  s?.close()
  link.value = 'off'
}

export function useLive() {
  if (!started) {
    started = true
    document.addEventListener('visibilitychange', sync)
    connect()
  }
  return { link, status }
}

// Nothing is queued while the link is down: a burst of stale pointer moves or volume changes
// landing seconds later would be worse than losing them.
export function send(bytes: Uint8Array) {
  if (open) socket?.send(bytes)
}

// The Audio tab listens while it's on screen; the PC only does the work while someone listens.
export function listenToAudio(listener: (m: ServerMessage) => void) {
  audioListeners.add(listener)
  if (audioListeners.size === 1) send(encodeSubscribe(Subscribe.audio))
  return () => {
    audioListeners.delete(listener)
    if (audioListeners.size === 0) send(encodeSubscribe(0))
  }
}
