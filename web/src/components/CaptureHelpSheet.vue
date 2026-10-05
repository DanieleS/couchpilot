<script setup lang="ts">
import { computed } from 'vue'
import type { CaptureAction } from '../captures'
import type { CaptureTool } from '../types'
import AppIcon from './AppIcon.vue'
import BottomSheet from './BottomSheet.vue'

// What to turn on at the PC when a capture can't be made: Game Bar's captures, its background
// recording, NVIDIA's Instant Replay.
const props = defineProps<{ help: { code: string; message: string; action: CaptureAction } | null; tool: CaptureTool | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ retry: [action: CaptureAction] }>()

const content = computed(() => {
  if (props.help?.code === 'capture-off') {
    return {
      title: 'Le acquisizioni di Game Bar sono spente',
      lead: 'Windows non lascia catturare niente finché non le riaccendi. Una volta sul PC:',
      steps: ['Impostazioni di Windows → <b>Giochi</b> → <b>Acquisizioni</b>', 'Accendi le acquisizioni, e <b>Xbox Game Bar</b> in Giochi'],
      note: null,
    }
  }
  if (props.tool === 'nvidia') {
    return {
      title: 'Il PC non ha salvato nessuna clip',
      lead: 'NVIDIA tiene da parte gli ultimi minuti solo col Replay istantaneo acceso. Una volta sul PC:',
      steps: ["Premi <b>Alt+Z</b> per aprire l'overlay di NVIDIA", 'Apri <b>Replay istantaneo</b> e scegli <b>Attiva</b>'],
      note: 'Se è già acceso, il gioco dev’essere in primo piano e le scorciatoie quelle di serie (Alt+F10 per salvare).',
    }
  }
  return {
    title: 'Il PC non sta tenendo gli ultimi secondi',
    lead: "Game Bar li tiene da parte solo se registra in sottofondo, e quell'opzione è spenta. Accendila una volta sul PC:",
    steps: ['Impostazioni di Windows → <b>Giochi</b> → <b>Acquisizioni</b>', 'Accendi <b>«Registra ciò che è successo»</b> e scegli quanto tenere'],
    note: 'Costa qualche fps mentre giochi. Con una scheda NVIDIA, il Replay istantaneo pesa meno: sceglilo in Playnite, CouchPilot → Cattura con.',
  }
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
      <button class="btn btn-ghost" @click="open = false">Chiudi</button>
      <button class="btn btn-wait" @click="retry">Riprova</button>
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
