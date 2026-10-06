<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Achievement } from '../types'
import AppIcon from './AppIcon.vue'

// An achievement's icon: the game's own, greyed out while it's locked unless the game has a
// locked one of its own; a trophy when there's none to show.
const props = defineProps<{ achievement: Achievement; size?: number }>()

const failed = ref(false)
watch(() => props.achievement.id, () => (failed.value = false))
const url = computed(() => {
  const a = props.achievement
  if (failed.value) return null
  return a.unlocked ? a.icon : a.lockedIcon ?? a.icon
})
// Greyed out only when standing in for a locked icon the game doesn't have.
const grey = computed(() => !props.achievement.unlocked && !props.achievement.lockedIcon)
</script>

<template>
  <span class="icon" :class="{ grey, locked: !achievement.unlocked }" :style="{ inlineSize: `${size ?? 40}px`, blockSize: `${size ?? 40}px` }">
    <img v-if="url" :src="url" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer" @error="failed = true" />
    <AppIcon v-else name="trophy" :size="Math.round((size ?? 40) * 0.5)" />
  </span>
</template>

<style scoped>
.icon {
  position: relative;
  display: grid;
  place-items: center;
  flex-shrink: 0;
  border-radius: 8px;
  overflow: hidden;
  background: var(--card);
  box-shadow: var(--card-edge);
  color: var(--ink-2);
}
img { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: cover; }
.grey img { filter: grayscale(1) brightness(0.7); }
.locked { opacity: 0.85; }
</style>
