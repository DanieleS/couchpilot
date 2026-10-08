<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { api } from '../api'
import { onChanged } from '../live'
import { Topic } from '../protocol'
import { useCouchPilot } from '../store'
import { locale, t } from '../i18n'
import type { Game, Stats, StatsRangeName } from '../types'
import { capitalize, duration, parseDay } from '../utils'
import { unlockedLabel } from '../achievements'
import AchievementIcon from './AchievementIcon.vue'
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
// A session ends → Playnite updates the game's playtime → CHANGED library; a few seconds later
// GameActivity writes the session down → CHANGED stats.
const stop = onChanged((topics) => topics & (Topic.library | Topic.stats) && load())
onScopeDispose(stop)

const ranges: { key: StatsRangeName; label: string }[] = [
  { key: 'week', label: t.stats.ranges.week },
  { key: 'month', label: t.stats.ranges.month },
  { key: 'year', label: t.stats.ranges.year },
]
const title = computed(() => {
  const s = stats.value
  if (!s) return ''
  const from = parseDay(s.from)
  if (s.range === 'year') return String(from.getFullYear())
  if (s.range === 'month') return capitalize(from.toLocaleDateString(locale, { month: 'long', year: 'numeric' }))
  if (s.offset === 0) return t.stats.thisWeek
  if (s.offset === -1) return t.stats.lastWeek
  const to = parseDay(s.to)
  to.setDate(to.getDate() - 1)
  const fmt = (d: Date) => d.toLocaleDateString(locale, { day: 'numeric', month: 'short' })
  return `${fmt(from)} – ${fmt(to)}`
})
// Compared with the period before; while this one is under way, with the period before up to
// the same point. Only when that period was logged in full, or the difference would be noise.
const delta = computed(() => {
  const s = stats.value
  if (!s || !s.trackingSince || s.trackingSince > s.previousFrom) return null
  const running = s.today >= s.from && s.today < s.to
  const than = t.stats.than[s.range][running ? 'running' : 'done']
  const diff = s.totalSeconds - s.previousTotalSeconds
  if (Math.abs(diff) < 60) return t.stats.same(than)
  return t.stats.diff(`${diff > 0 ? '+' : '−'}${duration(Math.abs(diff))}`, than)
})
// Before GameActivity's first session, or without GameActivity, there's nothing to show by day;
// say so instead of zeros.
const untracked = computed(() => {
  const s = stats.value
  return !!s && (!s.activity || !s.trackingSince || s.trackingSince >= s.to)
})
const trackingSinceLabel = computed(() =>
  stats.value?.trackingSince ? parseDay(stats.value.trackingSince).toLocaleDateString(locale, { day: 'numeric', month: 'long', year: 'numeric' }) : null,
)
const topMax = computed(() => Math.max(1, ...(stats.value?.top.map((g) => g.seconds) ?? [1])))
const number = (n: number) => n.toLocaleString(locale)
const hoursTotal = (seconds: number) => t.stats.hours(number(Math.round(seconds / 3600)))
const since = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString(locale, { month: 'long', year: 'numeric' }) : '')

const open = (id: string) => {
  const g = games.value?.find((x) => x.id === id)
  if (g) emit('open', g)
}
</script>

<template>
  <div class="page">
    <button class="back" @click="emit('back')"><AppIcon name="chevron-left" :size="16" />{{ t.stats.back }}</button>
    <header class="page-header">
      <h1>{{ t.stats.title }}</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <div class="seg" role="tablist" :aria-label="t.stats.period">
      <button v-for="r in ranges" :key="r.key" role="tab" :aria-selected="range === r.key" :class="{ on: range === r.key }" @click="range = r.key">
        {{ r.label }}
      </button>
    </div>

    <div v-if="!stats && failed" class="empty">
      <p class="empty-title">{{ t.stats.failed.title }}</p>
      <p class="muted">{{ t.stats.failed.hint }}</p>
    </div>
    <div v-else-if="!stats" class="skeleton" />

    <template v-else>
      <section class="period card">
        <div class="nav">
          <button class="icon-btn" :aria-label="t.stats.previous" @click="offset--"><AppIcon name="chevron-left" :size="18" /></button>
          <span class="period-title">{{ title }}</span>
          <button class="icon-btn" :aria-label="t.stats.next" :disabled="offset >= 0" @click="offset++"><AppIcon name="chevron-right" :size="18" /></button>
        </div>

        <template v-if="untracked">
          <p class="untracked">{{ stats.activity ? t.stats.untracked(trackingSinceLabel) : t.stats.noActivity }}</p>
        </template>
        <template v-else>
          <div class="headline">
            <div>
              <p class="label">{{ t.stats.played }}</p>
              <p class="big tabular">{{ duration(stats.totalSeconds) }}</p>
              <p v-if="delta" class="muted small">{{ delta }}</p>
            </div>
            <div class="side">
              <p class="muted small tabular">{{ stats.sessions }} {{ stats.sessions === 1 ? t.stats.session : t.stats.sessions }}</p>
              <p v-if="stats.sessions" class="muted small tabular">{{ t.stats.average(duration(stats.totalSeconds / stats.sessions)) }}</p>
            </div>
          </div>
          <StatsBars :buckets="stats.buckets" :range="stats.range" :today="stats.today" />
        </template>
      </section>

      <template v-if="stats.activity && stats.top.length">
        <p class="eyebrow">{{ t.stats.top }}</p>
        <ol class="top">
          <li v-for="g in stats.top" :key="g.id">
            <button class="top-row card" @click="open(g.id)">
              <GameCover :id="g.id" :name="g.name" :has-cover="g.hasCover" class="mini" />
              <span class="top-text">
                <span class="name truncate">{{ g.name }}</span>
                <span class="meter">
                  <span class="meter-track"><span class="meter-fill" :style="{ transform: `scaleX(${g.seconds / topMax})` }" /></span>
                  <span class="meter-value tabular">{{ duration(g.seconds) }}</span>
                </span>
              </span>
            </button>
          </li>
        </ol>
      </template>

      <template v-if="stats.achievements">
        <p class="eyebrow"><span>{{ t.stats.achievements }}</span><span class="aside">{{ t.stats.fromSuccessStory }}</span></p>
        <div class="ach card">
          <p class="ach-count tabular"><b>{{ number(stats.achievements.unlocked) }}</b> {{ stats.achievements.unlocked === 1 ? t.stats.unlockedOne : t.stats.unlockedMany }}</p>
          <div v-if="stats.achievements.recent.length" class="ach-list">
            <button v-for="a in stats.achievements.recent" :key="a.gameId + a.id" class="ach-item" @click="open(a.gameId)">
              <AchievementIcon :achievement="a" :size="40" />
              <span class="ach-text">
                <span class="name truncate">{{ a.name }}</span>
                <span class="muted small truncate">{{ a.game }} · {{ unlockedLabel(a.unlockedAt) }}</span>
              </span>
            </button>
          </div>
          <p v-else class="muted small">{{ t.stats.noAchievements }}</p>
        </div>
      </template>

      <p class="eyebrow"><span>{{ t.stats.library }}</span><span class="aside">{{ t.stats.fromPlaynite }}</span></p>
      <div class="tiles">
        <div class="tile card"><span class="n tabular">{{ hoursTotal(stats.library.playtimeSeconds) }}</span><span class="l">{{ t.stats.playedTotal }}</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.games) }}</span><span class="l">{{ t.stats.games(number(stats.library.installed)) }}</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.installedNeverPlayed) }}</span><span class="l">{{ t.stats.neverPlayed }}</span></div>
        <div class="tile card"><span class="n tabular">{{ number(stats.library.playedThisYear) }}</span><span class="l">{{ t.stats.playedThisYear }}</span></div>
      </div>

      <template v-if="stats.resume.length">
        <p class="eyebrow"><span>{{ t.stats.resume }}</span><span class="aside">{{ t.stats.resumeHint }}</span></p>
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

.ach { display: grid; gap: var(--size-2); padding: var(--size-3); }
.ach-count { font-size: 0.8125rem; font-weight: 650; color: var(--ink-2); }
.ach-count b { font-size: 1.375rem; font-weight: 800; letter-spacing: -0.02em; color: var(--ink); }
.ach-list { display: grid; gap: 4px; }
.ach-item { display: flex; align-items: center; gap: 10px; inline-size: 100%; padding: 6px; border-radius: var(--r-inner); background: var(--well); text-align: left; }
.ach-text { min-width: 0; display: grid; gap: 2px; }

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
