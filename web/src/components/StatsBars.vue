<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { locale, t } from '../i18n'
import type { StatsRangeName } from '../types'
import { capitalize, duration, parseDay } from '../utils'

// Time played per day (week, month) or per month (year). One series, so one ink: the bar in
// focus (today, or the one you tapped) full strength, the others a quieter step of it.
const props = defineProps<{ buckets: { date: string; seconds: number }[]; range: StatsRangeName; today: string }>()

const { weekdays, months } = t.stats.bars
const label = (date: string, i: number) => {
  const d = parseDay(date)
  if (props.range === 'week') return weekdays[i]
  if (props.range === 'year') return months[d.getMonth()]
  // A month: every fifth day, so thirty labels don't collide.
  return d.getDate() === 1 || d.getDate() % 5 === 0 ? String(d.getDate()) : ''
}
const caption = (date: string) => {
  const d = parseDay(date)
  return capitalize(
    props.range === 'year'
      ? d.toLocaleDateString(locale, { month: 'long', year: 'numeric' })
      : d.toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long' }),
  )
}

// Above a week's bars, where there's room for little: "3:20", "45′", nothing for a day off.
const short = (seconds: number) => {
  const minutes = Math.round(seconds / 60)
  if (minutes === 0) return ''
  return minutes < 60 ? `${minutes}′` : `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, '0')}`
}

const max = computed(() => Math.max(1, ...props.buckets.map((b) => b.seconds)))
const todayIndex = computed(() =>
  props.buckets.findIndex((b) => (props.range === 'year' ? b.date.slice(0, 7) === props.today.slice(0, 7) : b.date === props.today)),
)
const selected = ref<number | null>(null)
watch(() => props.buckets, () => (selected.value = null))
const focus = computed(() => selected.value ?? (todayIndex.value >= 0 ? todayIndex.value : null))
const focused = computed(() => (focus.value === null ? null : props.buckets[focus.value]))
</script>

<template>
  <div class="chart">
    <p class="caption" aria-live="polite">
      <template v-if="focused"><span class="when">{{ caption(focused.date) }}</span> · {{ duration(focused.seconds) }}</template>
      <template v-else>{{ t.stats.bars.hint }}</template>
    </p>
    <div class="bars" :class="range" role="group" :aria-label="t.stats.bars.label">
      <button
        v-for="(b, i) in buckets"
        :key="b.date"
        class="bar"
        :class="{ on: i === focus }"
        :aria-label="`${caption(b.date)}: ${duration(b.seconds)}`"
        :aria-pressed="i === selected"
        @click="selected = selected === i ? null : i"
      >
        <span v-if="range === 'week'" class="value tabular">{{ short(b.seconds) }}</span>
        <span class="track"><span class="fill" :style="{ height: `${(b.seconds / max) * 100}%` }" /></span>
        <span class="label">{{ label(b.date, i) }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.chart { display: grid; gap: var(--size-2); }
.caption { font-size: 0.75rem; font-weight: 650; color: var(--ink-2); min-block-size: 1.2em; }
.when { color: var(--ink); font-weight: 750; }
.bars { display: grid; grid-auto-flow: column; grid-auto-columns: 1fr; gap: 2px; align-items: end; }
.bars.week { gap: 6px; }
.bars.year { gap: 4px; }
.bar { display: grid; gap: 4px; justify-items: center; min-width: 0; padding: 0; }
.track { display: flex; align-items: flex-end; inline-size: 100%; block-size: 7rem; }
.fill {
  inline-size: 100%;
  min-block-size: 2px;
  border-radius: 4px 4px 1px 1px;
  background: var(--bar-dim);
  transition: height 250ms var(--ease-out-3), background 120ms;
}
.month .fill { border-radius: 2px 2px 1px 1px; }
.on .fill { background: var(--ink); }
.label { font-size: 0.5625rem; font-weight: 750; color: var(--ink-2); block-size: 0.9rem; }
.on .label { color: var(--ink); }
.value { font-size: 0.5625rem; font-weight: 750; color: var(--ink-2); block-size: 0.9rem; white-space: nowrap; }
.on .value { color: var(--ink); }
</style>
