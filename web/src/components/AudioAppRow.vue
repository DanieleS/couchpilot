<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { audioAppIconUrls } from '../api'
import type { MixerChannel } from '../audio'
import { t } from '../i18n'
import type { VolumeChange } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'
import LevelMeter from './LevelMeter.vue'
import VolumeSlider from './VolumeSlider.vue'

const props = defineProps<{ app: MixerChannel; level: number }>()
const emit = defineEmits<{ change: [change: VolumeChange] }>()

// Try each icon in turn; the initial on a coloured tile if none loads.
const iconAttempt = ref(0)
watch(() => props.app.stableKey, () => (iconAttempt.value = 0))
// Windows names the system-sounds channel in its own language: the phone uses its own.
const name = computed(() => (props.app.role === 'system' ? t.audio.systemSounds : props.app.name))
const icon = computed(() => audioAppIconUrls(props.app)[iconAttempt.value] ?? null)
const volume = computed({
  get: () => props.app.volume,
  // Moving the slider of a muted app means you want to hear it.
  set: (v) => emit('change', props.app.muted ? { volume: v, muted: false } : { volume: v }),
})
</script>

<template>
  <li class="app card">
    <div class="head">
      <span class="icon" :style="icon ? undefined : { background: placeholderGradient(name) }">
        <img v-if="icon" :src="icon" alt="" @error="iconAttempt++" />
        <AppIcon v-else-if="app.role === 'system'" name="speaker" :size="12" />
        <template v-else>{{ name.slice(0, 1).toUpperCase() }}</template>
      </span>
      <span class="name truncate">{{ name }}</span>
    </div>
    <div class="row">
      <VolumeSlider v-model="volume" :label="t.audio.volumeOf(name)" :muted="app.muted" />
      <span class="pct tabular">{{ app.volume }}</span>
      <button
        class="icon-btn mute"
        :class="{ on: app.muted }"
        :aria-label="app.muted ? t.audio.unmute(name) : t.audio.mute(name)"
        :aria-pressed="app.muted"
        @click="emit('change', { muted: !app.muted })"
      >
        <AppIcon :name="app.muted ? 'speaker-off' : 'speaker'" :size="16" />
      </button>
    </div>
    <LevelMeter :level="level" />
  </li>
</template>

<style scoped>
.app { display: grid; gap: 6px; padding: 8px 6px 10px 10px; }
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
.row { display: flex; align-items: center; gap: var(--size-2); }
.pct { inline-size: 1.75rem; text-align: right; font-size: 0.75rem; font-weight: 750; }
.mute { color: var(--ink-2); }
.mute.on { background: var(--danger); color: #fff; }
.app :deep(.meter) { margin-right: 4px; }
</style>
