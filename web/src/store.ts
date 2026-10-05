import { computed, onScopeDispose, ref, watch } from 'vue'
import { api } from './api'
import type { Game, Status } from './types'

const STATUS_POLL_MS = 2000
const GAMES_POLL_MS = 60000

const status = ref<Status | null>(null)
const games = ref<Game[] | null>(null)
const error = ref<string | null>(null)
// When the pending display change expires, in phone time. The PC only reports whole seconds
// every poll, so the phone counts down on its own between polls.
const pendingDeadline = ref<number | null>(null)
let started = false

const refreshStatus = () =>
  api
    .status()
    .then((s) => {
      status.value = s
      const left = s.displays.pending?.secondsLeft
      pendingDeadline.value = left === undefined ? null : Date.now() + left * 1000
    })
    .catch(() => (status.value = null))

const refreshGames = () =>
  api.games().then((g) => (games.value = g)).catch(() => {})

// One poll loop for the whole app: the phone asks the PC what's going on every 2 s.
export function useCouchPilot() {
  if (!started) {
    started = true
    refreshStatus()
    refreshGames()
    setInterval(refreshStatus, STATUS_POLL_MS)
    setInterval(refreshGames, GAMES_POLL_MS)
  }

  const running = computed(() => status.value?.running ?? [])
  const runningIds = computed(() => new Set(running.value.map((r) => r.id)))
  const displays = computed(() => status.value?.displays ?? null)

  // Every action: call, surface the error if any, then refresh what's on screen.
  async function act(fn: () => Promise<unknown>) {
    try {
      await fn()
      return true
    } catch (e) {
      error.value = (e as Error).message
      return false
    } finally {
      refreshStatus()
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
