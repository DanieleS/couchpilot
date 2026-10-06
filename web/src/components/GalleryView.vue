<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { imageUrl } from '../api'
import { dayLabel, fileSize, playDayKey, toolName, useCaptures } from '../captures'
import { plural, t } from '../i18n'
import { useCouchPilot } from '../store'
import type { Capture } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'
import CaptureThumb from './CaptureThumb.vue'
import PcChip from './PcChip.vue'

// Every screenshot and clip, by day, narrowed to a kind and a game.
const props = defineProps<{ game?: string | null }>()
const emit = defineEmits<{ back: []; view: [items: Capture[], index: number] }>()

const { status, games } = useCouchPilot()
const { capture, list, items, markSeen } = useCaptures()

type Kind = 'all' | 'photo' | 'video'
const kind = ref<Kind>('all')
// A Playnite game id, or the name the tool gave a capture no game claims ("Desktop").
const game = ref<string | null>(props.game ?? null)
watch(() => props.game, (g) => (game.value = g ?? null))

// Opening the gallery is seeing what's new; so is a capture arriving while it's open.
onMounted(markSeen)
watch(items, markSeen)

const keyOf = (c: Capture) => c.gameId ?? c.game ?? ''
const ofGame = computed(() => (game.value === null ? items.value : items.value.filter((c) => keyOf(c) === game.value)))
const shown = computed(() => (kind.value === 'all' ? ofGame.value : ofGame.value.filter((c) => c.kind === kind.value)))

const kinds = computed(() => [
  { key: 'all' as Kind, label: t.captures.gallery.all, n: ofGame.value.length },
  { key: 'photo' as Kind, label: t.captures.gallery.photos, n: ofGame.value.filter((c) => c.kind === 'photo').length },
  { key: 'video' as Kind, label: t.captures.gallery.clips, n: ofGame.value.filter((c) => c.kind === 'video').length },
])

// The games with captures, the most captured first; the one opened from a game's sheet leads,
// so it's in view.
const gameChips = computed(() => {
  const byKey = new Map<string, { key: string; name: string; id: string | null; n: number }>()
  for (const c of items.value) {
    const key = keyOf(c)
    const chip = byKey.get(key) ?? { key, name: c.game ?? t.captures.gallery.other, id: c.gameId, n: 0 }
    chip.n++
    byKey.set(key, chip)
  }
  const lead = props.game ?? null
  return [...byKey.values()].sort((a, b) => Number(b.key === lead) - Number(a.key === lead) || b.n - a.n)
})
const coverOf = (id: string | null) => {
  const g = id ? games.value?.find((x) => x.id === id) : null
  return g?.hasCover ? imageUrl(g.id, 'cover') : null
}

// A long history renders a page at a time.
const PAGE = 90
const limit = ref(PAGE)
watch([kind, game], () => (limit.value = PAGE))
const days = computed(() => {
  const out: { key: string; label: string; games: string; items: Capture[] }[] = []
  for (const c of shown.value.slice(0, limit.value)) {
    const key = playDayKey(c.takenAt)
    let day = out[out.length - 1]
    if (day?.key !== key) out.push((day = { key, label: dayLabel(key), games: '', items: [] }))
    day.items.push(c)
  }
  for (const d of out) d.games = [...new Set(d.items.map((c) => c.game ?? t.captures.gallery.other))].join(', ')
  return out
})

const summary = computed(() => {
  const l = list.value
  if (!l) return ''
  const tools = [...new Set(l.items.map((c) => toolName(c.tool)).filter(Boolean))].join(t.captures.gallery.toolsJoin)
  return [plural(l.items.length, t.captures.gallery.captureOne, t.captures.gallery.captureOther), fileSize(l.bytes), tools].filter(Boolean).join(' · ')
})

const view = (c: Capture) => emit('view', shown.value, shown.value.indexOf(c))
</script>

<template>
  <div class="page">
    <button class="back" @click="emit('back')"><AppIcon name="chevron-left" :size="16" />{{ t.captures.gallery.back }}</button>
    <header class="page-header">
      <h1>{{ t.captures.gallery.title }}</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <div v-if="!capture" class="empty">
      <p class="empty-title">{{ t.captures.gallery.offTitle }}</p>
      <p class="muted">{{ t.captures.gallery.offHint }}</p>
    </div>
    <template v-else-if="list">
      <p class="summary muted tabular">{{ summary }}</p>

      <div class="seg" role="tablist" :aria-label="t.captures.gallery.kind">
        <button v-for="k in kinds" :key="k.key" role="tab" :aria-selected="kind === k.key" :class="{ on: kind === k.key }" @click="kind = k.key">
          {{ k.label }} <span class="n tabular">{{ k.n }}</span>
        </button>
      </div>

      <div v-if="gameChips.length > 1" class="scroll-row chips">
        <button class="chip" :class="{ on: game === null }" @click="game = null">{{ t.captures.gallery.allGames }}</button>
        <button v-for="g in gameChips" :key="g.key" class="chip" :class="{ on: game === g.key }" @click="game = g.key">
          <span class="art" :style="coverOf(g.id) ? undefined : { background: placeholderGradient(g.name, 48, 20) }">
            <img v-if="coverOf(g.id)" :src="coverOf(g.id)!" alt="" loading="lazy" />
          </span>
          {{ g.name }}
        </button>
      </div>

      <div v-if="!shown.length" class="empty">
        <template v-if="items.length">
          <p class="empty-title">{{ t.captures.gallery.nothingTitle }}</p>
          <p class="muted">{{ t.captures.gallery.nothingHint }}</p>
        </template>
        <template v-else>
          <p class="empty-title">{{ t.captures.gallery.noneTitle }}</p>
          <p class="muted">
            {{ t.captures.gallery.noneHint }}
            <template v-if="list.folders.length">{{ t.captures.gallery.folders(list.folders.join(', ')) }}</template>
          </p>
        </template>
      </div>

      <section v-for="d in days" :key="d.key" :aria-label="d.label">
        <div class="eyebrow day">
          <span class="when">{{ d.label }}</span>
          <span class="aside truncate">{{ d.games }} · {{ d.items.length }}</span>
        </div>
        <div class="grid">
          <button v-for="c in d.items" :key="c.id" class="tile" :aria-label="t.captures.gallery.tile(c.kind === 'video', c.game)" @click="view(c)">
            <CaptureThumb :capture="c" />
          </button>
        </div>
      </section>

      <button v-if="shown.length > limit" class="btn btn-ghost btn-block more" @click="limit += PAGE">{{ t.captures.gallery.more }}</button>
    </template>
    <div v-else class="skeleton" />
  </div>
</template>

<style scoped>
.back { display: inline-flex; align-items: center; gap: 2px; margin-left: -4px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.page-header { padding-top: 4px; }
.summary { margin-top: var(--size-2); font-size: 0.75rem; font-weight: 600; }

.seg { display: grid; grid-template-columns: repeat(3, 1fr); gap: 3px; margin-top: var(--size-4); padding: 3px; border-radius: var(--r-button); background: var(--card); box-shadow: var(--card-edge); }
.seg button { padding: 0.5rem 0; border-radius: 9px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.seg button.on { background: var(--ink); color: var(--bg); }
.seg .n { font-weight: 500; opacity: 0.7; }

.chips { margin-top: var(--size-3); }
.chip {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-block-size: 2.25rem;
  padding: 4px 12px 4px 4px;
  border-radius: var(--radius-round);
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.75rem;
  font-weight: 700;
  white-space: nowrap;
}
.chip:first-child { padding-left: 12px; }
.chip.on { background: var(--ink); color: var(--bg); }
.art { position: relative; inline-size: 1.75rem; block-size: 1.75rem; border-radius: var(--radius-round); overflow: hidden; flex-shrink: 0; }
.art img { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: cover; }

.day { gap: var(--size-3); }
.when { color: var(--ink); }
.aside { min-width: 0; letter-spacing: 0.02em; text-transform: none; font-weight: 600; }
.grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 4px; }
.tile { display: block; min-width: 0; border-radius: 8px; overflow: hidden; transition: transform 100ms var(--ease-out-2); }
.tile:active { transform: scale(0.97); }
.more { margin-top: var(--size-4); }

.empty { padding: var(--size-9) 0; text-align: center; font-size: 0.8125rem; line-height: 1.45; }
.empty-title { font-weight: 750; font-size: 0.9375rem; margin-bottom: 4px; }
.skeleton { margin-top: var(--size-4); block-size: 14rem; border-radius: var(--r-card); background: var(--card); animation: var(--animation-blink); }
</style>
