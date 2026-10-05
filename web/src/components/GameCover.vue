<script setup lang="ts">
import { computed, ref } from 'vue'
import { imageUrl } from '../api'
import { placeholderGradient } from '../utils'

const props = defineProps<{ id: string; name: string; hasCover: boolean; badge?: string }>()

const failed = ref(false)
const url = computed(() => (props.hasCover && !failed.value ? imageUrl(props.id, 'cover') : null))
</script>

<template>
  <div class="cover" :style="url ? undefined : { background: placeholderGradient(name, 48, 20) }">
    <img v-if="url" :src="url" :alt="name" loading="lazy" @error="failed = true" />
    <span v-if="badge" class="badge">{{ badge }}</span>
  </div>
</template>

<style scoped>
.cover {
  position: relative;
  aspect-ratio: 3 / 4;
  border-radius: var(--r-cover);
  overflow: hidden;
  background: var(--well);
}
img {
  position: absolute;
  inset: 0;
  inline-size: 100%;
  block-size: 100%;
  object-fit: cover;
}
.badge {
  position: absolute;
  top: 6px;
  left: 6px;
  padding: 4px 6px;
  border-radius: 4px;
  background: var(--run);
  color: var(--run-ink);
  font-size: 0.5625rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
</style>
