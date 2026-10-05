<script setup lang="ts">
// A fat slider whose fill is the value: easy to hit with a thumb, and readable at a glance.
defineProps<{ label: string; muted?: boolean; tone?: 'ink' | 'run'; big?: boolean; disabled?: boolean }>()
const value = defineModel<number>({ required: true })
</script>

<template>
  <input
    v-model.number="value"
    class="slider"
    :class="[tone ?? 'ink', { muted, big }]"
    type="range"
    min="0"
    max="100"
    step="1"
    :aria-label="label"
    :disabled="disabled"
    :style="{ '--p': `${value}%` }"
  />
</template>

<style scoped>
.slider {
  --fill: var(--ink);
  -webkit-appearance: none;
  appearance: none;
  flex: 1;
  min-width: 0;
  block-size: 2rem;
  margin: 0;
  border-radius: var(--r-inner);
  background: linear-gradient(to right, var(--fill) var(--p), var(--slider-track) var(--p));
  cursor: pointer;
  /* A vertical swipe that starts on the slider still scrolls the page. */
  touch-action: pan-y;
  outline-offset: 2px;
}
.slider.big { block-size: 3rem; border-radius: var(--r-field); }
.slider.run { --fill: var(--run); }
.slider.muted { --fill: var(--line); }
.slider:disabled { opacity: 0.45; cursor: default; }
.slider::-webkit-slider-thumb { -webkit-appearance: none; inline-size: 4px; block-size: 45%; border-radius: 2px; background: var(--card); opacity: 0.85; }
.slider::-moz-range-thumb { inline-size: 4px; block-size: 45%; border: 0; border-radius: 2px; background: var(--card); opacity: 0.85; }
</style>
