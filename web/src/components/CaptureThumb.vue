<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { captureThumbUrl, imageUrl } from '../api'
import { clipLength, isGif } from '../captures'
import { locale } from '../i18n'
import { useCouchPilot } from '../store'
import type { Capture } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'

// A screenshot or clip at grid size: the PC's thumbnail, a clip's length, optionally the time.
const props = defineProps<{ capture: Capture; time?: boolean }>()
const { games } = useCouchPilot()

// The PC's thumbnail; failing that (a damaged clip), the game's cover, so a tile is never a blank.
const failed = ref(false)
watch(() => props.capture.id + props.capture.version, () => (failed.value = false))
const url = computed(() => (failed.value ? null : captureThumbUrl(props.capture)))
const onError = () => (failed.value = true)
const cover = computed(() => {
  const g = props.capture.gameId ? games.value?.find((x) => x.id === props.capture.gameId) : null
  return g?.hasCover ? imageUrl(g.id, 'cover') : null
})
const clock = computed(() => new Date(props.capture.takenAt).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' }))
</script>

<template>
  <span class="thumb" :style="url || cover ? undefined : { background: placeholderGradient(capture.game ?? capture.id, 30, 14) }">
    <img v-if="url" :src="url" alt="" loading="lazy" decoding="async" @error="onError" />
    <template v-else>
      <img v-if="cover" class="cover" :src="cover" alt="" loading="lazy" />
      <AppIcon v-if="capture.kind === 'video'" class="mark" name="play" :size="22" />
      <AppIcon v-else class="mark" name="image" :size="22" />
    </template>
    <span v-if="capture.kind === 'video'" class="badge start tabular">
      <AppIcon :name="capture.replay ? 'replay' : 'play'" :size="9" />{{ clipLength(capture.seconds) }}
    </span>
    <span v-else-if="isGif(capture)" class="badge start">GIF</span>
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
/* The game's cover standing in: dimmed, so it doesn't pass for the capture itself. */
img.cover { filter: brightness(0.55) saturate(0.8); }
.mark { position: absolute; inset: 50% auto auto 50%; transform: translate(-50%, -50%); color: rgb(255 255 255 / 0.85); }
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
