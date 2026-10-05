<script setup lang="ts">
import { computed } from 'vue'
import { useRemoteInput } from '../input'
import { useCouchPilot } from '../store'
import KeyboardPanel from './KeyboardPanel.vue'
import PcChip from './PcChip.vue'
import TouchPad from './TouchPad.vue'

const { status } = useCouchPilot()
const input = useRemoteInput()
const turnedOff = computed(() => status.value?.input === false)
const disabled = computed(() => input.link.value !== 'open')
</script>

<template>
  <div class="page">
    <header class="page-header">
      <h1>Controllo</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <TouchPad class="pad" :input="input" :disabled="disabled">
      <template #disabled>
        <div class="off">
          <template v-if="turnedOff">
            <p class="title">Touchpad e tastiera sono spenti</p>
            <p class="muted">
              Accendili in Playnite: Componenti aggiuntivi → Impostazioni estensioni → CouchPilot →
              «Permetti al telefono di usare mouse e tastiera del PC».
            </p>
          </template>
          <template v-else>
            <p class="title">Mi collego al PC…</p>
            <p class="muted">Il PC e il telefono devono essere sulla stessa rete.</p>
          </template>
        </div>
      </template>
    </TouchPad>

    <p class="eyebrow">Tastiera</p>
    <KeyboardPanel :input="input" :disabled="disabled" />
  </div>
</template>

<style scoped>
.pad { margin-top: var(--size-4); }
.off { max-inline-size: 18rem; padding: var(--size-4); text-align: center; }
.title { font-weight: 800; }
.muted { font-size: 0.75rem; line-height: 1.45; margin-top: 4px; }
</style>
