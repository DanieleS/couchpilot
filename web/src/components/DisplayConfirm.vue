<script setup lang="ts">
import { computed } from 'vue'
import { t } from '../i18n'
import { useCountdown } from '../store'
import type { Displays } from '../types'

const props = defineProps<{ displays: Displays }>()
const emit = defineEmits<{ keep: []; revert: [] }>()

const previousName = computed(
  () => props.displays.presets.find((p) => p.id === props.displays.pending?.previousPresetId)?.name ?? null,
)
const { seconds: left, fraction } = useCountdown()
</script>

<template>
  <div v-if="displays.pending" class="confirm card" role="alertdialog" aria-live="polite">
    <div class="pie" :style="{ '--p': `${fraction * 100}%` }">
      <b class="tabular">{{ left }}</b>
    </div>
    <div>
      <p class="q">{{ t.displays.confirm.question }}</p>
      <p class="s">
        {{ previousName ? t.displays.confirm.toPreset(previousName) : t.displays.confirm.toPrevious }}
      </p>
      <div class="actions">
        <button class="btn btn-ghost" @click="emit('revert')">{{ t.displays.confirm.revert }}</button>
        <button class="btn btn-wait" @click="emit('keep')">{{ t.displays.confirm.keep }}</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.confirm {
  display: grid;
  grid-template-columns: 4.625rem 1fr;
  gap: var(--size-3);
  align-items: center;
  padding: var(--size-3);
  box-shadow: inset 0 0 0 2px var(--wait);
}
.pie {
  inline-size: 4.625rem;
  block-size: 4.625rem;
  border-radius: var(--radius-round);
  background: conic-gradient(var(--wait) 0 var(--p), var(--line) 0);
  display: grid;
  place-items: center;
}
.pie b {
  inline-size: 3.25rem;
  block-size: 3.25rem;
  border-radius: var(--radius-round);
  background: var(--card);
  display: grid;
  place-items: center;
  font-size: 1.5rem;
  font-weight: 800;
  font-stretch: 80%;
}
.q { font-size: 0.875rem; font-weight: 800; }
.s { font-size: 0.71875rem; line-height: 1.35; color: var(--ink-2); margin-top: 3px; }
.actions { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin-top: 10px; }
.actions .btn { padding: 0.625rem 6px; font-size: 0.75rem; }
</style>
