import { computed, onScopeDispose, reactive, shallowRef } from 'vue'
import { listenToAudio, send } from './live'
import { encodeMute, encodeSetDefault, encodeVolume, type AudioChannel, type AudioFlow, type AudioState } from './protocol'
import type { VolumeChange } from './types'

// While you drag, and for a moment after, a slider shows what you set, not what the PC last
// said: a VOLUMES already on its way would otherwise snap it back.
const HOLD_MS = 1000
// A drag sends at most one change every 30 ms, always ending with the last value.
const SEND_MS = 30

// A channel as the Audio tab draws it: what the PC said, with what you're dragging on top.
export type MixerChannel = AudioChannel & { stableKey: string }

// Endpoints keep their slot across generations by role; apps by their key.
const stableKey = (c: AudioChannel) => (c.role === 'output' || c.role === 'input' ? c.role : `app:${c.key}`)

// The mixer for as long as the Audio tab is on screen: STATE, VOLUMES and LEVELS from the PC,
// VOLUME, MUTE and DEFAULT back (docs/protocol.md).
export function useAudio() {
  const server = shallowRef<AudioState | null>(null)
  // One byte per channel, in STATE order: the peak's square root × 255. Kept apart from the
  // rest so 25 updates a second only redraw the meters.
  const levels = shallowRef<Uint8Array>(new Uint8Array())
  const held = reactive(new Map<string, { change: VolumeChange; until: number }>())
  const queues = new Map<string, { pending: VolumeChange; timer: number; lastSent: number }>()

  const stop = listenToAudio((m) => {
    const s = server.value
    if (m.op === 'audioState') {
      server.value = m.state
      levels.value = new Uint8Array(m.state.channels.length)
    } else if (m.op === 'audioVolumes' && s && m.gen === s.gen) {
      server.value = { ...s, channels: s.channels.map((c, i) => ({ ...c, ...m.volumes[i] })) }
    } else if (m.op === 'audioLevels' && s && m.gen === s.gen) {
      levels.value = m.levels
      return
    } else {
      return
    }
    const now = Date.now()
    for (const [key, h] of held) if (h.until < now) held.delete(key)
  })
  onScopeDispose(() => {
    stop()
    for (const q of queues.values()) clearTimeout(q.timer)
  })

  const state = computed(() => {
    const s = server.value
    if (!s) return null
    const channels: MixerChannel[] = s.channels.map((c) => {
      const key = stableKey(c)
      return { ...c, ...held.get(key)?.change, stableKey: key }
    })
    return { ...s, channels }
  })

  const levelOf = (c: AudioChannel) => (levels.value[c.index] ?? 0) / 255

  // Sent by key, resolved to a channel index only when it leaves: if the list changed shape in
  // the meantime, the change still lands on the right app, or nowhere if it's gone.
  function set(c: MixerChannel, change: VolumeChange) {
    const key = c.stableKey
    held.set(key, { change: { ...held.get(key)?.change, ...change }, until: Date.now() + HOLD_MS })
    const q = queues.get(key) ?? { pending: {}, timer: 0, lastSent: 0 }
    queues.set(key, q)
    q.pending = { ...q.pending, ...change }
    if (q.timer) return
    q.timer = window.setTimeout(() => {
      const { volume, muted } = q.pending
      q.pending = {}
      q.timer = 0
      q.lastSent = Date.now()
      const s = server.value
      const target = s?.channels.find((x) => stableKey(x) === key)
      if (!s || !target) return
      if (muted !== undefined) send(encodeMute(s.gen, target.index, muted))
      if (volume !== undefined) send(encodeVolume(s.gen, target.index, volume))
    }, Math.max(0, q.lastSent + SEND_MS - Date.now()))
  }

  function setDefault(flow: AudioFlow, device: number) {
    const s = server.value
    if (s) send(encodeSetDefault(s.gen, flow, device))
  }

  return { state, levelOf, set, setDefault }
}
