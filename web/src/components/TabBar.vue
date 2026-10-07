<script setup lang="ts">
import { t } from '../i18n'

export type Tab = 'games' | 'displays' | 'audio' | 'control'

const tab = defineModel<Tab>({ required: true })
const tabs: { key: Tab; label: string; shape: 'circle' | 'square' | 'half' | 'triangle' }[] = [
  { key: 'games', label: t.app.tabs.games, shape: 'circle' },
  { key: 'displays', label: t.app.tabs.displays, shape: 'square' },
  { key: 'audio', label: t.app.tabs.audio, shape: 'half' },
  { key: 'control', label: t.app.tabs.control, shape: 'triangle' },
]
</script>

<template>
  <nav class="tabbar" :aria-label="t.app.tabs.label">
    <button
      v-for="item in tabs"
      :key="item.key"
      :class="{ on: tab === item.key }"
      :aria-current="tab === item.key ? 'page' : undefined"
      @click="tab = item.key"
    >
      <span class="shape" :class="item.shape" aria-hidden="true" />
      {{ item.label }}
    </button>
  </nav>
</template>

<style scoped>
.tabbar {
  position: fixed;
  inset: auto 0 0;
  z-index: 30;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  padding: var(--size-2) 0 var(--safe-bottom);
  background: var(--nav-bg);
  box-shadow: var(--nav-edge);
  backdrop-filter: blur(12px);
}
button {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 4px 0;
  font-size: 0.65625rem;
  font-weight: 700;
  color: var(--ink-2);
}
button.on { color: var(--ink); }
.shape { inline-size: 16px; block-size: 16px; background: var(--nav-shape); transition: background 150ms, transform 150ms var(--ease-spring-3); }
.on .shape { background: var(--accent); transform: scale(1.1); }
.circle { border-radius: var(--radius-round); }
.square { border-radius: 4px; }
.triangle { clip-path: polygon(50% 0, 100% 100%, 0 100%); }
/* A half disc, the fourth Bauhaus shape. */
.half { block-size: 8px; margin-block: 4px; border-radius: 8px 8px 0 0; }
</style>
