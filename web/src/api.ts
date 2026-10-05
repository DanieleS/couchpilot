import type { AudioChannel } from './protocol'
import type { Game, GameDetail, Preset, Status } from './types'
import { mockRequest } from './mock'

const useMock = import.meta.env.VITE_MOCK === '1'

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (useMock) return mockRequest(method, path, body) as Promise<T>
  const res = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let message = res.statusText
    try {
      message = (await res.json()).error ?? message
    } catch {}
    throw new Error(message)
  }
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  status: () => request<Status>('GET', '/status'),
  games: () => request<Game[]>('GET', '/games'),
  game: (id: string) => request<GameDetail>('GET', `/games/${id}`),
  start: (id: string) => request<void>('POST', `/games/${id}/start`),
  stop: (id: string) => request<void>('POST', `/games/${id}/stop`),
  focus: (id: string) => request<void>('POST', `/games/${id}/focus`),
  savePreset: (name: string) => request<Preset>('POST', '/displays/presets', { name }),
  renamePreset: (id: string, name: string) => request<void>('PATCH', `/displays/presets/${id}`, { name }),
  deletePreset: (id: string) => request<void>('DELETE', `/displays/presets/${id}`),
  applyPreset: (id: string) => request<void>('POST', `/displays/presets/${id}/apply`),
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
