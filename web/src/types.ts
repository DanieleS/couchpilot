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
  // Screenshots and clips, or null when they're off in the extension settings.
  capture: CaptureStatus | null
  controllers: Controller[]
}

export type CaptureTool = 'gamebar' | 'nvidia'

export type CaptureStatus = {
  tool: CaptureTool
  // When the recording started from the phone began, or null.
  recordingSince: string | null
  // How much a replay keeps, when the tool says (Game Bar does).
  replaySeconds: number | null
}

// A screenshot or clip in the capture tools' folders.
export type Capture = {
  id: string
  kind: 'photo' | 'video'
  // A clip of what just happened (Game Bar's «Record that», NVIDIA's Instant Replay).
  replay: boolean
  takenAt: string
  bytes: number
  width: number | null
  height: number | null
  seconds: number | null
  // The Playnite game it belongs to, when the PC can tell.
  gameId: string | null
  // The game's name, or what the tool called the window ("Desktop"), or null.
  game: string | null
  tool: CaptureTool | null
  fileName: string
  // Changes with the file; the thumbnail's URL carries it.
  version: string
}

// GET /api/captures, newest first.
export type Captures = {
  items: Capture[]
  bytes: number
  folders: string[]
}

// An Xbox-style controller connected to the PC.
export type Controller = {
  // 1–4, like the light on the controller.
  slot: number
  wired: boolean
  // Null when wired, or when the pad doesn't say.
  battery: 'empty' | 'low' | 'medium' | 'full' | null
}

// What Windows' media controls show: the track some app is playing.
export type NowPlaying = {
  title: string
  artist: string | null
  album: string | null
  // How Windows names the app: "Spotify.exe", "MSEdge", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"…
  app: string | null
  playing: boolean
  canPlayPause: boolean
  canNext: boolean
  canPrevious: boolean
  // As of readAt; null when the app doesn't say.
  positionSeconds: number | null
  durationSeconds: number | null
  readAt: string
  artVersion: string | null
}

export type StatsRangeName = 'week' | 'month' | 'year'

// GET /api/stats. Days are days of play, 5:00 to 5:00, as yyyy-MM-dd.
export type Stats = {
  range: StatsRangeName
  offset: number
  from: string
  // Exclusive.
  to: string
  today: string
  // The first day CouchPilot logged a session, or null: before it, only Playnite's totals exist.
  trackingSince: string | null
  totalSeconds: number
  // The previous period, up to the same point when this one is under way.
  previousTotalSeconds: number
  previousFrom: string
  sessions: number
  // Days for a week or month, months for a year.
  buckets: { date: string; seconds: number }[]
  top: { id: string; name: string; seconds: number; hasCover: boolean }[]
  library: { playtimeSeconds: number; games: number; installed: number; installedNeverPlayed: number; playedThisYear: number }
  resume: { id: string; name: string; playtimeSeconds: number; lastActivity: string | null; hasCover: boolean }[]
}

// GET /api/games/{id}/stats.
export type GameStats = {
  playtimeSeconds: number
  playCount: number
  lastActivity: string | null
  completionStatus: string | null
  sessions: number
  averageSeconds: number
  // The last 12 weeks, oldest first, this week last.
  weeks: number[]
  lastSession: { start: string; seconds: number } | null
  trackingSince: string | null
}

export type VolumeChange = { volume?: number; muted?: boolean }
