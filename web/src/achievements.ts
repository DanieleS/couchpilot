import { computed, effectScope, ref, watch, type Ref } from 'vue'
import { api } from './api'
import { onChanged } from './live'
import { Topic } from './protocol'
import { serialized, useCouchPilot } from './store'
import type { GameAchievements, RecentAchievement } from './types'

// Achievements, from SuccessStory's data on the PC: the latest unlocked across every game,
// fetched again whenever the live socket says SuccessStory wrote something, and a toast for what
// was just unlocked.

const recent = ref<RecentAchievement[]>([])
// Unlocked since the phone last looked, newest first: what the toast announces.
const fresh = ref<RecentAchievement[] | null>(null)
let known: Set<string> | null = null
let started = false

// SuccessStory refreshes at the end of a session, so an unlock can be hours old when it shows
// up; older than this, it's history rather than news.
const FRESH_MS = 12 * 3600 * 1000

const keyOf = (a: RecentAchievement) => `${a.gameId}/${a.id}`

const refresh = serialized(() =>
  api.recentAchievements().then((list) => {
    if (known) {
      const now = Date.now()
      const added = list.filter((a) => !known!.has(keyOf(a)) && a.unlockedAt && now - new Date(a.unlockedAt).getTime() < FRESH_MS)
      if (added.length) fresh.value = added
    }
    known = new Set(list.map(keyOf))
    recent.value = list
  }).catch(() => {}),
)

export function useAchievements() {
  const { status } = useCouchPilot()
  const available = computed(() => status.value?.achievements ?? false)

  if (!started) {
    started = true
    onChanged((topics) => topics & Topic.achievements && available.value && refresh())
    effectScope(true).run(() =>
      watch(available, (now, before) => {
        if (now && !before) refresh()
        if (!now) {
          recent.value = []
          known = null
        }
      }, { immediate: true }),
    )
  }

  return { available, recent, fresh }
}

// One game's achievements, fetched when the id changes and again when SuccessStory writes.
export function useGameAchievements(gameId: Ref<string | null | undefined>) {
  const { status } = useCouchPilot()
  const data = ref<GameAchievements | null>(null)
  const loading = ref(false)
  let request = 0

  async function load() {
    const id = gameId.value
    data.value = null
    if (!id || !status.value?.achievements) return
    const mine = ++request
    loading.value = true
    try {
      const d = await api.achievements(id)
      if (mine === request) data.value = d
    } catch {
      // 404: SuccessStory has nothing on this game.
    } finally {
      if (mine === request) loading.value = false
    }
  }
  watch([gameId, () => status.value?.achievements], load, { immediate: true })
  const stop = onChanged((topics) => topics & Topic.achievements && load())

  return { data, loading, stop }
}

// «Raro», «Rarissimo»: SuccessStory's own bands, for the few that deserve a word.
export function rarity(percent: number | null) {
  if (percent === null) return null
  if (percent < 2) return 'Rarissimo'
  if (percent < 10) return 'Raro'
  if (percent < 30) return 'Non comune'
  return null
}

export function percentLabel(percent: number | null) {
  if (percent === null) return ''
  const text = percent < 10 ? percent.toLocaleString('it-IT', { maximumFractionDigits: 1 }) : Math.round(percent).toLocaleString('it-IT')
  return `${text}% dei giocatori`
}

export function unlockedLabel(iso: string | null) {
  if (!iso) return 'Sbloccato'
  const d = new Date(iso)
  const days = Math.round((new Date().setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 86400000)
  const time = d.toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' })
  if (days === 0) return `Oggi alle ${time}`
  if (days === 1) return `Ieri alle ${time}`
  return d.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: days > 300 ? 'numeric' : undefined })
}
