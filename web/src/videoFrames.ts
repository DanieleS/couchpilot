import { reactive } from 'vue'
import { captureFileUrl } from './api'
import type { Capture } from './types'

// Thumbnails the phone makes itself, for clips the PC can't: Windows draws video thumbnails
// with its own codecs, and without the HEVC extension (or for AV1) it has none. Phones decode
// both in hardware. The frame is taken a little way in (the very first is often black), shown
// at once, and sent to the PC, which keeps it for next time and for the other phones.

// Object URLs of the frames made so far, by capture id and version.
export const madeFrames = reactive(new Map<string, string>())

const LONG_SIDE = 480
const TIMEOUT_MS = 15000
const tried = new Map<string, Promise<string | null>>()
let queue: Promise<unknown> = Promise.resolve()

const keyOf = (c: Capture) => `${c.id}-${c.version}`

export const frameOf = (c: Capture) => madeFrames.get(keyOf(c)) ?? null

// One clip at a time: a gallery full of them shouldn't open twenty videos at once.
export function makeFrame(c: Capture): Promise<string | null> {
  const key = keyOf(c)
  let made = tried.get(key)
  if (!made) {
    made = queue.then(() => grab(c)).catch(() => null)
    queue = made
    tried.set(key, made)
    made.then((url) => url && madeFrames.set(key, url))
  }
  return made
}

async function grab(c: Capture): Promise<string | null> {
  const src = captureFileUrl(c)
  if (!src || c.kind !== 'video') return null
  const video = document.createElement('video')
  video.muted = true
  video.playsInline = true
  video.preload = 'auto'
  try {
    video.src = src
    await event(video, 'loadeddata')
    if (!video.videoWidth) return null
    const at = Math.min(2, (Number.isFinite(video.duration) ? video.duration : c.seconds ?? 0) * 0.25)
    if (at > 0) {
      video.currentTime = at
      await event(video, 'seeked')
    }
    const scale = Math.min(1, LONG_SIDE / Math.max(video.videoWidth, video.videoHeight))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(video.videoWidth * scale)
    canvas.height = Math.round(video.videoHeight * scale)
    canvas.getContext('2d')!.drawImage(video, 0, 0, canvas.width, canvas.height)
    const jpeg = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.82))
    if (!jpeg) return null
    // The PC keeping it is a bonus: the phone shows it either way.
    fetch(`/api/captures/${c.id}/thumb`, { method: 'POST', headers: { 'Content-Type': 'image/jpeg' }, body: jpeg }).catch(() => {})
    return URL.createObjectURL(jpeg)
  } finally {
    // Let go of the connection and the decoder.
    video.removeAttribute('src')
    video.load()
  }
}

function event(video: HTMLVideoElement, name: 'loadeddata' | 'seeked') {
  return new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => done(new Error('timeout')), TIMEOUT_MS)
    const done = (error?: Error) => {
      clearTimeout(timer)
      video.removeEventListener(name, ok)
      video.removeEventListener('error', fail)
      if (error) reject(error)
      else resolve()
    }
    const ok = () => done()
    const fail = () => done(new Error('decode'))
    video.addEventListener(name, ok)
    video.addEventListener('error', fail)
  })
}
