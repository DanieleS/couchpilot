<script setup lang="ts">
import { computed } from 'vue'
import type { AudioDevice, AudioFlow } from '../protocol'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'
import { deviceIcon } from './deviceIcon'

const props = defineProps<{ outputs: AudioDevice[]; inputs: AudioDevice[] }>()
const emit = defineEmits<{ select: [flow: AudioFlow, index: number] }>()
const open = defineModel<boolean>('open', { required: true })
const flow = defineModel<AudioFlow>('flow', { required: true })

const devices = computed(() => (flow.value === 'output' ? props.outputs : props.inputs))
const pick = (d: AudioDevice) => {
  if (!d.isDefault) emit('select', flow.value, d.index)
  open.value = false
}
</script>

<template>
  <BottomSheet v-model:open="open" :title="flow === 'output' ? 'Uscita audio' : 'Ingresso audio'">
    <h2 class="title">{{ flow === 'output' ? "Dove esce l'audio" : 'Da dove entra la voce' }}</h2>
    <div class="seg" role="tablist">
      <button role="tab" :aria-selected="flow === 'output'" :class="{ on: flow === 'output' }" @click="flow = 'output'">Uscita</button>
      <button role="tab" :aria-selected="flow === 'input'" :class="{ on: flow === 'input' }" @click="flow = 'input'">Ingresso</button>
    </div>
    <ul class="list">
      <li v-for="d in devices" :key="d.index">
        <button class="device" :class="{ on: d.isDefault }" @click="pick(d)">
          <span class="ico"><AppIcon :name="deviceIcon(d.kind)" :size="18" /></span>
          <span class="text">
            <span class="name truncate">{{ d.name }}</span>
            <span v-if="d.detail" class="detail truncate">{{ d.detail }}</span>
          </span>
          <AppIcon v-if="d.isDefault" name="check" :size="18" class="check" />
        </button>
      </li>
    </ul>
    <p v-if="devices.length === 0" class="muted empty">
      {{ flow === 'output' ? 'Nessuna uscita audio attiva sul PC.' : 'Nessun microfono attivo sul PC.' }}
    </p>
    <p class="muted note">
      Diventa il dispositivo predefinito di Windows: le app che seguono il predefinito ci passano da sole.
    </p>
  </BottomSheet>
</template>

<style scoped>
.title { font-size: 1.125rem; font-weight: 800; letter-spacing: -0.02em; }
.seg {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 3px;
  margin-top: var(--size-3);
  padding: 3px;
  border-radius: var(--r-button);
  background: var(--well);
}
.seg button { padding: 0.5rem 0; border-radius: 9px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.seg button.on { background: var(--card); color: var(--ink); box-shadow: var(--card-edge); }
.list { display: grid; gap: 6px; margin-top: var(--size-3); }
.device {
  display: flex;
  align-items: center;
  gap: var(--size-3);
  inline-size: 100%;
  padding: 10px;
  border-radius: var(--r-card);
  background: var(--well);
  text-align: left;
  transition: transform 100ms var(--ease-out-2);
}
.device:active { transform: scale(0.98); }
.device.on { box-shadow: inset 0 0 0 2px var(--wait); }
.ico { flex-shrink: 0; display: grid; place-items: center; inline-size: 2.25rem; block-size: 2.25rem; border-radius: var(--r-inner); background: var(--card); }
.text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.name { font-size: 0.875rem; font-weight: 750; }
.detail { font-size: 0.6875rem; color: var(--ink-2); margin-top: 1px; }
.check { color: var(--wait); flex-shrink: 0; }
.empty { margin-top: var(--size-3); font-size: 0.8125rem; }
.note { margin: var(--size-3) 0 var(--size-2); font-size: 0.6875rem; line-height: 1.45; }
</style>
