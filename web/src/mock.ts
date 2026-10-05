// In-memory stand-in for the plugin's API, for `pnpm dev:mock` on a machine without Playnite.
import type { AudioApp, AudioDevice, AudioState, Game, Monitor, Preset, RunningGame, Status, VolumeChange } from './types'

const names: [string, string, number][] = [
  ['Elden Ring', 'Steam', 142], ['Warhammer 40,000: Space Marine 2', 'Steam', 31], ['Hades II', 'Epic', 54],
  ['Forza Horizon 5', 'Xbox', 88], ["Baldur's Gate 3", 'GOG', 210], ['Hollow Knight: Silksong', 'Steam', 19],
  ['Cyberpunk 2077', 'GOG', 97], ['Balatro', 'Steam', 64], ['Clair Obscur: Expedition 33', 'Epic', 12],
  ['Celeste', 'Steam', 23], ['Disco Elysium', 'GOG', 41], ['Sea of Thieves', 'Xbox', 76],
  ['Outer Wilds', 'Epic', 18], ['Monster Hunter Wilds', 'Steam', 120], ['Stardew Valley', 'Steam', 300],
]

const games: Game[] = names.map(([name, source, hours], i) => ({
  id: `game-${i}`,
  name,
  source,
  installed: i % 5 !== 4,
  favorite: i % 4 === 0,
  hidden: false,
  running: false,
  playtimeMinutes: hours * 60,
  lastActivity: i < 9 ? new Date(Date.now() - i * 86400000 * 1.7).toISOString() : null,
  added: new Date(Date.now() - i * 86400000 * 10).toISOString(),
  genres: ['Action'],
  releaseYear: 2015 + (i % 10),
  hasCover: false,
  hasBackground: false,
}))

const m = (name: string, x: number, y: number, width: number, height: number, refreshHz: number, virtual = false): Monitor => ({
  name, devicePath: name, x, y, width, height, refreshHz, primary: x === 0 && y === 0, virtual,
})
const presets: Preset[] = [
  { id: 'p1', name: 'Scrivania', monitors: [m('DELL U2723QE', -2560, 0, 2560, 1440, 60), m('ASUS PG27AQN', 0, 0, 2560, 1440, 240), m('DELL U2723QE ', 2560, 0, 2560, 1440, 60)] },
  { id: 'p2', name: 'Solo TV', monitors: [m('LG OLED C3', 0, 0, 3840, 2160, 120)] },
  { id: 'p3', name: 'TV + monitor', monitors: [m('ASUS PG27AQN', -2560, 400, 2560, 1440, 240), m('LG OLED C3', 0, 0, 3840, 2160, 120)] },
  { id: 'p4', name: 'Gaming 240 Hz', monitors: [m('ASUS PG27AQN', 0, 0, 2560, 1440, 240)] },
]

// `?stream=1` pretends a Vibepollo stream is running: its virtual display is the primary.
const streaming = new URLSearchParams(location.search).has('stream')
// `?noinput=1` pretends touchpad and keyboard are turned off in the extension settings.
const inputEnabled = !new URLSearchParams(location.search).has('noinput')
let current = streaming
  ? [m('Sunshine Virtual Display', 0, 0, 2532, 1170, 120, true), m('ASUS PG27AQN', 2532, 0, 2560, 1440, 240)]
  : presets[0].monitors
let previous: Monitor[] | null = null
let pending: { presetId: string; previousPresetId: string | null; deadline: number } | null = null
const running: RunningGame[] = []

function status(): Status {
  if (pending && Date.now() > pending.deadline) {
    current = previous ?? current
    pending = null
  }
  return {
    pc: 'PC-GAMING',
    running,
    input: inputEnabled,
    displays: {
      monitors: current,
      presets,
      activePresetId: presets.find((p) => p.monitors === current)?.id ?? null,
      streamActive: current.some((x) => x.virtual),
      pending: pending && { presetId: pending.presetId, previousPresetId: pending.previousPresetId, secondsLeft: Math.ceil((pending.deadline - Date.now()) / 1000) },
    },
  }
}

const outputs: AudioDevice[] = [
  { id: 'out-tv', name: 'LG OLED C3', detail: 'NVIDIA High Definition Audio', kind: 'display' },
  { id: 'out-hp', name: 'Arctis Nova Pro', detail: 'SteelSeries Arctis Nova Pro', kind: 'headphones' },
  { id: 'out-mon', name: 'ASUS PG27AQN', detail: 'NVIDIA High Definition Audio', kind: 'display' },
  { id: 'out-spk', name: 'Altoparlanti', detail: 'Realtek(R) Audio', kind: 'speakers' },
]
const inputs: AudioDevice[] = [
  { id: 'in-yeti', name: 'Blue Yeti', detail: 'Yeti Stereo Microphone', kind: 'mic' },
  { id: 'in-hp', name: 'Arctis Nova Pro Mic', detail: 'SteelSeries Arctis Nova Pro', kind: 'headphones' },
]
const endpoints: Record<string, { volume: number; muted: boolean }> = Object.fromEntries(
  [...outputs, ...inputs].map((d) => [d.id, { volume: 62, muted: false }]),
)
// Volume and mute of running games, which come and go.
const gameAudio: Record<string, VolumeChange> = {}
let defaultOutput = 'out-tv'
let defaultInput = 'in-yeti'
const apps: AudioApp[] = [
  { id: 'app-spotify', name: 'Spotify', volume: 35, muted: false, peak: 0, system: false, gameId: null, hasIcon: false },
  { id: 'app-discord', name: 'Discord', volume: 100, muted: true, peak: 0, system: false, gameId: null, hasIcon: false },
  { id: 'app-chrome', name: 'Google Chrome', volume: 80, muted: false, peak: 0, system: false, gameId: null, hasIcon: false },
  { id: 'system', name: 'Suoni di sistema', volume: 50, muted: false, peak: 0, system: true, gameId: null, hasIcon: false },
]

// Whatever is running plays something; the levels wobble so the meters have something to show.
function audio(): AudioState {
  const wobble = (base: number) => Math.max(0, Math.min(1, base + (Math.random() - 0.5) * 0.3))
  const game: AudioApp[] = running.map((r) => ({
    id: `app-${r.id}`, name: r.name, volume: 100, muted: false, ...gameAudio[`app-${r.id}`],
    peak: wobble(0.6), system: false, gameId: r.id, hasIcon: false,
  }))
  const endpoint = (id: string, peak: number) => {
    const d = [...outputs, ...inputs].find((x) => x.id === id)!
    return { ...d, ...endpoints[id], peak }
  }
  return {
    output: endpoint(defaultOutput, wobble(0.5)),
    input: endpoint(defaultInput, 0),
    outputs,
    inputs,
    apps: [...game, ...apps.map((a) => ({ ...a, peak: a.id === 'app-spotify' ? wobble(0.4) : a.id === 'system' ? 0 : wobble(0.1) }))],
  }
}

// Hand out copies, like a real JSON response: shared references would hide changes from Vue.
export async function mockRequest(method: string, path: string, body?: any): Promise<unknown> {
  await new Promise((r) => setTimeout(r, 150))
  const result = await handle(method, path, body)
  return result === undefined ? undefined : structuredClone(result)
}

async function handle(method: string, path: string, body?: any): Promise<unknown> {
  const [, a, b, c] = path.split('/')
  if (method === 'GET' && a === 'status') return status()
  if (method === 'GET' && a === 'games' && !b) return games.map((g) => ({ ...g, running: running.some((r) => r.id === g.id) }))
  const game = games.find((g) => g.id === b)
  if (method === 'GET' && a === 'games' && game) return { ...game, description: 'Descrizione finta del gioco, quella vera arriva da Playnite.', developers: ['Studio'] }
  if (method === 'POST' && a === 'games' && game && c === 'start') {
    if (!game.installed) throw new Error('Il gioco non è installato.')
    running.push({ id: game.id, name: game.name, source: game.source, startedAt: new Date().toISOString(), hasCover: false, hasBackground: false })
    return
  }
  if (method === 'POST' && a === 'games' && c === 'stop') {
    setTimeout(() => running.splice(running.findIndex((r) => r.id === b), 1), 1500)
    return
  }
  if (method === 'POST' && a === 'games' && c === 'focus') return
  if (a === 'displays') {
    const preset = presets.find((p) => p.id === c)
    if (streaming && method === 'POST' && (path.endsWith('/apply') || (b === 'presets' && !c))) {
      throw new Error("C'è uno stream Vibepollo in corso: gli schermi li sta gestendo lui.")
    }
    if (method === 'POST' && b === 'presets' && !c) {
      const p = { id: `p${Date.now()}`, name: body?.name || `Preset ${presets.length + 1}`, monitors: current }
      presets.push(p)
      return p
    }
    if (method === 'PATCH' && preset) return void (preset.name = body.name)
    if (method === 'DELETE' && preset) return void presets.splice(presets.indexOf(preset), 1)
    if (method === 'POST' && preset && path.endsWith('/apply')) {
      const previousPresetId = pending ? pending.previousPresetId : (presets.find((p) => p.monitors === current)?.id ?? null)
      if (!pending) previous = current
      current = preset.monitors
      pending = { presetId: preset.id, previousPresetId, deadline: Date.now() + 15000 }
      return
    }
    if (b === 'confirm') return void (pending = null)
    if (b === 'revert') {
      current = previous ?? current
      pending = null
      return
    }
  }
  if (method === 'GET' && a === 'audio') return audio()
  if (method === 'POST' && a === 'audio' && b === 'default') {
    if (outputs.some((d) => d.id === body.id)) defaultOutput = body.id
    else defaultInput = body.id
    return
  }
  if (method === 'POST' && a === 'audio' && b === 'device') return void Object.assign(endpoints[body.id], strip(body))
  if (method === 'POST' && a === 'audio' && b === 'app') {
    const app = apps.find((x) => x.id === body.id)
    if (app) return void Object.assign(app, strip(body))
    if (!running.some((r) => `app-${r.id}` === body.id)) throw new Error("L'app non sta più usando l'audio.")
    return void (gameAudio[body.id] = { ...gameAudio[body.id], ...strip(body) })
  }
  throw new Error(`Mock: ${method} ${path} non gestito`)
}

const strip = ({ id, ...change }: { id: string; volume?: number; muted?: boolean }) => change
