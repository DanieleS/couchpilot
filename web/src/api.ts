import { ref } from 'vue'
import type { AudioChannel } from './protocol'
import type { AudioOutput, Capture, Captures, Game, GameAchievements, GameDetail, GameStats, NowPlaying, PairedDevice, Preset, RecentAchievement, Stats, StatsRangeName, Status, TrimRequest } from './types'
import { mockCaptureArt, mockRequest } from './mock'

const useMock = import.meta.env.VITE_MOCK === '1'

// The PC wants a PIN from this phone before it answers anything: the pairing screen takes over
// until it has one. Set by a 401 here, or by the live socket closing with 1008.
export const unpaired = ref(false)

// What the PC answered when it refused; `code` says what's wrong when it can (see CaptureService.cs).
export class ApiError extends Error {
  constructor(message: string, readonly code: string | null, readonly attemptsLeft = 0) {
    super(message)
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (useMock) {
    return (mockRequest(method, path, body) as Promise<T>).catch((e) => {
      if ((e as ApiError).code === 'unpaired') unpaired.value = true
      throw e
    })
  }
  const res = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let message = res.statusText
    let code: string | null = null
    let attemptsLeft = 0
    try {
      const error = await res.json()
      message = error.error ?? message
      code = error.code ?? null
      attemptsLeft = error.attemptsLeft ?? 0
    } catch {}
    if (res.status === 401 && code === 'unpaired') unpaired.value = true
    throw new ApiError(message, code, attemptsLeft)
  }
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  status: () => request<Status>('GET', '/status'),
  games: () => request<Game[]>('GET', '/games'),
  game: (id: string) => request<GameDetail>('GET', `/games/${id}`),
  gameStats: (id: string) => request<GameStats>('GET', `/games/${id}/stats`),
  stats: (range: StatsRangeName, offset: number) => request<Stats>('GET', `/stats?range=${range}&offset=${offset}`),
  start: (id: string) => request<void>('POST', `/games/${id}/start`),
  stop: (id: string) => request<void>('POST', `/games/${id}/stop`),
  install: (id: string) => request<void>('POST', `/games/${id}/install`),
  uninstall: (id: string) => request<void>('POST', `/games/${id}/uninstall`),
  focus: (id: string) => request<void>('POST', `/games/${id}/focus`),
  savePreset: (name: string) => request<Preset>('POST', '/displays/presets', { name }),
  renamePreset: (id: string, name: string) => request<void>('PATCH', `/displays/presets/${id}`, { name }),
  deletePreset: (id: string) => request<void>('DELETE', `/displays/presets/${id}`),
  applyPreset: (id: string) => request<void>('POST', `/displays/presets/${id}/apply`),
  setPresetAudio: (id: string, output: AudioOutput | null) =>
    request<void>('PUT', `/displays/presets/${id}/audio`, { outputId: output?.id ?? null, outputName: output?.name ?? null }),
  audioOutputs: () => request<AudioOutput[]>('GET', '/audio/outputs'),
  media: () => request<NowPlaying | null>('GET', '/media'),
  mediaPlayPause: () => request<void>('POST', '/media/play-pause'),
  mediaNext: () => request<void>('POST', '/media/next'),
  mediaPrevious: () => request<void>('POST', '/media/previous'),
  captures: () => request<Captures>('GET', '/captures'),
  // These answer once the file is on the PC's disk, with the new capture.
  screenshot: () => request<Capture>('POST', '/captures/screenshot'),
  saveReplay: () => request<Capture>('POST', '/captures/replay'),
  startRecording: () => request<void>('POST', '/captures/record/start'),
  stopRecording: () => request<Capture>('POST', '/captures/record/stop'),
  deleteCapture: (id: string) => request<void>('DELETE', `/captures/${id}`),
  // Answers once ffmpeg is done, with the new clip or GIF.
  trimCapture: (id: string, body: TrimRequest) => request<Capture>('POST', `/captures/${id}/trim`, body),
  // Pairing: these work before the phone is paired.
  pairing: () => request<{ required: boolean; device: PairedDevice | null }>('GET', '/pair'),
  requestPin: (name: string) => request<{ expiresAt: string; seconds: number }>('POST', '/pair/request', { name }),
  pair: (pin: string, name: string) => request<PairedDevice>('POST', '/pair', { pin, name }),
  unpair: () => request<void>('DELETE', '/pair'),
  // Achievements, from SuccessStory; 404 for a game it has nothing on.
  achievements: (id: string) => request<GameAchievements>('GET', `/games/${id}/achievements`),
  recentAchievements: () => request<RecentAchievement[]>('GET', '/achievements/recent'),
  confirmDisplays: () => request<void>('POST', '/displays/confirm'),
  revertDisplays: () => request<void>('POST', '/displays/revert'),
}

export const imageUrl = (id: string, kind: 'cover' | 'background') =>
  useMock ? null : `/api/games/${id}/${kind}`

// A game shows its Playnite icon, any other app (or a game without one) its executable's.
export const audioAppIconUrls = (app: AudioChannel) =>
  useMock
    ? []
    : [app.gameId && `/api/games/${app.gameId}/icon`, app.hasIcon && app.key && `/api/audio/apps/${app.key}/icon`].filter((u): u is string => !!u)

export const mediaArtUrl = (version: string) => (useMock ? null : `/api/media/art?v=${encodeURIComponent(version)}`)

export const captureThumbUrl = (c: Capture) => (useMock ? mockCaptureArt(c) : `/api/captures/${c.id}/thumb?v=${c.version}`)
export const captureFileUrl = (c: Capture) => (useMock ? (c.kind === 'photo' ? mockCaptureArt(c) : null) : `/api/captures/${c.id}/file?v=${c.version}`)
// What the viewer shows of a screenshot: a JPEG sized for the screen, the file itself staying for
// saving and sharing. Clips and GIFs have none (a GIF's still wouldn't move).
export const capturePreviewUrl = (c: Capture) =>
  c.kind !== 'photo' || /\.gif$/i.test(c.fileName) ? null : useMock ? mockCaptureArt(c) : `/api/captures/${c.id}/preview?v=${c.version}`
// The trimmer's strip of frames across a clip.
export const captureStripUrl = (c: Capture) => (useMock ? mockCaptureArt(c) : `/api/captures/${c.id}/strip?v=${c.version}`)
