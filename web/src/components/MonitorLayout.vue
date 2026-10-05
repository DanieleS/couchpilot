<script setup lang="ts">
import { computed } from 'vue'
import type { Monitor } from '../types'

// The monitors as rectangles, the way Windows' display settings draws them.
const props = defineProps<{ monitors: Monitor[]; selected?: boolean; labels?: boolean }>()

const box = computed(() => {
  const ms = props.monitors
  if (ms.length === 0) return null
  const minX = Math.min(...ms.map((m) => m.x))
  const minY = Math.min(...ms.map((m) => m.y))
  const w = Math.max(...ms.map((m) => m.x + m.width)) - minX
  const h = Math.max(...ms.map((m) => m.y + m.height)) - minY
  const gap = Math.max(w, h) * 0.02
  return { minX, minY, w, h, gap, radius: Math.min(w, h) * 0.05 }
})

const fill = (m: Monitor) =>
  props.selected
    ? m.primary ? 'var(--monitor-primary-selected)' : 'var(--monitor-selected)'
    : m.primary ? 'var(--accent)' : 'var(--monitor)'
</script>

<template>
  <svg
    v-if="box"
    :viewBox="`${box.minX - box.gap} ${box.minY - box.gap} ${box.w + box.gap * 2} ${box.h + box.gap * 2}`"
    preserveAspectRatio="xMidYMid meet"
    role="img"
    :aria-label="monitors.map((m) => m.name).join(', ')"
  >
    <g v-for="m in monitors" :key="m.devicePath + m.x">
      <rect
        :x="m.x + box.gap / 2"
        :y="m.y + box.gap / 2"
        :width="m.width - box.gap"
        :height="m.height - box.gap"
        :rx="box.radius"
        :fill="fill(m)"
      />
      <text
        v-if="labels"
        :x="m.x + m.width / 2"
        :y="m.y + m.height / 2"
        :font-size="Math.min(box.w, box.h) * 0.085"
        text-anchor="middle"
        dominant-baseline="middle"
        font-weight="700"
        :fill="m.primary ? '#fff' : 'var(--ink-2)'"
      >
        {{ m.name }}
      </text>
    </g>
  </svg>
  <svg v-else />
</template>
