<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRemoteInput, Vk, type Mod } from '../input'
import { useCouchPilot } from '../store'
import { useVisualViewport } from '../viewport'
import KeyboardBar from './KeyboardBar.vue'
import PcChip from './PcChip.vue'
import TouchPad from './TouchPad.vue'

const { status } = useCouchPilot()
const input = useRemoteInput()
const viewport = useVisualViewport()
const turnedOff = computed(() => status.value?.input === false)
const disabled = computed(() => input.link.value !== 'open')

const shortcuts: { label: string; vk: number; mods?: Mod[] }[] = [
  { label: 'Alt+Tab', vk: Vk.tab, mods: ['alt'] },
  { label: 'Schermo intero', vk: Vk.enter, mods: ['alt'] },
  { label: 'Start', vk: Vk.win },
  { label: 'Desktop', vk: Vk.d, mods: ['win'] },
  { label: 'Alt+F4', vk: Vk.f4, mods: ['alt'] },
]
const media: { label: string; vk: number }[] = [
  { label: 'Vol −', vk: Vk.volumeDown },
  { label: 'Muto', vk: Vk.mute },
  { label: 'Vol +', vk: Vk.volumeUp },
  { label: 'Play/Pausa', vk: Vk.playPause },
]

// With the keyboard up, the touchpad shrinks into what's left between the header and the bar,
// so you can point and type without switching screens.
const keyboard = ref<InstanceType<typeof KeyboardBar>>()
const keyboardOpen = ref(false)
const barHeight = ref(0)
const pad = ref<InstanceType<typeof TouchPad>>()
const padHeight = ref<number | null>(null)

watch(
  [keyboardOpen, viewport.height, viewport.offsetTop, barHeight],
  async ([open]) => {
    if (!open) return void (padHeight.value = null)
    window.scrollTo({ top: 0 })
    await nextTick()
    const top = (pad.value?.$el as HTMLElement | undefined)?.getBoundingClientRect().top ?? 0
    const visibleBottom = viewport.offsetTop.value + viewport.height.value - barHeight.value
    // 6px of card padding above and below the surface, and a little air above the bar.
    padHeight.value = Math.max(96, visibleBottom - top - 12 - 10)
  },
)
</script>

<template>
  <div class="page" :class="{ typing: keyboardOpen }">
    <header class="page-header">
      <h1>Controllo</h1>
      <PcChip :name="status?.pc ?? null" />
    </header>

    <TouchPad ref="pad" class="pad" :input="input" :disabled="disabled" :height="padHeight" :compact="keyboardOpen">
      <template #disabled>
        <div class="off">
          <template v-if="turnedOff">
            <p class="title">Touchpad e tastiera sono spenti</p>
            <p class="muted">
              Accendili in Playnite: Componenti aggiuntivi → Impostazioni estensioni → CouchPilot →
              «Permetti al telefono di usare mouse e tastiera del PC».
            </p>
          </template>
          <template v-else>
            <p class="title">Mi collego al PC…</p>
            <p class="muted">Il PC e il telefono devono essere sulla stessa rete.</p>
          </template>
        </div>
      </template>
    </TouchPad>

    <button class="btn btn-dark btn-block keyboard" :disabled="disabled" @click="keyboard?.show()">
      <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <rect x="2" y="5" width="20" height="14" rx="2" /><path d="M6 9h.01M10 9h.01M14 9h.01M18 9h.01M6 13h.01M18 13h.01M9 15h6" />
      </svg>
      Tastiera
    </button>

    <p class="eyebrow">Scorciatoie</p>
    <div class="scroll-row">
      <button v-for="s in shortcuts" :key="s.label" class="key chip" :disabled="disabled" @click="input.key(s.vk, s.mods)">{{ s.label }}</button>
    </div>

    <p class="eyebrow">Audio</p>
    <div class="media">
      <button v-for="m in media" :key="m.label" class="key" :disabled="disabled" @click="input.key(m.vk)">{{ m.label }}</button>
    </div>

    <KeyboardBar
      ref="keyboard"
      v-model:open="keyboardOpen"
      v-model:bar-height="barHeight"
      :input="input"
      :disabled="disabled"
      :bottom-offset="viewport.bottomOffset.value"
      :keyboard-height="viewport.keyboardHeight.value"
    />
  </div>
</template>

<style scoped>
.pad { margin-top: var(--size-4); }
.off { max-inline-size: 18rem; padding: var(--size-4); text-align: center; }
.title { font-weight: 800; }
.muted { font-size: 0.75rem; line-height: 1.45; margin-top: 4px; }
.keyboard { margin-top: var(--size-3); }
/* While typing only the touchpad stays: everything else would sit under the keyboard. */
.typing .keyboard, .typing .eyebrow, .typing .scroll-row, .typing .media { display: none; }

.media { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
.key {
  block-size: 2.75rem;
  border-radius: var(--r-inner);
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.75rem;
  font-weight: 700;
  transition: transform 80ms var(--ease-out-2), background 100ms;
}
.key:active:not(:disabled) { transform: scale(0.95); background: var(--well); }
.key:disabled { opacity: 0.45; }
.chip { flex-shrink: 0; padding: 0 var(--size-3); white-space: nowrap; }
</style>
