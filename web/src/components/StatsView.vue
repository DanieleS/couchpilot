<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { api } from '../api'
import { onChanged } from '../live'
import { Topic } from '../protocol'
import { useCouchPilot } from '../store'
import type { Game, Stats, StatsRangeName } from '../types'
import { capitalize, duration, parseDay } from '../utils'
import AppIcon from './AppIcon.vue'
import GameCover from './GameCover.vue'
import PcChip from './PcChip.vue'
import StatsBars from './StatsBars.vue'

// How much you play: a week, month or year at a time, then the library at large.
const emit = defineEmits<{ back: []; open: [game: Game] }>()
const { status, games } = useCouchPilot()

const range = ref<StatsRangeName>('week')
const offset = ref(0)
const stats = ref<Stats | null>(null)
const failed = ref(false)

let request = 0
async function load() {
  const mine = ++request
  try {
    const s = await api.stats(range.value, offset.value)
    if (mine === request) {
      stats.value = s
      failed.value = false
    }
  } catch {
    if (mine === request) failed.value = true
  }
}
watch([range, offset], load, { immediate: true })
watch(range, () => (offset.value = 0))
// A session ends → Playnite updates the game's playtime → CHANGED library.
const stop = onChanged((topics) => topics & Topic.library && load())
onScopeDispose(stop)

const ranges: { key: StatsRangeName; label: string }[] = [
  { key: 'week', label: 'Settimana' },
  { key: 'month', label: 'Mese' },
  { key: 'year', label: 'Anno' },
]
const title = computed(() => {
  const s = stats.value
  if (!s) return ''
  const from = parseDay(s.from)
  if (s.range === 'year') return String(from.getFullYear())
  if (s.range === 'month') return capitalize(from.toLocaleDateString('it-IT', { month: 'long', year: 'numeric' }))
  if (s.offset === 0) return 'Questa settimana'
  if (s.offset === -1) return 'La settimana scorsa'
  const to = parseDay(s.to)
  to.setDate(to.getDate() - 1)
  const fmt = (d: Date) => d.toLocaleDateString('it-IT', { day: 'numeric', month: 'short' })
  return `${fmt(from)} – ${fmt(to)}`
})
// Compared with the period before; while this one is under way, with the period before up to
// the same point. Only when that period was logged in full, or the difference would be noise.
const delta = computed(() => {
  const s = stats.value
  if (!s || !s.trackingSince || s.trackingSince > s.previousFrom) return null
  const running = s.today >= s.from && s.today < s.to
  const than = {
    week: running ? 'della settimana scorsa a questo punto' : 'della settimana prima',
    month: running ? 'del mese scorso a questo punto' : 'del mese prima',
    year: running ? "dell'anno scorso a questo punto" : "dell'anno prima",
  }[s.range]
  const diff = s.totalSeconds - s.previousTotalSeconds
  if (Math.abs(diff) < 60) return `Come quelle ${than}`
  return `${diff > 0 ? '+' : '−'}${duration(Math.abs(diff))} rispetto a quelle ${than}`
})
// Before CouchPilot started logging there's nothing to show by day; say so instead of zeros.
const untracked = computed(() => {
  const s = stats.value
  return !!s && (!s.trackingSince || s.trackingSince >= s.to)
})
const trackingSinceLabel = computed(() =>
  stats.value?.trackingSince ? parseDay(stats.value.trackingSince).toLocaleDateString('it-IT', { day: 'numeric', month: 'long' }) : null,
)
const topMax = computed(() => Math.max(1, ...(stats.value?.top.map((t) => t.seconds) ?? [1])))
const number = (n: number) => n.toLocaleString('it-IT')
const hoursTotal = (seconds: number) => `${number(Math.round(seconds / 3600))} h`
const since = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString('it-IT', { month: 'long', year: 'numeric' }) : '')

const open = (id: string) => {
  const g = games.value?.find((x) => x.id === id)
  if (g) emit('open', g)
}
</script>

<template>
  <div class="page">
    <button class="back" @click="emit('back')"><AppIcon name="chevron-left" :size="16" />Giochi</button>
    <header class="page-header">
      <h1>Statistiche</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <div class="seg" role="tablist" aria-label="Periodo">
      <button v-for="r in ranges" :key="r.key" role="tab" :aria-selected="range === r.key" :class="{ on: range === r.key }" @click="range = r.key">
        {{ r.label }}
      </button>
    </div>

    <div v-if="!stats && failed" class="empty">
      <p class="empty-title">Non riesco a leggere le statistiche</p>
      <p class="muted">Il PC e il telefono devono essere sulla stessa rete.</p>
    </div>
    <div v-else-if="!stats" class="skeleton" />

    <template v-else>
      <section class="period card">
        <div class="nav">
          <button class="icon-btn" aria-label="Periodo precedente" @click="offset--"><AppIcon name="chevron-left" :size="18" /></button>
          <span class="period-title">{{ title }}</span>
          <button class="icon-btn" aria-label="Periodo successivo" :disabled="offset >= 0" @click="offset++"><AppIcon name="chevron-right" :size="18" /></button>
        </div>

        <template v-if="untracked">
          <p class="untracked">
            CouchPilot registra le partite da quando è stato aggiornato<template v-if="trackingSinceLabel"> (dal {{ trackingSinceLabel }})</template>: per questo periodo
            non ci sono sessioni. Playnite tiene solo i totali, che trovi qui sotto.
          </p>
        </template>
        <template v-else>
          <div class="headline">
            <div>
              <p class="label">Giocate</p>
              <p class="big tabular">{{ duration(stats.totalSeconds) }}</p>
              <p v-if="delta" class="muted small">{{ delta }}</p>
            </div>
            <div class="side">
              <p class="muted small tabular">{{ stats.sessions }} {{ stats.sessions === 1 ? 'sessione' : 'sessioni' }}</p>
              <p v-if="stats.sessions" class="muted small tabular">media {{ duration(stats.totalSeconds / stats.sessions) }}</p>
            </div>
          </div>
          <StatsBars :buckets="stats.buckets" :range="stats.range" :today="stats.today" />
        </template>
      </section>

      <template v-if="stats.top.length">
        <p class="eyebrow">Più giocati</p>
        <ol class="top">
          <li v-for="t in stats.top" :key="t.id">
            <button class="top-row card" @click="open(t.id)">
              <GameCover :id="t.id" :name="t.name" :has-cover="t.hasCover" class="mini" />
              <span class="top-text">
                <span class="name truncate">{{ t.name }}</span>
                <span class="meter">
                  <span class="meter-track"><span class="meter-fill" :style="{ transform: `scaleX(${t.seconds / topMax})` }" /></span>
                  <span class="meter-value tabular">{{ duration(t.seconds) }}</span>
                </span>
              </span>
            </button>
          </li>
        </ol>
      </template>

      <p class="eyebrow"><span>Libreria</span><span class="aside">da Playnite</span></p>
      <div class="tiles">
        <div class="tile card"><span class="n tabular">{{ hoursTotal(stats.library.playtimeSeconds) }}</span><span class="l">giocate in tutto</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.games) }}</span><span class="l">giochi, {{ number(stats.library.installed) }} installati</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.installedNeverPlayed) }}</span><span class="l">installati e mai avviati</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.playedThisYear) }}</span><span class="l">giocati quest'anno</span></div>
      </div>

      <template v-if="stats.resume.length">
        <p class="eyebrow"><span>Da riprendere</span><span class="aside">installati, fermi da 3 mesi</span></p>
        <div class="resume scroll-row">
          <button v-for="r in stats.resume" :key="r.id" class="resume-item" @click="open(r.id)">
            <GameCover :id="r.id" :name="r.name" :has-cover="r.hasCover" />
            <span class="name line-clamp-2">{{ r.name }}</span>
            <span class="muted small">{{ hoursTotal(r.playtimeSeconds) }} · {{ since(r.lastActivity) }}</span>
          </button>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.back { display: inline-flex; align-items: center; gap: 2px; margin-left: -4px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.page-header { padding-top: 4px; }
.seg { display: grid; grid-template-columns: repeat(3, 1fr); gap: 3px; margin-top: var(--size-4); padding: 3px; border-radius: var(--r-button); background: var(--card); box-shadow: var(--card-edge); }
.seg button { padding: 0.5rem 0; border-radius: 9px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.seg button.on { background: var(--ink); color: var(--bg); }

.period { display: grid; gap: var(--size-3); margin-top: var(--size-3); padding: var(--size-2) var(--size-3) var(--size-3); }
.nav { display: flex; align-items: center; justify-content: space-between; margin-inline: -6px; }
.nav .icon-btn:disabled { opacity: 0.25; }
.period-title { font-size: 0.875rem; font-weight: 800; }
.headline { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--size-2); }
.label { font-size: 0.5625rem; font-weight: 800; letter-spacing: 0.1em; text-transform: uppercase; color: var(--ink-2); }
.big { font-size: 2rem; font-weight: 800; letter-spacing: -0.03em; line-height: 1.05; }
.side { text-align: right; }
.small { font-size: 0.6875rem; font-weight: 650; }
.untracked { font-size: 0.8125rem; line-height: 1.45; color: var(--ink-2); padding-bottom: var(--size-2); }

.aside { letter-spacing: 0.02em; text-transform: none; font-weight: 600; }
.top { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; }
.top-row { display: grid; grid-template-columns: 2.25rem minmax(0, 1fr); gap: var(--size-3); align-items: center; inline-size: 100%; padding: 8px; text-align: left; }
.mini { border-radius: 6px; }
.top-text { min-width: 0; display: grid; gap: 4px; }
.name { font-size: 0.8125rem; font-weight: 750; }
.meter { display: flex; align-items: center; gap: var(--size-2); }
.meter-track { flex: 1; block-size: 6px; border-radius: 3px; background: var(--slider-track); overflow: hidden; }
.meter-fill { display: block; block-size: 100%; border-radius: 3px; background: var(--ink); transform-origin: left; transition: transform 250ms var(--ease-out-3); }
.meter-value { min-inline-size: 3.25rem; text-align: right; font-size: 0.6875rem; font-weight: 750; }

.tiles { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
.tile { display: grid; gap: 2px; padding: var(--size-3); }
.n { font-size: 1.375rem; font-weight: 800; letter-spacing: -0.02em; line-height: 1.1; }
.l { font-size: 0.6875rem; font-weight: 650; color: var(--ink-2); line-height: 1.3; }

.resume-item { flex-shrink: 0; display: grid; gap: 4px; inline-size: 5.5rem; text-align: left; }
.resume-item .name { font-size: 0.6875rem; line-height: 1.2; }

.empty { padding: var(--size-9) 0; text-align: center; font-size: 0.8125rem; }
.empty-title { font-weight: 750; font-size: 0.9375rem; margin-bottom: 4px; }
.skeleton { margin-top: var(--size-3); block-size: 14rem; border-radius: var(--r-card); background: var(--card); animation: var(--animation-blink); }
</style>
