import { computed, onScopeDispose, ref, watch } from 'vue'
import { api } from './api'
import { onChanged, useLive } from './live'
import { Topic } from './protocol'
import type { Game, Status } from './types'

const status = ref<Status | null>(null)
const games = ref<Game[] | null>(null)
const error = ref<string | null>(null)
// When the pending display change expires, in phone time. The PC only reports whole seconds,
// so the phone counts down on its own between updates.
const pendingDeadline = ref<number | null>(null)
let started = false

// One fetch at a time: a CHANGED arriving mid-fetch queues exactly one more.
export function serialized(fetch: () => Promise<unknown>) {
  let running: Promise<unknown> | null = null
  let again = false
  const run = (): void => {
    if (running) return void (again = true)
    running = fetch().finally(() => {
      running = null
      if (again) {
        again = false
        run()
      }
    })
  }
  return run
}

const refreshStatus = serialized(() =>
  api.status().then((s) => {
    status.value = s
    const left = s.displays.pending?.secondsLeft
    pendingDeadline.value = left === undefined ? null : Date.now() + left * 1000
  }).catch(() => {}),
)
const refreshGames = serialized(() => api.games().then((g) => (games.value = g)).catch(() => {}))

// Status and library are JSON over HTTP, fetched again whenever the live socket says they
// changed (and on every reconnect). Nothing polls.
export function useCouchPilot() {
  const { online } = useLive()
  if (!started) {
    started = true
    onChanged((topics) => {
      if (topics & Topic.status) refreshStatus()
      if (topics & Topic.library) refreshGames()
    })
    watch(online, (on) => !on && (status.value = null))
  }

  const running = computed(() => status.value?.running ?? [])
  const runningIds = computed(() => new Set(running.value.map((r) => r.id)))
  const displays = computed(() => status.value?.displays ?? null)

  // Every action: call, and surface the error if any. What it changed comes back over the socket.
  async function act(fn: () => Promise<unknown>) {
    try {
      await fn()
      return true
    } catch (e) {
      error.value = (e as Error).message
      return false
    }
  }

  return { status, games, error, running, runningIds, displays, act }
}

export const CONFIRM_WINDOW_MS = 15000

// The pending display change's countdown: whole seconds for the label, and a 0..1 fraction
// updated every animation frame so the ring empties smoothly instead of ticking.
export function useCountdown() {
  const now = ref(Date.now())
  let frame = 0
  const tick = () => {
    now.value = Date.now()
    frame = pendingDeadline.value === null ? 0 : requestAnimationFrame(tick)
  }
  // Only animate while there is something to count down.
  watch(pendingDeadline, (d) => d !== null && frame === 0 && tick(), { immediate: true })
  onScopeDispose(() => cancelAnimationFrame(frame))

  const remainingMs = computed(() => (pendingDeadline.value === null ? 0 : Math.max(0, pendingDeadline.value - now.value)))
  return {
    seconds: computed(() => Math.ceil(remainingMs.value / 1000)),
    fraction: computed(() => Math.min(1, remainingMs.value / CONFIRM_WINDOW_MS)),
  }
}
