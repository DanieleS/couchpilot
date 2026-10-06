<script setup lang="ts">
export type IconName =
  | 'play' | 'search' | 'focus' | 'power' | 'plus' | 'pencil' | 'trash' | 'check' | 'x'
  | 'speaker' | 'speaker-off' | 'mic' | 'mic-off' | 'headphones' | 'tv' | 'download'
  | 'pause' | 'next' | 'previous' | 'music' | 'chart' | 'chevron-left' | 'chevron-right'
  | 'camera' | 'record' | 'stop' | 'replay' | 'image' | 'share' | 'scissors' | 'trophy' | 'lock' | 'eye'

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
    <template v-else-if="name === 'chart'"><path d="M3 3v16a2 2 0 0 0 2 2h16" /><path d="M8 17v-4" /><path d="M13 17V9" /><path d="M18 17V5" /></template>
    <path v-else-if="name === 'chevron-left'" d="m15 18-6-6 6-6" />
    <path v-else-if="name === 'chevron-right'" d="m9 18 6-6-6-6" />
    <template v-else-if="name === 'pause'"><rect x="6" y="4" width="4" height="16" rx="1" /><rect x="14" y="4" width="4" height="16" rx="1" /></template>
    <template v-else-if="name === 'next'"><polygon points="5 4 15 12 5 20 5 4" /><path d="M19 5v14" /></template>
    <template v-else-if="name === 'previous'"><polygon points="19 20 9 12 19 4 19 20" /><path d="M5 19V5" /></template>
    <template v-else-if="name === 'music'"><path d="M9 18V5l12-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="18" cy="16" r="3" /></template>
    <template v-else-if="name === 'download'"><path d="M12 15V3" /><path d="m7 10 5 5 5-5" /><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /></template>
    <template v-else-if="name === 'tv'"><rect x="2" y="7" width="20" height="15" rx="2" /><path d="m17 2-5 5-5-5" /></template>
    <template v-else-if="name === 'camera'"><path d="M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z" /><circle cx="12" cy="13" r="3" /></template>
    <!-- The dot takes the danger colour: it's what says «this records». -->
    <template v-else-if="name === 'record'"><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="4.5" fill="var(--danger)" stroke="none" /></template>
    <rect v-else-if="name === 'stop'" x="6" y="6" width="12" height="12" rx="2" fill="currentColor" stroke="none" />
    <template v-else-if="name === 'replay'"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" /><path d="M3 3v5h5" /><path d="M12 7v5l3 2" /></template>
    <template v-else-if="name === 'image'"><rect x="3" y="3" width="18" height="18" rx="2" /><circle cx="9" cy="9" r="2" /><path d="m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21" /></template>
    <template v-else-if="name === 'share'"><circle cx="18" cy="5" r="3" /><circle cx="6" cy="12" r="3" /><circle cx="18" cy="19" r="3" /><path d="m8.6 13.5 6.8 4" /><path d="m15.4 6.5-6.8 4" /></template>
    <template v-else-if="name === 'scissors'"><circle cx="6" cy="6" r="3" /><path d="M8.12 8.12 12 12" /><path d="M20 4 8.12 15.88" /><circle cx="6" cy="18" r="3" /><path d="M14.8 14.8 20 20" /></template>
    <template v-else-if="name === 'trophy'"><path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6" /><path d="M18 9h1.5a2.5 2.5 0 0 0 0-5H18" /><path d="M4 22h16" /><path d="M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22" /><path d="M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22" /><path d="M18 2H6v7a6 6 0 0 0 12 0V2Z" /></template>
    <template v-else-if="name === 'lock'"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" /></template>
    <template v-else-if="name === 'eye'"><path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" /><circle cx="12" cy="12" r="3" /></template>
  </svg>
</template>
