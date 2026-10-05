<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import type { Game, GameDetail } from '../types'
import { stripHtml } from '../utils'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'
import GameCover from './GameCover.vue'

const props = defineProps<{ game: Game | null; running: boolean }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ start: []; focus: []; stop: []; install: []; uninstall: [] }>()

const detail = ref<GameDetail | null>(null)
watch(
  () => props.game?.id,
  (id) => {
    detail.value = null
    if (id) api.game(id).then((d) => (detail.value = d)).catch(() => {})
  },
  { immediate: true },
)

const hours = (m: number) => (m < 60 ? `${m} min` : `${Math.round(m / 60)} h`)
const meta = computed(() => {
  const g = props.game
  if (!g) return ''
  return [g.source, g.playtimeMinutes ? `${hours(g.playtimeMinutes)} giocate` : null, g.releaseYear].filter(Boolean).join(' · ')
})
const description = computed(() => (detail.value?.description ? stripHtml(detail.value.description) : ''))
</script>

<template>
  <BottomSheet v-model:open="open" :title="game?.name ?? ''">
    <template v-if="game">
      <div class="head">
        <GameCover :id="game.id" :name="game.name" :has-cover="game.hasCover" class="cover" />
        <div class="info">
          <h2 class="name">{{ game.name }}</h2>
          <p class="meta">{{ meta }}</p>
          <p v-if="game.genres.length" class="meta">{{ game.genres.slice(0, 3).join(', ') }}</p>
        </div>
      </div>

      <div v-if="running" class="actions two">
        <button class="btn btn-dark" @click="emit('focus')"><AppIcon name="focus" :size="16" />Primo piano</button>
        <button class="btn btn-danger" @click="emit('stop')"><AppIcon name="power" :size="16" />Chiudi gioco</button>
      </div>
      <button v-else-if="game.installing || game.uninstalling" class="btn btn-ink btn-block play" disabled>
        <span class="spinner" aria-hidden="true" />{{ game.installing ? 'Installazione in corso…' : 'Disinstallazione in corso…' }}
      </button>
      <template v-else-if="game.installed">
        <button class="btn btn-ink btn-block play" @click="emit('start')"><AppIcon name="play" :size="18" />Gioca</button>
        <button class="uninstall" @click="emit('uninstall')"><AppIcon name="trash" :size="14" />Disinstalla</button>
      </template>
      <button v-else class="btn btn-dark btn-block play" @click="emit('install')"><AppIcon name="download" :size="18" />Installa</button>

      <p v-if="description" class="desc">{{ description }}</p>
    </template>
  </BottomSheet>
</template>

<style scoped>
.head { display: flex; gap: var(--size-3); align-items: flex-end; }
.cover { inline-size: 6rem; flex-shrink: 0; }
.info { min-width: 0; }
.name { font-size: 1.375rem; font-weight: 800; line-height: 1.05; letter-spacing: -0.02em; }
.meta { font-size: 0.75rem; color: var(--ink-2); margin-top: 4px; }
.actions.two { display: grid; grid-template-columns: 1fr 1fr; gap: var(--size-2); margin-top: var(--size-4); }
.play { margin-top: var(--size-4); padding-block: 0.9rem; font-size: 0.9375rem; }
.uninstall {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  inline-size: 100%;
  margin-top: var(--size-2);
  padding: 0.5rem;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--ink-2);
}
.spinner {
  inline-size: 16px;
  block-size: 16px;
  border-radius: var(--radius-round);
  border: 2px solid currentColor;
  border-right-color: transparent;
  animation: spin 800ms linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.desc {
  margin: var(--size-4) 0 var(--size-2);
  font-size: 0.8125rem;
  line-height: 1.5;
  color: var(--ink-2);
  display: -webkit-box;
  -webkit-line-clamp: 6;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
