<script setup lang="ts">
import type { Controller } from '../types'

// The Xbox-style pads connected to the PC and their battery, four bars like Windows' own.
defineProps<{ controllers: Controller[]; onColor?: boolean }>()

const bars = { empty: 0, low: 1, medium: 2, full: 3 } as const
const label = (c: Controller) =>
  c.wired ? `Controller ${c.slot}, via cavo` : c.battery ? `Controller ${c.slot}, batteria ${({ empty: 'scarica', low: 'bassa', medium: 'a metà', full: 'carica' })[c.battery]}` : `Controller ${c.slot}`
</script>

<template>
  <ul v-if="controllers.length" class="pads" :class="{ 'on-color': onColor }">
    <li v-for="c in controllers" :key="c.slot" class="pad" :class="{ low: c.battery === 'low' || c.battery === 'empty' }" :aria-label="label(c)">
      <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M6 12h4M8 10v4M15 13h.01M18 11h.01" /><rect x="2" y="6" width="20" height="12" rx="6" />
      </svg>
      <span class="slot tabular">{{ c.slot }}</span>
      <span v-if="c.wired" class="wired">cavo</span>
      <span v-else-if="c.battery" class="battery" aria-hidden="true">
        <i v-for="n in 3" :key="n" :class="{ lit: n <= bars[c.battery] }" />
      </span>
    </li>
  </ul>
</template>

<style scoped>
.pads { display: flex; flex-wrap: wrap; gap: 6px; }
.pad {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 5px 8px;
  border-radius: var(--radius-round);
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.6875rem;
  font-weight: 750;
  color: var(--ink);
}
.on-color .pad { background: rgb(0 0 0 / 0.12); box-shadow: none; color: inherit; }
.slot { opacity: 0.7; }
.wired { font-weight: 650; opacity: 0.7; }
.battery { display: inline-flex; gap: 2px; align-items: flex-end; }
.battery i { inline-size: 4px; block-size: 9px; border-radius: 1px; background: currentColor; opacity: 0.2; }
.battery i.lit { opacity: 1; }
.pad.low { color: var(--danger); }
.on-color .pad.low { background: var(--danger); color: #fff; }
</style>
