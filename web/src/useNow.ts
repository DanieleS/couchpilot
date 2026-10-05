import { onScopeDispose, ref } from 'vue'

// A clock that ticks every few seconds, for "playing for 2:40" style labels.
export function useNow(intervalMs = 5000) {
  const now = ref(Date.now())
  const id = setInterval(() => (now.value = Date.now()), intervalMs)
  onScopeDispose(() => clearInterval(id))
  return now
}
