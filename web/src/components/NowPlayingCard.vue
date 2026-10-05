<script setup lang="ts">
import { computed } from 'vue'
import type { RunningGame } from '../types'
import { useNow } from '../useNow'
import AppIcon from './AppIcon.vue'
import GameCover from './GameCover.vue'

const props = defineProps<{ game: RunningGame; screen?: string | null }>()
const emit = defineEmits<{ focus: []; close: [] }>()

const now = useNow()
const clock = computed(() => {
  if (!props.game.startedAt) return null
  const minutes = Math.max(0, Math.floor((now.value - new Date(props.game.startedAt).getTime()) / 60000))
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
})
const label = computed(() => ['In gioco', props.game.source, props.screen && `su ${props.screen}`].filter(Boolean).join(' · '))
</script>

<template>
  <section class="hero" aria-label="Gioco in esecuzione">
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
.actions { position: relative; display: grid; grid-template-columns: 1fr 1fr; gap: var(--size-2); margin-top: var(--size-3); }
</style>
