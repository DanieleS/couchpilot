import { computed, effectScope, ref, watch } from 'vue'
import { api, type ApiError } from './api'
import { onChanged } from './live'
import { Topic } from './protocol'
import { serialized, useCouchPilot } from './store'
import type { Capture, Captures } from './types'
import { capitalize } from './utils'

// Screenshots and clips: the list in the capture tools' folders, fetched again whenever the live
// socket says it changed, and the three commands that make new ones (the PC presses Game Bar's or
// NVIDIA's shortcuts and answers once the file is there).

export type CaptureAction = 'screenshot' | 'replay' | 'record'

const list = ref<Captures | null>(null)
// The capture to announce: one that just turned up, from the phone or from the PC's own keys.
const fresh = ref<Capture | null>(null)
// What the PC said when a replay couldn't be saved, for the sheet that explains how to turn it on.
const help = ref<{ code: string; message: string; action: CaptureAction } | null>(null)
const pending = ref<CaptureAction | null>(null)
const lastSeen = ref(readLastSeen())
// Ids already listed or announced: anything beyond them is new. Null before the first list.
let known: Set<string> | null = null
let started = false

const LAST_SEEN_KEY = 'couchpilot-captures-seen'
// Older than this, a capture turning up isn't news: a folder added in the settings, say.
const FRESH_MS = 2 * 60 * 1000

function readLastSeen() {
  try {
    return localStorage.getItem(LAST_SEEN_KEY) ?? ''
  } catch {
    return ''
  }
}

function announce(capture: Capture) {
  known?.add(capture.id)
  fresh.value = capture
}

const refresh = serialized(() =>
  api.captures().then((c) => {
    if (known) {
      const now = Date.now()
      const added = c.items.filter((i) => !known!.has(i.id) && now - new Date(i.takenAt).getTime() < FRESH_MS)
      if (added.length) announce(added[0])
    }
    known = new Set(c.items.map((i) => i.id))
    list.value = c
  }).catch(() => {}),
)

export function useCaptures() {
  const { status, error } = useCouchPilot()
  const capture = computed(() => status.value?.capture ?? null)

  if (!started) {
    started = true
    onChanged((topics) => topics & Topic.captures && capture.value && refresh())
    // Turned on in the settings, or the status arrived after the socket's first CHANGED. Detached
    // from whichever component asked first: the list outlives it.
    effectScope(true).run(() =>
      watch(() => status.value?.capture ?? null, (now, before) => {
        if (now && !before) refresh()
        if (!now) {
          list.value = null
          known = null
        }
      }, { immediate: true }),
    )
  }

  const items = computed(() => list.value?.items ?? [])
  const unseen = computed(() => !!items.value[0] && items.value[0].takenAt > lastSeen.value)
  const recordingSince = computed(() => capture.value?.recordingSince ?? null)

  function markSeen() {
    const newest = items.value[0]?.takenAt
    if (!newest) return
    lastSeen.value = newest
    try {
      localStorage.setItem(LAST_SEEN_KEY, newest)
    } catch {}
  }

  async function run(action: CaptureAction, command: () => Promise<Capture | void>) {
    if (pending.value) return
    pending.value = action
    try {
      const made = await command()
      if (made && !known?.has(made.id)) announce(made)
    } catch (e) {
      const code = (e as ApiError).code ?? null
      if (code === 'replay-off' || code === 'capture-off') help.value = { code, message: (e as Error).message, action }
      else error.value = (e as Error).message
    } finally {
      pending.value = null
    }
  }

  const screenshot = () => run('screenshot', api.screenshot)
  const saveReplay = () => run('replay', api.saveReplay)
  const toggleRecording = () => run('record', recordingSince.value ? api.stopRecording : api.startRecording)

  async function remove(c: Capture) {
    try {
      await api.deleteCapture(c.id)
      if (list.value) list.value = { ...list.value, items: list.value.items.filter((i) => i.id !== c.id), bytes: list.value.bytes - c.bytes }
      return true
    } catch (e) {
      error.value = (e as Error).message
      return false
    }
  }

  return { capture, list, items, fresh, help, pending, unseen, recordingSince, markSeen, screenshot, saveReplay, toggleRecording, remove, announce }
}

// Days of play run 5:00 to 5:00, like the statistics: a night's session stays in one day.
export function playDayKey(iso: string) {
  const d = new Date(new Date(iso).getTime() - 5 * 3600000)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function dayLabel(key: string) {
  const today = playDayKey(new Date().toISOString())
  const yesterday = playDayKey(new Date(Date.now() - 86400000).toISOString())
  if (key === today) return 'Oggi'
  if (key === yesterday) return 'Ieri'
  const [y, m, d] = key.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  return capitalize(date.toLocaleDateString('it-IT', { weekday: 'long', day: 'numeric', month: 'long', year: y === new Date().getFullYear() ? undefined : 'numeric' }))
}

// 0:30, 2:14, 1:02:07.
export function clipLength(seconds: number | null) {
  if (seconds === null) return ''
  const s = Math.round(seconds)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const rest = String(s % 60).padStart(2, '0')
  return h ? `${h}:${String(m).padStart(2, '0')}:${rest}` : `${m}:${rest}`
}

export function fileSize(bytes: number) {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} kB`
  if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toLocaleString('it-IT', { maximumFractionDigits: bytes < 10 * 1024 ** 2 ? 1 : 0 })} MB`
  return `${(bytes / 1024 ** 3).toLocaleString('it-IT', { maximumFractionDigits: 1 })} GB`
}

export const toolName = (tool: string | null) => (tool === 'gamebar' ? 'Game Bar' : tool === 'nvidia' ? 'NVIDIA' : null)

// A GIF the clip editor made: a photo to the gallery, one that moves.
export const isGif = (c: Capture) => /\.gif$/i.test(c.fileName)

// «Ultimi 30 s», «Ultimi 2 min», or plain «Replay» when the tool doesn't say how long.
export function replayLabel(seconds: number | null) {
  if (!seconds) return 'Replay'
  return seconds < 60 ? `Ultimi ${seconds} s` : `Ultimi ${Math.round(seconds / 60)} min`
}
