<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mediaArtUrl } from '../api'
import { t } from '../i18n'
import { appName, useNowPlaying } from '../media'
import { useNow } from '../useNow'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'

// The track playing on the PC, with play/pause and skip: Windows' media flyout, on the phone.
const { media, receivedAt, playPause, next, previous } = useNowPlaying()
const now = useNow(1000)

const artFailed = ref(false)
watch(() => media.value?.artVersion, () => (artFailed.value = false))
const art = computed(() => (media.value?.artVersion && !artFailed.value ? mediaArtUrl(media.value.artVersion) : null))
const subtitle = computed(() => [media.value?.artist, appName(media.value?.app ?? null)].filter(Boolean).join(' · '))

// The PC reports the position as of when it answered; while playing, the bar moves on here.
const position = computed(() => {
  const m = media.value
  if (!m || m.positionSeconds === null || m.durationSeconds === null) return null
  const elapsed = m.playing ? (now.value - receivedAt.value) / 1000 : 0
  return Math.min(m.durationSeconds, m.positionSeconds + elapsed)
})
const clock = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`
</script>

<template>
  <section v-if="media" class="media card" :aria-label="t.audio.media.label">
    <div class="top">
      <span class="art" :style="art ? undefined : { background: placeholderGradient(media.title) }">
        <img v-if="art" :src="art" alt="" @error="artFailed = true" />
        <AppIcon v-else name="music" :size="20" />
      </span>
      <div class="text">
        <p class="label">{{ media.playing ? t.audio.media.playing : t.audio.media.paused }}</p>
        <p class="title truncate">{{ media.title }}</p>
        <p v-if="subtitle" class="sub truncate">{{ subtitle }}</p>
      </div>
    </div>
    <div v-if="position !== null && media.durationSeconds" class="timeline">
      <span class="bar"><span class="fill" :style="{ transform: `scaleX(${position / media.durationSeconds})` }" /></span>
      <span class="times tabular"><span>{{ clock(position) }}</span><span>{{ clock(media.durationSeconds) }}</span></span>
    </div>
    <div class="controls">
      <button class="icon-btn" :disabled="!media.canPrevious" :aria-label="t.audio.media.previous" @click="previous"><AppIcon name="previous" :size="18" /></button>
      <button class="icon-btn play" :disabled="!media.canPlayPause" :aria-label="media.playing ? t.audio.media.pause : t.audio.media.play" @click="playPause">
        <AppIcon :name="media.playing ? 'pause' : 'play'" :size="20" />
      </button>
      <button class="icon-btn" :disabled="!media.canNext" :aria-label="t.audio.media.next" @click="next"><AppIcon name="next" :size="18" /></button>
    </div>
  </section>
</template>

<style scoped>
.media { display: grid; gap: var(--size-3); padding: var(--size-3); }
.top { display: flex; align-items: center; gap: var(--size-3); min-width: 0; }
.art {
  flex-shrink: 0;
  display: grid;
  place-items: center;
  inline-size: 3.5rem;
  block-size: 3.5rem;
  border-radius: var(--r-inner);
  overflow: hidden;
  color: #fff;
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.18);
}
.art img { inline-size: 100%; block-size: 100%; object-fit: cover; }
.text { min-width: 0; display: grid; gap: 2px; }
.label { font-size: 0.5625rem; font-weight: 800; letter-spacing: 0.1em; text-transform: uppercase; color: var(--ink-2); }
.title { font-size: 0.9375rem; font-weight: 800; letter-spacing: -0.01em; }
.sub { font-size: 0.75rem; font-weight: 600; color: var(--ink-2); }
.timeline { display: grid; gap: 4px; }
.bar { display: block; block-size: 4px; border-radius: 2px; background: var(--slider-track); overflow: hidden; }
.fill { display: block; block-size: 100%; background: var(--ink); transform-origin: left; transition: transform 1s linear; }
.times { display: flex; justify-content: space-between; font-size: 0.625rem; font-weight: 700; color: var(--ink-2); }
.controls { display: flex; align-items: center; justify-content: center; gap: var(--size-5); }
.controls .icon-btn { color: var(--ink); }
.controls .icon-btn:disabled { opacity: 0.3; }
.controls .play { inline-size: 3rem; block-size: 3rem; background: var(--ink); color: var(--bg); }
</style>
