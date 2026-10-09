<script setup lang="ts">
import { ToggleGroupItem, ToggleGroupRoot } from 'reka-ui'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { playDayKey, useCaptures } from '../captures'
import { t } from '../i18n'
import type { Capture, Game, RunningGame } from '../types'
import { useLive } from '../live'
import { api } from '../api'
import { useCouchPilot } from '../store'
import AppIcon from './AppIcon.vue'
import CaptureThumb from './CaptureThumb.vue'
import DisplayConfirm from './DisplayConfirm.vue'
import GameCover from './GameCover.vue'
import LibrarySortSheet from './LibrarySortSheet.vue'
import NowPlayingBar from './NowPlayingBar.vue'
import NowPlayingCard from './NowPlayingCard.vue'
import PcChip from './PcChip.vue'
import PresetCards from './PresetCards.vue'
import StreamNotice from './StreamNotice.vue'

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

const { status, games, view, running, runningIds, displays, act } = useCouchPilot()
const { unreachable } = useLive()
const { capture, items: captures, unseen } = useCaptures()
// Today's screenshots and clips, whichever game: a day of play, 5:00 to 5:00.
const today = computed(() => {
  const day = playDayKey(new Date().toISOString())
  return captures.value.filter((c) => playDayKey(c.takenAt) === day)
})

const query = ref('')
const sorting = ref(false)
const current = computed(() => running.value[0] ?? null)
const activeScreen = computed(() => displays.value?.presets.find((p) => p.id === displays.value?.activePresetId)?.name ?? null)

type Group = { key: string; name: string | null; games: Game[] }

// What Playnite shows on the PC, in its order and groups. A search looks through the whole
// library instead, by name; so does the phone when Playnite can't say what it shows.
const groups = computed<Group[]>(() => {
  const list = games.value ?? []
  const q = query.value.trim().toLowerCase()
  if (q) return [{ key: 'search', name: null, games: list.filter((g) => g.name.toLowerCase().includes(q)) }]
  if (!view.value) return [{ key: 'all', name: null, games: list }]
  const byId = new Map(list.map((g) => [g.id, g]))
  return view.value.groups
    .map((group, i) => ({ key: `${i}:${group.name}`, name: group.name, games: group.ids.flatMap((id) => byId.get(id) ?? []) }))
    .filter((group) => group.games.length > 0)
})
const grouped = computed(() => !query.value.trim() && !!view.value?.grouping)
const total = computed(() => new Set(groups.value.flatMap((g) => g.games.map((x) => x.id))).size)
const sortLabel = computed(() => (view.value ? t.games.view.sorts[view.value.sort] ?? view.value.sortLabel : ''))

// The PC answers over the live socket; until then the phone shows what was picked.
function applyPreset(id: unknown) {
  if (typeof id !== 'string' || !id || !view.value || id === view.value.activePreset) return
  view.value = { ...view.value, activePreset: id }
  act(() => api.applyFilterPreset(id))
}
function sort(key: string, descending: boolean) {
  if (!view.value) return
  view.value = { ...view.value, sort: key, descending }
  act(() => api.sortLibrary(key, descending))
}

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

</script>

<template>
  <div class="page">
    <Transition name="drop">
      <div v-if="current && !heroVisible" class="pinned">
        <NowPlayingBar :game="current" @focus="emit('focus', current)" @close="emit('close', current)" />
      </div>
    </Transition>

    <header class="page-header">
      <h1>{{ t.games.title }}</h1>
      <span class="header-tools">
        <button v-if="capture" class="icon-btn tool" :aria-label="t.games.gallery" @click="emit('gallery')">
          <AppIcon name="image" :size="17" /><span v-if="unseen" class="new" :aria-label="t.games.newCaptures" />
        </button>
        <button class="icon-btn tool stats" :aria-label="t.games.stats" @click="emit('stats')"><AppIcon name="chart" :size="17" /></button>
        <PcChip :name="status?.pc ?? null" />
      </span>
    </header>

    <div v-if="current" ref="hero" class="hero-slot">
      <NowPlayingCard :game="current" :screen="activeScreen" @focus="emit('focus', current)" @close="emit('close', current)" />
    </div>

    <template v-if="capture && today.length">
      <div class="eyebrow">
        <span>{{ t.games.todayCaptures(today.length) }}</span>
        <button class="link" @click="emit('gallery')">{{ t.games.gallery }}</button>
      </div>
      <div class="scroll-row">
        <button v-for="(c, i) in today.slice(0, 12)" :key="c.id" class="today" @click="emit('viewCapture', today, i)">
          <CaptureThumb :capture="c" time />
        </button>
      </div>
    </template>

    <template v-if="displays">
      <div class="eyebrow">
        <span>{{ t.games.screen }}</span>
        <button class="link" @click="emit('manageDisplays')">{{ t.games.manage }}</button>
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
      <input v-model="query" type="search" :placeholder="t.games.search" enterkeyhint="search" />
    </label>

    <template v-if="view && !query">
      <ToggleGroupRoot type="single" :model-value="view.activePreset ?? ''" class="filters" @update:model-value="applyPreset">
        <span v-if="!view.activePreset" class="filter custom">{{ t.games.view.custom }}</span>
        <ToggleGroupItem v-for="p in view.presets" :key="p.id" :value="p.id" class="filter">{{ p.name }}</ToggleGroupItem>
      </ToggleGroupRoot>
      <div class="view-bar">
        <span class="muted truncate">
          {{ t.games.view.count(total) }}<template v-if="view.grouping"> · {{ t.games.view.groupedBy(view.grouping) }}</template>
        </span>
        <button class="sort" @click="sorting = true">
          <AppIcon name="sort" :size="14" />{{ sortLabel }}<span class="dir" aria-hidden="true">{{ view.descending ? '↓' : '↑' }}</span>
        </button>
      </div>
      <LibrarySortSheet v-model:open="sorting" :view="view" @sort="sort" />
    </template>

    <div v-if="games === null && unreachable" class="empty">
      <p class="empty-title">{{ t.games.offline.title }}</p>
      <p class="muted">{{ t.games.offline.hint }}</p>
    </div>
    <div v-else-if="games === null" class="grid">
      <div v-for="i in 9" :key="i" class="skeleton" />
    </div>
    <div v-else-if="total === 0" class="empty">
      <p class="empty-title">{{ query ? t.games.noMatch(query) : t.games.nothing }}</p>
      <p class="muted">{{ query ? t.games.noMatchHint : t.games.nothingHint }}</p>
    </div>
    <template v-else>
      <section v-for="group in groups" :key="group.key">
        <h2 v-if="grouped" class="group">
          <span class="truncate">{{ group.name ?? t.games.view.none }}</span><span class="n tabular">{{ group.games.length }}</span>
        </h2>
        <div class="grid">
          <button v-for="g in group.games" :key="g.id" class="tile" :class="{ uninstalled: !g.installed }" @click="emit('open', g)">
            <GameCover
              :id="g.id"
              :name="g.name"
              :has-cover="g.hasCover"
              :badge="runningIds.has(g.id) ? t.games.badge.playing : g.installing ? t.games.badge.installing : g.uninstalling ? t.games.badge.uninstalling : undefined"
              :class="{ running: runningIds.has(g.id) }"
            />
            <span class="caption line-clamp-2">{{ g.name }}</span>
          </button>
        </div>
      </section>
    </template>
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
.filter.custom { box-shadow: inset 0 0 0 1.5px var(--line); }

.view-bar { display: flex; align-items: center; justify-content: space-between; gap: var(--size-2); margin-top: var(--size-3); font-size: 0.75rem; }
.sort {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex-shrink: 0;
  padding: 0.375rem 0.625rem;
  border-radius: var(--radius-round);
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--ink);
}
.sort .dir { color: var(--ink-2); }

.group { display: flex; align-items: baseline; gap: 6px; margin-top: var(--size-5); font-size: 0.875rem; font-weight: 800; letter-spacing: -0.01em; }
.group .n { font-size: 0.75rem; font-weight: 600; color: var(--ink-2); }

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
