<script setup lang="ts">
import { ToastAction, ToastDescription, ToastProvider, ToastRoot, ToastTitle, ToastViewport } from 'reka-ui'
import { computed, ref, watch } from 'vue'
import { api, unpaired } from './api'
import type { AudioOutput, Capture, Game, Preset, RecentAchievement, RunningGame } from './types'
import { useCouchPilot } from './store'
import { useAchievements } from './achievements'
import { clipLength, isGif, toolName, useCaptures, type CaptureAction } from './captures'
import AchievementIcon from './components/AchievementIcon.vue'
import AchievementsSheet from './components/AchievementsSheet.vue'
import CaptureHelpSheet from './components/CaptureHelpSheet.vue'
import ClipTrimmer from './components/ClipTrimmer.vue'
import PairingView from './components/PairingView.vue'
import CaptureThumb from './components/CaptureThumb.vue'
import CaptureViewer from './components/CaptureViewer.vue'
import GalleryView from './components/GalleryView.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import AudioView from './components/AudioView.vue'
import ControlView from './components/ControlView.vue'
import DisplaysView from './components/DisplaysView.vue'
import GameSheet from './components/GameSheet.vue'
import GamesView from './components/GamesView.vue'
import NamePrompt from './components/NamePrompt.vue'
import StatsView from './components/StatsView.vue'
import PresetAudioSheet from './components/PresetAudioSheet.vue'
import TabBar, { type Tab } from './components/TabBar.vue'
import { t } from './i18n'

const { error, games, running, runningIds, act } = useCouchPilot()

// The manifest's shortcuts (long press on the app's icon) open a tab straight away: /?tab=audio.
function initialTab(): Tab {
  const asked = new URLSearchParams(location.search).get('tab')
  if (asked) history.replaceState(null, '', '/')
  return asked === 'displays' || asked === 'audio' || asked === 'control' ? asked : 'games'
}

const tab = ref<Tab>(initialTab())
// Statistics and the gallery live behind icons in Giochi: pages of that tab, not tabs of their own.
const statsOpen = ref(false)
const galleryOpen = ref(false)
// The gallery opened from a game's sheet shows that game.
const galleryGame = ref<string | null>(null)
watch(tab, () => {
  statsOpen.value = false
  galleryOpen.value = false
  window.scrollTo({ top: 0 })
})
watch([statsOpen, galleryOpen], () => window.scrollTo({ top: 0 }))
const openGallery = (gameId: string | null) => {
  galleryGame.value = gameId
  statsOpen.value = false
  galleryOpen.value = true
  tab.value = 'games'
}

// Game sheet. It follows the library while open, so an install finishing shows up in it.
const sheetGameId = ref<string | null>(null)
const sheetSnapshot = ref<Game | null>(null)
const sheetGame = computed(() => games.value?.find((g) => g.id === sheetGameId.value) ?? sheetSnapshot.value)
const sheetOpen = ref(false)
const openGame = (g: Game) => {
  sheetGameId.value = g.id
  sheetSnapshot.value = g
  sheetOpen.value = true
}
const installSheetGame = () => sheetGame.value && act(() => api.install(sheetGame.value!.id))

// Uninstalling asks first: getting the game back means downloading it again.
const uninstalling = ref<Game | null>(null)
const uninstallOpen = computed({
  get: () => uninstalling.value !== null,
  set: (v) => !v && (uninstalling.value = null),
})
const confirmUninstall = () => {
  const g = uninstalling.value
  if (g) act(() => api.uninstall(g.id))
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

// A preset's audio output, optional.
const audioPreset = ref<Preset | null>(null)
const audioSheetOpen = ref(false)
const askPresetAudio = (p: Preset) => {
  audioPreset.value = p
  audioSheetOpen.value = true
}
const setPresetAudio = (output: AudioOutput | null) => audioPreset.value && act(() => api.setPresetAudio(audioPreset.value!.id, output))

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

// Screenshots and clips: the viewer works on a copy of the list it was opened with, so a capture
// arriving meanwhile doesn't shift what you're looking at.
const captures = useCaptures()
const viewerItems = ref<Capture[]>([])
const viewerIndex = ref(0)
const viewerOpen = ref(false)
const viewCapture = (items: Capture[], index: number) => {
  if (index < 0 || !items.length) return
  viewerItems.value = [...items]
  viewerIndex.value = index
  viewerOpen.value = true
}
const openGalleryFromSheet = () => {
  sheetOpen.value = false
  openGallery(sheetGame.value?.id ?? null)
}
const viewCaptureFromSheet = (items: Capture[], index: number) => {
  sheetOpen.value = false
  viewCapture(items, index)
}

// Cutting a clip: the trimmer opens over the viewer; what it makes joins the viewer's list, right
// after the clip it came from, and is shown.
const trimming = ref<Capture | null>(null)
const trimOpen = computed({
  get: () => trimming.value !== null,
  set: (v) => !v && (trimming.value = null),
})
function onTrimmed(made: Capture) {
  const at = viewerItems.value.findIndex((x) => x.id === made.id)
  if (at >= 0) {
    viewerIndex.value = at
  } else {
    const after = Math.min(viewerIndex.value + 1, viewerItems.value.length)
    viewerItems.value.splice(after, 0, made)
    viewerIndex.value = after
  }
  captures.announce(made)
}

// Achievements: a game's full list, from its sheet or from the toast of a fresh unlock.
const achievements = useAchievements()
const achievementsGame = ref<Game | null>(null)
const achievementsOpen = ref(false)
const openAchievements = (g: Game | null) => {
  if (!g) return
  achievementsGame.value = g
  sheetOpen.value = false
  achievementsOpen.value = true
}
const freshAchievementsOpen = computed({
  get: () => achievements.fresh.value !== null,
  set: (v) => !v && (achievements.fresh.value = null),
})
const freshAchievement = computed<RecentAchievement | null>(() => achievements.fresh.value?.[0] ?? null)
const freshAchievementsMore = computed(() => Math.max(0, (achievements.fresh.value?.length ?? 0) - 1))
const openFreshAchievements = () => {
  const a = freshAchievement.value
  achievements.fresh.value = null
  if (!a) return
  const g = games.value?.find((x) => x.id === a.gameId)
  if (g) openAchievements(g)
}

// Deleting asks first, though it only goes as far as the PC's Recycle Bin.
const removingCapture = ref<Capture | null>(null)
const removeCaptureOpen = computed({
  get: () => removingCapture.value !== null,
  set: (v) => !v && (removingCapture.value = null),
})
async function confirmRemoveCapture() {
  const c = removingCapture.value
  if (!c || !(await captures.remove(c))) return
  const i = viewerItems.value.findIndex((x) => x.id === c.id)
  if (i >= 0) viewerItems.value.splice(i, 1)
  if (!viewerItems.value.length) viewerOpen.value = false
  else viewerIndex.value = Math.min(viewerIndex.value, viewerItems.value.length - 1)
}

const helpOpen = computed({
  get: () => captures.help.value !== null,
  set: (v) => !v && (captures.help.value = null),
})
const retryCapture = (action: CaptureAction) =>
  action === 'screenshot' ? captures.screenshot() : action === 'replay' ? captures.saveReplay() : captures.toggleRecording()

// A capture just landed, made from the phone or with the PC's own keys.
const freshOpen = computed({
  get: () => captures.fresh.value !== null,
  set: (v) => !v && (captures.fresh.value = null),
})
const freshTitle = computed(() => {
  const c = captures.fresh.value
  if (!c) return ''
  return c.edited ? (isGif(c) ? t.app.fresh.gif : t.app.fresh.trimmed) : c.kind === 'photo' ? t.app.fresh.screenshot : c.replay ? t.app.fresh.replay : t.app.fresh.clip
})
const freshDetail = computed(() => {
  const c = captures.fresh.value
  if (!c) return ''
  const size = c.kind === 'photo' ? c.width && c.height && `${c.width}×${c.height}` : clipLength(c.seconds)
  return [c.game, size, c.edited ? t.app.fresh.fromClip : toolName(c.tool)].filter(Boolean).join(' · ')
})
const openFresh = () => {
  const c = captures.fresh.value
  if (!c) return
  const list = captures.items.value
  const i = list.findIndex((x) => x.id === c.id)
  if (i >= 0) viewCapture(list, i)
  else viewCapture([c], 0)
  captures.fresh.value = null
}

const toastOpen = computed({
  get: () => error.value !== null,
  set: (v) => !v && (error.value = null),
})
</script>

<template>
  <PairingView v-if="unpaired" />
  <ToastProvider v-else :duration="4000" swipe-direction="down">
    <main>
      <StatsView v-if="tab === 'games' && statsOpen" @back="statsOpen = false" @open="openGame" />
      <GalleryView v-else-if="tab === 'games' && galleryOpen" :game="galleryGame" @back="galleryOpen = false" @view="viewCapture" />
      <GamesView
        v-else-if="tab === 'games'"
        @open="openGame"
        @focus="(g) => focusGame(g.id)"
        @close="askClose"
        @apply-preset="applyPreset"
        @save-preset="askSave"
        @keep-displays="keepDisplays"
        @revert-displays="revertDisplays"
        @manage-displays="tab = 'displays'"
        @stats="statsOpen = true"
        @gallery="openGallery(null)"
        @view-capture="viewCapture"
      />
      <DisplaysView
        v-else-if="tab === 'displays'"
        @apply="applyPreset"
        @save="askSave"
        @rename="askRename"
        @remove="(p) => (removing = p)"
        @audio="askPresetAudio"
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
      @install="installSheetGame"
      @uninstall="uninstalling = sheetGame"
      @gallery="openGalleryFromSheet"
      @view-capture="viewCaptureFromSheet"
      @achievements="openAchievements(sheetGame)"
    />

    <AchievementsSheet v-model:open="achievementsOpen" :game="achievementsGame" />

    <CaptureViewer v-model:open="viewerOpen" v-model:index="viewerIndex" :items="viewerItems" @remove="(c) => (removingCapture = c)" @trim="(c) => (trimming = c)" />

    <ClipTrimmer v-model:open="trimOpen" :capture="trimming" @made="onTrimmed" />

    <ConfirmDialog
      v-model:open="removeCaptureOpen"
      icon="trash"
      :title="removingCapture?.kind === 'video' ? t.app.removeCapture.clip : t.app.removeCapture.screenshot"
      :description="t.app.removeCapture.description"
      :confirm-label="t.app.removeCapture.confirm"
      @confirm="confirmRemoveCapture"
    />

    <CaptureHelpSheet v-model:open="helpOpen" :help="captures.help.value" :tool="captures.capture.value?.tool ?? null" @retry="retryCapture" />

    <ConfirmDialog
      v-model:open="uninstallOpen"
      icon="trash"
      :title="t.app.uninstall.title(uninstalling?.name ?? '')"
      :description="t.app.uninstall.description"
      :confirm-label="t.app.uninstall.confirm"
      @confirm="confirmUninstall"
    />

    <ConfirmDialog
      v-model:open="closeOpen"
      icon="power"
      :title="t.app.close.title(closing?.name ?? '')"
      :description="t.app.close.description"
      :confirm-label="t.app.close.confirm"
      @confirm="confirmClose"
    />

    <ConfirmDialog
      v-model:open="removeOpen"
      icon="trash"
      :title="t.app.removePreset.title(removing?.name ?? '')"
      :description="t.app.removePreset.description"
      :confirm-label="t.app.removePreset.confirm"
      @confirm="confirmRemove"
    />

    <PresetAudioSheet v-model:open="audioSheetOpen" :preset="audioPreset" @select="setPresetAudio" />

    <NamePrompt
      v-model:open="promptOpen"
      :title="renaming ? t.app.preset.rename : t.app.preset.save"
      :description="renaming ? undefined : t.app.preset.saveDescription"
      :initial="renaming?.name"
      :confirm-label="renaming ? t.app.preset.renameConfirm : t.app.preset.saveConfirm"
      @submit="submitName"
    />

    <ToastRoot v-model:open="toastOpen" class="toast">
      <ToastDescription>{{ error }}</ToastDescription>
    </ToastRoot>
    <ToastRoot v-model:open="freshOpen" :duration="6000" class="toast fresh">
      <CaptureThumb v-if="captures.fresh.value" :capture="captures.fresh.value" class="fresh-thumb" />
      <div class="fresh-text">
        <ToastTitle class="fresh-title">{{ freshTitle }}</ToastTitle>
        <ToastDescription class="fresh-detail truncate">{{ freshDetail }}</ToastDescription>
      </div>
      <ToastAction :alt-text="t.app.fresh.openAlt" as-child>
        <button class="fresh-open" @click="openFresh">{{ t.app.fresh.open }}</button>
      </ToastAction>
    </ToastRoot>
    <ToastRoot v-model:open="freshAchievementsOpen" :duration="8000" class="toast fresh">
      <AchievementIcon v-if="freshAchievement" :achievement="freshAchievement" :size="44" class="fresh-ach" />
      <div class="fresh-text">
        <ToastTitle class="fresh-title">{{ t.app.fresh.achievement }}<template v-if="freshAchievementsMore"> · +{{ freshAchievementsMore }}</template></ToastTitle>
        <ToastDescription class="fresh-detail truncate">{{ freshAchievement?.name }} · {{ freshAchievement?.game }}</ToastDescription>
      </div>
      <ToastAction :alt-text="t.app.fresh.achievementsAlt" as-child>
        <button class="fresh-open" @click="openFreshAchievements">{{ t.app.fresh.see }}</button>
      </ToastAction>
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
.toast.fresh {
  display: flex;
  align-items: center;
  gap: var(--size-3);
  padding: 10px;
  background: var(--button-dark);
  color: var(--button-dark-ink);
  text-align: left;
}
.fresh-thumb { inline-size: 4.5rem; flex-shrink: 0; border-radius: 8px; }
.fresh-ach { box-shadow: 0 0 0 2px var(--run); }
.fresh-text { flex: 1; min-width: 0; }
.fresh-title { font-size: 0.875rem; font-weight: 750; }
.fresh-detail { font-size: 0.75rem; font-weight: 500; opacity: 0.65; }
.fresh-open { padding: 10px 12px; border-radius: 10px; background: rgb(255 255 255 / 0.14); font-size: 0.8125rem; font-weight: 700; }
@keyframes toast-in { from { transform: translateY(12px); opacity: 0; } }
</style>
