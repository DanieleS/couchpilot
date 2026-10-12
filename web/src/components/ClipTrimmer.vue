<script setup lang="ts">
import { DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle, VisuallyHidden } from 'reka-ui'
import { computed, ref, watch } from 'vue'
import { api, captureFileUrl, captureLightUrl, captureStripUrl } from '../api'
import { clipLength } from '../captures'
import { t } from '../i18n'
import { useCouchPilot } from '../store'
import type { Capture } from '../types'
import AppIcon from './AppIcon.vue'

// Cut a piece out of a clip: two handles over a strip of its frames, the video looping what's
// between them, then the PC writes it as a shorter MP4 or a GIF next to the clip.
const props = defineProps<{ capture: Capture | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ made: [capture: Capture] }>()

const { error } = useCouchPilot()

const MIN_SECONDS = 0.5
const MAX_GIF_SECONDS = 20

const video = ref<HTMLVideoElement | null>(null)
const track = ref<HTMLElement | null>(null)
// The clip's length: what the PC read from the file, or what the player finds out.
const knownLength = ref<number | null>(null)
const length = computed(() => props.capture?.seconds ?? knownLength.value ?? 0)
const from = ref(0)
const to = ref(0)
const format = ref<'mp4' | 'gif'>('mp4')
const audio = ref(true)
const working = ref(false)
const stripFailed = ref(false)

// The light copy when the PC has made it (the viewer asked): the loop seeks back every few
// seconds, which the original, tens of Mbit/s, keeps buffering on. Null while the PC is asked.
const lightReady = ref<boolean | null>(null)
const file = computed(() => {
  const c = props.capture
  if (!c || lightReady.value === null) return null
  return (lightReady.value && captureLightUrl(c)) || captureFileUrl(c)
})
const strip = computed(() => (props.capture && !stripFailed.value ? captureStripUrl(props.capture) : null))
const selected = computed(() => to.value - from.value)
const tooLongForGif = computed(() => format.value === 'gif' && selected.value > MAX_GIF_SECONDS)
const canMake = computed(() => !working.value && selected.value >= MIN_SECONDS && !tooLongForGif.value)

// A fresh clip each time the trimmer opens: the whole of it selected.
watch([open, () => props.capture?.id], ([o]) => {
  if (!o) return
  knownLength.value = null
  stripFailed.value = false
  from.value = 0
  to.value = props.capture?.seconds ?? 0
  format.value = 'mp4'
  audio.value = true
  working.value = false
  lightReady.value = null
  const id = props.capture?.id
  if (!id) return
  api.captureLight(id)
    .then((l) => l.state === 'ready', () => false)
    .then((ready) => {
      if (open.value && props.capture?.id === id) lightReady.value = ready
    })
})
watch(length, (l) => {
  if (to.value === 0 || to.value > l) to.value = l
})

const onLoaded = () => {
  const v = video.value
  if (v && isFinite(v.duration)) knownLength.value = v.duration
}
// The player loops what's selected, so you see the cut as it'll be.
const onTime = () => {
  const v = video.value
  if (!v || dragging.value) return
  if (v.currentTime >= to.value || v.currentTime < from.value - 0.3) v.currentTime = from.value
}
function play() {
  const v = video.value
  if (!v) return
  v.currentTime = from.value
  v.play().catch(() => {})
}

// Dragging a handle shows that frame; letting go plays the selection from its start.
type Handle = 'from' | 'to' | 'both'
const dragging = ref<Handle | null>(null)
let dragStartX = 0
let dragFrom = 0
let dragTo = 0
const clock = (s: number) => clipLength(s)
const pct = (s: number) => (length.value ? `${(s / length.value) * 100}%` : '0%')

function pointerDown(e: PointerEvent, handle: Handle) {
  if (!length.value || working.value) return
  dragging.value = handle
  dragStartX = e.clientX
  dragFrom = from.value
  dragTo = to.value
  ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
  video.value?.pause()
}
function pointerMove(e: PointerEvent) {
  const h = dragging.value
  const t = track.value
  if (!h || !t) return
  const dx = ((e.clientX - dragStartX) / t.clientWidth) * length.value
  const clamp = (s: number) => Math.max(0, Math.min(length.value, s))
  if (h === 'from') from.value = Math.min(clamp(dragFrom + dx), to.value - MIN_SECONDS)
  else if (h === 'to') to.value = Math.max(clamp(dragTo + dx), from.value + MIN_SECONDS)
  else {
    const span = dragTo - dragFrom
    const start = Math.max(0, Math.min(length.value - span, dragFrom + dx))
    from.value = start
    to.value = start + span
  }
  const v = video.value
  if (v) v.currentTime = h === 'to' ? to.value : from.value
}
function pointerUp() {
  if (!dragging.value) return
  dragging.value = null
  play()
}
// Half a second at a time, for a cut the thumb can't quite make.
function nudge(handle: 'from' | 'to', by: number) {
  if (handle === 'from') from.value = Math.max(0, Math.min(to.value - MIN_SECONDS, from.value + by))
  else to.value = Math.min(length.value, Math.max(from.value + MIN_SECONDS, to.value + by))
  const v = video.value
  if (v) v.currentTime = handle === 'to' ? to.value : from.value
}

async function make() {
  const c = props.capture
  if (!c || !canMake.value) return
  working.value = true
  video.value?.pause()
  try {
    const made = await api.trimCapture(c.id, { from: round(from.value), to: round(to.value), format: format.value, audio: format.value === 'mp4' && audio.value })
    emit('made', made)
    open.value = false
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    working.value = false
  }
}
const round = (s: number) => Math.round(s * 1000) / 1000
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="trim-overlay" />
      <DialogContent class="trimmer" :aria-describedby="undefined" @escape-key-down="working && $event.preventDefault()" @pointer-down-outside="working && $event.preventDefault()">
        <VisuallyHidden>
          <DialogTitle>{{ t.captures.trimmer.title }}</DialogTitle>
          <DialogDescription>{{ t.captures.trimmer.description }}</DialogDescription>
        </VisuallyHidden>
        <header class="bar">
          <button class="round" :aria-label="t.captures.trimmer.close" :disabled="working" @click="open = false"><AppIcon name="x" :size="18" /></button>
          <p class="heading">{{ t.captures.trimmer.heading }}</p>
          <span class="round-space" />
        </header>

        <div class="stage">
          <video
            v-if="file"
            ref="video"
            :key="capture?.id"
            class="media"
            :src="file"
            muted
            playsinline
            autoplay
            preload="auto"
            @loadedmetadata="onLoaded"
            @timeupdate="onTime"
            @ended="play"
          />
          <div v-else class="media placeholder"><AppIcon name="play" :size="28" /></div>
          <p v-if="working" class="working"><span class="spinner" aria-hidden="true" />{{ t.captures.trimmer.working }}</p>
        </div>

        <section class="editor">
          <div class="times tabular">
            <span class="t"><b>{{ clock(from) }}</b></span>
            <span class="t mid">{{ clock(selected) }} {{ format === 'gif' ? t.captures.trimmer.ofGif : t.captures.trimmer.ofClip }}</span>
            <span class="t"><b>{{ clock(to) }}</b></span>
          </div>

          <div ref="track" class="track" :class="{ working }" @pointermove="pointerMove" @pointerup="pointerUp" @pointercancel="pointerUp">
            <img v-if="strip" class="strip" :src="strip" alt="" draggable="false" @error="stripFailed = true" />
            <div class="dim left" :style="{ width: pct(from) }" />
            <div class="dim right" :style="{ width: pct(length - to) }" />
            <div class="window" :style="{ left: pct(from), width: pct(selected) }" @pointerdown="pointerDown($event, 'both')" />
            <button class="handle start" :class="{ on: dragging === 'from' }" :style="{ left: pct(from) }" :aria-label="t.captures.trimmer.start" @pointerdown="pointerDown($event, 'from')"><i /></button>
            <button class="handle end" :class="{ on: dragging === 'to' }" :style="{ left: pct(to) }" :aria-label="t.captures.trimmer.end" @pointerdown="pointerDown($event, 'to')"><i /></button>
          </div>

          <div class="nudges">
            <span class="nudge-group">
              <button class="nudge" :aria-label="t.captures.trimmer.startEarlier" @click="nudge('from', -0.5)">−</button>
              <span>{{ t.captures.trimmer.startShort }}</span>
              <button class="nudge" :aria-label="t.captures.trimmer.startLater" @click="nudge('from', 0.5)">+</button>
            </span>
            <span class="nudge-group">
              <button class="nudge" :aria-label="t.captures.trimmer.endEarlier" @click="nudge('to', -0.5)">−</button>
              <span>{{ t.captures.trimmer.endShort }}</span>
              <button class="nudge" :aria-label="t.captures.trimmer.endLater" @click="nudge('to', 0.5)">+</button>
            </span>
          </div>

          <div class="options">
            <div class="seg" role="tablist" :aria-label="t.captures.trimmer.format">
              <button role="tab" :aria-selected="format === 'mp4'" :class="{ on: format === 'mp4' }" @click="format = 'mp4'">{{ t.captures.trimmer.clip }}</button>
              <button role="tab" :aria-selected="format === 'gif'" :class="{ on: format === 'gif' }" @click="format = 'gif'">GIF</button>
            </div>
            <label v-if="format === 'mp4'" class="audio">
              <input v-model="audio" type="checkbox" />
              <span class="box"><AppIcon name="check" :size="12" /></span>
              {{ t.captures.trimmer.withAudio }}
            </label>
            <p v-else class="gif-note">{{ t.captures.trimmer.gifNote(MAX_GIF_SECONDS) }}</p>
          </div>
          <p v-if="tooLongForGif" class="warn">{{ t.captures.trimmer.tooLong(MAX_GIF_SECONDS) }}</p>

          <button class="btn make" :disabled="!canMake" @click="make">
            <span v-if="working" class="spinner" aria-hidden="true" /><AppIcon v-else name="scissors" :size="17" />
            {{ working ? t.captures.trimmer.cutting : format === 'gif' ? t.captures.trimmer.makeGif : t.captures.trimmer.makeClip }}
          </button>
          <p class="foot">{{ t.captures.trimmer.foot }}</p>
        </section>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
/* Over the viewer (35, 36), under the dialogs it may show (40, 41). */
.trim-overlay { position: fixed; inset: 0; z-index: 37; background: #0d0d0f; }
.trimmer {
  position: fixed;
  inset: 0;
  z-index: 38;
  display: flex;
  flex-direction: column;
  padding: var(--safe-top) 0 var(--safe-bottom);
  background: #0d0d0f;
  color: #f3efe6;
  outline: none;
  animation: fade-in 160ms var(--ease-out-2);
}
.bar { display: flex; align-items: center; justify-content: space-between; padding: var(--size-2) var(--gutter) 0; }
.round { display: grid; place-items: center; inline-size: 2.5rem; block-size: 2.5rem; border-radius: var(--radius-round); background: rgb(255 255 255 / 0.1); color: #fff; }
.round:disabled { opacity: 0.4; }
.round-space { inline-size: 2.5rem; }
.heading { font-size: 0.875rem; font-weight: 800; }

.stage { position: relative; flex: 1; min-block-size: 0; display: grid; place-items: center; }
.media { max-inline-size: 100%; max-block-size: 100%; inline-size: 100%; block-size: auto; object-fit: contain; background: #000; }
.placeholder { display: grid; place-items: center; aspect-ratio: 16 / 9; color: rgb(255 255 255 / 0.4); }
.working {
  position: absolute;
  inset: auto var(--gutter) var(--size-3);
  display: flex;
  align-items: center;
  gap: var(--size-2);
  padding: 10px var(--size-3);
  border-radius: var(--r-inner);
  background: rgb(0 0 0 / 0.7);
  font-size: 0.8125rem;
  font-weight: 650;
  backdrop-filter: blur(8px);
}

.editor { padding: var(--size-3) var(--gutter) 0; }
.times { display: flex; justify-content: space-between; font-size: 0.75rem; color: rgb(243 239 230 / 0.6); }
.times b { color: #f3efe6; font-weight: 800; }
.mid { font-weight: 650; }

.track { position: relative; block-size: 4rem; margin-top: var(--size-2); border-radius: 10px; overflow: visible; background: #1e1e21; touch-action: none; user-select: none; }
.track.working { pointer-events: none; opacity: 0.6; }
.strip { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: fill; border-radius: 10px; pointer-events: none; }
.dim { position: absolute; top: 0; bottom: 0; background: rgb(13 13 15 / 0.7); pointer-events: none; }
.dim.left { left: 0; border-radius: 10px 0 0 10px; }
.dim.right { right: 0; border-radius: 0 10px 10px 0; }
.window { position: absolute; top: 0; bottom: 0; box-shadow: inset 0 3px 0 var(--run), inset 0 -3px 0 var(--run); cursor: grab; }
.handle {
  position: absolute;
  top: -6px;
  bottom: -6px;
  inline-size: 1.75rem;
  margin-left: -0.875rem;
  display: grid;
  place-items: center;
  touch-action: none;
}
.handle i { display: block; inline-size: 14px; block-size: 100%; border-radius: 7px; background: var(--run); box-shadow: 0 2px 8px rgb(0 0 0 / 0.5); }
.handle i::after { content: ''; display: block; inline-size: 2px; block-size: 1.25rem; margin: 0 auto; position: relative; top: calc(50% - 0.625rem); border-radius: 1px; background: rgb(0 0 0 / 0.5); }
.handle.on i { transform: scaleX(1.15); }

.nudges { display: flex; justify-content: space-between; margin-top: var(--size-3); font-size: 0.6875rem; font-weight: 700; color: rgb(243 239 230 / 0.6); }
.nudge-group { display: inline-flex; align-items: center; gap: 8px; }
.nudge { display: grid; place-items: center; inline-size: 2rem; block-size: 2rem; border-radius: var(--radius-round); background: rgb(255 255 255 / 0.1); color: #fff; font-size: 1rem; font-weight: 800; }

.options { display: flex; align-items: center; justify-content: space-between; gap: var(--size-3); margin-top: var(--size-4); }
.seg { display: grid; grid-template-columns: 1fr 1fr; gap: 3px; padding: 3px; border-radius: var(--r-button); background: rgb(255 255 255 / 0.08); }
.seg button { padding: 0.45rem 1rem; border-radius: 9px; font-size: 0.75rem; font-weight: 750; color: rgb(243 239 230 / 0.6); }
.seg button.on { background: #f3efe6; color: #141414; }
.audio { display: inline-flex; align-items: center; gap: 8px; font-size: 0.8125rem; font-weight: 650; }
.audio input { position: absolute; opacity: 0; inline-size: 1px; block-size: 1px; }
.audio .box { display: grid; place-items: center; inline-size: 1.25rem; block-size: 1.25rem; border-radius: 6px; box-shadow: inset 0 0 0 2px rgb(243 239 230 / 0.4); color: transparent; }
.audio input:checked + .box { background: var(--run); color: #141414; box-shadow: none; }
.gif-note { font-size: 0.6875rem; font-weight: 600; color: rgb(243 239 230 / 0.6); text-align: right; }
.warn { margin-top: var(--size-2); font-size: 0.75rem; font-weight: 650; color: #ffc600; }

.make { inline-size: 100%; margin-top: var(--size-4); padding-block: 0.9rem; background: #f3efe6; color: #141414; font-size: 0.9375rem; }
.foot { margin-top: var(--size-2); font-size: 0.6875rem; line-height: 1.4; text-align: center; color: rgb(243 239 230 / 0.5); }
</style>
