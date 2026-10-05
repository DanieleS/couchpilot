<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAudio, type MixerChannel } from '../audio'
import type { AudioFlow } from '../protocol'
import { useCouchPilot } from '../store'
import AppIcon from './AppIcon.vue'
import AudioAppRow from './AudioAppRow.vue'
import AudioDeviceSheet from './AudioDeviceSheet.vue'
import AudioGameCard from './AudioGameCard.vue'
import LevelMeter from './LevelMeter.vue'
import PcChip from './PcChip.vue'
import VolumeSlider from './VolumeSlider.vue'
import { deviceIcon } from './deviceIcon'

const { status } = useCouchPilot()
const { state, levelOf, set, setDefault } = useAudio()

const channel = (role: MixerChannel['role']) => computed(() => state.value?.channels.find((c) => c.role === role) ?? null)
const output = channel('output')
const input = channel('input')
const outputDevice = computed(() => state.value?.outputs.find((d) => d.isDefault) ?? null)
const games = computed(() => state.value?.channels.filter((c) => c.role === 'game') ?? [])
const apps = computed(() => state.value?.channels.filter((c) => c.role === 'app' || c.role === 'system') ?? [])

const sheetOpen = ref(false)
const sheetFlow = ref<AudioFlow>('output')
const choose = (flow: AudioFlow) => {
  sheetFlow.value = flow
  sheetOpen.value = true
}

// Dragging a muted device's slider unmutes it, as in Windows.
const endpointVolume = (c: () => MixerChannel | null) =>
  computed({
    get: () => c()?.volume ?? 0,
    set: (v) => {
      const d = c()
      if (d) set(d, d.muted ? { volume: v, muted: false } : { volume: v })
    },
  })
const outputVolume = endpointVolume(() => output.value)
const inputVolume = endpointVolume(() => input.value)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Audio</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <div v-if="!state && !status" class="empty">
      <p class="empty-title">Mi collego al PC…</p>
      <p class="muted">Il PC e il telefono devono essere sulla stessa rete.</p>
    </div>
    <div v-else-if="!state" class="skeleton" />

    <template v-else>
      <section class="output">
        <button class="device card" @click="choose('output')">
          <span class="ico"><AppIcon :name="outputDevice ? deviceIcon(outputDevice.kind) : 'speaker-off'" :size="18" /></span>
          <span class="text">
            <span class="label">Uscita</span>
            <span class="name truncate">{{ outputDevice?.name ?? 'Nessuna uscita attiva' }}</span>
          </span>
          <span class="change">Cambia</span>
        </button>

        <div v-if="output" class="master card">
          <div class="master-top">
            <span class="label-strong">Volume generale</span>
            <span class="big-number tabular" :class="{ off: output.muted }">{{ output.muted ? 'Muto' : output.volume }}</span>
          </div>
          <div class="row">
            <VolumeSlider v-model="outputVolume" big label="Volume generale" :muted="output.muted" />
            <button
              class="icon-btn mute"
              :class="{ on: output.muted }"
              :aria-label="output.muted ? 'Riattiva l\'audio' : 'Silenzia tutto'"
              :aria-pressed="output.muted"
              @click="set(output, { muted: !output.muted })"
            >
              <AppIcon :name="output.muted ? 'speaker-off' : 'speaker'" :size="18" />
            </button>
          </div>
          <LevelMeter :level="levelOf(output)" />
        </div>
      </section>

      <template v-if="output">
        <p class="eyebrow">Mixer</p>
        <AudioGameCard v-for="g in games" :key="g.stableKey" class="game" :channel="g" :level="levelOf(g)" @change="set(g, $event)" />
        <p v-if="apps.length === 0 && games.length === 0" class="muted hint">Nessuna app sta usando l'audio adesso.</p>
        <TransitionGroup v-else tag="ul" name="list" class="apps">
          <AudioAppRow v-for="a in apps" :key="a.stableKey" :app="a" :level="levelOf(a)" @change="set(a, $event)" />
        </TransitionGroup>
      </template>

      <p class="eyebrow">Ingresso</p>
      <div class="card mic">
        <button class="mic-head" @click="choose('input')">
          <span class="ico small"><AppIcon :name="input?.muted ? 'mic-off' : 'mic'" :size="15" /></span>
          <span class="name truncate">{{ input?.name ?? 'Nessun microfono attivo' }}</span>
          <span class="change">Cambia</span>
        </button>
        <div v-if="input" class="row">
          <VolumeSlider v-model="inputVolume" label="Volume del microfono" :muted="input.muted" />
          <span class="pct tabular">{{ input.volume }}</span>
          <button
            class="icon-btn mute"
            :class="{ on: input.muted }"
            :aria-label="input.muted ? 'Riattiva il microfono' : 'Silenzia il microfono'"
            :aria-pressed="input.muted"
            @click="set(input, { muted: !input.muted })"
          >
            <AppIcon :name="input.muted ? 'mic-off' : 'mic'" :size="16" />
          </button>
        </div>
      </div>

      <AudioDeviceSheet
        v-model:open="sheetOpen"
        v-model:flow="sheetFlow"
        :outputs="state.outputs"
        :inputs="state.inputs"
        @select="setDefault"
      />
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

.game { margin-bottom: 6px; }
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
