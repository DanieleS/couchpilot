<script setup lang="ts">
import { ToggleGroupItem, ToggleGroupRoot } from 'reka-ui'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { playDayKey, useCaptures } from '../captures'
import type { Capture, Game, RunningGame } from '../types'
import { useLive } from '../live'
import { useCouchPilot } from '../store'
import AppIcon from './AppIcon.vue'
import CaptureThumb from './CaptureThumb.vue'
import DisplayConfirm from './DisplayConfirm.vue'
import GameCover from './GameCover.vue'
import NowPlayingBar from './NowPlayingBar.vue'
import NowPlayingCard from './NowPlayingCard.vue'
import PcChip from './PcChip.vue'
import PresetCards from './PresetCards.vue'
import StreamNotice from './StreamNotice.vue'

type Filter = 'installed' | 'recent' | 'favorite' | 'all'

const emit = defineEmits<{
  open: [game: Game]
  focus: [game: RunningGame]
  close: [game: RunningGame]
  applyPreset: [id: string]
  savePreset: []
  keepDisplays: []
  revertDisplays: []
  manageDisplays: []
  stats: []
  gallery: []
  viewCapture: [items: Capture[], index: number]
}>()

const { status, games, running, runningIds, displays } = useCouchPilot()
const { unreachable } = useLive()
const { capture, items: captures, unseen } = useCaptures()
// Today's screenshots and clips, whichever game: a day of play, 5:00 to 5:00.
const today = computed(() => {
  const day = playDayKey(new Date().toISOString())
  return captures.value.filter((c) => playDayKey(c.takenAt) === day)
})

const query = ref('')
const filter = ref<Filter>('installed')
const current = computed(() => running.value[0] ?? null)
const activeScreen = computed(() => displays.value?.presets.find((p) => p.id === displays.value?.activePresetId)?.name ?? null)

const counts = computed(() => ({
  installed: games.value?.filter((g) => g.installed).length ?? 0,
  all: games.value?.length ?? 0,
}))

const visible = computed(() => {
  const list = games.value ?? []
  const q = query.value.trim().toLowerCase()
  if (q) return list.filter((g) => g.name.toLowerCase().includes(q))
  switch (filter.value) {
    case 'installed': return list.filter((g) => g.installed)
    case 'favorite': return list.filter((g) => g.favorite)
    case 'recent':
      return list
        .filter((g) => g.lastActivity)
        .sort((a, b) => b.lastActivity!.localeCompare(a.lastActivity!))
        .slice(0, 30)
    default: return list
  }
})

// Once the card scrolls away, the running game stays reachable from a bar pinned on top.
const hero = ref<HTMLElement | null>(null)
const heroVisible = ref(true)
let observer: IntersectionObserver | null = null
watch(hero, (el) => {
  observer?.disconnect()
  heroVisible.value = true
  if (!el) return
  observer = new IntersectionObserver(([e]) => (heroVisible.value = e.isIntersecting), { threshold: 0.15 })
  observer.observe(el)
})
onBeforeUnmount(() => observer?.disconnect())

const setFilter = (v: unknown) => v && (filter.value = v as Filter)
</script>

<template>
  <div class="page">
    <Transition name="drop">
      <div v-if="current && !heroVisible" class="pinned">
        <NowPlayingBar :game="current" @focus="emit('focus', current)" @close="emit('close', current)" />
      </div>
    </Transition>

    <header class="page-header">
      <h1>Giochi</h1>
      <span class="header-tools">
        <button v-if="capture" class="icon-btn tool" aria-label="Galleria" @click="emit('gallery')">
          <AppIcon name="image" :size="17" /><span v-if="unseen" class="new" aria-label="Catture nuove" />
        </button>
        <button class="icon-btn tool stats" aria-label="Statistiche" @click="emit('stats')"><AppIcon name="chart" :size="17" /></button>
        <PcChip :name="status?.pc ?? null" />
      </span>
    </header>

    <div v-if="current" ref="hero" class="hero-slot">
      <NowPlayingCard :game="current" :screen="activeScreen" @focus="emit('focus', current)" @close="emit('close', current)" />
    </div>

    <template v-if="capture && today.length">
      <div class="eyebrow">
        <span>Catture di oggi · {{ today.length }}</span>
        <button class="link" @click="emit('gallery')">Galleria</button>
      </div>
      <div class="scroll-row">
        <button v-for="(c, i) in today.slice(0, 12)" :key="c.id" class="today" @click="emit('viewCapture', today, i)">
          <CaptureThumb :capture="c" time />
        </button>
      </div>
    </template>

    <template v-if="displays">
      <div class="eyebrow">
        <span>Schermo</span>
        <button class="link" @click="emit('manageDisplays')">Gestisci</button>
      </div>
      <StreamNotice v-if="displays.streamActive" class="stream" />
      <PresetCards
        :displays="displays"
        :locked="displays.streamActive"
        @apply="emit('applyPreset', $event)"
        @add="emit('savePreset')"
      />
      <DisplayConfirm
        v-if="displays.pending"
        class="confirm"
        :displays="displays"
        @keep="emit('keepDisplays')"
        @revert="emit('revertDisplays')"
      />
    </template>

    <label class="search card">
      <AppIcon name="search" :size="16" />
      <input v-model="query" type="search" placeholder="Cerca nella libreria" enterkeyhint="search" />
    </label>

    <ToggleGroupRoot v-if="!query" type="single" :model-value="filter" class="filters" @update:model-value="setFilter">
      <ToggleGroupItem value="installed" class="filter">Installati<span class="n tabular">{{ counts.installed }}</span></ToggleGroupItem>
      <ToggleGroupItem value="recent" class="filter">Recenti</ToggleGroupItem>
      <ToggleGroupItem value="favorite" class="filter">Preferiti</ToggleGroupItem>
      <ToggleGroupItem value="all" class="filter">Tutti<span class="n tabular">{{ counts.all }}</span></ToggleGroupItem>
    </ToggleGroupRoot>

    <div v-if="games === null && unreachable" class="empty">
      <p class="empty-title">Il PC non risponde</p>
      <p class="muted">Accendilo e apri Playnite: mi ricollego da solo.</p>
    </div>
    <div v-else-if="games === null" class="grid">
      <div v-for="i in 9" :key="i" class="skeleton" />
    </div>
    <div v-else-if="visible.length === 0" class="empty">
      <p class="empty-title">{{ query ? `Nessun gioco per «${query}»` : 'Niente qui' }}</p>
      <p class="muted">{{ query ? 'I giochi nascosti in Playnite non compaiono.' : 'Prova un altro filtro.' }}</p>
    </div>
    <div v-else class="grid">
      <button v-for="g in visible" :key="g.id" class="tile" :class="{ uninstalled: !g.installed }" @click="emit('open', g)">
        <GameCover
          :id="g.id"
          :name="g.name"
          :has-cover="g.hasCover"
          :badge="runningIds.has(g.id) ? 'In gioco' : g.installing ? 'Installo…' : g.uninstalling ? 'Disinstallo…' : undefined"
          :class="{ running: runningIds.has(g.id) }"
        />
        <span class="caption line-clamp-2">{{ g.name }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.pinned {
  position: fixed;
  inset: 0 0 auto;
  z-index: 30;
  padding: var(--safe-top) var(--gutter) var(--size-2);
  background: color-mix(in srgb, var(--bg) 85%, transparent);
  backdrop-filter: blur(12px);
}
.drop-enter-active, .drop-leave-active { transition: transform 200ms var(--ease-out-3), opacity 200ms; }
.drop-enter-from, .drop-leave-to { transform: translateY(-100%); opacity: 0; }

.header-tools { display: flex; align-items: center; gap: 6px; }
.tool { position: relative; color: var(--ink); background: var(--card); box-shadow: var(--card-edge); }
.stats { color: var(--ink-2); }
.new { position: absolute; top: 2px; right: 2px; inline-size: 9px; block-size: 9px; border-radius: var(--radius-round); background: var(--accent); box-shadow: 0 0 0 2px var(--bg); }
.today { flex-shrink: 0; inline-size: 9.5rem; border-radius: var(--r-inner); overflow: hidden; transition: transform 100ms var(--ease-out-2); }
.today:active { transform: scale(0.97); }
.hero-slot { margin-top: var(--size-3); }
.link { font-size: 0.75rem; font-weight: 600; letter-spacing: 0.02em; text-transform: none; color: var(--ink-2); }
.confirm { margin-top: var(--size-2); }
.stream { margin-bottom: var(--size-2); }

.search {
  display: flex;
  align-items: center;
  gap: var(--size-2);
  margin-top: var(--size-5);
  padding: 0.75rem var(--size-3);
  border-radius: var(--r-field);
  color: var(--ink-2);
}
.search input { flex: 1; min-width: 0; border: 0; background: none; outline: none; font-size: 0.875rem; color: var(--ink); }
.search input::placeholder { color: var(--ink-2); }

.filters { display: flex; gap: 6px; margin-top: var(--size-3); overflow-x: auto; scrollbar-width: none; }
.filter {
  flex-shrink: 0;
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-round);
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--ink-2);
}
.filter[data-state='on'] { background: var(--ink); color: var(--bg); }
.filter .n { margin-left: 4px; font-weight: 500; opacity: 0.7; }

.grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--size-3) var(--size-2); margin-top: var(--size-3); }
.tile { display: flex; flex-direction: column; text-align: left; min-width: 0; transition: transform 100ms var(--ease-out-2); }
.tile:active { transform: scale(0.97); }
.tile.uninstalled { opacity: 0.45; }
.caption { margin-top: 6px; font-size: 0.6875rem; font-weight: 600; line-height: 1.2; }
.running { box-shadow: 0 0 0 3px var(--bg), 0 0 0 6px var(--run); }
.skeleton { aspect-ratio: 3 / 4; border-radius: var(--r-cover); background: var(--card); animation: var(--animation-blink); }

.empty { padding: var(--size-9) 0; text-align: center; font-size: 0.8125rem; }
.empty-title { font-weight: 750; font-size: 0.9375rem; margin-bottom: 4px; }
</style>
