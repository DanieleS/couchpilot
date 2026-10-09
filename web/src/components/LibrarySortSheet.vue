<script setup lang="ts">
import { computed } from 'vue'
import { t } from '../i18n'
import type { LibraryView } from '../types'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'

const props = defineProps<{ view: LibraryView }>()
const emit = defineEmits<{ sort: [sort: string, descending: boolean] }>()
const open = defineModel<boolean>('open', { required: true })

// The sorts worth having on the couch. Playnite has more; one set on the PC shows up on top
// under Playnite's own name.
const sorts = Object.keys(t.games.view.sorts)
// Text reads A to Z; numbers and dates read biggest and newest first.
const ascendingFirst = new Set(['name', 'library', 'completionStatus'])

const options = computed(() =>
  sorts.includes(props.view.sort)
    ? sorts.map((key) => ({ key, label: t.games.view.sorts[key] }))
    : [{ key: props.view.sort, label: props.view.sortLabel }, ...sorts.map((key) => ({ key, label: t.games.view.sorts[key] }))],
)

const pick = (key: string) => {
  if (key !== props.view.sort) emit('sort', key, !ascendingFirst.has(key))
  open.value = false
}
const direction = (descending: boolean) => descending !== props.view.descending && emit('sort', props.view.sort, descending)
</script>

<template>
  <BottomSheet v-model:open="open" :title="t.games.view.sortTitle">
    <h2 class="title">{{ t.games.view.sortTitle }}</h2>
    <div class="seg" role="tablist">
      <button role="tab" :aria-selected="!view.descending" :class="{ on: !view.descending }" @click="direction(false)">{{ t.games.view.ascending }}</button>
      <button role="tab" :aria-selected="view.descending" :class="{ on: view.descending }" @click="direction(true)">{{ t.games.view.descending }}</button>
    </div>
    <ul class="list">
      <li v-for="o in options" :key="o.key">
        <button class="option" :class="{ on: o.key === view.sort }" @click="pick(o.key)">
          <span class="name truncate">{{ o.label }}</span>
          <AppIcon v-if="o.key === view.sort" name="check" :size="18" class="check" />
        </button>
      </li>
    </ul>
    <p class="muted note">{{ t.games.view.sortNote }}</p>
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
.list { display: grid; gap: 4px; margin-top: var(--size-3); }
.option {
  display: flex;
  align-items: center;
  gap: var(--size-3);
  inline-size: 100%;
  padding: 12px;
  border-radius: var(--r-card);
  background: var(--well);
  text-align: left;
  transition: transform 100ms var(--ease-out-2);
}
.option:active { transform: scale(0.98); }
.option.on { box-shadow: inset 0 0 0 2px var(--wait); }
.name { flex: 1; min-width: 0; font-size: 0.875rem; font-weight: 750; }
.check { color: var(--wait); flex-shrink: 0; }
.note { margin: var(--size-3) 0 var(--size-2); font-size: 0.6875rem; line-height: 1.45; }
</style>
