// The live WebSocket's wire format, phone side. docs/protocol.md is the specification, byte by
// byte; LiveSocket.cs, AudioProtocol.cs and RemoteInput.cs are the PC side. Keep all of them in step.
export const PROTOCOL_VERSION = 1

export const Op = {
  // Phone → PC: mouse and keyboard (0x01–0x0F)
  move: 0x01, wheel: 0x02, down: 0x03, up: 0x04, click: 0x05, key: 0x06, char: 0x07, text: 0x08,
  // Phone → PC: audio (0x10–0x1F)
  volume: 0x10, mute: 0x11, setDefault: 0x12,
  // Phone → PC: the connection (0x20–0x2F)
  subscribe: 0x20,
  // PC → phone (0x80–0x8F)
  hello: 0x80, changed: 0x81, audioState: 0x82, audioVolumes: 0x83, audioLevels: 0x84,
} as const

export const Subscribe = { audio: 1 } as const

// What CHANGED says to fetch again over HTTP.
export const Topic = { status: 1, library: 2, media: 4, captures: 8, achievements: 16 } as const

export type AudioRole = 'output' | 'input' | 'app' | 'system' | 'game'
export type AudioDeviceKind = 'speakers' | 'headphones' | 'display' | 'mic'
export type AudioFlow = 'output' | 'input'

export const roles: readonly AudioRole[] = ['output', 'input', 'app', 'system', 'game']
export const kinds: readonly AudioDeviceKind[] = ['speakers', 'headphones', 'display', 'mic']

export type AudioDevice = {
  // Its position in its list: what DEFAULT names.
  index: number
  name: string
  detail: string
  kind: AudioDeviceKind
  isDefault: boolean
}

export type AudioChannel = {
  // Its position in the list: what VOLUME, MUTE, VOLUMES and LEVELS go by.
  index: number
  role: AudioRole
  volume: number
  muted: boolean
  hasIcon: boolean
  name: string
  // Apps only: stable across generations, and the name of the app's icon.
  key: string | null
  // Games only: the Playnite id.
  gameId: string | null
}

export type AudioState = {
  gen: number
  outputs: AudioDevice[]
  inputs: AudioDevice[]
  channels: AudioChannel[]
}

export type ServerMessage =
  | { op: 'hello'; version: number }
  // Bits of `Topic`.
  | { op: 'changed'; topics: number }
  | { op: 'audioState'; state: AudioState }
  | { op: 'audioVolumes'; gen: number; volumes: { volume: number; muted: boolean }[] }
  // One byte a channel: the peak's square root × 255.
  | { op: 'audioLevels'; gen: number; levels: Uint8Array }
  | { op: 'unknown'; code: number }

const FLAG_DEFAULT = 1
const FLAG_MUTED = 1
const FLAG_ICON = 2

const decoder = new TextDecoder()
const encoder = new TextEncoder()

class Reader {
  private at = 0
  private readonly view: DataView
  constructor(private readonly bytes: Uint8Array) {
    this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  }
  u8() {
    if (this.at >= this.bytes.length) throw new RangeError('Truncated message')
    return this.view.getUint8(this.at++)
  }
  take(n: number) {
    if (this.at + n > this.bytes.length) throw new RangeError('Truncated message')
    const slice = this.bytes.subarray(this.at, this.at + n)
    this.at += n
    return slice
  }
  str() {
    return decoder.decode(this.take(this.u8()))
  }
  rest() {
    return this.take(this.bytes.length - this.at)
  }
}

const hex = (bytes: Uint8Array) => [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')
const guid = (bytes: Uint8Array) => {
  const h = hex(bytes)
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

function devices(r: Reader): AudioDevice[] {
  const count = r.u8()
  return Array.from({ length: count }, (_, index) => {
    const kind = kinds[r.u8()] ?? 'speakers'
    const flags = r.u8()
    return { index, kind, isDefault: (flags & FLAG_DEFAULT) !== 0, name: r.str(), detail: r.str() }
  })
}

// Throws on a malformed message; an unknown opcode is not an error, it's from a newer PC.
export function decode(data: ArrayBuffer | Uint8Array): ServerMessage {
  const r = new Reader(data instanceof Uint8Array ? data : new Uint8Array(data))
  const op = r.u8()
  switch (op) {
    case Op.hello:
      return { op: 'hello', version: r.u8() }
    case Op.changed:
      return { op: 'changed', topics: r.u8() }
    case Op.audioState: {
      const gen = r.u8()
      const outputs = devices(r)
      const inputs = devices(r)
      const count = r.u8()
      const channels = Array.from({ length: count }, (_, index): AudioChannel => {
        const role = roles[r.u8()] ?? 'app'
        const volume = r.u8()
        const flags = r.u8()
        const name = r.str()
        const app = role === 'app' || role === 'system' || role === 'game'
        const key = app ? r.str() : null
        const gameId = role === 'game' ? guid(r.take(16)) : null
        return { index, role, volume, muted: (flags & FLAG_MUTED) !== 0, hasIcon: (flags & FLAG_ICON) !== 0, name, key, gameId }
      })
      return { op: 'audioState', state: { gen, outputs, inputs, channels } }
    }
    case Op.audioVolumes: {
      const gen = r.u8()
      const body = r.rest()
      const volumes = []
      for (let i = 0; i + 1 < body.length; i += 2) volumes.push({ volume: body[i], muted: (body[i + 1] & FLAG_MUTED) !== 0 })
      return { op: 'audioVolumes', gen, volumes }
    }
    case Op.audioLevels:
      return { op: 'audioLevels', gen: r.u8(), levels: r.rest() }
    default:
      return { op: 'unknown', code: op }
  }
}

// Phone → PC

const clamp16 = (n: number) => Math.max(-32768, Math.min(32767, Math.round(n)))
const clamp100 = (n: number) => Math.max(0, Math.min(100, Math.round(n)))

export function pair(op: number, a: number, b: number) {
  const bytes = new Uint8Array(5)
  const view = new DataView(bytes.buffer)
  bytes[0] = op
  view.setInt16(1, clamp16(a), true)
  view.setInt16(3, clamp16(b), true)
  return bytes
}

export function withText(head: Uint8Array, text: string) {
  const body = encoder.encode(text)
  const bytes = new Uint8Array(head.length + body.length)
  bytes.set(head)
  bytes.set(body, head.length)
  return bytes
}

export const encodeVolume = (gen: number, channel: number, volume: number) => Uint8Array.of(Op.volume, gen, channel, clamp100(volume))
export const encodeMute = (gen: number, channel: number, muted: boolean) => Uint8Array.of(Op.mute, gen, channel, muted ? 1 : 0)
export const encodeSetDefault = (gen: number, flow: AudioFlow, device: number) =>
  Uint8Array.of(Op.setDefault, gen, flow === 'output' ? 0 : 1, device)
export const encodeSubscribe = (bits: number) => Uint8Array.of(Op.subscribe, bits)
