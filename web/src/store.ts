import { computed, onScopeDispose, ref, watch } from 'vue'
import { api } from './api'
import { useLive } from './live'
import type { Game } from './types'

const GAMES_POLL_MS = 60000

const games = ref<Game[] | null>(null)
const error = ref<string | null>(null)
// When the pending display change expires, in phone time. The PC only reports whole seconds,
// so the phone counts down on its own between updates.
const pendingDeadline = ref<number | null>(null)
let started = false

const refreshGames = () =>
  api.games().then((g) => (games.value = g)).catch(() => {})

// The PC pushes its state over the live socket whenever it changes; the library is fetched
// now and then, since it only changes when Playnite imports something.
export function useCouchPilot() {
  const { status } = useLive()
  if (!started) {
    started = true
    watch(status, (s) => {
      const left = s?.displays.pending?.secondsLeft
      pendingDeadline.value = left === undefined ? null : Date.now() + left * 1000
    })
    refreshGames()
    setInterval(refreshGames, GAMES_POLL_MS)
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
