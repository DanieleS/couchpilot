<script setup lang="ts">
import { t } from '../i18n'
import type { RunningGame } from '../types'
import AppIcon from './AppIcon.vue'
import GameCover from './GameCover.vue'

defineProps<{ game: RunningGame }>()
const emit = defineEmits<{ focus: []; close: [] }>()
</script>

<template>
  <div class="bar">
    <GameCover :id="game.id" :name="game.name" :has-cover="game.hasCover" class="mini" />
    <div class="info">
      <p class="label">{{ t.audio.nowPlaying.inGame }}</p>
      <p class="name truncate">{{ game.name }}</p>
    </div>
    <button class="icon-btn dark" :aria-label="t.audio.nowPlaying.focus" @click="emit('focus')"><AppIcon name="focus" :size="16" /></button>
    <button class="icon-btn danger" :aria-label="t.audio.nowPlaying.close" @click="emit('close')"><AppIcon name="power" :size="16" /></button>
  </div>
</template>

<style scoped>
.bar {
  display: flex;
  align-items: center;
  gap: var(--size-2);
  padding: 6px 8px 6px 6px;
  border-radius: var(--r-card);
  background: var(--run);
  color: var(--run-ink);
  box-shadow: 0 8px 24px -8px rgb(0 0 0 / 0.35);
}
.mini { inline-size: 2rem; border-radius: 6px; flex-shrink: 0; }
.info { flex: 1; min-width: 0; }
.label { font-size: 0.5625rem; font-weight: 800; letter-spacing: 0.1em; text-transform: uppercase; opacity: 0.75; }
.name { font-size: 0.8125rem; font-weight: 750; }
.dark { background: var(--button-dark); color: var(--button-dark-ink); }
.danger { background: var(--danger); color: #fff; }
</style>
