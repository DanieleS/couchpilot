<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { useGameAchievements } from '../achievements'
import { api } from '../api'
import { useCaptures } from '../captures'
import type { Capture, Game, GameDetail, GameStats } from '../types'
import { duration, stripHtml } from '../utils'
import AchievementIcon from './AchievementIcon.vue'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'
import CaptureThumb from './CaptureThumb.vue'
import GameCover from './GameCover.vue'

const props = defineProps<{ game: Game | null; running: boolean }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ start: []; focus: []; stop: []; install: []; uninstall: []; gallery: []; viewCapture: [items: Capture[], index: number]; achievements: [] }>()

// Achievements, when SuccessStory has them: the count, and the last few unlocked.
const gameId = computed(() => (open.value ? props.game?.id ?? null : null))
const { data: achievements, stop: stopAchievements } = useGameAchievements(gameId)
onScopeDispose(stopAchievements)
const latestUnlocked = computed(() => (achievements.value?.items ?? []).filter((a) => a.unlocked).slice(0, 5))
const achievementPercent = computed(() => (achievements.value?.total ? Math.round((achievements.value.unlocked / achievements.value.total) * 100) : 0))

const { capture, items: allCaptures } = useCaptures()
const captures = computed(() => (props.game ? allCaptures.value.filter((c) => c.gameId === props.game!.id) : []))
const captureCounts = computed(() => {
  const clips = captures.value.filter((c) => c.kind === 'video').length
  const photos = captures.value.length - clips
  return [photos && `${photos} foto`, clips && `${clips} clip`].filter(Boolean).join(', ')
})

const detail = ref<GameDetail | null>(null)
const stats = ref<GameStats | null>(null)
watch(
  () => props.game?.id,
  (id) => {
    detail.value = null
    stats.value = null
    if (id) api.game(id).then((d) => (detail.value = d)).catch(() => {})
    if (id) api.gameStats(id).then((s) => (stats.value = s)).catch(() => {})
  },
  { immediate: true },
)
// Sessions only exist since CouchPilot started logging them; Playnite's totals always do.
const weeksMax = computed(() => Math.max(1, ...(stats.value?.weeks ?? [1])))
const lastPlayed = computed(() => {
  const s = stats.value
  const when = s?.lastSession?.start ?? s?.lastActivity
  if (!when) return null
  const date = new Date(when)
  const days = Math.round((new Date().setHours(0, 0, 0, 0) - new Date(date).setHours(0, 0, 0, 0)) / 86400000)
  const day = days === 0 ? 'oggi' : days === 1 ? 'ieri' : days < 7 ? `${days} giorni fa` : date.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: days > 300 ? 'numeric' : undefined })
  return { day, length: s?.lastSession ? duration(s.lastSession.seconds) : null }
})

const hours = (m: number) => (m < 60 ? `${m} min` : `${Math.round(m / 60)} h`)
const meta = computed(() => {
  const g = props.game
  if (!g) return ''
  return [g.source, g.playtimeMinutes ? `${hours(g.playtimeMinutes)} giocate` : null, g.releaseYear].filter(Boolean).join(' · ')
})
const description = computed(() => (detail.value?.description ? stripHtml(detail.value.description) : ''))
</script>

<template>
  <BottomSheet v-model:open="open" :title="game?.name ?? ''">
    <template v-if="game">
      <div class="head">
        <GameCover :id="game.id" :name="game.name" :has-cover="game.hasCover" class="cover" />
        <div class="info">
          <h2 class="name">{{ game.name }}</h2>
          <p class="meta">{{ meta }}</p>
          <p v-if="game.genres.length" class="meta">{{ game.genres.slice(0, 3).join(', ') }}</p>
        </div>
      </div>

      <div v-if="running" class="actions two">
        <button class="btn btn-dark" @click="emit('focus')"><AppIcon name="focus" :size="16" />Primo piano</button>
        <button class="btn btn-danger" @click="emit('stop')"><AppIcon name="power" :size="16" />Chiudi gioco</button>
      </div>
      <button v-else-if="game.installing || game.uninstalling" class="btn btn-ink btn-block play" disabled>
        <span class="spinner" aria-hidden="true" />{{ game.installing ? 'Installazione in corso…' : 'Disinstallazione in corso…' }}
      </button>
      <template v-else-if="game.installed">
        <button class="btn btn-ink btn-block play" @click="emit('start')"><AppIcon name="play" :size="18" />Gioca</button>
        <button class="uninstall" @click="emit('uninstall')"><AppIcon name="trash" :size="14" />Disinstalla</button>
      </template>
      <button v-else class="btn btn-dark btn-block play" @click="emit('install')"><AppIcon name="download" :size="18" />Installa</button>

      <section v-if="stats && stats.playtimeSeconds > 0" class="stats" aria-label="Statistiche">
        <div class="figures">
          <div><span class="n tabular">{{ duration(stats.playtimeSeconds) }}</span><span class="l">in tutto</span></div>
          <div v-if="stats.sessions"><span class="n tabular">{{ stats.sessions }}</span><span class="l">{{ stats.sessions === 1 ? 'sessione' : 'sessioni' }}</span></div>
          <div v-else><span class="n tabular">{{ stats.playCount }}</span><span class="l">avvii</span></div>
          <div v-if="stats.sessions"><span class="n tabular">{{ duration(stats.averageSeconds) }}</span><span class="l">a sessione</span></div>
          <div v-else-if="stats.playCount"><span class="n tabular">{{ duration(stats.playtimeSeconds / stats.playCount) }}</span><span class="l">ad avvio</span></div>
        </div>
        <div v-if="stats.sessions" class="weeks">
          <p class="weeks-head"><span>Ultime 12 settimane</span><span>{{ duration(stats.weeks[11]) }} questa</span></p>
          <div class="spark" role="img" :aria-label="`Ore per settimana nelle ultime 12 settimane, l'ultima ${duration(stats.weeks[11])}`">
            <i v-for="(w, i) in stats.weeks" :key="i" :class="{ cur: i === 11 }" :style="{ height: `${(w / weeksMax) * 100}%` }" />
          </div>
        </div>
        <p v-if="lastPlayed" class="last">
          Ultima volta <b>{{ lastPlayed.day }}</b><template v-if="lastPlayed.length">, per <b>{{ lastPlayed.length }}</b></template>
          <template v-if="stats.completionStatus"> · {{ stats.completionStatus }}</template>
        </p>
      </section>

      <section v-if="achievements" class="achievements" aria-label="Obiettivi">
        <div class="eyebrow">
          <span>Obiettivi · {{ achievements.unlocked }}/{{ achievements.total }}</span>
          <button class="link" @click="emit('achievements')">Vedi tutti<AppIcon name="chevron-right" :size="14" /></button>
        </div>
        <button class="ach card" @click="emit('achievements')">
          <span class="ach-meter"><span class="ach-fill" :style="{ transform: `scaleX(${achievementPercent / 100})` }" /></span>
          <span class="ach-row">
            <span class="ach-icons">
              <AchievementIcon v-for="a in latestUnlocked" :key="a.id" :achievement="a" :size="40" />
              <span v-if="!latestUnlocked.length" class="ach-none"><AppIcon name="trophy" :size="18" /></span>
            </span>
            <span class="ach-text tabular">
              <b>{{ achievementPercent }}%</b>
              <span class="ach-sub">{{ latestUnlocked.length ? `ultimo: ${latestUnlocked[0].name}` : 'nessuno ancora' }}</span>
            </span>
          </span>
        </button>
      </section>

      <section v-if="capture && captures.length" class="captures" aria-label="Catture">
        <div class="eyebrow">
          <span>Catture · {{ captures.length }}</span>
          <button class="link" @click="emit('gallery')">Vedi tutte<AppIcon name="chevron-right" :size="14" /></button>
        </div>
        <div class="shots" :class="`n${Math.min(3, captures.length)}`">
          <button v-for="(c, i) in captures.slice(0, 3)" :key="c.id" class="shot" @click="emit('viewCapture', captures, i)">
            <CaptureThumb :capture="c" />
          </button>
        </div>
        <p class="shots-meta">{{ captureCounts }}</p>
      </section>

      <p v-if="description" class="desc">{{ description }}</p>
    </template>
  </BottomSheet>
</template>

<style scoped>
.head { display: flex; gap: var(--size-3); align-items: flex-end; }
.cover { inline-size: 6rem; flex-shrink: 0; }
.info { min-width: 0; }
.name { font-size: 1.375rem; font-weight: 800; line-height: 1.05; letter-spacing: -0.02em; }
.meta { font-size: 0.75rem; color: var(--ink-2); margin-top: 4px; }
.actions.two { display: grid; grid-template-columns: 1fr 1fr; gap: var(--size-2); margin-top: var(--size-4); }
.play { margin-top: var(--size-4); padding-block: 0.9rem; font-size: 0.9375rem; }
.stats { margin-top: var(--size-4); display: grid; gap: var(--size-3); }
.figures { display: grid; grid-auto-flow: column; grid-auto-columns: 1fr; border-radius: var(--r-field); background: var(--well); }
.figures div { display: grid; gap: 1px; padding: 10px 6px; text-align: center; }
.figures div + div { box-shadow: inset 1px 0 0 var(--line); }
.figures .n { font-size: 1.0625rem; font-weight: 800; letter-spacing: -0.02em; }
.figures .l { font-size: 0.625rem; font-weight: 650; color: var(--ink-2); }
.weeks { display: grid; gap: 6px; }
.weeks-head { display: flex; justify-content: space-between; font-size: 0.625rem; font-weight: 700; color: var(--ink-2); }
.spark { display: grid; grid-template-columns: repeat(12, 1fr); gap: 3px; align-items: end; block-size: 2.5rem; }
.spark i { display: block; min-block-size: 2px; border-radius: 3px 3px 1px 1px; background: var(--bar-dim); }
.spark i.cur { background: var(--ink); }
.last { font-size: 0.75rem; font-weight: 600; color: var(--ink-2); }
.last b { color: var(--ink); font-weight: 750; }
.captures .eyebrow, .achievements .eyebrow { margin-top: var(--size-4); }
.ach { display: grid; gap: 10px; inline-size: 100%; padding: 10px; text-align: left; background: var(--well); box-shadow: none; }
.ach-meter { display: block; block-size: 6px; border-radius: 3px; background: var(--slider-track); overflow: hidden; }
.ach-fill { display: block; block-size: 100%; border-radius: 3px; background: var(--run); transform-origin: left; transition: transform 300ms var(--ease-out-3); }
.ach-row { display: flex; align-items: center; justify-content: space-between; gap: var(--size-3); }
.ach-icons { display: flex; gap: 4px; }
.ach-none { display: grid; place-items: center; inline-size: 40px; block-size: 40px; border-radius: 8px; background: var(--card); color: var(--ink-2); }
.ach-text { display: grid; text-align: right; min-width: 0; }
.ach-text b { font-size: 1.125rem; font-weight: 800; letter-spacing: -0.02em; }
.ach-sub { font-size: 0.6875rem; font-weight: 600; color: var(--ink-2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-inline-size: 9rem; }
.link { display: inline-flex; align-items: center; gap: 2px; font-size: 0.75rem; font-weight: 600; letter-spacing: 0.02em; text-transform: none; color: var(--ink-2); }
/* The latest one large, the next two stacked beside it. */
.shots { display: grid; gap: 4px; }
.shots.n2 { grid-template-columns: 1fr 1fr; }
.shots.n3 { grid-template-columns: 2fr 1fr; grid-template-rows: 1fr 1fr; }
.shot { display: block; min-width: 0; border-radius: var(--r-inner); overflow: hidden; }
.n3 .shot:first-child { grid-row: span 2; }
.n3 .shot:first-child :deep(.thumb) { aspect-ratio: auto; block-size: 100%; }
.shots-meta { margin-top: 6px; font-size: 0.75rem; font-weight: 600; color: var(--ink-2); }
.uninstall {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  inline-size: 100%;
  margin-top: var(--size-2);
  padding: 0.5rem;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--ink-2);
}
.desc {
  margin: var(--size-4) 0 var(--size-2);
  font-size: 0.8125rem;
  line-height: 1.5;
  color: var(--ink-2);
  display: -webkit-box;
  -webkit-line-clamp: 6;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
