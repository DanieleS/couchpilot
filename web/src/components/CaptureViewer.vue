<script setup lang="ts">
import { DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle, VisuallyHidden } from 'reka-ui'
import { computed, nextTick, ref, watch } from 'vue'
import { captureFileUrl, capturePreviewUrl, captureThumbUrl, imageUrl } from '../api'
import { clipLength, dayLabel, fileSize, isGif, playDayKey, toolName } from '../captures'
import { locale, t } from '../i18n'
import { useCouchPilot } from '../store'
import type { Capture } from '../types'
import { placeholderGradient } from '../utils'
import AppIcon from './AppIcon.vue'
import CaptureThumb from './CaptureThumb.vue'

// One capture at a time, full screen: swipe or tap the strip for the others, share it from the
// phone, save it, or send it to the PC's Recycle Bin.
const props = defineProps<{ items: Capture[] }>()
const open = defineModel<boolean>('open', { required: true })
const index = defineModel<number>('index', { required: true })
const emit = defineEmits<{ remove: [capture: Capture]; trim: [capture: Capture] }>()

const { games, error, status } = useCouchPilot()
// Clips can be cut when the PC has ffmpeg, and this phone may see captures at all.
const canTrim = computed(() => !!status.value?.capture?.editing)
const current = computed(() => props.items[index.value] ?? null)
const file = computed(() => (current.value ? captureFileUrl(current.value) : null))

// A screenshot shows as its preview, a fraction of the PNG's weight, over its thumbnail while it
// loads; the file itself only when the PC has no preview to give. Save and share keep the file.
const previewFailed = ref<string | null>(null)
const shown = computed(() => {
  const c = current.value
  if (!c || c.kind === 'video') return null
  return (previewFailed.value !== c.id && capturePreviewUrl(c)) || file.value
})
const stillStyle = computed(() => {
  const c = current.value
  if (!c) return undefined
  return {
    backgroundImage: `url("${captureThumbUrl(c)}")`,
    aspectRatio: c.width && c.height ? `${c.width} / ${c.height}` : undefined,
  }
})
const onPreviewError = () => {
  if (current.value && shown.value !== file.value) previewFailed.value = current.value.id
}
// The screenshots either side are fetched ahead, so a swipe lands on a picture already there.
watch([index, open], () => {
  if (!open.value) return
  for (const c of [props.items[index.value + 1], props.items[index.value - 1]]) {
    const url = c && capturePreviewUrl(c)
    if (url) new Image().src = url
  }
}, { immediate: true })

const go = (to: number) => {
  if (to >= 0 && to < props.items.length) index.value = to
}

// A horizontal swipe on the picture moves to the next one; anything else is the player's.
let startX = 0
let startY = 0
const touchStart = (e: TouchEvent) => {
  startX = e.touches[0].clientX
  startY = e.touches[0].clientY
}
const touchEnd = (e: TouchEvent) => {
  const dx = e.changedTouches[0].clientX - startX
  const dy = e.changedTouches[0].clientY - startY
  if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) go(index.value + (dx < 0 ? 1 : -1))
}

const cover = computed(() => {
  const id = current.value?.gameId
  const g = id ? games.value?.find((x) => x.id === id) : null
  return g?.hasCover ? imageUrl(g.id, 'cover') : null
})
const meta = computed(() => {
  const c = current.value
  if (!c) return ''
  const day = dayLabel(playDayKey(c.takenAt))
  const time = new Date(c.takenAt).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })
  return [
    t.captures.viewer.at(day, time),
    c.kind === 'video' ? (c.replay ? t.captures.viewer.replayOf(clipLength(c.seconds)) : clipLength(c.seconds)) : isGif(c) ? 'GIF' : null,
    c.width && c.height ? `${c.width}×${c.height}` : null,
    fileSize(c.bytes),
    c.edited ? t.captures.viewer.cutFromClip : toolName(c.tool),
  ].filter(Boolean).join(' · ')
})

// Sharing hands the file itself to the phone's apps (WhatsApp, Telegram, the gallery), which
// takes HTTPS: over plain HTTP the button saves the file instead.
const canShare = typeof navigator.canShare === 'function'
const sharing = ref(false)
async function share() {
  const c = current.value
  if (!c || !file.value) return
  sharing.value = true
  try {
    const blob = await (await fetch(file.value)).blob()
    const shared = new File([blob], c.fileName, { type: blob.type || (c.kind === 'video' ? 'video/mp4' : isGif(c) ? 'image/gif' : 'image/png') })
    if (!navigator.canShare({ files: [shared] })) throw new Error(t.captures.viewer.cantShare)
    await navigator.share({ files: [shared], title: c.game ?? c.fileName })
  } catch (e) {
    if ((e as Error).name !== 'AbortError') error.value = (e as Error).message
  } finally {
    sharing.value = false
  }
}

// The strip shows the captures around this one, and follows along.
const STRIP = 30
const stripFrom = computed(() => Math.max(0, index.value - STRIP))
const stripItems = computed(() => props.items.slice(stripFrom.value, index.value + STRIP + 1))
const strip = ref<HTMLElement | null>(null)
watch([index, open], async () => {
  await nextTick()
  strip.value?.querySelector<HTMLElement>('.frame.on')?.scrollIntoView({ inline: 'center', block: 'nearest', behavior: 'smooth' })
})
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="viewer-overlay" />
      <DialogContent class="viewer" :aria-describedby="undefined">
        <VisuallyHidden>
          <DialogTitle>{{ current?.game ?? t.captures.viewer.capture }}</DialogTitle>
          <DialogDescription>{{ meta }}</DialogDescription>
        </VisuallyHidden>
        <header class="bar">
          <button class="round" :aria-label="t.captures.viewer.close" @click="open = false"><AppIcon name="x" :size="18" /></button>
          <p class="count tabular">{{ t.captures.viewer.position(index + 1, items.length) }}</p>
          <span class="round-space" />
        </header>

        <div v-if="current" class="stage" @touchstart.passive="touchStart" @touchend="touchEnd">
          <video
            v-if="current.kind === 'video' && file"
            :key="current.id"
            class="media"
            :src="file"
            :poster="captureThumbUrl(current) ?? undefined"
            controls
            playsinline
            preload="metadata"
          />
          <img v-else-if="shown" :key="current.id" class="media still" :src="shown" :style="stillStyle" :alt="current.game ?? ''" @error="onPreviewError" />
          <CaptureThumb v-else class="media" :capture="current" />
          <button v-if="index > 0" class="step prev" :aria-label="t.captures.viewer.prev" @click="go(index - 1)"><AppIcon name="chevron-left" :size="20" /></button>
          <button v-if="index < items.length - 1" class="step next" :aria-label="t.captures.viewer.next" @click="go(index + 1)"><AppIcon name="chevron-right" :size="20" /></button>
        </div>

        <section v-if="current" class="info">
          <div class="who">
            <span class="cover" :style="cover ? undefined : { background: placeholderGradient(current.game ?? '?', 48, 20) }">
              <img v-if="cover" :src="cover" alt="" />
            </span>
            <div class="text">
              <h2 class="game truncate">{{ current.game ?? t.captures.viewer.other }}</h2>
              <p class="meta">{{ meta }}</p>
            </div>
          </div>
          <div class="actions">
            <button v-if="canShare" class="btn share" :disabled="sharing || !file" @click="share">
              <span v-if="sharing" class="spinner" aria-hidden="true" /><AppIcon v-else name="share" :size="17" />{{ sharing ? t.captures.viewer.preparing : t.captures.viewer.share }}
            </button>
            <button v-if="current.kind === 'video' && canTrim" class="btn small" @click="emit('trim', current)"><AppIcon name="scissors" :size="17" />{{ t.captures.viewer.trim }}</button>
            <a v-if="file" class="btn small" :href="file" :download="current.fileName"><AppIcon name="download" :size="17" />{{ t.captures.viewer.save }}</a>
            <button class="btn small danger" @click="emit('remove', current)"><AppIcon name="trash" :size="17" />{{ t.captures.viewer.delete }}</button>
          </div>
        </section>

        <div ref="strip" class="strip">
          <button
            v-for="(c, i) in stripItems"
            :key="c.id"
            class="frame"
            :class="{ on: stripFrom + i === index }"
            :aria-label="t.captures.viewer.frame(stripFrom + i + 1)"
            @click="go(stripFrom + i)"
          >
            <CaptureThumb :capture="c" />
          </button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
/* Over the tab bar (30), under the sheets and dialogs (40, 41) it opens, like the delete confirm. */
.viewer-overlay { position: fixed; inset: 0; z-index: 35; background: #0d0d0f; }
.viewer {
  position: fixed;
  inset: 0;
  z-index: 36;
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
.round-space { inline-size: 2.5rem; }
.count { font-size: 0.8125rem; font-weight: 700; color: rgb(243 239 230 / 0.7); }

.stage { position: relative; flex: 1; min-block-size: 0; display: grid; place-items: center; }
.media { max-inline-size: 100%; max-block-size: 100%; inline-size: 100%; object-fit: contain; background: #000; }
img.media, video.media { block-size: auto; }
/* The thumbnail under the preview, laid out like it, until the preview covers it. */
.still { background: #000 center / contain no-repeat; }
.step {
  position: absolute;
  top: 50%;
  display: grid;
  place-items: center;
  inline-size: 2.25rem;
  block-size: 2.25rem;
  margin-top: -1.125rem;
  border-radius: var(--radius-round);
  background: rgb(0 0 0 / 0.45);
  color: #fff;
}
.prev { left: 8px; }
.next { right: 8px; }

.info { padding: var(--size-3) var(--gutter) 0; }
.who { display: flex; align-items: center; gap: var(--size-3); }
.cover { position: relative; inline-size: 2.5rem; aspect-ratio: 3 / 4; border-radius: 6px; overflow: hidden; flex-shrink: 0; }
.cover img { position: absolute; inset: 0; inline-size: 100%; block-size: 100%; object-fit: cover; }
.text { min-width: 0; }
.game { font-size: 1.125rem; font-weight: 800; line-height: 1.15; }
.meta { margin-top: 4px; font-size: 0.75rem; line-height: 1.35; color: rgb(243 239 230 / 0.6); }
.actions { display: flex; gap: var(--size-2); margin-top: var(--size-3); }
.actions > * { flex: 1; }
.actions > .share { flex: 2; }
.actions .btn { text-decoration: none; }
.share { background: #f3efe6; color: #141414; font-size: 0.875rem; }
.small { flex-direction: column; gap: 2px; padding: 0.5rem; background: rgb(255 255 255 / 0.1); color: #fff; font-size: 0.6875rem; }
.danger { background: rgb(255 59 78 / 0.16); color: #ff6b78; }

.strip { display: flex; gap: 4px; overflow-x: auto; padding: var(--size-4) var(--gutter) var(--size-3); scrollbar-width: none; }
.strip::-webkit-scrollbar { display: none; }
.frame { flex-shrink: 0; inline-size: 4rem; border-radius: 6px; overflow: hidden; opacity: 0.5; transition: opacity 150ms; }
.frame.on { opacity: 1; box-shadow: 0 0 0 2px var(--run); }
</style>
