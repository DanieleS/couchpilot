<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { api, imageUrl } from '../api'
import type { MixerChannel } from '../audio'
import { t } from '../i18n'
import { useCouchPilot } from '../store'
import type { GameDetail, VolumeChange } from '../types'
import { useNow } from '../useNow'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'
import GameCover from './GameCover.vue'
import VolumeSlider from './VolumeSlider.vue'

// The game Playnite launched, at the top of the mixer: its art, what Playnite knows about it,
// and its own volume with a proper meter, since it's what you came here to adjust.
const props = defineProps<{ channel: MixerChannel; level: number }>()
const emit = defineEmits<{ change: [change: VolumeChange] }>()

const { games, running } = useCouchPilot()
const game = computed(() => games.value?.find((g) => g.id === props.channel.gameId) ?? null)
const session = computed(() => running.value.find((r) => r.id === props.channel.gameId) ?? null)

const detail = ref<GameDetail | null>(null)
watch(
  () => props.channel.gameId,
  (id) => {
    detail.value = null
    if (id) api.game(id).then((d) => (detail.value = d)).catch(() => {})
  },
  { immediate: true },
)

// The background art if Playnite has it, else the cover blown up and blurred, else the same
// gradient the library uses for games without art.
const artFailed = ref(false)
const art = computed(() => {
  const id = props.channel.gameId
  if (!id || artFailed.value) return null
  if (game.value?.hasBackground) return { url: imageUrl(id, 'background'), blur: false }
  if (game.value?.hasCover) return { url: imageUrl(id, 'cover'), blur: true }
  return null
})

const now = useNow()
const playing = computed(() => {
  const started = session.value?.startedAt
  if (!started) return null
  const minutes = Math.max(0, Math.floor((now.value - new Date(started).getTime()) / 60000))
  if (minutes < 1) return t.audio.game.justStarted
  return minutes < 60 ? t.audio.game.forMinutes(minutes) : t.audio.game.forHours(Math.floor(minutes / 60), String(minutes % 60).padStart(2, '0'))
})
const hours = (m: number) => (m < 60 ? `${m} min` : `${Math.round(m / 60)} h`)
const label = computed(() => [t.audio.game.inGame, game.value?.source].filter(Boolean).join(' · '))
const credits = computed(() =>
  [detail.value?.developers[0], game.value?.releaseYear, game.value?.genres[0]].filter(Boolean).join(' · '),
)
const time = computed(() =>
  [playing.value, game.value?.playtimeMinutes ? t.audio.game.total(hours(game.value.playtimeMinutes)) : null].filter(Boolean).join(' · '),
)

const volume = computed({
  get: () => props.channel.volume,
  set: (v) => emit('change', props.channel.muted ? { volume: v, muted: false } : { volume: v }),
})

// A segmented meter like a mixing desk's, with a peak marker that falls back slowly.
const SEGMENTS = 24
const hold = ref(0)
watch(
  () => props.level,
  (level) => (hold.value = Math.max(level, hold.value)),
)
// Falls on its own clock: when the game goes quiet the PC stops sending levels altogether.
const fall = setInterval(() => (hold.value = Math.max(props.level, hold.value - 0.015)), 40)
onScopeDispose(() => clearInterval(fall))
const lit = computed(() => Math.round(props.level * SEGMENTS))
const held = computed(() => Math.min(SEGMENTS, Math.round(hold.value * SEGMENTS)))
const zone = (i: number) => (i >= SEGMENTS - 2 ? 'hot' : i >= SEGMENTS - 6 ? 'warm' : 'cool')
</script>

<template>
  <section class="game" :aria-label="t.audio.game.running">
    <div class="art" :style="art ? undefined : { background: placeholderGradient(channel.name, 40, 14) }" aria-hidden="true">
      <img v-if="art" :src="art.url!" alt="" :class="{ blur: art.blur }" @error="artFailed = true" />
    </div>
    <div class="shade" aria-hidden="true" />

    <div class="top">
      <GameCover :id="channel.gameId!" :name="channel.name" :has-cover="game?.hasCover ?? false" class="cover" />
      <div class="info">
        <p class="label truncate">{{ label }}</p>
        <h2 class="title line-clamp-2">{{ channel.name }}</h2>
        <p v-if="credits" class="meta truncate">{{ credits }}</p>
        <p v-if="time" class="meta tabular truncate">{{ time }}</p>
      </div>
    </div>

    <div class="vu" aria-hidden="true">
      <span
        v-for="i in SEGMENTS"
        :key="i"
        class="seg"
        :class="[zone(i - 1), { on: !channel.muted && i <= lit, peak: !channel.muted && i === held && held > lit }]"
      />
    </div>

    <div class="row">
      <VolumeSlider v-model="volume" big tone="run" :label="t.audio.volumeOf(channel.name)" :muted="channel.muted" />
      <span class="pct tabular">{{ channel.muted ? t.audio.muted : channel.volume }}</span>
      <button
        class="icon-btn mute"
        :class="{ on: channel.muted }"
        :aria-label="channel.muted ? t.audio.unmute(channel.name) : t.audio.mute(channel.name)"
        :aria-pressed="channel.muted"
        @click="emit('change', { muted: !channel.muted })"
      >
        <AppIcon :name="channel.muted ? 'speaker-off' : 'speaker'" :size="18" />
      </button>
    </div>
  </section>
</template>

<style scoped>
/* Always dark: it sits on the game's own art, whatever the phone's theme. */
.game {
  --slider-track: rgb(255 255 255 / 0.16);
  --line: rgb(255 255 255 / 0.3);
  --card: #fff;
  position: relative;
  overflow: hidden;
  display: grid;
  gap: var(--size-3);
  padding: var(--size-3);
  border-radius: var(--r-hero);
  background: #141416;
  color: #fff;
  box-shadow: inset 0 0 0 2px var(--run), 0 10px 30px -12px rgb(0 0 0 / 0.5);
}
.art, .shade { position: absolute; inset: 0; }
.art img { inline-size: 100%; block-size: 100%; object-fit: cover; }
.art img.blur { filter: blur(18px) saturate(1.2); transform: scale(1.3); }
.shade { background: linear-gradient(180deg, rgb(10 10 12 / 0.35), rgb(10 10 12 / 0.85) 70%); }
.top, .vu, .row { position: relative; }

.top { display: flex; gap: var(--size-3); align-items: flex-end; }
.cover { inline-size: 4.25rem; flex-shrink: 0; border-radius: 10px; box-shadow: 0 6px 18px rgb(0 0 0 / 0.45); }
.info { min-width: 0; display: grid; gap: 3px; }
.label { font-size: 0.625rem; font-weight: 800; letter-spacing: 0.12em; text-transform: uppercase; color: var(--run); }
.title { font-size: 1.25rem; font-weight: 800; line-height: 1.05; letter-spacing: -0.02em; text-wrap: balance; }
.meta { font-size: 0.71875rem; font-weight: 600; color: rgb(255 255 255 / 0.72); }

.vu { display: grid; grid-template-columns: repeat(24, 1fr); gap: 3px; block-size: 10px; }
.seg { border-radius: 2px; background: rgb(255 255 255 / 0.12); transition: background 60ms linear; }
.seg.on.cool { background: #fff; }
.seg.on.warm, .seg.peak.warm { background: var(--run); }
.seg.on.hot, .seg.peak.hot { background: var(--accent); }
.seg.peak.cool { background: rgb(255 255 255 / 0.7); }

.row { display: flex; align-items: center; gap: var(--size-2); }
.pct { min-inline-size: 2.25rem; text-align: right; font-size: 0.8125rem; font-weight: 800; }
.mute { color: rgb(255 255 255 / 0.8); background: rgb(255 255 255 / 0.12); }
.mute.on { background: var(--danger); color: #fff; }
</style>
