import { onScopeDispose, ref, watch } from 'vue'
import { useCouchPilot } from './store'

const useMock = import.meta.env.VITE_MOCK === '1'

export type Mod = 'ctrl' | 'alt' | 'shift' | 'win'
export type MouseButton = 'l' | 'r' | 'm'
export type LinkState = 'off' | 'connecting' | 'open'

// The touchpad and keyboard talk to the PC over one WebSocket, a few bytes per command
// (the format is at the bottom of this file and in RemoteInput.cs). Connected only while the Control tab
// is on screen, the phone is awake and the PC allows it; it reconnects by itself.
export function useRemoteInput() {
  const { status } = useCouchPilot()
  const link = ref<LinkState>('off')
  let socket: WebSocket | null = null
  let retries = 0
  let timer = 0
  let disposed = false
  // The last answer from the PC: one missed status poll shouldn't drop the link mid-drag.
  let allowed = false

  const wanted = () => !disposed && allowed && document.visibilityState === 'visible'

  function connect() {
    if (socket || !wanted()) return
    if (useMock) {
      link.value = 'open'
      return
    }
    link.value = 'connecting'
    const ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/input`)
    socket = ws
    ws.onopen = () => {
      retries = 0
      link.value = 'open'
    }
    ws.onclose = () => {
      if (socket !== ws) return
      socket = null
      link.value = wanted() ? 'connecting' : 'off'
      clearTimeout(timer)
      timer = window.setTimeout(connect, Math.min(5000, 400 * 2 ** retries++))
    }
  }

  function disconnect() {
    clearTimeout(timer)
    const ws = socket
    socket = null
    ws?.close()
    link.value = 'off'
  }

  const sync = () => (wanted() ? connect() : disconnect())
  watch(
    () => status.value?.input,
    (input) => {
      if (input === undefined) return
      allowed = input
      sync()
    },
    { immediate: true },
  )
  document.addEventListener('visibilitychange', sync)
  onScopeDispose(() => {
    disposed = true
    document.removeEventListener('visibilitychange', sync)
    disconnect()
  })

  // Nothing is queued while the link is down: a burst of stale pointer moves or key presses
  // landing seconds later would be worse than losing them.
  function send(bytes: Uint8Array) {
    if (useMock) return void console.debug('input:', [...bytes])
    if (socket?.readyState === WebSocket.OPEN) socket.send(bytes)
  }

  return {
    link,
    move: (dx: number, dy: number) => send(pair(Op.move, dx, dy)),
    // 120 is one notch; positive scrolls up / right.
    wheel: (vertical: number, horizontal: number) => send(pair(Op.wheel, vertical, horizontal)),
    down: (b: MouseButton) => send(Uint8Array.of(Op.down, buttons[b])),
    up: (b: MouseButton) => send(Uint8Array.of(Op.up, buttons[b])),
    click: (b: MouseButton) => send(Uint8Array.of(Op.click, buttons[b])),
    // A Windows virtual key, see `Vk`.
    key: (vk: number, mods: Mod[] = []) => send(Uint8Array.of(Op.key, modBits(mods), vk)),
    // The key that types this character on the PC, pressed with modifiers: Ctrl+C.
    char: (c: string, mods: Mod[]) => send(withText(Uint8Array.of(Op.char, modBits(mods)), c)),
    type: (text: string) => send(withText(Uint8Array.of(Op.text), text)),
  }
}

export type RemoteInput = ReturnType<typeof useRemoteInput>

// The wire format, described in full in RemoteInput.cs: an opcode byte, then fixed fields.
const Op = { move: 1, wheel: 2, down: 3, up: 4, click: 5, key: 6, char: 7, text: 8 } as const
const buttons: Record<MouseButton, number> = { l: 0, r: 1, m: 2 }
const modMask: Record<Mod, number> = { ctrl: 1, alt: 2, shift: 4, win: 8 }
const modBits = (mods: Mod[]) => mods.reduce((bits, m) => bits | modMask[m], 0)
const encoder = new TextEncoder()

const clamp16 = (n: number) => Math.max(-32768, Math.min(32767, Math.round(n)))
function pair(op: number, a: number, b: number) {
  const bytes = new Uint8Array(5)
  const view = new DataView(bytes.buffer)
  bytes[0] = op
  view.setInt16(1, clamp16(a), true)
  view.setInt16(3, clamp16(b), true)
  return bytes
}
function withText(head: Uint8Array, text: string) {
  const body = encoder.encode(text)
  const bytes = new Uint8Array(head.length + body.length)
  bytes.set(head)
  bytes.set(body, head.length)
  return bytes
}

// Windows virtual-key codes for the keys the phone's own keyboard doesn't have.
export const Vk = {
  backspace: 0x08, tab: 0x09, enter: 0x0d, esc: 0x1b, space: 0x20,
  pageUp: 0x21, pageDown: 0x22, end: 0x23, home: 0x24,
  left: 0x25, up: 0x26, right: 0x27, down: 0x28, delete: 0x2e,
  win: 0x5b, d: 0x44, f1: 0x70, f4: 0x73,
  mute: 0xad, volumeDown: 0xae, volumeUp: 0xaf, nextTrack: 0xb0, prevTrack: 0xb1, playPause: 0xb3,
} as const
