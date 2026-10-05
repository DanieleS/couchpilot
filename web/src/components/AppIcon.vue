<script setup lang="ts">
export type IconName =
  | 'play' | 'search' | 'focus' | 'power' | 'plus' | 'pencil' | 'trash' | 'check' | 'x'

defineProps<{ name: IconName; size?: number }>()
</script>

<template>
  <svg
    :width="size ?? 18"
    :height="size ?? 18"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <polygon v-if="name === 'play'" points="6 3 20 12 6 21 6 3" />
    <template v-else-if="name === 'search'"><circle cx="11" cy="11" r="8" /><path d="m21 21-4.3-4.3" /></template>
    <template v-else-if="name === 'focus'"><path d="M15 3h6v6" /><path d="M10 14 21 3" /><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" /></template>
    <template v-else-if="name === 'power'"><path d="M12 2v10" /><path d="M18.4 6.6a9 9 0 1 1-12.77.04" /></template>
    <template v-else-if="name === 'plus'"><path d="M5 12h14" /><path d="M12 5v14" /></template>
    <path v-else-if="name === 'pencil'" d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z" />
    <template v-else-if="name === 'trash'"><path d="M3 6h18" /><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" /><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" /></template>
    <path v-else-if="name === 'check'" d="M20 6 9 17l-5-5" />
    <template v-else-if="name === 'x'"><path d="M18 6 6 18" /><path d="m6 6 12 12" /></template>
  </svg>
</template>
