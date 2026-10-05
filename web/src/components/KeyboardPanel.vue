<script setup lang="ts">
import { ref } from 'vue'
import { Vk, type Mod, type RemoteInput } from '../input'

const props = defineProps<{ input: RemoteInput; disabled: boolean }>()

// Modifiers latch: tap Ctrl, then a key or a letter, and it's released after that one press.
const mods = ref<Mod[]>([])
const modKeys: { mod: Mod; label: string }[] = [
  { mod: 'ctrl', label: 'Ctrl' },
  { mod: 'alt', label: 'Alt' },
  { mod: 'shift', label: 'Maiusc' },
  { mod: 'win', label: 'Win' },
]
const toggle = (m: Mod) => (mods.value = mods.value.includes(m) ? mods.value.filter((x) => x !== m) : [...mods.value, m])
const takeMods = () => {
  const m = mods.value
  mods.value = []
  return m
}
const press = (vk: number, extra: Mod[] = []) => props.input.key(vk, [...new Set([...takeMods(), ...extra])])

const keys: { label: string; vk: number; wide?: boolean }[] = [
  { label: 'Esc', vk: Vk.esc },
  { label: 'Tab', vk: Vk.tab },
  { label: '⌫', vk: Vk.backspace },
  { label: 'Canc', vk: Vk.delete },
  { label: '←', vk: Vk.left },
  { label: '↑', vk: Vk.up },
  { label: '↓', vk: Vk.down },
  { label: '→', vk: Vk.right },
  { label: 'Invio', vk: Vk.enter, wide: true },
  { label: 'Spazio', vk: Vk.space, wide: true },
]
const shortcuts: { label: string; vk: number; mods: Mod[] }[] = [
  { label: 'Alt+Tab', vk: Vk.tab, mods: ['alt'] },
  { label: 'Schermo intero', vk: Vk.enter, mods: ['alt'] },
  { label: 'Start', vk: Vk.win, mods: [] },
  { label: 'Desktop', vk: Vk.d, mods: ['win'] },
  { label: 'Alt+F4', vk: Vk.f4, mods: ['alt'] },
]
const media: { label: string; vk: number }[] = [
  { label: 'Vol −', vk: Vk.volumeDown },
  { label: 'Muto', vk: Vk.mute },
  { label: 'Vol +', vk: Vk.volumeUp },
  { label: '⏯', vk: Vk.playPause },
]
const fKeys = Array.from({ length: 12 }, (_, i) => ({ label: `F${i + 1}`, vk: Vk.f1 + i }))

// Phone keyboards don't send reliable key events (autocorrect, swipe typing, IMEs), so the
// field is the source of truth: every change is diffed against what was already sent and
// replayed on the PC as backspaces plus new text. A leading space keeps the field never quite
// empty, so a backspace on an "empty" field still shows up as a change.
const SENTINEL = ' '
const field = ref<HTMLInputElement>()
const empty = ref(true)
let sent = [...SENTINEL]

function reset() {
  sent = [...SENTINEL]
  empty.value = true
  if (field.value) field.value.value = SENTINEL
}

function onInput() {
  const now = [...(field.value?.value ?? '')]
  let same = 0
  while (same < sent.length && same < now.length && sent[same] === now[same]) same++
  for (let i = same; i < sent.length; i++) props.input.key(Vk.backspace)
  const added = now.slice(same).join('')
  sent = now
  empty.value = now.join('') === SENTINEL

  if (added && mods.value.length > 0 && now.length === same + 1) {
    // A letter typed with Ctrl armed is a shortcut, not text.
    props.input.char(added, takeMods())
    reset()
    return
  }
  if (added) props.input.type(added)
  // Start over at word boundaries once the field gets long: autocorrect only ever rewrites
  // the current word, and a short field keeps the diff cheap.
  if (now.length === 0 || (now.length > 40 && added.endsWith(' '))) reset()
}

function onKeydown(e: KeyboardEvent) {
  if (e.key !== 'Enter' || e.isComposing) return
  e.preventDefault()
  press(Vk.enter)
  reset()
}

// A backspace with the caret before the sentinel deletes nothing and sends nothing.
function caretToEnd() {
  const f = field.value
  if (f) requestAnimationFrame(() => f.setSelectionRange(f.value.length, f.value.length))
}
</script>

<template>
  <section class="keyboard" :class="{ disabled }">
    <div class="mods">
      <button
        v-for="m in modKeys"
        :key="m.mod"
        class="key"
        :class="{ on: mods.includes(m.mod) }"
        :aria-pressed="mods.includes(m.mod)"
        :disabled="disabled"
        @click="toggle(m.mod)"
      >{{ m.label }}</button>
    </div>

    <label class="field card">
      <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <rect x="2" y="5" width="20" height="14" rx="2" /><path d="M6 9h.01M10 9h.01M14 9h.01M18 9h.01M6 13h.01M18 13h.01M9 15h6" />
      </svg>
      <input
        ref="field"
        :value="SENTINEL"
        :disabled="disabled"
        type="text"
        inputmode="text"
        enterkeyhint="send"
        autocomplete="off"
        autocorrect="off"
        autocapitalize="off"
        spellcheck="false"
        aria-label="Scrivi sul PC"
        @input="onInput"
        @keydown="onKeydown"
        @focus="caretToEnd"
        @click="caretToEnd"
      />
      <span v-if="empty" class="placeholder" aria-hidden="true">Tocca e scrivi: arriva subito sul PC</span>
    </label>

    <div class="keys">
      <button v-for="k in keys" :key="k.label" class="key" :class="{ wide: k.wide }" :disabled="disabled" @click="press(k.vk)">{{ k.label }}</button>
    </div>

    <p class="eyebrow">Scorciatoie</p>
    <div class="scroll-row">
      <button v-for="s in shortcuts" :key="s.label" class="key chip" :disabled="disabled" @click="press(s.vk, s.mods)">{{ s.label }}</button>
    </div>
    <div class="scroll-row">
      <button v-for="k in fKeys" :key="k.label" class="key chip" :disabled="disabled" @click="press(k.vk)">{{ k.label }}</button>
    </div>

    <p class="eyebrow">Audio</p>
    <div class="media">
      <button v-for="k in media" :key="k.label" class="key" :disabled="disabled" @click="press(k.vk)">{{ k.label }}</button>
    </div>
  </section>
</template>

<style scoped>
.mods, .keys, .media { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
.keys { margin-top: var(--size-2); }
.key {
  block-size: 2.75rem;
  border-radius: var(--r-inner);
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.8125rem;
  font-weight: 700;
  transition: transform 80ms var(--ease-out-2), background 100ms, color 100ms;
}
.key:active:not(:disabled) { transform: scale(0.95); background: var(--well); }
.key:disabled { opacity: 0.45; }
.key.wide { grid-column: span 2; }
.key.on { background: var(--wait); color: #fff; box-shadow: none; }
.chip { flex-shrink: 0; padding: 0 var(--size-3); white-space: nowrap; }

.field {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--size-2);
  margin-top: var(--size-2);
  padding: 0 var(--size-3);
  border-radius: var(--r-field);
  color: var(--ink-2);
}
.field input {
  flex: 1;
  min-width: 0;
  block-size: 3rem;
  border: 0;
  background: none;
  outline: none;
  font-size: 1rem;
  color: var(--ink);
}
.field:focus-within { box-shadow: inset 0 0 0 2px var(--wait); }
/* The field always holds the sentinel space, so a real placeholder would never show. */
.placeholder {
  position: absolute;
  inset: 50% auto auto calc(var(--size-3) + 18px + var(--size-2));
  translate: 0 -50%;
  font-size: 0.8125rem;
  font-weight: 600;
  pointer-events: none;
}
.field:focus-within .placeholder { display: none; }
.eyebrow { margin-bottom: var(--size-2); }
.scroll-row + .scroll-row { margin-top: -2px; }
</style>
