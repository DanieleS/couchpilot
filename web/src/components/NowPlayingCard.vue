<script setup lang="ts">
import { computed } from 'vue'
import { clipLength, replayLabel, useCaptures } from '../captures'
import type { RunningGame } from '../types'
import { useNow } from '../useNow'
import AppIcon from './AppIcon.vue'
import ControllerChips from './ControllerChips.vue'
import GameCover from './GameCover.vue'
import { useCouchPilot } from '../store'

const props = defineProps<{ game: RunningGame; screen?: string | null }>()
const emit = defineEmits<{ focus: []; close: [] }>()

const { status } = useCouchPilot()
const now = useNow()
const { capture, pending, recordingSince, screenshot, saveReplay, toggleRecording } = useCaptures()
// The recording's own clock ticks every second; the session's every few.
const second = useNow(1000)
const recording = computed(() =>
  recordingSince.value ? clipLength(Math.max(0, (second.value - new Date(recordingSince.value).getTime()) / 1000)) : null,
)
const clock = computed(() => {
  if (!props.game.startedAt) return null
  const minutes = Math.max(0, Math.floor((now.value - new Date(props.game.startedAt).getTime()) / 60000))
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
})
const label = computed(() => ['In gioco', props.game.source, props.screen && `su ${props.screen}`].filter(Boolean).join(' · '))
</script>

<template>
  <section class="hero" :class="{ recording }" aria-label="Gioco in esecuzione">
    <svg class="deco" viewBox="0 0 320 200" preserveAspectRatio="xMaxYMin slice" aria-hidden="true">
      <circle cx="318" cy="-6" r="56" fill="var(--hero-deco-1)" />
      <polygon points="262,52 290,100 234,100" fill="var(--hero-deco-2)" />
    </svg>
    <div class="top">
      <GameCover :id="game.id" :name="game.name" :has-cover="game.hasCover" class="mini" />
      <div class="info">
        <p class="label truncate">{{ label }}</p>
        <p v-if="clock" class="clock tabular">{{ clock }}</p>
        <h2 class="title line-clamp-2">{{ game.name }}</h2>
      </div>
    </div>
    <span v-if="recording" class="rec tabular" role="status"><span class="rec-dot" aria-hidden="true" />REC {{ recording }}</span>
    <ControllerChips v-if="status?.controllers?.length" :controllers="status.controllers" on-color class="pads" />
    <div v-if="capture" class="captures">
      <button class="capture" :disabled="!!pending" @click="screenshot">
        <span v-if="pending === 'screenshot'" class="spinner" aria-hidden="true" /><AppIcon v-else name="camera" :size="20" />Scatta
      </button>
      <button class="capture" :class="{ stop: recording }" :disabled="!!pending" @click="toggleRecording">
        <span v-if="pending === 'record'" class="spinner" aria-hidden="true" /><AppIcon v-else :name="recording ? 'stop' : 'record'" :size="20" />
        <span class="tabular">{{ recording ? `Ferma · ${recording}` : 'Registra' }}</span>
      </button>
      <button class="capture dark" :disabled="!!pending || !!recording" @click="saveReplay">
        <span v-if="pending === 'replay'" class="spinner" aria-hidden="true" /><AppIcon v-else name="replay" :size="20" />{{ replayLabel(capture.replaySeconds) }}
      </button>
    </div>
    <div class="actions">
      <button class="btn btn-dark" @click="emit('focus')"><AppIcon name="focus" :size="16" />Primo piano</button>
      <button class="btn btn-danger" @click="emit('close')"><AppIcon name="power" :size="16" />Chiudi</button>
    </div>
  </section>
</template>

<style scoped>
.hero {
  position: relative;
  overflow: hidden;
  padding: var(--size-3);
  border-radius: var(--r-hero);
  background: var(--run);
  color: var(--run-ink);
}
.deco { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; pointer-events: none; }
.top { position: relative; display: flex; gap: var(--size-3); align-items: flex-start; }
.mini { inline-size: 3.625rem; flex-shrink: 0; border-radius: 8px; box-shadow: 0 4px 12px rgb(0 0 0 / 0.25); }
.info { min-width: 0; }
.label { font-size: 0.65625rem; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; opacity: 0.8; }
.clock { font-size: 2.75rem; font-weight: 800; font-stretch: 75%; letter-spacing: -0.02em; line-height: 0.9; margin-top: 6px; }
.title { font-size: 1.0625rem; font-weight: 800; line-height: 1.05; margin-top: 6px; }
.pads { position: relative; margin-top: var(--size-3); }
/* Room for the REC badge, so it doesn't sit on the label. */
.recording .top { padding-right: 6.5rem; }
.rec {
  position: absolute;
  top: var(--size-3);
  right: var(--size-3);
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 10px;
  border-radius: var(--radius-round);
  background: var(--button-dark);
  color: var(--button-dark-ink);
  font-size: 0.6875rem;
  font-weight: 800;
  letter-spacing: 0.08em;
}
.rec-dot { inline-size: 8px; block-size: 8px; border-radius: var(--radius-round); background: var(--danger); animation: blink 1.2s steps(1) infinite; }
@keyframes blink { 50% { opacity: 0.25; } }
.captures { position: relative; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: var(--size-2); margin-top: var(--size-3); }
.capture {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  block-size: 4rem;
  border-radius: var(--r-button);
  background: rgb(255 255 255 / 0.6);
  font-size: 0.75rem;
  font-weight: 700;
  transition: transform 100ms var(--ease-out-2), opacity 100ms;
}
.capture:active:not(:disabled) { transform: scale(0.97); }
.capture:disabled { opacity: 0.45; }
.capture.stop { background: var(--danger); color: #fff; }
.capture.dark { background: var(--button-dark); color: var(--button-dark-ink); }
.actions { position: relative; display: grid; grid-template-columns: 1fr 1fr; gap: var(--size-2); margin-top: var(--size-3); }
</style>
