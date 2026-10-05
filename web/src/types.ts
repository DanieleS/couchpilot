export type Game = {
  id: string
  name: string
  source: string
  installed: boolean
  // Playnite is installing or uninstalling it right now.
  installing: boolean
  uninstalling: boolean
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
  // The audio output this preset switches to, or null to leave the audio alone. The name is
  // there for when the device isn't plugged in.
  audioOutputId: string | null
  audioOutputName: string | null
}

export type AudioOutput = {
  id: string
  name: string
  detail: string | null
  kind: 'speakers' | 'headphones' | 'display'
  default: boolean
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
  controllers: Controller[]
}

// An Xbox-style controller connected to the PC.
export type Controller = {
  // 1–4, like the light on the controller.
  slot: number
  wired: boolean
  // Null when wired, or when the pad doesn't say.
  battery: 'empty' | 'low' | 'medium' | 'full' | null
}

export type VolumeChange = { volume?: number; muted?: boolean }
