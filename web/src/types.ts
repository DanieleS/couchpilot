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

// Playnite's library view, as LibraryView.cs reads it: what the PC shows, and in which order.
export type LibraryView = {
  // Each mode keeps its own presets, filters and sort.
  mode: 'desktop' | 'fullscreen'
  // Playnite's filter presets, in the user's order.
  presets: { id: string; name: string }[]
  // null when the filters were changed by hand since.
  activePreset: string | null
  // A Playnite SortOrder in camelCase (name, lastActivity, playtime…), and its name in Playnite's language.
  sort: string
  sortLabel: string
  descending: boolean
  // What Playnite groups by, in Playnite's language, or null when it doesn't group.
  grouping: string | null
  // The games in Playnite's order. Ungrouped: one group with no name. A game can sit in more
  // than one group (two genres, say). A null name is the group of games without a value.
  groups: { name: string | null; ids: string[] }[]
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
  // Screenshots and clips, or null when they're off in the extension settings, or not for this phone.
  capture: CaptureStatus | null
  controllers: Controller[]
  // Phones have to type a PIN before they're let in.
  pairing: boolean
  // This phone, as the PC knows it; null while pairing is off.
  device: PairedDevice | null
  // SuccessStory is installed and has data: games can show their achievements.
  achievements: boolean
  // The PC is on the lock screen right now.
  locked: boolean
  // This phone may unlock the PC: set up on the PC, locked, and permitted for this phone.
  unlock: boolean
  // Games started from CouchPilot hand their cover's colours to the lights; null while Home
  // Assistant is off in the extension settings.
  coverLights: boolean | null
}

// A phone the PC paired with, and what it may do.
export type PairedDevice = {
  id: string
  name: string
  input: boolean
  captures: boolean
  unlock: boolean
}

export type CaptureTool = 'gamebar' | 'nvidia'

export type CaptureStatus = {
  tool: CaptureTool
  // When the recording started from the phone began, or null.
  recordingSince: string | null
  // How much a replay keeps, when the tool says (Game Bar does).
  replaySeconds: number | null
  // Clips can be trimmed on the PC (the bundled ffmpeg is there).
  editing: boolean
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
  // Cut out of another clip from the phone; that clip's id while it's still there.
  edited: boolean
  sourceId: string | null
}

// POST /api/captures/{id}/trim.
export type TrimRequest = {
  from: number
  to: number
  format: 'mp4' | 'gif'
  audio: boolean
}

// GET/POST /api/captures/{id}/light: the clip's light copy for watching on the phone. `original`
// means play the file itself (light enough already, or the PC can't make one); `none` that it
// isn't made and nobody asked yet.
export type ClipLight = {
  state: 'ready' | 'queued' | 'making' | 'original' | 'none'
  // 0 to 1 while making.
  progress: number | null
  bytes: number | null
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
  // GameActivity's data was found on the PC. Without it there are no sessions: totals, buckets
  // and top are empty, and only `library`, `resume` and `achievements` say anything.
  activity: boolean
  // The day of GameActivity's first session, or null: before it, only Playnite's totals exist.
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
  // Unlocked in the period, by SuccessStory's dates; null without SuccessStory.
  achievements: { unlocked: number; recent: RecentAchievement[] } | null
}

// An achievement, from SuccessStory's data.
export type Achievement = {
  id: string
  name: string
  description: string | null
  unlocked: boolean
  // When it was unlocked, or null: still locked, or the source doesn't say when.
  unlockedAt: string | null
  // The game hides it until it's unlocked.
  hidden: boolean
  // How many players have it, 0–100; null when the source doesn't say.
  percent: number | null
  gamerScore: number | null
  icon: string | null
  // The greyed-out icon the game shows while it's locked, or null to grey out the other.
  lockedIcon: string | null
}

export type RecentAchievement = Achievement & {
  gameId: string
  game: string
  hasCover: boolean
}

// GET /api/games/{id}/achievements: unlocked first, newest first; then the locked ones.
export type GameAchievements = {
  gameId: string
  game: string
  total: number
  unlocked: number
  lastRefresh: string | null
  items: Achievement[]
}

// GET /api/games/{id}/stats.
export type GameStats = {
  playtimeSeconds: number
  playCount: number
  lastActivity: string | null
  completionStatus: string | null
  // GameActivity's data was found on the PC: without it sessions, averageSeconds, weeks and
  // lastSession are empty, and only Playnite's totals above are there.
  activity: boolean
  sessions: number
  averageSeconds: number
  // The last 12 weeks, oldest first, this week last.
  weeks: number[]
  lastSession: { start: string; seconds: number } | null
  trackingSince: string | null
}

export type VolumeChange = { volume?: number; muted?: boolean }
