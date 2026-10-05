<script setup lang="ts">
// How loud it is right now. Peaks are linear amplitude: the square root reads closer to
// what the ear hears, so quiet sounds still move the bar.
defineProps<{ peak: number }>()
</script>

<template>
  <span class="meter" aria-hidden="true"><span class="level" :style="{ transform: `scaleX(${Math.sqrt(peak)})` }" /></span>
</template>

<style scoped>
.meter { display: block; block-size: 3px; border-radius: 2px; background: var(--slider-track); overflow: hidden; }
.level {
  display: block;
  block-size: 100%;
  background: var(--ok);
  transform-origin: left;
  /* Polls come every half second: glide between them instead of jumping. */
  transition: transform 450ms linear;
}
</style>
