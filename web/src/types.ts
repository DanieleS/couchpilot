export type Game = {
  id: string
  name: string
  source: string
  installed: boolean
  favorite: boolean
  hidden: boolean
  running: boolean
  playtimeMinutes: number
  lastActivity: string | null
  added: string | null
  genres: string[]
  releaseYear: number | null
  hasCover: boolean
  hasBackground: boolean
}

export type GameDetail = Game & {
  description: string | null
  developers: string[]
}

export type RunningGame = {
  id: string
  name: string
  source: string
  startedAt: string | null
  hasCover: boolean
  hasBackground: boolean
}

export type Monitor = {
  name: string
  devicePath: string
  x: number
  y: number
  width: number
  height: number
  refreshHz: number
  primary: boolean
  virtual: boolean
}

export type Preset = {
  id: string
  name: string
  monitors: Monitor[]
}

export type Displays = {
  monitors: Monitor[]
  presets: Preset[]
  activePresetId: string | null
  pending: { presetId: string; previousPresetId: string | null; secondsLeft: number } | null
  streamActive: boolean
}

export type Status = {
  pc: string
  running: RunningGame[]
  displays: Displays
  // Touchpad and keyboard are allowed in the extension settings.
  input: boolean
}

export type AudioDeviceKind = 'speakers' | 'headphones' | 'display' | 'mic'

export type AudioDevice = {
  id: string
  name: string
  // The adapter it hangs off, to tell two «Speakers» apart.
  detail: string | null
  kind: AudioDeviceKind
}

// Volumes are 0..100, peaks 0..1.
export type AudioEndpoint = AudioDevice & {
  volume: number
  muted: boolean
  peak: number
}

export type AudioApp = {
  id: string
  name: string
  volume: number
  muted: boolean
  peak: number
  system: boolean
  // Set when the app is a game Playnite launched.
  gameId: string | null
  hasIcon: boolean
}

export type AudioState = {
  output: AudioEndpoint | null
  input: AudioEndpoint | null
  outputs: AudioDevice[]
  inputs: AudioDevice[]
  apps: AudioApp[]
}

export type VolumeChange = { volume?: number; muted?: boolean }
