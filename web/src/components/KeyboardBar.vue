<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { Vk, type Mod, type RemoteInput } from '../input'

const props = defineProps<{ input: RemoteInput; disabled: boolean; bottomOffset: number; keyboardHeight: number }>()
const open = defineModel<boolean>('open', { default: false })
// The bar's height, so the page can fit the touchpad in what's left above it.
const barHeight = defineModel<number>('barHeight', { default: 0 })

// ---- Modifiers, Termux style: a tap holds them for the next key, a double tap locks them.
type ModState = 'once' | 'lock'
const mods = reactive(new Map<Mod, ModState>())
const lastModTap = new Map<Mod, number>()
const DOUBLE_TAP_MS = 350

function tapMod(m: Mod, at: number) {
  const state = mods.get(m)
  const double = at - (lastModTap.get(m) ?? -Infinity) < DOUBLE_TAP_MS
  lastModTap.set(m, at)
  if (state === 'once' && double) mods.set(m, 'lock')
  else if (state) mods.delete(m)
  else mods.set(m, 'once')
}
// The modifiers for this press; the one-shot ones are spent.
function takeMods(): Mod[] {
  const active = [...mods.keys()]
  for (const [m, s] of mods) if (s === 'once') mods.delete(m)
  return active
}

// ---- The two rows. Fn swaps the left of the bar for the function keys.
type BarKey = { label: string; vk?: number; mod?: Mod; fn?: true; repeat?: true; aria?: string }
const fn = ref(false)
const k = (label: string, vk: number, repeat = false, aria?: string): BarKey => ({ label, vk, repeat: repeat || undefined, aria })
const mod = (label: string, m: Mod): BarKey => ({ label, mod: m })
const F = (n: number) => k(`F${n}`, Vk.f1 + n - 1)
const rows = computed<BarKey[][]>(() =>
  fn.value
    ? [
        [F(1), F(2), F(3), F(4), F(5), F(6), F(7), F(8)],
        [{ label: 'Fn', fn: true }, F(9), F(10), F(11), F(12), mod('Ctrl', 'ctrl'), mod('Alt', 'alt'), mod('⇧', 'shift')],
      ]
    : [
        [k('Esc', Vk.esc), k('Tab', Vk.tab), mod('Ctrl', 'ctrl'), mod('Alt', 'alt'), mod('Win', 'win'), k('Home', Vk.home), k('↑', Vk.up, true, 'Su'), k('End', Vk.end)],
        [{ label: 'Fn', fn: true }, mod('⇧', 'shift'), k('Canc', Vk.delete, true), k('Pag↑', Vk.pageUp, true), k('Pag↓', Vk.pageDown, true), k('←', Vk.left, true, 'Sinistra'), k('↓', Vk.down, true, 'Giù'), k('→', Vk.right, true, 'Destra')],
      ],
)

// Arrows and Canc repeat while held, like a real keyboard.
let repeatDelay = 0
let repeatTimer = 0
function stopRepeat() {
  clearTimeout(repeatDelay)
  clearInterval(repeatTimer)
}

const pressed = ref<string | null>(null)
function onKeyDown(key: BarKey, e: PointerEvent) {
  if (props.disabled) return
  pressed.value = key.label
  if (key.fn) return void (fn.value = !fn.value)
  if (key.mod) return tapMod(key.mod, e.timeStamp)
  const vk = key.vk!
  const used = takeMods()
  props.input.key(vk, used)
  if (key.repeat) {
    stopRepeat()
    repeatDelay = window.setTimeout(() => (repeatTimer = window.setInterval(() => props.input.key(vk, used), 50)), 400)
  }
}
function onKeyUp() {
  pressed.value = null
  stopRepeat()
}

// ---- The phone's own keyboard types into a hidden field.
// Phone keyboards don't send reliable key events (autocorrect, swipe typing, IMEs), so the
// field is the source of truth: every change is diffed against what was already sent and
// replayed on the PC as backspaces plus new text. A leading space keeps the field never quite
// empty, so a backspace on an "empty" field still shows up as a change.
const SENTINEL = ' '
const field = ref<HTMLInputElement>()
let sent = [...SENTINEL]

function reset() {
  sent = [...SENTINEL]
  if (field.value) field.value.value = SENTINEL
}

function onInput() {
  const now = [...(field.value?.value ?? '')]
  let same = 0
  while (same < sent.length && same < now.length && sent[same] === now[same]) same++
  for (let i = same; i < sent.length; i++) props.input.key(Vk.backspace)
  const added = now.slice(same).join('')
  sent = now

  if (added && mods.size > 0 && now.length === same + 1) {
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

function onFieldKeydown(e: KeyboardEvent) {
  if (e.key !== 'Enter' || e.isComposing) return
  e.preventDefault()
  props.input.key(Vk.enter, takeMods())
  reset()
}

// A backspace with the caret before the sentinel deletes nothing and sends nothing.
function caretToEnd() {
  const f = field.value
  if (f) requestAnimationFrame(() => f.setSelectionRange(f.value.length, f.value.length))
}

// Called from a tap, so the phone agrees to raise its keyboard. Already focused (Android's
// back button hides the keyboard but keeps the focus) needs a blur first.
function show() {
  const f = field.value
  if (!f || props.disabled) return
  if (document.activeElement === f) f.blur()
  reset()
  f.focus({ preventScroll: true })
}
const hide = () => field.value?.blur()
defineExpose({ show })

function onFocus() {
  open.value = true
  seenKeyboard = false
  caretToEnd()
}
function onBlur() {
  open.value = false
  fn.value = false
  for (const [m, s] of mods) if (s === 'once') mods.delete(m)
  onKeyUp()
}

// Android's back button closes the keyboard and leaves the field focused: once the keyboard
// has come up and gone again, let go of the field so the bar goes away with it.
let seenKeyboard = false
watch(
  () => props.keyboardHeight,
  (h) => {
    if (!open.value) return
    if (h > 150) seenKeyboard = true
    else if (seenKeyboard && h < 60) hide()
  },
)
watch(() => props.disabled, (d) => d && hide())

const bar = ref<HTMLElement>()
const observer = new ResizeObserver(([entry]) => (barHeight.value = entry.borderBoxSize?.[0]?.blockSize ?? entry.target.clientHeight))
watch(bar, (el, old) => {
  if (old) observer.unobserve(old)
  if (el) observer.observe(el)
})
onBeforeUnmount(() => {
  observer.disconnect()
  stopRepeat()
})
</script>

<template>
  <input
    ref="field"
    class="field"
    value=" "
    type="text"
    inputmode="text"
    enterkeyhint="enter"
    autocomplete="off"
    autocorrect="off"
    autocapitalize="off"
    spellcheck="false"
    tabindex="-1"
    aria-label="Scrivi sul PC"
    @input="onInput"
    @keydown="onFieldKeydown"
    @focus="onFocus"
    @blur="onBlur"
    @click="caretToEnd"
  />
  <!-- Touches here must not reach the page: that would take the focus off the field and the
       phone's keyboard would drop. Everything happens on pointer events instead of clicks. -->
  <div
    v-show="open"
    ref="bar"
    class="bar"
    :style="{ transform: `translateY(${-bottomOffset}px)` }"
    @touchstart.prevent
    @mousedown.prevent
  >
    <div v-for="(row, i) in rows" :key="i" class="row">
      <button
        v-for="key in row"
        :key="key.label"
        type="button"
        tabindex="-1"
        :aria-label="key.aria"
        :class="{
          mod: key.mod || key.fn,
          once: key.mod && mods.get(key.mod) === 'once',
          lock: (key.mod && mods.get(key.mod) === 'lock') || (key.fn && fn),
          down: pressed === key.label,
        }"
        @pointerdown.prevent="(e) => onKeyDown(key, e)"
        @pointerup="onKeyUp"
        @pointercancel="onKeyUp"
        @pointerleave="onKeyUp"
      >{{ key.label }}</button>
    </div>
    <button type="button" tabindex="-1" class="done" @pointerdown.prevent @pointerup="hide">Fine</button>
  </div>
</template>

<style scoped>
/* Focusable but invisible; 16px keeps iOS from zooming in when it gets the focus. */
.field {
  position: fixed;
  inset: auto auto 0 0;
  inline-size: 1px;
  block-size: 1px;
  padding: 0;
  border: 0;
  opacity: 0;
  font-size: 16px;
  pointer-events: none;
}

.bar {
  position: fixed;
  inset: auto 0 0;
  z-index: 35;
  display: grid;
  gap: 5px;
  padding: 6px var(--size-1);
  background: var(--nav-bg);
  box-shadow: var(--nav-edge);
  backdrop-filter: blur(12px);
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}
.row { display: grid; grid-template-columns: repeat(8, minmax(0, 1fr)); gap: 4px; }
.row button {
  block-size: 2.5rem;
  border-radius: 8px;
  background: var(--card);
  box-shadow: var(--card-edge);
  font-size: 0.75rem;
  font-weight: 700;
  white-space: nowrap;
  overflow: hidden;
  touch-action: none;
}
.row button.mod { color: var(--ink-2); }
.row button.down { background: var(--well); }
.row button.once { color: var(--wait); box-shadow: inset 0 0 0 2px var(--wait); }
.row button.lock { background: var(--wait); color: #fff; box-shadow: none; }

.done {
  position: absolute;
  inset: auto var(--size-2) calc(100% + 6px) auto;
  padding: 6px 12px;
  border-radius: var(--radius-round);
  background: var(--button-dark);
  color: var(--button-dark-ink);
  font-size: 0.6875rem;
  font-weight: 750;
  box-shadow: 0 4px 12px -4px rgb(0 0 0 / 0.4);
}
</style>
