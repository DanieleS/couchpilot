import { computed } from 'vue'
import { send, useLive } from './live'
import { useCouchPilot } from './store'
import { Op, pair, withText } from './protocol'

export type Mod = 'ctrl' | 'alt' | 'shift' | 'win'
export type MouseButton = 'l' | 'r' | 'm'
export type LinkState = 'off' | 'connecting' | 'open'

// The touchpad and keyboard ride the live socket, a few bytes per command (opcodes 0x01–0x0F in
// docs/protocol.md). Usable while the socket is open and the PC allows it.
export function useRemoteInput() {
  const { link: live } = useLive()
  const { status } = useCouchPilot()
  const link = computed<LinkState>(() => (status.value?.input === false ? 'off' : live.value === 'open' && status.value ? 'open' : 'connecting'))

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

const buttons: Record<MouseButton, number> = { l: 0, r: 1, m: 2 }
const modMask: Record<Mod, number> = { ctrl: 1, alt: 2, shift: 4, win: 8 }
const modBits = (mods: Mod[]) => mods.reduce((bits, m) => bits | modMask[m], 0)

// Windows virtual-key codes for the keys the phone's own keyboard doesn't have.
export const Vk = {
  backspace: 0x08, tab: 0x09, enter: 0x0d, esc: 0x1b, space: 0x20,
  pageUp: 0x21, pageDown: 0x22, end: 0x23, home: 0x24,
  left: 0x25, up: 0x26, right: 0x27, down: 0x28, delete: 0x2e,
  win: 0x5b, d: 0x44, f1: 0x70, f4: 0x73,
  mute: 0xad, volumeDown: 0xae, volumeUp: 0xaf, nextTrack: 0xb0, prevTrack: 0xb1, playPause: 0xb3,
} as const
