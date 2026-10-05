<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { audioAppIconUrls } from '../api'
import type { AudioApp, VolumeChange } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'
import LevelMeter from './LevelMeter.vue'
import VolumeSlider from './VolumeSlider.vue'

const props = defineProps<{ app: AudioApp }>()
const emit = defineEmits<{ change: [change: VolumeChange] }>()

// Try each icon in turn; the initial on a coloured tile if none loads.
const iconAttempt = ref(0)
watch(() => props.app.id, () => (iconAttempt.value = 0))
const icon = computed(() => audioAppIconUrls(props.app)[iconAttempt.value] ?? null)
const volume = computed({
  get: () => props.app.volume,
  // Moving the slider of a muted app means you want to hear it.
  set: (v) => emit('change', props.app.muted ? { volume: v, muted: false } : { volume: v }),
})
</script>

<template>
  <li class="app card" :class="{ game: app.gameId }">
    <div class="head">
      <span class="icon" :style="icon ? undefined : { background: placeholderGradient(app.name) }">
        <img v-if="icon" :src="icon" alt="" @error="iconAttempt++" />
        <AppIcon v-else-if="app.system" name="speaker" :size="12" />
        <template v-else>{{ app.name.slice(0, 1).toUpperCase() }}</template>
      </span>
      <span class="name truncate">{{ app.name }}</span>
      <span v-if="app.gameId" class="badge">In gioco</span>
    </div>
    <div class="row">
      <VolumeSlider v-model="volume" :label="`Volume di ${app.name}`" :muted="app.muted" :tone="app.gameId ? 'run' : 'ink'" />
      <span class="pct tabular">{{ app.volume }}</span>
      <button
        class="icon-btn mute"
        :class="{ on: app.muted }"
        :aria-label="app.muted ? `Riattiva ${app.name}` : `Silenzia ${app.name}`"
        :aria-pressed="app.muted"
        @click="emit('change', { muted: !app.muted })"
      >
        <AppIcon :name="app.muted ? 'speaker-off' : 'speaker'" :size="16" />
      </button>
    </div>
    <LevelMeter :peak="app.muted ? 0 : app.peak" />
  </li>
</template>

<style scoped>
.app { display: grid; gap: 6px; padding: 8px 6px 10px 10px; }
.app.game { box-shadow: inset 0 0 0 2px var(--run); }
.head { display: flex; align-items: center; gap: var(--size-2); min-width: 0; padding-right: 4px; }
.icon {
  flex-shrink: 0;
  display: grid;
  place-items: center;
  inline-size: 1.375rem;
  block-size: 1.375rem;
  border-radius: 6px;
  overflow: hidden;
  font-size: 0.625rem;
  font-weight: 900;
  color: #fff;
}
.icon img { inline-size: 100%; block-size: 100%; object-fit: contain; }
.name { flex: 1; min-width: 0; font-size: 0.8125rem; font-weight: 700; }
.badge {
  flex-shrink: 0;
  padding: 3px 7px;
  border-radius: var(--radius-round);
  background: var(--run);
  color: var(--run-ink);
  font-size: 0.5625rem;
  font-weight: 850;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
.row { display: flex; align-items: center; gap: var(--size-2); }
.pct { inline-size: 1.75rem; text-align: right; font-size: 0.75rem; font-weight: 750; }
.mute { color: var(--ink-2); }
.mute.on { background: var(--danger); color: #fff; }
.app :deep(.meter) { margin-right: 4px; }
</style>
