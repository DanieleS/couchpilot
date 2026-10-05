<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import type { AudioOutput, Preset } from '../types'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'
import { deviceIcon } from './deviceIcon'

// Which audio output a display preset switches to, if any.
const props = defineProps<{ preset: Preset | null }>()
const emit = defineEmits<{ select: [output: AudioOutput | null] }>()
const open = defineModel<boolean>('open', { required: true })

const outputs = ref<AudioOutput[] | null>(null)
const failed = ref(false)
watch(open, (o) => {
  if (!o) return
  outputs.value = null
  failed.value = false
  api.audioOutputs().then((list) => (outputs.value = list)).catch(() => (failed.value = true))
})

// The saved output may be unplugged right now: still show it, so it's clear what's set.
const missing = computed(() => {
  const p = props.preset
  if (!p?.audioOutputId || !outputs.value || outputs.value.some((o) => o.id === p.audioOutputId)) return null
  return p.audioOutputName ?? 'Dispositivo audio'
})
const pick = (output: AudioOutput | null) => {
  if ((output?.id ?? null) !== (props.preset?.audioOutputId ?? null)) emit('select', output)
  open.value = false
}
</script>

<template>
  <BottomSheet v-model:open="open" :title="`Audio con ${preset?.name ?? ''}`">
    <h2 class="title">Audio con «{{ preset?.name }}»</h2>
    <p class="muted lead">Quando applichi il preset, l'audio passa qui. Se il preset viene annullato, torna com'era.</p>
    <ul class="list">
      <li>
        <button class="device" :class="{ on: !preset?.audioOutputId }" @click="pick(null)">
          <span class="ico"><AppIcon name="x" :size="18" /></span>
          <span class="text"><span class="name">Non cambiare l'audio</span></span>
          <AppIcon v-if="!preset?.audioOutputId" name="check" :size="18" class="check" />
        </button>
      </li>
      <li v-if="missing">
        <button class="device on" disabled>
          <span class="ico"><AppIcon name="speaker-off" :size="18" /></span>
          <span class="text"><span class="name truncate">{{ missing }}</span><span class="detail">Non collegato adesso</span></span>
          <AppIcon name="check" :size="18" class="check" />
        </button>
      </li>
      <li v-for="o in outputs ?? []" :key="o.id">
        <button class="device" :class="{ on: o.id === preset?.audioOutputId }" @click="pick(o)">
          <span class="ico"><AppIcon :name="deviceIcon(o.kind)" :size="18" /></span>
          <span class="text">
            <span class="name truncate">{{ o.name }}</span>
            <span v-if="o.detail" class="detail truncate">{{ o.detail }}</span>
          </span>
          <AppIcon v-if="o.id === preset?.audioOutputId" name="check" :size="18" class="check" />
        </button>
      </li>
    </ul>
    <p v-if="failed" class="muted note">Non riesco a leggere le uscite audio del PC.</p>
    <p v-else-if="outputs === null" class="muted note">Leggo le uscite audio…</p>
    <p v-else class="muted note">
      L'uscita di uno schermo (HDMI, DisplayPort) compare solo quando lo schermo è acceso: aspetto qualche secondo che arrivi.
    </p>
  </BottomSheet>
</template>

<style scoped>
.title { font-size: 1.125rem; font-weight: 800; letter-spacing: -0.02em; }
.lead { margin-top: 4px; font-size: 0.8125rem; line-height: 1.45; }
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
}
.device.on { box-shadow: inset 0 0 0 2px var(--wait); }
.ico { flex-shrink: 0; display: grid; place-items: center; inline-size: 2.25rem; block-size: 2.25rem; border-radius: var(--r-inner); background: var(--card); }
.text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.name { font-size: 0.875rem; font-weight: 750; }
.detail { font-size: 0.6875rem; color: var(--ink-2); margin-top: 1px; }
.check { color: var(--wait); flex-shrink: 0; }
.note { margin: var(--size-3) 0 var(--size-2); font-size: 0.6875rem; line-height: 1.45; }
</style>
