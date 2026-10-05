// In-memory stand-in for the plugin's API, for `pnpm dev:mock` on a machine without Playnite.
import { decode, type ServerMessage } from './protocol'
import type { Game, Monitor, Preset, RunningGame, Status } from './types'

const names: [string, string, number][] = [
  ['Elden Ring', 'Steam', 142], ['Warhammer 40,000: Space Marine 2', 'Steam', 31], ['Hades II', 'Epic', 54],
  ['Forza Horizon 5', 'Xbox', 88], ["Baldur's Gate 3", 'GOG', 210], ['Hollow Knight: Silksong', 'Steam', 19],
  ['Cyberpunk 2077', 'GOG', 97], ['Balatro', 'Steam', 64], ['Clair Obscur: Expedition 33', 'Epic', 12],
  ['Celeste', 'Steam', 23], ['Disco Elysium', 'GOG', 41], ['Sea of Thieves', 'Xbox', 76],
  ['Outer Wilds', 'Epic', 18], ['Monster Hunter Wilds', 'Steam', 120], ['Stardew Valley', 'Steam', 300],
]

const games: Game[] = names.map(([name, source, hours], i) => ({
  // Shaped like Playnite's ids, which the live socket carries as 16 raw bytes.
  id: `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`,
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

// A pretend PC on the other end of the live socket (docs/protocol.md). It speaks the real byte
// format, mirroring LiveSocket.cs and AudioProtocol.cs, so the phone's decoder runs exactly as it
// would against Playnite.
type MockDevice = { id: string; name: string; detail: string; kind: number }
type MockChannel = { role: number; name: string; key?: string; gameId?: string; volume: number; muted: boolean; loudness: number }

const KIND = { speakers: 0, headphones: 1, display: 2, mic: 3 }
const ROLE = { output: 0, input: 1, app: 2, system: 3, game: 4 }
const outputs: MockDevice[] = [
  { id: 'out-tv', name: 'LG OLED C3', detail: 'NVIDIA High Definition Audio', kind: KIND.display },
  { id: 'out-hp', name: 'Arctis Nova Pro', detail: 'SteelSeries Arctis Nova Pro', kind: KIND.headphones },
  { id: 'out-mon', name: 'ASUS PG27AQN', detail: 'NVIDIA High Definition Audio', kind: KIND.display },
  { id: 'out-spk', name: 'Altoparlanti', detail: 'Realtek(R) Audio', kind: KIND.speakers },
]
const inputs: MockDevice[] = [
  { id: 'in-yeti', name: 'Blue Yeti', detail: 'Yeti Stereo Microphone', kind: KIND.mic },
  { id: 'in-hp', name: 'Arctis Nova Pro Mic', detail: 'SteelSeries Arctis Nova Pro', kind: KIND.headphones },
]
let defaultOutput = 0
let defaultInput = 0
const endpointAudio: Record<string, { volume: number; muted: boolean }> = Object.fromEntries(
  [...outputs, ...inputs].map((d) => [d.id, { volume: 62, muted: false }]),
)
const appAudio: Record<string, { volume: number; muted: boolean }> = {
  'app-spotify': { volume: 35, muted: false },
  'app-discord': { volume: 100, muted: true },
  'app-chrome': { volume: 80, muted: false },
  system: { volume: 50, muted: false },
}
const apps = [
  { key: 'app-discord', name: 'Discord', role: ROLE.app, loudness: 0.2 },
  { key: 'app-chrome', name: 'Google Chrome', role: ROLE.app, loudness: 0.1 },
  { key: 'app-spotify', name: 'Spotify', role: ROLE.app, loudness: 0.45 },
  { key: 'system', name: 'Suoni di sistema', role: ROLE.system, loudness: 0 },
]

// In the PC's order: output, input, the running games, then the apps.
function mockChannels(): MockChannel[] {
  const out = outputs[defaultOutput]
  const inp = inputs[defaultInput]
  return [
    { role: ROLE.output, name: out.name, ...endpointAudio[out.id], loudness: 0.55 },
    { role: ROLE.input, name: inp.name, ...endpointAudio[inp.id], loudness: 0 },
    ...running.map((r) => ({
      role: ROLE.game, name: r.name, key: `app-${r.id}`, gameId: r.id, loudness: 0.7,
      ...(appAudio[`app-${r.id}`] ??= { volume: 100, muted: false }),
    })),
    ...apps.map((a) => ({ ...a, ...appAudio[a.key] })),
  ]
}

const utf8 = new TextEncoder()
const str = (text: string) => {
  const bytes = utf8.encode(text).subarray(0, 255)
  return [bytes.length, ...bytes]
}
// Big-endian, in the order the text form reads.
const guidBytes = (id: string) => id.replace(/-/g, '').match(/../g)!.map((h) => parseInt(h, 16))

function encodeState(gen: number, channels: MockChannel[]) {
  const devices = (list: MockDevice[], current: number) =>
    [list.length, ...list.flatMap((d, i) => [d.kind, i === current ? 1 : 0, ...str(d.name), ...str(d.detail)])]
  return Uint8Array.from([
    0x82, gen,
    ...devices(outputs, defaultOutput),
    ...devices(inputs, defaultInput),
    channels.length,
    ...channels.flatMap((c) => [
      c.role, c.volume, c.muted ? 1 : 0, ...str(c.name),
      ...(c.role >= ROLE.app ? str(c.key!) : []),
      ...(c.role === ROLE.game ? guidBytes(c.gameId!) : []),
    ]),
  ])
}
const encodeVolumes = (gen: number, channels: MockChannel[]) =>
  Uint8Array.from([0x83, gen, ...channels.flatMap((c) => [c.volume, c.muted ? 1 : 0])])
const encodeLevels = (gen: number, channels: MockChannel[]) =>
  Uint8Array.from([0x84, gen, ...channels.map((c) => (c.muted || !c.loudness ? 0 : Math.round(Math.sqrt(Math.max(0, Math.min(1, c.loudness + (Math.random() - 0.5) * 0.35))) * 255)))])

export function createMockLink(receive: (m: ServerMessage) => void, onClose: () => void) {
  let closed = false
  let audio = false
  let gen = 0
  let shape = ''
  let lastVolumes = ''
  let lastStatus = ''
  const deliver = (bytes: Uint8Array) => setTimeout(() => !closed && receive(decode(bytes)))
  const shapeOf = (channels: MockChannel[]) =>
    JSON.stringify([defaultOutput, defaultInput, channels.map((c) => [c.role, c.key, c.name])])

  // CHANGED status when the status differs, countdown aside, like LiveSocket.cs; the phone then
  // fetches it from mockRequest as it would from /api/status.
  const statusKey = () => {
    const s = status()
    return JSON.stringify({ ...s, displays: { ...s.displays, pending: s.displays.pending && { ...s.displays.pending, secondsLeft: 0 } } })
  }
  function pushStatus() {
    const key = statusKey()
    if (key === lastStatus) return
    lastStatus = key
    deliver(Uint8Array.of(0x81, 1))
  }

  function pushAudio(force = false) {
    const channels = mockChannels()
    const nextShape = shapeOf(channels)
    if (force || nextShape !== shape) {
      if (nextShape !== shape) gen = (gen + 1) & 0xff
      shape = nextShape
      lastVolumes = JSON.stringify(channels.map((c) => [c.volume, c.muted]))
      deliver(encodeState(gen, channels))
    } else {
      const volumes = JSON.stringify(channels.map((c) => [c.volume, c.muted]))
      if (volumes !== lastVolumes) {
        lastVolumes = volumes
        deliver(encodeVolumes(gen, channels))
      }
    }
    deliver(encodeLevels(gen, channels))
  }

  deliver(Uint8Array.of(0x80, 1))
  // Everything counts as changed on a new connection.
  lastStatus = statusKey()
  deliver(Uint8Array.of(0x81, 1 | 2))
  let tick = 0
  const timer = setInterval(() => {
    if (tick++ % 12 === 0) pushStatus()
    if (audio) pushAudio()
  }, 40)

  return {
    send(bytes: Uint8Array) {
      const [op, a, b, c] = bytes
      if (op === 0x20) {
        audio = (a & 1) !== 0
        if (audio) pushAudio(true)
        return
      }
      if (op >= 0x01 && op <= 0x0f) return void console.debug('input:', [...bytes])
      if (a !== gen) return
      const channels = mockChannels()
      const target = channels[b]
      if ((op === 0x10 || op === 0x11) && target) {
        const id = target.role === ROLE.output ? outputs[defaultOutput].id : target.role === ROLE.input ? inputs[defaultInput].id : null
        const audioOf = id ? endpointAudio[id] : appAudio[target.key!]
        if (op === 0x10) audioOf.volume = Math.min(100, c)
        else audioOf.muted = c !== 0
      }
      if (op === 0x12) {
        if (b === 0 && c < outputs.length) defaultOutput = c
        if (b === 1 && c < inputs.length) defaultInput = c
      }
    },
    close() {
      closed = true
      clearInterval(timer)
      onClose()
    },
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
  throw new Error(`Mock: ${method} ${path} non gestito`)
}

