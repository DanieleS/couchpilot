<script setup lang="ts">
import { t } from '../i18n'
import type { Preset } from '../types'
import { useCouchPilot } from '../store'
import AppIcon from './AppIcon.vue'
import DisplayConfirm from './DisplayConfirm.vue'
import MonitorLayout from './MonitorLayout.vue'
import PcChip from './PcChip.vue'
import StreamNotice from './StreamNotice.vue'

const emit = defineEmits<{
  apply: [id: string]
  save: []
  rename: [preset: Preset]
  remove: [preset: Preset]
  audio: [preset: Preset]
  keep: []
  revert: []
}>()

const { status, displays } = useCouchPilot()
const state = (p: Preset) =>
  displays.value?.pending?.presetId === p.id ? 'pending' : displays.value?.activePresetId === p.id ? 'active' : 'idle'
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>{{ t.displays.title }}</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <template v-if="displays">
      <StreamNotice v-if="displays.streamActive" class="stream" />
      <section class="current card">
        <p class="eyebrow first">{{ t.displays.current }}</p>
        <div class="well">
          <MonitorLayout :monitors="displays.monitors" labels />
        </div>
        <ul class="monitors">
          <li v-for="m in displays.monitors" :key="m.devicePath + m.x" :class="{ primary: m.primary && !m.virtual, virtual: m.virtual }">
            <span class="truncate">{{ m.name }}<template v-if="m.virtual"> · {{ t.displays.virtual }}</template><template v-else-if="m.primary"> · {{ t.displays.primary }}</template></span>
            <span class="muted tabular">{{ m.width }}×{{ m.height }} · {{ m.refreshHz }} Hz</span>
          </li>
        </ul>
      </section>

      <DisplayConfirm
        v-if="displays.pending"
        class="confirm"
        :displays="displays"
        @keep="emit('keep')"
        @revert="emit('revert')"
      />

      <p class="eyebrow">{{ t.displays.presets }}</p>
      <p v-if="displays.presets.length === 0" class="muted hint">
        {{ t.displays.empty }}
      </p>
      <ul class="presets">
        <li v-for="p in displays.presets" :key="p.id" class="preset card" :class="state(p)">
          <button class="main" :disabled="state(p) !== 'idle' || displays.streamActive" @click="emit('apply', p.id)">
            <span class="thumb"><MonitorLayout :monitors="p.monitors" /></span>
            <span class="text">
              <span class="name truncate">{{ p.name }}</span>
              <span class="sub truncate">{{ p.monitors.map((m) => m.name).join(' + ') }}</span>
              <span v-if="p.audioOutputId" class="sub audio truncate"><AppIcon name="speaker" :size="11" />{{ p.audioOutputName }}</span>
            </span>
            <span class="status">
              <template v-if="state(p) === 'active'"><AppIcon name="check" :size="16" /></template>
              <template v-else-if="state(p) === 'pending'">{{ t.displays.testing }}</template>
              <template v-else-if="!displays.streamActive">{{ t.displays.apply }}</template>
            </span>
          </button>
          <button class="icon-btn tool" :class="{ set: p.audioOutputId }" :aria-label="t.displays.presetAudio" @click="emit('audio', p)"><AppIcon name="speaker" :size="15" /></button>
          <button class="icon-btn tool" :aria-label="t.displays.rename" @click="emit('rename', p)"><AppIcon name="pencil" :size="15" /></button>
          <button class="icon-btn tool" :aria-label="t.displays.remove" @click="emit('remove', p)"><AppIcon name="trash" :size="15" /></button>
        </li>
      </ul>
      <button class="save" :disabled="displays.streamActive" @click="emit('save')"><AppIcon name="plus" :size="16" />{{ t.displays.saveCurrent }}</button>
    </template>
  </div>
</template>

<style scoped>
.stream { margin-top: var(--size-4); }
.current { margin-top: var(--size-4); padding: var(--size-3); }
.eyebrow.first { margin-top: 0; }
.well { display: grid; place-items: center; padding: var(--size-4) var(--size-2); border-radius: var(--r-inner); background: var(--well); }
.well svg { inline-size: 100%; block-size: 6.5rem; }
.monitors { margin-top: var(--size-3); display: grid; gap: 4px; }
.monitors li { display: flex; justify-content: space-between; gap: var(--size-2); font-size: 0.71875rem; font-weight: 650; }
.monitors li.primary > span:first-child { color: var(--accent); }
.monitors li.virtual > span:first-child { color: var(--wait); }
.confirm { margin-top: var(--size-3); }
.hint { font-size: 0.8125rem; line-height: 1.45; margin-bottom: var(--size-3); }

.presets { display: grid; grid-template-columns: minmax(0, 1fr); gap: var(--size-2); }
.preset { min-width: 0; display: flex; align-items: center; gap: 2px; padding: 6px 6px 6px 0; }
.preset.active { box-shadow: inset 0 0 0 2px var(--accent); }
.preset.pending { box-shadow: inset 0 0 0 2px var(--wait); }
.main { flex: 1; min-width: 0; display: flex; align-items: center; gap: var(--size-3); padding: 6px var(--size-2) 6px var(--size-3); text-align: left; }
.thumb { flex-shrink: 0; display: grid; place-items: center; inline-size: 3rem; block-size: 2rem; padding: 4px; border-radius: 8px; background: var(--well); }
.thumb svg { inline-size: 100%; block-size: 100%; }
.text { min-width: 0; display: flex; flex-direction: column; }
.name { font-size: 0.875rem; font-weight: 750; }
.sub { font-size: 0.6875rem; color: var(--ink-2); margin-top: 2px; }
.status { margin-left: auto; font-size: 0.75rem; font-weight: 750; color: var(--accent); white-space: nowrap; }
.pending .status { color: var(--wait); }
.tool { color: var(--ink-2); }
.tool.set { color: var(--wait); }
.sub.audio { display: flex; align-items: center; gap: 4px; color: var(--wait); }

.save {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--size-2);
  inline-size: 100%;
  margin-top: var(--size-3);
  padding: 0.875rem;
  border-radius: var(--r-card);
  box-shadow: inset 0 0 0 1.5px var(--line);
  font-size: 0.8125rem;
  font-weight: 700;
  color: var(--ink-2);
}
.save:disabled { opacity: 0.45; }
</style>
