<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAudio } from '../audio'
import { useCouchPilot } from '../store'
import type { AudioEndpoint } from '../types'
import AppIcon from './AppIcon.vue'
import AudioAppRow from './AudioAppRow.vue'
import AudioDeviceSheet from './AudioDeviceSheet.vue'
import LevelMeter from './LevelMeter.vue'
import PcChip from './PcChip.vue'
import VolumeSlider from './VolumeSlider.vue'
import { deviceIcon } from './deviceIcon'

const { status } = useCouchPilot()
const { state, failed, set, setDefault } = useAudio()

const sheetOpen = ref(false)
const sheetFlow = ref<'output' | 'input'>('output')
const choose = (flow: 'output' | 'input') => {
  sheetFlow.value = flow
  sheetOpen.value = true
}

// Dragging a muted device's slider unmutes it, as in Windows.
const endpointVolume = (e: () => AudioEndpoint | null | undefined) =>
  computed({
    get: () => e()?.volume ?? 0,
    set: (v) => {
      const d = e()
      if (d) set({ kind: 'device', id: d.id }, d.muted ? { volume: v, muted: false } : { volume: v })
    },
  })
const outputVolume = endpointVolume(() => state.value?.output)
const inputVolume = endpointVolume(() => state.value?.input)
const toggleMute = (d: AudioEndpoint) => set({ kind: 'device', id: d.id }, { muted: !d.muted })
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Audio</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <div v-if="!state && failed" class="empty">
      <p class="empty-title">Non riesco a leggere l'audio del PC</p>
      <p class="muted">Il PC e il telefono devono essere sulla stessa rete.</p>
    </div>
    <div v-else-if="!state" class="skeleton" />

    <template v-else>
      <section class="output">
        <button class="device card" @click="choose('output')">
          <span class="ico"><AppIcon :name="state.output ? deviceIcon(state.output.kind) : 'speaker-off'" :size="18" /></span>
          <span class="text">
            <span class="label">Uscita</span>
            <span class="name truncate">{{ state.output?.name ?? 'Nessuna uscita attiva' }}</span>
          </span>
          <span class="change">Cambia</span>
        </button>

        <div v-if="state.output" class="master card">
          <div class="master-top">
            <span class="label-strong">Volume generale</span>
            <span class="big-number tabular" :class="{ off: state.output.muted }">{{ state.output.muted ? 'Muto' : state.output.volume }}</span>
          </div>
          <div class="row">
            <VolumeSlider v-model="outputVolume" big label="Volume generale" :muted="state.output.muted" />
            <button
              class="icon-btn mute"
              :class="{ on: state.output.muted }"
              :aria-label="state.output.muted ? 'Riattiva l\'audio' : 'Silenzia tutto'"
              :aria-pressed="state.output.muted"
              @click="toggleMute(state.output)"
            >
              <AppIcon :name="state.output.muted ? 'speaker-off' : 'speaker'" :size="18" />
            </button>
          </div>
          <LevelMeter :peak="state.output.muted ? 0 : state.output.peak" />
        </div>
      </section>

      <template v-if="state.output">
        <p class="eyebrow">Mixer</p>
        <p v-if="state.apps.length === 0" class="muted hint">Nessuna app sta usando l'audio adesso.</p>
        <TransitionGroup v-else tag="ul" name="list" class="apps">
          <AudioAppRow v-for="a in state.apps" :key="a.id" :app="a" @change="set({ kind: 'app', id: a.id }, $event)" />
        </TransitionGroup>
      </template>

      <p class="eyebrow">Ingresso</p>
      <div class="card mic">
        <button class="mic-head" @click="choose('input')">
          <span class="ico small"><AppIcon :name="state.input?.muted ? 'mic-off' : 'mic'" :size="15" /></span>
          <span class="name truncate">{{ state.input?.name ?? 'Nessun microfono attivo' }}</span>
          <span class="change">Cambia</span>
        </button>
        <div v-if="state.input" class="row">
          <VolumeSlider v-model="inputVolume" label="Volume del microfono" :muted="state.input.muted" />
          <span class="pct tabular">{{ state.input.volume }}</span>
          <button
            class="icon-btn mute"
            :class="{ on: state.input.muted }"
            :aria-label="state.input.muted ? 'Riattiva il microfono' : 'Silenzia il microfono'"
            :aria-pressed="state.input.muted"
            @click="toggleMute(state.input)"
          >
            <AppIcon :name="state.input.muted ? 'mic-off' : 'mic'" :size="16" />
          </button>
        </div>
      </div>

      <AudioDeviceSheet v-model:open="sheetOpen" v-model:flow="sheetFlow" :state="state" @select="setDefault" />
    </template>
  </div>
</template>

<style scoped>
.output { display: grid; gap: 6px; margin-top: var(--size-4); }
.device { display: flex; align-items: center; gap: var(--size-3); padding: 10px; text-align: left; }
.device:active { transform: scale(0.99); }
.ico { flex-shrink: 0; display: grid; place-items: center; inline-size: 2.5rem; block-size: 2.5rem; border-radius: var(--r-inner); background: var(--well); }
.ico.small { inline-size: 2rem; block-size: 2rem; }
.text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.label { font-size: 0.5625rem; font-weight: 800; letter-spacing: 0.1em; text-transform: uppercase; color: var(--ink-2); }
.name { font-size: 0.875rem; font-weight: 750; }
.change { flex-shrink: 0; padding: 6px 4px; font-size: 0.75rem; font-weight: 750; color: var(--wait); }

.master { display: grid; gap: 10px; padding: var(--size-3); }
.master-top { display: flex; align-items: baseline; justify-content: space-between; }
.label-strong { font-size: 0.8125rem; font-weight: 750; }
.big-number { font-size: 1.625rem; font-weight: 800; letter-spacing: -0.03em; line-height: 1; }
.big-number.off { color: var(--danger); font-size: 1.125rem; }
.row { display: flex; align-items: center; gap: var(--size-2); }
.mute { color: var(--ink-2); }
.mute.on { background: var(--danger); color: #fff; }
.pct { inline-size: 1.75rem; text-align: right; font-size: 0.75rem; font-weight: 750; }

.apps { display: grid; gap: 6px; }
.list-move, .list-enter-active, .list-leave-active { transition: transform 200ms var(--ease-out-3), opacity 200ms; }
.list-enter-from, .list-leave-to { opacity: 0; }
.hint { font-size: 0.8125rem; }

.mic { display: grid; gap: var(--size-2); padding: 10px 6px 10px 10px; }
.mic-head { display: flex; align-items: center; gap: var(--size-2); min-width: 0; text-align: left; }
.mic-head .name { flex: 1; min-width: 0; font-size: 0.8125rem; }

.empty { padding: var(--size-9) 0; text-align: center; font-size: 0.8125rem; }
.empty-title { font-weight: 750; font-size: 0.9375rem; margin-bottom: 4px; }
.skeleton { margin-top: var(--size-4); block-size: 9rem; border-radius: var(--r-card); background: var(--card); animation: var(--animation-blink); }
</style>
