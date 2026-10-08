<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { percentLabel, rarity, unlockedLabel, useGameAchievements } from '../achievements'
import { t } from '../i18n'
import type { Achievement, Game } from '../types'
import AchievementIcon from './AchievementIcon.vue'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'
import GameCover from './GameCover.vue'

// Every achievement of a game: the unlocked ones first, newest first, then what's left to do.
const props = defineProps<{ game: Game | null }>()
const open = defineModel<boolean>('open', { required: true })

const gameId = computed(() => (open.value ? props.game?.id ?? null : null))
const { data, loading, stop } = useGameAchievements(gameId)
onScopeDispose(stop)

type Filter = 'all' | 'unlocked' | 'locked'
const filter = ref<Filter>('all')
watch(open, (o) => o && (filter.value = 'all'))
// Hidden achievements stay hidden until you ask: the game didn't want to spoil them.
const revealed = ref(new Set<string>())
watch(gameId, () => (revealed.value = new Set()))
const reveal = (a: Achievement) => {
  revealed.value = new Set([...revealed.value, a.id])
}

const items = computed(() => {
  const all = data.value?.items ?? []
  return filter.value === 'all' ? all : all.filter((a) => a.unlocked === (filter.value === 'unlocked'))
})
const percent = computed(() => (data.value?.total ? Math.round((data.value.unlocked / data.value.total) * 100) : 0))
const score = computed(() => {
  const list = data.value?.items ?? []
  if (!list.some((a) => a.gamerScore)) return null
  const got = list.filter((a) => a.unlocked).reduce((t, a) => t + (a.gamerScore ?? 0), 0)
  const all = list.reduce((t, a) => t + (a.gamerScore ?? 0), 0)
  return `${Math.round(got)} / ${Math.round(all)} G`
})
const isHidden = (a: Achievement) => a.hidden && !a.unlocked && !revealed.value.has(a.id)
</script>

<template>
  <BottomSheet v-model:open="open" :title="t.achievements.sheetTitle(game?.name ?? '')">
    <template v-if="game">
      <div class="head">
        <GameCover :id="game.id" :name="game.name" :has-cover="game.hasCover" class="cover" />
        <div class="info">
          <h2 class="name">{{ game.name }}</h2>
          <template v-if="data">
            <p class="count tabular"><b>{{ data.unlocked }}</b> {{ t.achievements.count(data.total) }}<template v-if="score"> · {{ score }}</template></p>
            <span class="meter"><span class="fill" :style="{ transform: `scaleX(${percent / 100})` }" /></span>
            <p class="pct tabular">{{ percent }}%<template v-if="percent === 100"> · {{ t.achievements.all100 }}</template></p>
          </template>
        </div>
      </div>

      <div v-if="data" class="seg" role="tablist" :aria-label="t.achievements.which">
        <button role="tab" :aria-selected="filter === 'all'" :class="{ on: filter === 'all' }" @click="filter = 'all'">{{ t.achievements.filters.all }}</button>
        <button role="tab" :aria-selected="filter === 'unlocked'" :class="{ on: filter === 'unlocked' }" @click="filter = 'unlocked'">{{ t.achievements.filters.unlocked }} <span class="n tabular">{{ data.unlocked }}</span></button>
        <button role="tab" :aria-selected="filter === 'locked'" :class="{ on: filter === 'locked' }" @click="filter = 'locked'">{{ t.achievements.filters.locked }} <span class="n tabular">{{ data.total - data.unlocked }}</span></button>
      </div>

      <div v-if="loading && !data" class="skeleton" />
      <p v-else-if="!data" class="empty muted">{{ t.achievements.none }}</p>
      <p v-else-if="!items.length" class="empty muted">{{ filter === 'locked' ? t.achievements.allDone : t.achievements.noneYet }}</p>
      <ul v-else class="list">
        <li v-for="a in items" :key="a.id" class="row" :class="{ locked: !a.unlocked }">
          <AchievementIcon :achievement="a" :size="48" />
          <div class="text">
            <template v-if="isHidden(a)">
              <p class="title muted"><AppIcon name="lock" :size="12" />{{ t.achievements.hidden }}</p>
              <button class="reveal" @click="reveal(a)"><AppIcon name="eye" :size="12" />{{ t.achievements.reveal }}</button>
            </template>
            <template v-else>
              <p class="title">{{ a.name }}</p>
              <p v-if="a.description" class="desc">{{ a.description }}</p>
            </template>
            <p class="meta tabular">
              <span v-if="a.unlocked" class="when">{{ unlockedLabel(a.unlockedAt) }}</span>
              <span v-if="rarity(a.percent)" class="rare" :class="{ ultra: (a.percent ?? 100) < 2 }">{{ rarity(a.percent) }}</span>
              <span v-if="a.percent !== null" class="pctof">{{ percentLabel(a.percent) }}</span>
              <span v-if="a.gamerScore" class="score">{{ Math.round(a.gamerScore) }} G</span>
            </p>
          </div>
        </li>
      </ul>
    </template>
  </BottomSheet>
</template>

<style scoped>
.head { display: flex; gap: var(--size-3); align-items: flex-end; }
.cover { inline-size: 4.5rem; flex-shrink: 0; }
.info { min-width: 0; flex: 1; }
.name { font-size: 1.25rem; font-weight: 800; line-height: 1.05; letter-spacing: -0.02em; }
.count { margin-top: 6px; font-size: 0.8125rem; color: var(--ink-2); }
.count b { color: var(--ink); font-weight: 800; }
.meter { display: block; block-size: 8px; margin-top: 8px; border-radius: 4px; background: var(--slider-track); overflow: hidden; }
.fill { display: block; block-size: 100%; border-radius: 4px; background: var(--run); transform-origin: left; transition: transform 300ms var(--ease-out-3); }
.pct { margin-top: 4px; font-size: 0.6875rem; font-weight: 750; color: var(--ink-2); }

.seg { display: grid; grid-template-columns: repeat(3, 1fr); gap: 3px; margin-top: var(--size-4); padding: 3px; border-radius: var(--r-button); background: var(--well); }
.seg button { padding: 0.5rem 0; border-radius: 9px; font-size: 0.75rem; font-weight: 750; color: var(--ink-2); }
.seg button.on { background: var(--ink); color: var(--bg); }
.seg .n { font-weight: 500; opacity: 0.7; }

.list { display: grid; gap: 4px; margin-top: var(--size-3); }
.row { display: flex; gap: var(--size-3); align-items: flex-start; padding: 10px; border-radius: var(--r-inner); background: var(--well); }
.row.locked { opacity: 0.75; }
.text { min-width: 0; flex: 1; }
.title { display: flex; align-items: center; gap: 4px; font-size: 0.875rem; font-weight: 750; line-height: 1.25; }
.desc { margin-top: 2px; font-size: 0.75rem; line-height: 1.4; color: var(--ink-2); }
.reveal { display: inline-flex; align-items: center; gap: 4px; margin-top: 4px; font-size: 0.6875rem; font-weight: 700; color: var(--wait); }
.meta { display: flex; flex-wrap: wrap; gap: 4px 10px; margin-top: 6px; font-size: 0.6875rem; font-weight: 650; color: var(--ink-2); }
.when { color: var(--ink); }
.rare { padding: 1px 6px; border-radius: 4px; background: color-mix(in srgb, var(--wait) 16%, transparent); color: var(--wait); font-weight: 800; }
.rare.ultra { background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--accent); }
.empty { padding: var(--size-6) 0; text-align: center; font-size: 0.8125rem; }
.skeleton { margin-top: var(--size-3); block-size: 10rem; border-radius: var(--r-inner); background: var(--well); animation: var(--animation-blink); }
</style>
