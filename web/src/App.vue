<script setup lang="ts">
import { ToastDescription, ToastProvider, ToastRoot, ToastViewport } from 'reka-ui'
import { computed, ref, watch } from 'vue'
import { api } from './api'
import type { Game, Preset, RunningGame } from './types'
import { useCouchPilot } from './store'
import ConfirmDialog from './components/ConfirmDialog.vue'
import AudioView from './components/AudioView.vue'
import ControlView from './components/ControlView.vue'
import DisplaysView from './components/DisplaysView.vue'
import GameSheet from './components/GameSheet.vue'
import GamesView from './components/GamesView.vue'
import NamePrompt from './components/NamePrompt.vue'
import TabBar, { type Tab } from './components/TabBar.vue'

const { error, running, runningIds, act } = useCouchPilot()

// The manifest's shortcuts (long press on the app's icon) open a tab straight away: /?tab=audio.
function initialTab(): Tab {
  const asked = new URLSearchParams(location.search).get('tab')
  if (asked) history.replaceState(null, '', '/')
  return asked === 'displays' || asked === 'audio' || asked === 'control' ? asked : 'games'
}

const tab = ref<Tab>(initialTab())
watch(tab, () => window.scrollTo({ top: 0 }))

// Game sheet
const sheetGame = ref<Game | null>(null)
const sheetOpen = ref(false)
const openGame = (g: Game) => {
  sheetGame.value = g
  sheetOpen.value = true
}
const start = async (g: Game) => {
  if (await act(() => api.start(g.id))) sheetOpen.value = false
}

// Closing a game always asks first: there's no undo for an unsaved run.
const closing = ref<RunningGame | null>(null)
const closeOpen = computed({
  get: () => closing.value !== null,
  set: (v) => !v && (closing.value = null),
})
const askClose = (g: RunningGame) => (closing.value = g)
const askCloseFromSheet = () => {
  const g = running.value.find((r) => r.id === sheetGame.value?.id)
  if (g) askClose(g)
}
const confirmClose = () => {
  const g = closing.value
  if (!g) return
  sheetOpen.value = false
  act(() => api.stop(g.id))
}

// Display presets
const promptOpen = ref(false)
const renaming = ref<Preset | null>(null)
const askSave = () => {
  renaming.value = null
  promptOpen.value = true
}
const askRename = (p: Preset) => {
  renaming.value = p
  promptOpen.value = true
}
const submitName = (name: string) =>
  renaming.value ? act(() => api.renamePreset(renaming.value!.id, name)) : act(() => api.savePreset(name))

const removing = ref<Preset | null>(null)
const removeOpen = computed({
  get: () => removing.value !== null,
  set: (v) => !v && (removing.value = null),
})
const confirmRemove = () => removing.value && act(() => api.deletePreset(removing.value!.id))

const focusGame = (id: string) => act(() => api.focus(id))
const focusSheetGame = () => sheetGame.value && focusGame(sheetGame.value.id)
const startSheetGame = () => sheetGame.value && start(sheetGame.value)

const applyPreset = (id: string) => act(() => api.applyPreset(id))
const keepDisplays = () => act(api.confirmDisplays)
const revertDisplays = () => act(api.revertDisplays)

const toastOpen = computed({
  get: () => error.value !== null,
  set: (v) => !v && (error.value = null),
})
</script>

<template>
  <ToastProvider :duration="4000" swipe-direction="down">
    <main>
      <GamesView
        v-if="tab === 'games'"
        @open="openGame"
        @focus="(g) => focusGame(g.id)"
        @close="askClose"
        @apply-preset="applyPreset"
        @save-preset="askSave"
        @keep-displays="keepDisplays"
        @revert-displays="revertDisplays"
        @manage-displays="tab = 'displays'"
      />
      <DisplaysView
        v-else-if="tab === 'displays'"
        @apply="applyPreset"
        @save="askSave"
        @rename="askRename"
        @remove="(p) => (removing = p)"
        @keep="keepDisplays"
        @revert="revertDisplays"
      />
      <AudioView v-else-if="tab === 'audio'" />
      <ControlView v-else />
    </main>

    <TabBar v-model="tab" />

    <GameSheet
      v-model:open="sheetOpen"
      :game="sheetGame"
      :running="!!sheetGame && runningIds.has(sheetGame.id)"
      @start="startSheetGame"
      @focus="focusSheetGame"
      @stop="askCloseFromSheet"
    />

    <ConfirmDialog
      v-model:open="closeOpen"
      icon="power"
      :title="`Chiudere ${closing?.name ?? ''}?`"
      description="Prima chiedo al gioco di uscire. Se non risponde entro 10 secondi lo chiudo forzatamente."
      confirm-label="Chiudi"
      @confirm="confirmClose"
    />

    <ConfirmDialog
      v-model:open="removeOpen"
      icon="trash"
      :title="`Eliminare «${removing?.name ?? ''}»?`"
      description="Il preset sparisce, gli schermi restano come sono adesso."
      confirm-label="Elimina"
      @confirm="confirmRemove"
    />

    <NamePrompt
      v-model:open="promptOpen"
      :title="renaming ? 'Rinomina preset' : 'Salva questa configurazione'"
      :description="renaming ? undefined : 'Salvo gli schermi così come sono adesso. Dagli un nome che riconosci al volo.'"
      :initial="renaming?.name"
      :confirm-label="renaming ? 'Rinomina' : 'Salva'"
      @submit="submitName"
    />

    <ToastRoot v-model:open="toastOpen" class="toast">
      <ToastDescription>{{ error }}</ToastDescription>
    </ToastRoot>
    <ToastViewport class="toast-viewport" />
  </ToastProvider>
</template>

<style>
.toast-viewport {
  position: fixed;
  inset: auto var(--gutter) calc(var(--nav-height) + var(--size-3));
  z-index: 60;
  margin: 0;
  padding: 0;
  list-style: none;
  outline: none;
}
.toast {
  padding: var(--size-3);
  border-radius: var(--r-card);
  background: var(--danger);
  color: #fff;
  font-size: 0.8125rem;
  font-weight: 650;
  text-align: center;
  box-shadow: 0 8px 24px -8px rgb(0 0 0 / 0.4);
  animation: toast-in 200ms var(--ease-out-3);
}
@keyframes toast-in { from { transform: translateY(12px); opacity: 0; } }
</style>
