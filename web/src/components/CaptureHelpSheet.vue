<script setup lang="ts">
import { computed } from 'vue'
import type { CaptureAction } from '../captures'
import { t } from '../i18n'
import type { CaptureTool } from '../types'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'

// What to turn on at the PC when a capture can't be made: Game Bar's captures, its background
// recording, NVIDIA's Instant Replay.
const props = defineProps<{ help: { code: string; message: string; action: CaptureAction } | null; tool: CaptureTool | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ retry: [action: CaptureAction] }>()

const content = computed(() => {
  const h = t.captures.help
  if (props.help?.code === 'capture-off') return { ...h.captureOff, note: null }
  if (props.tool === 'nvidia') return h.nvidia
  return h.gamebar
})

const retry = () => {
  if (props.help) emit('retry', props.help.action)
  open.value = false
}
</script>

<template>
  <BottomSheet v-model:open="open" :title="content.title">
    <span class="badge"><AppIcon :name="help?.code === 'capture-off' ? 'camera' : 'replay'" :size="24" /></span>
    <h2 class="title">{{ content.title }}</h2>
    <p class="lead">{{ content.lead }}</p>
    <ol class="steps">
      <!-- Our own fixed strings, for the bold. -->
      <li v-for="(step, i) in content.steps" :key="i"><span class="n">{{ i + 1 }}</span><span v-html="step" /></li>
    </ol>
    <p v-if="content.note" class="note">{{ content.note }}</p>
    <div class="actions">
      <button class="btn btn-ghost" @click="open = false">{{ t.captures.help.close }}</button>
      <button class="btn btn-wait" @click="retry">{{ t.captures.help.retry }}</button>
    </div>
  </BottomSheet>
</template>

<style scoped>
.badge { display: grid; place-items: center; inline-size: 3rem; block-size: 3rem; border-radius: var(--radius-round); background: var(--wait); color: #fff; }
.title { margin-top: var(--size-3); font-size: 1.375rem; font-weight: 800; line-height: 1.1; letter-spacing: -0.02em; }
.lead { margin-top: var(--size-2); font-size: 0.875rem; line-height: 1.5; color: var(--ink-2); }
.steps { display: grid; gap: 10px; margin: var(--size-3) 0 0; padding: 0; list-style: none; }
.steps li { display: flex; gap: var(--size-3); align-items: flex-start; font-size: 0.875rem; line-height: 1.4; }
.steps .n { display: grid; place-items: center; inline-size: 1.5rem; block-size: 1.5rem; flex-shrink: 0; border-radius: var(--radius-round); background: var(--well); font-size: 0.75rem; font-weight: 800; }
.note { margin-top: var(--size-3); padding: var(--size-3); border-radius: var(--r-field); background: var(--well); font-size: 0.8125rem; line-height: 1.45; color: var(--ink-2); }
.actions { display: grid; grid-template-columns: 1fr 1fr; gap: var(--size-2); margin: var(--size-4) 0 var(--size-2); }
</style>
