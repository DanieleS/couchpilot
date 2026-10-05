<script setup lang="ts">
import { computed, ref } from 'vue'
import { captureThumbUrl } from '../api'
import { clipLength } from '../captures'
import type { Capture } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'

// A screenshot or clip at grid size: the PC's thumbnail, a clip's length, optionally the time.
const props = defineProps<{ capture: Capture; time?: boolean }>()

// A video Windows has no thumbnail for (no codec) falls back to the game's colours.
const failed = ref(false)
const url = computed(() => (failed.value ? null : captureThumbUrl(props.capture)))
const clock = computed(() => new Date(props.capture.takenAt).toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' }))
</script>

<template>
  <span class="thumb" :style="url ? undefined : { background: placeholderGradient(capture.game ?? capture.id, 30, 14) }">
    <img v-if="url" :src="url" alt="" loading="lazy" decoding="async" @error="failed = true" />
    <span v-if="capture.kind === 'video'" class="badge start tabular">
      <AppIcon :name="capture.replay ? 'replay' : 'play'" :size="9" />{{ clipLength(capture.seconds) }}
    </span>
    <span v-if="time" class="badge end tabular">{{ clock }}</span>
  </span>
</template>

<style scoped>
.thumb {
  position: relative;
  display: block;
  aspect-ratio: 16 / 9;
  overflow: hidden;
  border-radius: inherit;
  background: var(--well);
}
img {
  position: absolute;
  inset: 0;
  inline-size: 100%;
  block-size: 100%;
  object-fit: cover;
}
.badge {
  position: absolute;
  bottom: 4px;
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 1px 5px;
  border-radius: 5px;
  background: rgb(0 0 0 / 0.6);
  color: #fff;
  font-size: 0.625rem;
  font-weight: 700;
  line-height: 1.5;
}
.badge.start { left: 4px; }
.badge.end { right: 4px; font-weight: 600; background: rgb(0 0 0 / 0.45); }
</style>
