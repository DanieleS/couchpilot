import type { AudioChannel } from './protocol'
import type { AudioOutput, Capture, Captures, Game, GameDetail, GameStats, NowPlaying, Preset, Stats, StatsRangeName, Status } from './types'
import { mockCaptureArt, mockRequest } from './mock'

const useMock = import.meta.env.VITE_MOCK === '1'

// What the PC answered when it refused; `code` says what's wrong when it can (see CaptureService.cs).
export class ApiError extends Error {
  constructor(message: string, readonly code: string | null) {
    super(message)
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (useMock) return mockRequest(method, path, body) as Promise<T>
  const res = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let message = res.statusText
    let code: string | null = null
    try {
      const error = await res.json()
      message = error.error ?? message
      code = error.code ?? null
    } catch {}
    throw new ApiError(message, code)
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
