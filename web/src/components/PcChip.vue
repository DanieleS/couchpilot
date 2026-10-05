<script setup lang="ts">
defineProps<{ name: string | null }>()
</script>

<template>
  <span class="chip">
    <span class="dot" :class="{ online: name }" />
    {{ name ?? 'Offline' }}
  </span>
</template>

<style scoped>
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 10px;
  border-radius: var(--radius-round);
  background: var(--card);
  box-shadow: var(--card-edge);
  color: var(--ink-2);
  font-size: 0.6875rem;
  font-weight: 650;
}
.dot {
  inline-size: 7px;
  block-size: 7px;
  border-radius: var(--radius-round);
  background: var(--danger);
}
.dot.online { background: var(--ok); }
</style>
