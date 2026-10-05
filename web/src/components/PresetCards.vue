<script setup lang="ts">
import { useCountdown } from '../store'
import type { Displays } from '../types'
import AppIcon from './AppIcon.vue'
import MonitorLayout from './MonitorLayout.vue'

const props = defineProps<{ displays: Displays; locked?: boolean }>()
const emit = defineEmits<{ apply: [id: string]; add: [] }>()

const { seconds: secondsLeft } = useCountdown()

const isActive = (id: string) => !props.displays.pending && props.displays.activePresetId === id
const isPending = (id: string) => props.displays.pending?.presetId === id

const describe = (id: string) => {
  const p = props.displays.presets.find((x) => x.id === id)!
  if (isPending(id)) return `In prova · ${secondsLeft.value} s`
  const primary = p.monitors.find((m) => m.primary) ?? p.monitors[0]
  if (p.monitors.length > 1) return `${p.monitors.length} schermi`
  if (!primary) return ''
  const res = primary.height >= 2160 ? '4K' : `${primary.height}p`
  return `${res} · ${primary.refreshHz} Hz`
}
</script>

<template>
  <div class="scroll-row">
    <button
      v-for="p in displays.presets"
      :key="p.id"
      class="preset card"
      :class="{ active: isActive(p.id), pending: isPending(p.id) }"
      :disabled="locked || isActive(p.id) || isPending(p.id)"
      :aria-pressed="isActive(p.id)"
      @click="emit('apply', p.id)"
    >
      <span class="art">
        <MonitorLayout :monitors="p.monitors" :selected="isActive(p.id)" />
      </span>
      <span class="name truncate">{{ p.name }}</span>
      <span class="sub truncate tabular">{{ describe(p.id) }}</span>
    </button>
    <button class="add" aria-label="Salva la configurazione attuale" :disabled="locked" @click="emit('add')">
      <AppIcon name="plus" :size="20" />
    </button>
  </div>
</template>

<style scoped>
.preset {
  flex-shrink: 0;
  inline-size: 7rem;
  padding: 0.625rem;
  display: flex;
  flex-direction: column;
  text-align: left;
  transition: transform 100ms var(--ease-out-2);
}
.preset:active:not(:disabled) { transform: scale(0.97); }
.preset:disabled { cursor: default; }
.preset:disabled:not(.active, .pending), .add:disabled { opacity: 0.45; }
.art {
  display: grid;
  place-items: center;
  block-size: 1.875rem;
  padding: 4px;
  margin-bottom: 0.625rem;
  border-radius: var(--r-inner);
  background: var(--well);
}
.art svg { inline-size: 4rem; block-size: 100%; }
.name { font-size: 0.8125rem; font-weight: 700; line-height: 1.15; }
.sub { font-size: 0.65625rem; color: var(--ink-2); margin-top: 3px; }

.active {
  background: var(--selected);
  color: var(--selected-ink);
  box-shadow: var(--selected-glow);
}
.active .art { background: var(--selected-well); }
.active .sub { color: var(--selected-ink-2); }
.pending { box-shadow: inset 0 0 0 2px var(--wait); }
.pending .sub { color: var(--wait); font-weight: 650; }

.add {
  flex-shrink: 0;
  inline-size: 3.25rem;
  display: grid;
  place-items: center;
  border-radius: var(--r-card);
  box-shadow: inset 0 0 0 1.5px var(--line);
  color: var(--ink-2);
}
</style>
