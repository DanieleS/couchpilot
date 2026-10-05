<script setup lang="ts">
export type IconName =
  | 'play' | 'search' | 'focus' | 'power' | 'plus' | 'pencil' | 'trash' | 'check' | 'x'
  | 'speaker' | 'speaker-off' | 'mic' | 'mic-off' | 'headphones' | 'tv'

defineProps<{ name: IconName; size?: number }>()

const SPEAKER = 'M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z'
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
    <template v-else-if="name === 'speaker'"><path :d="SPEAKER" /><path d="M16 9a5 5 0 0 1 0 6" /><path d="M19.364 18.364a9 9 0 0 0 0-12.728" /></template>
    <template v-else-if="name === 'speaker-off'"><path :d="SPEAKER" /><path d="m22 9-6 6" /><path d="m16 9 6 6" /></template>
    <template v-else-if="name === 'mic'"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" /><path d="M19 10v2a7 7 0 0 1-14 0v-2" /><path d="M12 19v3" /></template>
    <template v-else-if="name === 'mic-off'"><path d="m2 2 20 20" /><path d="M18.89 13.23A7.12 7.12 0 0 0 19 12v-2" /><path d="M5 10v2a7 7 0 0 0 12 5" /><path d="M15 9.34V5a3 3 0 0 0-5.68-1.33" /><path d="M9 9v3a3 3 0 0 0 5.12 2.12" /><path d="M12 19v3" /></template>
    <path v-else-if="name === 'headphones'" d="M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3" />
    <template v-else-if="name === 'tv'"><rect x="2" y="7" width="20" height="15" rx="2" /><path d="m17 2-5 5-5-5" /></template>
  </svg>
</template>
