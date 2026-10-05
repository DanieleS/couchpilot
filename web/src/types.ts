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

export type VolumeChange = { volume?: number; muted?: boolean }
