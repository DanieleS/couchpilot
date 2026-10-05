import { computed, onScopeDispose, reactive, ref } from 'vue'
import { api } from './api'
import { useCouchPilot } from './store'
import type { AudioState, VolumeChange } from './types'

// Faster than the status poll, so the level meters move; only while the Audio tab is open.
const POLL_MS = 500
// While you drag, and for a moment after, the slider shows what you set, not what the
// last poll said: an answer already in flight would otherwise snap it back.
const HOLD_MS = 1200
// A drag sends at most one change every 80 ms, always ending with the last value.
const SEND_MS = 80

export type AudioTarget = { kind: 'device' | 'app'; id: string }

const keyOf = (t: AudioTarget) => `${t.kind}:${t.id}`

export function useAudio() {
  const { error } = useCouchPilot()
  const server = ref<AudioState | null>(null)
  const failed = ref(false)
  const held = reactive(new Map<string, { change: VolumeChange; until: number }>())
  const queues = new Map<string, { pending: VolumeChange; timer: number; lastSent: number }>()

  let timer = 0
  let stopped = false
  // Only the latest poll keeps the loop going, so an extra one after a change doesn't fork it.
  let latest = 0
  async function poll() {
    window.clearTimeout(timer)
    const mine = ++latest
    try {
      const state = await api.audio()
      if (stopped || mine !== latest) return
      server.value = state
      failed.value = false
      const now = Date.now()
      for (const [key, h] of held) if (h.until < now) held.delete(key)
    } catch {
      if (mine !== latest) return
      failed.value = true
    }
    if (!stopped) timer = window.setTimeout(poll, document.hidden ? POLL_MS * 6 : POLL_MS)
  }
  poll()
  onScopeDispose(() => {
    stopped = true
    window.clearTimeout(timer)
    for (const q of queues.values()) window.clearTimeout(q.timer)
  })

  const heldFor = (t: AudioTarget) => held.get(keyOf(t))?.change ?? {}

  const state = computed<AudioState | null>(() => {
    const s = server.value
    if (!s) return null
    const endpoint = (e: AudioState['output']) => e && { ...e, ...heldFor({ kind: 'device', id: e.id }) }
    return {
      ...s,
      output: endpoint(s.output),
      input: endpoint(s.input),
      apps: s.apps.map((a) => ({ ...a, ...heldFor({ kind: 'app', id: a.id }) })),
    }
  })

  function set(target: AudioTarget, change: VolumeChange) {
    const key = keyOf(target)
    held.set(key, { change: { ...held.get(key)?.change, ...change }, until: Date.now() + HOLD_MS })

    const q = queues.get(key) ?? { pending: {}, timer: 0, lastSent: 0 }
    queues.set(key, q)
    q.pending = { ...q.pending, ...change }
    if (q.timer) return
    q.timer = window.setTimeout(() => {
      const send = q.pending
      q.pending = {}
      q.timer = 0
      q.lastSent = Date.now()
      const call = target.kind === 'device' ? api.setAudioDevice(target.id, send) : api.setAudioApp(target.id, send)
      call.catch((e) => {
        held.delete(key)
        error.value = (e as Error).message
      })
    }, Math.max(0, q.lastSent + SEND_MS - Date.now()))
  }

  async function setDefault(id: string) {
    try {
      await api.setDefaultAudio(id)
    } catch (e) {
      error.value = (e as Error).message
    }
    poll()
  }

  return { state, failed, set, setDefault }
}
