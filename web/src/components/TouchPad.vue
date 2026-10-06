<script setup lang="ts">
import { onBeforeUnmount, reactive, ref } from 'vue'
import { t } from '../i18n'
import type { MouseButton, RemoteInput } from '../input'

// `height` overrides the surface's own size, e.g. to fit above the phone's keyboard; `compact`
// drops the buttons to make room.
const props = defineProps<{ input: RemoteInput; disabled: boolean; height?: number | null; compact?: boolean }>()

// Pointer speed: slow strokes stay precise, quick flicks cross the screen.
const BASE_GAIN = 1.4
const MAX_ACCEL = 3
// Two-finger scroll, in wheel units (120 = one notch) per pixel, sent in quarter notches:
// older apps treat every wheel message as a full notch.
const SCROLL_GAIN = 3
const WHEEL_STEP = 30
const TAP_MS = 250
const MULTI_TAP_MS = 350
const TAP_SLOP = 10
// A second touch this soon after a tap, near it, holds the button down: tap and drag.
const DRAG_GAP_MS = 300
const DRAG_RADIUS = 48

type Gesture = { start: number; fingers: number; travel: number; dragging: boolean }

const pad = ref<HTMLElement>()
const pointers = reactive(new Map<number, { x: number; y: number }>())
const dragging = ref(false)
let gesture: Gesture | null = null
let lastTap = { time: -Infinity, x: 0, y: 0 }
let lastMove = 0
const pending = { dx: 0, dy: 0, wy: 0, wx: 0 }
let frame = 0

// Moves are added up and sent once per frame, keeping the fractions for the next one.
function flush() {
  cancelAnimationFrame(frame)
  frame = 0
  const dx = Math.trunc(pending.dx)
  const dy = Math.trunc(pending.dy)
  if (dx || dy) {
    props.input.move(dx, dy)
    pending.dx -= dx
    pending.dy -= dy
  }
  const wy = Math.trunc(pending.wy / WHEEL_STEP) * WHEEL_STEP
  const wx = Math.trunc(pending.wx / WHEEL_STEP) * WHEEL_STEP
  if (wy || wx) {
    props.input.wheel(wy, wx)
    pending.wy -= wy
    pending.wx -= wx
  }
}
const schedule = () => frame || (frame = requestAnimationFrame(flush))

function onDown(e: PointerEvent) {
  if (props.disabled) return
  pad.value?.setPointerCapture(e.pointerId)
  pointers.set(e.pointerId, { x: e.clientX, y: e.clientY })
  lastMove = e.timeStamp
  if (pointers.size === 1) {
    const drag = e.timeStamp - lastTap.time < DRAG_GAP_MS && Math.hypot(e.clientX - lastTap.x, e.clientY - lastTap.y) < DRAG_RADIUS
    gesture = { start: e.timeStamp, fingers: 1, travel: 0, dragging: drag }
    if (drag) {
      lastTap.time = -Infinity
      dragging.value = true
      props.input.down('l')
    }
  } else if (gesture) {
    gesture.fingers = Math.max(gesture.fingers, pointers.size)
    pending.wx = pending.wy = 0
  }
}

function onMove(e: PointerEvent) {
  const p = pointers.get(e.pointerId)
  if (!p || !gesture) return
  const dx = e.clientX - p.x
  const dy = e.clientY - p.y
  p.x = e.clientX
  p.y = e.clientY

  if (gesture.fingers === 1) {
    const distance = Math.hypot(dx, dy)
    gesture.travel += distance
    const speed = distance / Math.max(1, e.timeStamp - lastMove)
    lastMove = e.timeStamp
    const gain = BASE_GAIN * Math.min(MAX_ACCEL, 0.6 + speed * 0.9)
    pending.dx += dx * gain
    pending.dy += dy * gain
  } else if (pointers.size === 2) {
    // Each finger reports on its own: half of each is the average of the two. Content follows
    // the fingers, as on the phone: dragging down scrolls up.
    gesture.travel += Math.hypot(dx, dy) / 2
    pending.wy += (dy / 2) * SCROLL_GAIN
    pending.wx -= (dx / 2) * SCROLL_GAIN
  } else {
    gesture.travel += Math.hypot(dx, dy)
  }
  schedule()
}

function onUp(e: PointerEvent, cancelled = false) {
  if (!pointers.delete(e.pointerId) || !gesture || pointers.size > 0) return
  const g = gesture
  gesture = null
  flush()
  pending.wx = pending.wy = 0

  if (g.dragging) {
    dragging.value = false
    props.input.up('l')
    return
  }
  const quick = e.timeStamp - g.start < (g.fingers === 1 ? TAP_MS : MULTI_TAP_MS)
  if (cancelled || !quick || g.travel > TAP_SLOP * g.fingers) return
  const button: MouseButton = g.fingers === 1 ? 'l' : g.fingers === 2 ? 'r' : 'm'
  props.input.click(button)
  if (button === 'l') lastTap = { time: e.timeStamp, x: e.clientX, y: e.clientY }
}

// Physical-style buttons under the pad: held for as long as the finger stays on them.
const held = reactive(new Set<MouseButton>())
function press(b: MouseButton, e: PointerEvent) {
  if (props.disabled) return
  ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
  held.add(b)
  props.input.down(b)
}
function release(b: MouseButton) {
  if (held.delete(b)) props.input.up(b)
}

onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  if (dragging.value) props.input.up('l')
  held.forEach((b) => props.input.up(b))
})

const dots = () => {
  const rect = pad.value?.getBoundingClientRect()
  return rect ? [...pointers.entries()].map(([id, p]) => ({ id, x: p.x - rect.left, y: p.y - rect.top })) : []
}
</script>

<template>
  <div class="touchpad card" :class="{ disabled }">
    <div
      ref="pad"
      class="surface"
      :style="height ? { blockSize: `${height}px` } : undefined"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointercancel="(e) => onUp(e, true)"
      @contextmenu.prevent
    >
      <slot v-if="disabled" name="disabled" />
      <template v-else>
        <ul class="hints" aria-hidden="true">
          <li><span class="shape circle" />{{ t.control.pad.move }}</li>
          <li><span class="shape square" />{{ t.control.pad.scroll }}</li>
          <li><span class="shape triangle" />{{ t.control.pad.drag }}</li>
        </ul>
        <span v-if="dragging" class="drag-pill">{{ t.control.pad.dragging }}</span>
      </template>
      <span v-for="d in dots()" :key="d.id" class="dot" :style="{ transform: `translate(${d.x}px, ${d.y}px)` }" />
    </div>
    <div v-if="!compact" class="buttons">
      <button
        :class="{ on: held.has('l') }"
        :disabled="disabled"
        @pointerdown="(e) => press('l', e)"
        @pointerup="release('l')"
        @pointercancel="release('l')"
        @contextmenu.prevent
      >{{ t.control.pad.left }}</button>
      <button
        :class="{ on: held.has('r') }"
        :disabled="disabled"
        @pointerdown="(e) => press('r', e)"
        @pointerup="release('r')"
        @pointercancel="release('r')"
        @contextmenu.prevent
      >{{ t.control.pad.right }}</button>
    </div>
  </div>
</template>

<style scoped>
.touchpad { display: flex; flex-direction: column; padding: 6px; }
.surface {
  position: relative;
  display: grid;
  place-items: center;
  block-size: clamp(14rem, 46dvh, 26rem);
  overflow: hidden;
  border-radius: var(--r-inner);
  background: var(--well);
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}
.hints { display: grid; gap: var(--size-2); padding: var(--size-4); font-size: 0.71875rem; font-weight: 650; color: var(--ink-2); pointer-events: none; }
.hints li { display: flex; align-items: center; gap: var(--size-2); }
.shape { inline-size: 10px; block-size: 10px; flex-shrink: 0; }
.circle { border-radius: var(--radius-round); background: var(--accent); }
.square { border-radius: 2px; background: var(--wait); }
.triangle { clip-path: polygon(50% 0, 100% 100%, 0 100%); background: var(--run); }
.drag-pill {
  position: absolute;
  inset: var(--size-2) auto auto 50%;
  translate: -50% 0;
  padding: 4px 10px;
  border-radius: var(--radius-round);
  background: var(--wait);
  color: #fff;
  font-size: 0.65625rem;
  font-weight: 750;
  pointer-events: none;
}
.dot {
  position: absolute;
  inset: 0 auto auto 0;
  inline-size: 44px;
  block-size: 44px;
  margin: -22px 0 0 -22px;
  border-radius: var(--radius-round);
  background: color-mix(in srgb, var(--accent) 22%, transparent);
  box-shadow: inset 0 0 0 2px color-mix(in srgb, var(--accent) 55%, transparent);
  pointer-events: none;
}

.buttons { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin-top: 6px; }
.buttons button {
  block-size: 3.25rem;
  border-radius: var(--r-inner);
  background: var(--well);
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--ink-2);
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
  touch-action: none;
  transition: background 100ms, color 100ms;
}
.buttons button.on { background: var(--ink); color: var(--bg); }
.buttons button:disabled { opacity: 0.45; }
.disabled .surface { touch-action: auto; }
</style>
