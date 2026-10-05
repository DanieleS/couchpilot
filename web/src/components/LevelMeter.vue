<script setup lang="ts">
// How loud it is right now, 0..1. The PC already sends the peak's square root, which reads
// closer to what the ear hears than the raw peak.
defineProps<{ level: number }>()
</script>

<template>
  <span class="meter" aria-hidden="true"><span class="level" :style="{ transform: `scaleX(${level})` }" /></span>
</template>

<style scoped>
.meter { display: block; block-size: 3px; border-radius: 2px; background: var(--slider-track); overflow: hidden; }
.level {
  display: block;
  block-size: 100%;
  background: var(--ok);
  transform-origin: left;
  /* Levels come 25 times a second: just enough glide to hide the steps. */
  transition: transform 60ms linear;
}
</style>
