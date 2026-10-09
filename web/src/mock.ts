// In-memory stand-in for the plugin's API, for `pnpm dev:mock` on a machine without Playnite.
import { decode, type ServerMessage } from './protocol'
import type { Achievement, Capture, CaptureStatus, Game, GameAchievements, Monitor, PairedDevice, Preset, RecentAchievement, RunningGame, Status } from './types'

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
  installing: false,
  uninstalling: false,
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
const presetAudio = { audioOutputId: null, audioOutputName: null }
const presets: Preset[] = [
  { ...presetAudio, id: 'p1', name: 'Scrivania', monitors: [m('DELL U2723QE', -2560, 0, 2560, 1440, 60), m('ASUS PG27AQN', 0, 0, 2560, 1440, 240), m('DELL U2723QE ', 2560, 0, 2560, 1440, 60)] },
  { ...presetAudio, id: 'p2', name: 'Solo TV', monitors: [m('LG OLED C3', 0, 0, 3840, 2160, 120)] },
  { ...presetAudio, id: 'p3', name: 'TV + monitor', monitors: [m('ASUS PG27AQN', -2560, 400, 2560, 1440, 240), m('LG OLED C3', 0, 0, 3840, 2160, 120)] },
  { ...presetAudio, id: 'p4', name: 'Gaming 240 Hz', monitors: [m('ASUS PG27AQN', 0, 0, 2560, 1440, 240)] },
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
    capture: captureStatus(),
    pairing: pairingRequired,
    device: pairingRequired ? pairedDevice : null,
    // `?noach=1` pretends SuccessStory isn't installed.
    achievements: !new URLSearchParams(location.search).has('noach'),
    // `?locked=1` pretends the PC is on the lock screen with unlocking set up and allowed here;
    // `?locked=view`, locked but this phone may not unlock it.
    locked: lockedPc,
    unlock: lockedPc && !lockedViewOnly,
    // `?nopads=1` pretends no controller is connected.
    controllers: new URLSearchParams(location.search).has('nopads')
      ? []
      : [{ slot: 1, wired: false, battery: 'medium' }, { slot: 2, wired: false, battery: 'low' }, { slot: 3, wired: true, battery: null }],
    displays: {
      monitors: current,
      presets,
      activePresetId: presets.find((p) => p.monitors === current)?.id ?? null,
      streamActive: current.some((x) => x.virtual),
      pending: pending && { presetId: pending.presetId, previousPresetId: pending.previousPresetId, secondsLeft: Math.ceil((pending.deadline - Date.now()) / 1000) },
    },
  }
}

// A track playing in Spotify, three songs to skip through. Bumped revision → CHANGED media.
const tracks = [
  { title: 'Lumière', artist: 'Lorien Testard', album: 'Clair Obscur: Expedition 33', duration: 264 },
  { title: 'Gustave', artist: 'Lorien Testard', album: 'Clair Obscur: Expedition 33', duration: 198 },
  { title: 'Nothing Is Lost', artist: 'Darren Korb', album: 'Hades II', duration: 231 },
]
const player = { track: 0, playing: true, position: 42, since: Date.now() }
let mediaRevision = 0
const playerPosition = () => Math.min(tracks[player.track].duration, player.position + (player.playing ? (Date.now() - player.since) / 1000 : 0))
function mediaCommand(command: string) {
  player.position = playerPosition()
  player.since = Date.now()
  if (command === 'play-pause') player.playing = !player.playing
  if (command === 'next') player.track = (player.track + 1) % tracks.length
  if (command === 'previous') player.track = (player.track + tracks.length - 1) % tracks.length
  if (command !== 'play-pause') player.position = 0
  mediaRevision++
}

// Statistics: invented but stable sessions over the last 60 days, a game or two a day,
// days off included. Days of play run 5:00 to 5:00, like StatsService.cs.
const DAY = 86400000
const dayKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const playDay = (t = Date.now()) => {
  const d = new Date(t - 5 * 3600000)
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}
const seed = (text: string) => [...text].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 17)
const trackingSince = new Date(playDay().getTime() - 60 * DAY)
// `?noactivity=1` pretends GameActivity isn't installed: no sessions, only Playnite's totals.
const activity = !new URLSearchParams(location.search).has('noactivity')
// [gameIndex, seconds] played on a day.
function playedOn(day: Date): [number, number][] {
  if (!activity || day < trackingSince || day > playDay()) return []
  const h = seed(dayKey(day))
  if (h % 10 < 3) return []
  const main = [0, 7, 2, 13][h % 4]
  const out: [number, number][] = [[main, 1800 + (h % 9) * 1200]]
  if (h % 3 === 0) out.push([[5, 9, 11][h % 3 === 0 ? (h >> 3) % 3 : 0], 900 + (h % 5) * 600])
  return out
}
function mockStats(range: string, offset: number) {
  const today = playDay()
  let from: Date, to: Date, prev: Date
  if (range === 'month') {
    from = new Date(today.getFullYear(), today.getMonth() + offset, 1)
    to = new Date(from.getFullYear(), from.getMonth() + 1, 1)
    prev = new Date(from.getFullYear(), from.getMonth() - 1, 1)
  } else if (range === 'year') {
    from = new Date(today.getFullYear() + offset, 0, 1)
    to = new Date(from.getFullYear() + 1, 0, 1)
    prev = new Date(from.getFullYear() - 1, 0, 1)
  } else {
    range = 'week'
    from = new Date(today.getTime() - ((today.getDay() + 6) % 7) * DAY + offset * 7 * DAY)
    to = new Date(from.getTime() + 7 * DAY)
    prev = new Date(from.getTime() - 7 * DAY)
  }
  const buckets = new Map<string, number>()
  if (range === 'year') for (let m = 0; m < 12; m++) buckets.set(dayKey(new Date(from.getFullYear(), m, 1)), 0)
  else for (let d = new Date(from); d < to; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) buckets.set(dayKey(d), 0)
  const perGame = new Map<number, number>()
  let total = 0, previous = 0, sessions = 0
  // Like StatsService.cs: the period under way against the previous one up to the same point.
  const previousTo = today >= from && today < to ? new Date(Math.min(from.getTime(), prev.getTime() + (today.getTime() - from.getTime()) + DAY)) : from
  for (let d = new Date(prev); d < to; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) {
    for (const [g, s] of playedOn(d)) {
      if (d < from) { if (d < previousTo) previous += s; continue }
      total += s
      sessions++
      const key = range === 'year' ? dayKey(new Date(d.getFullYear(), d.getMonth(), 1)) : dayKey(d)
      buckets.set(key, (buckets.get(key) ?? 0) + s)
      perGame.set(g, (perGame.get(g) ?? 0) + s)
    }
  }
  return {
    range, offset, from: dayKey(from), to: dayKey(to), today: dayKey(today), activity, trackingSince: activity ? dayKey(trackingSince) : null,
    totalSeconds: total, previousTotalSeconds: previous, previousFrom: dayKey(prev), sessions,
    buckets: [...buckets].map(([date, seconds]) => ({ date, seconds })),
    top: [...perGame].sort((a, b) => b[1] - a[1]).slice(0, 5).map(([g, seconds]) => ({ id: games[g].id, name: games[g].name, seconds, hasCover: false })),
    library: {
      playtimeSeconds: games.reduce((t, g) => t + g.playtimeMinutes * 60, 0), games: games.length,
      installed: games.filter((g) => g.installed).length, installedNeverPlayed: 1, playedThisYear: 9,
    },
    resume: [10, 12, 9].map((i) => ({ id: games[i].id, name: games[i].name, playtimeSeconds: games[i].playtimeMinutes * 60, lastActivity: new Date(Date.now() - (120 + i * 9) * DAY).toISOString(), hasCover: false })),
    achievements: (() => {
      const unlocked = mockRecentAchievements(from.getTime() + 5 * 3600000, to.getTime() + 5 * 3600000)
      return { unlocked: unlocked.length, recent: unlocked.slice(0, 8) }
    })(),
  }
}
function mockGameStats(index: number) {
  const g = games[index]
  const weeks = new Array(12).fill(0)
  let sessions = 0, seconds = 0
  let last: { start: string; seconds: number } | null = null
  const monday = new Date(playDay().getTime() - ((playDay().getDay() + 6) % 7) * DAY)
  for (let d = new Date(trackingSince); d <= playDay(); d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) {
    for (const [gi, s] of playedOn(d)) {
      if (gi !== index) continue
      sessions++
      seconds += s
      last = { start: new Date(d.getTime() + 21 * 3600000).toISOString(), seconds: s }
      const w = 11 - Math.floor((monday.getTime() - (d.getTime() - ((d.getDay() + 6) % 7) * DAY)) / (7 * DAY))
      if (w >= 0) weeks[w] += s
    }
  }
  return {
    playtimeSeconds: g.playtimeMinutes * 60, playCount: 20 + index, lastActivity: g.lastActivity, completionStatus: index % 3 ? 'In corso' : 'Completato',
    activity, sessions, averageSeconds: sessions ? Math.round(seconds / sessions) : 0, weeks, lastSession: last, trackingSince: activity ? dayKey(trackingSince) : null,
  }
}

// Captures: a few a day on the days games were played, plus the odd one of the desktop.
// `?nocapture=1` pretends they're off in the settings, `?nvidia=1` that NVIDIA makes them,
// `?noreplay=1` that Game Bar isn't recording in the background.
const query = new URLSearchParams(location.search)
const captureTool = query.has('nvidia') ? 'nvidia' : 'gamebar'
let recordingSince: string | null = null
let captureRevision = 0
const captures: Capture[] = []
function mockCapture(gameIndex: number | null, at: Date, kind: Capture['kind'], seconds: number | null = null, replay = false): Capture {
  const id = (seed(at.toISOString() + kind) >>> 0).toString(16).padStart(8, '0') + String(captures.length).padStart(4, '0')
  const g = gameIndex === null ? null : games[gameIndex]
  return {
    id, kind, replay, takenAt: at.toISOString(), seconds,
    bytes: kind === 'photo' ? 2_800_000 + (seed(id) % 900_000) : Math.round((seconds ?? 30) * 1_600_000),
    width: 2560, height: 1440,
    gameId: g?.id ?? null, game: g?.name ?? 'Desktop', tool: captureTool,
    fileName: `${g?.name ?? 'Desktop'} ${at.toISOString().slice(0, 19).replace(/[T:]/g, '-')}.${kind === 'photo' ? 'png' : 'mp4'}`,
    version: '1',
    edited: false,
    sourceId: null,
  }
}

// Pairing: `?pair=1` pretends the PC wants a PIN (it's 123456) from this phone.
const pairingRequired = new URLSearchParams(location.search).has('pair')
let pairedDevice: PairedDevice | null = null
let lockedPc = new URLSearchParams(location.search).has('locked')
const lockedViewOnly = new URLSearchParams(location.search).get('locked') === 'view'
let pinPending = false
let pinAttempts = 5
const fail = (message: string, code: string, extra: Record<string, unknown> = {}) => Object.assign(new Error(message), { code, ...extra })

// Achievements: a stable, invented set per game, unlocked in step with how much it was played.
const achievementNames = [
  ['Primi passi', 'Completa il tutorial'], ['Esploratore', 'Scopri dieci luoghi'], ['Collezionista', 'Trova venti oggetti rari'],
  ['Senza paura', 'Sconfiggi un boss senza subire danni'], ["Maestro d'armi", 'Padroneggia ogni arma'], ['Cento vittorie', 'Vinci cento scontri'],
  ['Fine del viaggio', 'Finisci la storia'], ['Perfezionista', 'Finisci la storia al massimo livello'], ['Amico degli animali', 'Accarezza il cane'],
  ['Nottambulo', 'Gioca per tre ore di fila'], ['Mani veloci', 'Completa un livello in meno di un minuto'], ['Cartografo', 'Rivela tutta la mappa'],
  ['Mercante', 'Spendi diecimila monete'], ['Sopravvissuto', 'Sopravvivi a dieci ondate'], ['Leggenda', 'Ottieni ogni altro obiettivo'],
]
const achievementIcon = (name: string, locked: boolean) => {
  const h = seed(name) % 360
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="12" fill="hsl(${h} ${locked ? 10 : 55}% ${locked ? 30 : 38}%)"/>` +
    `<circle cx="32" cy="28" r="14" fill="hsl(${(h + 40) % 360} ${locked ? 10 : 80}% ${locked ? 45 : 70}%)"/><rect x="22" y="44" width="20" height="8" rx="2" fill="rgb(255 255 255 / 0.6)"/></svg>`
  return `data:image/svg+xml,${encodeURIComponent(svg)}`
}
const extraUnlocks: { gameIndex: number; at: Date }[] = []
let achievementsRevision = 0
function mockAchievements(gameIndex: number): GameAchievements | null {
  const g = games[gameIndex]
  if (!g || gameIndex % 7 === 6) return null
  const h = seed(g.name)
  const total = 10 + (h % 6)
  const playedDays: Date[] = []
  for (let back = 0; back < 60; back++) {
    const day = new Date(playDay().getTime() - back * DAY)
    if (playedOn(day).some(([gi]) => gi === gameIndex)) playedDays.push(day)
  }
  const unlockedCount = Math.min(total, Math.round(total * Math.min(1, g.playtimeMinutes / 6000)) + (playedDays.length ? 2 : 0))
  const items: Achievement[] = achievementNames.slice(0, total).map(([name, description], i) => {
    const k = seed(g.name + name)
    const unlocked = i < unlockedCount
    const day = playedDays.length ? playedDays[(i * 7) % playedDays.length] : new Date(Date.now() - (30 + i) * DAY)
    const at = new Date(day.getTime() + (20 * 60 + (k % 200)) * 60000)
    const percent = i === total - 1 ? 0.8 : Math.max(1.2, 95 - i * (6 + (k % 5)))
    return {
      id: `ach_${i}`, name, description, unlocked, unlockedAt: unlocked ? at.toISOString() : null,
      hidden: i >= total - 3, percent: k % 9 === 0 ? null : Math.round(percent * 10) / 10, gamerScore: 10 + (i % 4) * 15,
      icon: achievementIcon(name, false), lockedIcon: k % 3 === 0 ? achievementIcon(name, true) : null,
    }
  })
  for (const u of extraUnlocks.filter((x) => x.gameIndex === gameIndex)) {
    const locked = items.find((a) => !a.unlocked)
    if (locked) Object.assign(locked, { unlocked: true, unlockedAt: u.at.toISOString() })
  }
  items.sort((a, b) => Number(b.unlocked) - Number(a.unlocked) || (b.unlockedAt ?? '').localeCompare(a.unlockedAt ?? '') || (a.unlocked ? 0 : (b.percent ?? 0) - (a.percent ?? 0)))
  return { gameId: g.id, game: g.name, total, unlocked: items.filter((a) => a.unlocked).length, lastRefresh: new Date(Date.now() - 3600000).toISOString(), items }
}
function mockRecentAchievements(fromMs = 0, toMs = Infinity): RecentAchievement[] {
  const out: RecentAchievement[] = []
  games.forEach((g, i) => {
    const data = mockAchievements(i)
    for (const a of data?.items ?? []) {
      if (!a.unlockedAt) continue
      const t = new Date(a.unlockedAt).getTime()
      if (t >= fromMs && t < toMs) out.push({ ...a, gameId: g.id, game: g.name, hasCover: false })
    }
  })
  return out.sort((a, b) => b.unlockedAt!.localeCompare(a.unlockedAt!))
}
// `?unlock=1`: an achievement gets unlocked a few seconds after the phone first looks, like a session ending.
let unlockScheduled = false
function scheduleMockUnlock() {
  if (unlockScheduled || !new URLSearchParams(location.search).has('unlock')) return
  unlockScheduled = true
  setTimeout(() => {
    extraUnlocks.push({ gameIndex: 2, at: new Date() })
    achievementsRevision++
  }, 6000)
}
for (let back = 0; back < 14; back++) {
  const day = new Date(playDay().getTime() - back * DAY)
  for (const [g] of playedOn(day)) {
    const h = seed(dayKey(day) + g)
    for (let i = 0; i < 2 + (h % 5); i++) {
      const at = new Date(day.getTime() + (20 * 60 + ((h >> i) % 180)) * 60000 + i * 97000)
      if (at.getTime() > Date.now()) continue
      const video = (h >> (i + 2)) % 4 === 0
      captures.push(mockCapture(g, at, video ? 'video' : 'photo', video ? ((h >> i) % 3 === 0 ? 134 : 30) : null, video && (h >> i) % 3 !== 0))
    }
  }
  if (back % 5 === 2) captures.push(mockCapture(null, new Date(day.getTime() + 15 * 3600000), 'photo'))
}
captures.sort((a, b) => b.takenAt.localeCompare(a.takenAt))

function captureStatus(): CaptureStatus | null {
  if (query.has('nocapture')) return null
  return { tool: captureTool, recordingSince, replaySeconds: captureTool === 'gamebar' && !query.has('noreplay') ? 30 : null, editing: !query.has('noffmpeg') }
}

// After a moment, as the PC answers once the file is on disk.
async function captureCommand(what: string): Promise<Capture | undefined> {
  const fail = (message: string, code: string) => Object.assign(new Error(message), { code })
  const playing = running[0] ? games.findIndex((g) => g.id === running[0].id) : null
  const add = (c: Capture) => {
    captures.unshift(c)
    captureRevision++
    return c
  }
  if (what === 'screenshot') {
    await new Promise((r) => setTimeout(r, 700))
    return add(mockCapture(playing, new Date(), 'photo'))
  }
  if (what === 'replay') {
    if (query.has('noreplay')) {
      throw captureTool === 'nvidia'
        ? fail('Il PC non ha salvato nessuna clip: il Replay istantaneo di NVIDIA è acceso?', 'replay-off')
        : fail('Game Bar non sta registrando in sottofondo: non c\'è niente da salvare.', 'replay-off')
    }
    await new Promise((r) => setTimeout(r, 2500))
    return add(mockCapture(playing, new Date(), 'video', 30, true))
  }
  if (what === 'start') {
    recordingSince = new Date().toISOString()
    return
  }
  if (what === 'stop') {
    const since = recordingSince ? new Date(recordingSince) : new Date()
    recordingSince = null
    await new Promise((r) => setTimeout(r, 1500))
    return add(mockCapture(playing, since, 'video', Math.max(1, Math.round((Date.now() - since.getTime()) / 1000))))
  }
}

// A stand-in for the thumbnail: the game's colours, a sun and a mountain, a little different each time.
export function mockCaptureArt(c: Capture) {
  const h = seed(c.game ?? 'x') % 360
  const k = seed(c.id)
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 90"><rect width="160" height="90" fill="hsl(${h} 35% 22%)"/>` +
    `<circle cx="${30 + (k % 100)}" cy="${24 + (k % 20)}" r="${14 + (k % 10)}" fill="hsl(${(h + 50) % 360} 80% 65%)"/>` +
    `<polygon points="${k % 40},90 ${60 + (k % 30)},${36 + (k % 18)} ${130 + (k % 30)},90" fill="rgb(0 0 0 / 0.35)"/></svg>`
  return `data:image/svg+xml,${encodeURIComponent(svg)}`
}

// Bumped whenever a game changes, like Playnite's ItemUpdated: the link then sends CHANGED library.
let libraryRevision = 0
// Installing or uninstalling takes a few seconds, like a small game from a fast CDN; a Steam game
// longer, as for someone answering the dialog Steam opens on the PC, time to try stopping the wait.
const libraryJobs = new Map<string, ReturnType<typeof setTimeout>>()
function libraryJob(game: Game, job: 'installing' | 'uninstalling') {
  game[job] = true
  libraryRevision++
  libraryJobs.set(game.id, setTimeout(() => {
    libraryJobs.delete(game.id)
    game[job] = false
    game.installed = job === 'installing'
    libraryRevision++
  }, game.source === 'Steam' ? 20000 : 4000))
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
  let lastLibrary = libraryRevision
  let lastMedia = mediaRevision
  let lastCaptures = captureRevision
  let lastAchievements = achievementsRevision
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
  deliver(Uint8Array.of(0x81, 1 | 2 | 4 | 8 | 16 | 32))
  let tick = 0
  const timer = setInterval(() => {
    if (tick % 12 === 0 && libraryRevision !== lastLibrary) {
      lastLibrary = libraryRevision
      deliver(Uint8Array.of(0x81, 2))
    }
    if (mediaRevision !== lastMedia) {
      lastMedia = mediaRevision
      deliver(Uint8Array.of(0x81, 4))
    }
    if (captureRevision !== lastCaptures) {
      lastCaptures = captureRevision
      deliver(Uint8Array.of(0x81, 8))
    }
    if (achievementsRevision !== lastAchievements) {
      lastAchievements = achievementsRevision
      deliver(Uint8Array.of(0x81, 16))
    }
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
  const [, a, b, c, d] = path.split('/')
  // Pairing comes first: like PairingGate.cs, everything else is 401 until the phone has typed the PIN.
  if (a === 'pair') {
    if (method === 'GET' && !b) return { required: pairingRequired, device: pairedDevice, token: pairedDevice ? 'mock-token' : null }
    if (method === 'POST' && b === 'request') {
      pinPending = true
      pinAttempts = 5
      console.info('CouchPilot mock: il PIN è 123456')
      return { expiresAt: new Date(Date.now() + 120000).toISOString(), seconds: 120 }
    }
    if (method === 'POST' && !b) {
      if (!pinPending) throw fail('Il PIN è scaduto: chiedine un altro.', 'expired')
      if (body?.pin !== '123456') {
        if (--pinAttempts <= 0) {
          pinPending = false
          throw fail('Troppi tentativi: chiedi un altro PIN.', 'expired')
        }
        throw fail(`PIN sbagliato, ${pinAttempts} tentativi rimasti.`, 'wrong-pin', { attemptsLeft: pinAttempts })
      }
      pinPending = false
      pairedDevice = { id: 'dev1', name: body?.name || 'Telefono', input: true, captures: true, unlock: false }
      return { ...pairedDevice, token: 'mock-token' }
    }
    if (method === 'POST' && b === 'resume') {
      if (body?.token !== 'mock-token') throw fail('Il PC non conosce più questo telefono: abbinalo di nuovo.', 'forgotten')
      pairedDevice ??= { id: 'dev1', name: 'Telefono', input: true, captures: true, unlock: false }
      return pairedDevice
    }
    if (method === 'DELETE') return void (pairedDevice = null)
  }
  if (pairingRequired && !pairedDevice) throw fail('Questo telefono non è abbinato al PC.', 'unpaired')
  if (method === 'GET' && a === 'status') return status()

  // The lock screen: pretend the handoff worked and the PC unlocks a moment later.
  if (method === 'POST' && a === 'unlock') {
    if (!lockedPc) throw fail('Il PC non è bloccato.', 'not-locked')
    setTimeout(() => (lockedPc = false), 400)
    return { ok: true }
  }
  if (method === 'GET' && a === 'achievements' && b === 'recent') {
    scheduleMockUnlock()
    return mockRecentAchievements().slice(0, 40)
  }
  if (method === 'GET' && a === 'games' && c === 'achievements' && !d) {
    const data = mockAchievements(games.findIndex((g) => g.id === b))
    if (!data) throw fail('Nessun obiettivo per questo gioco.', 'not-found')
    return data
  }
  if (method === 'GET' && a?.startsWith('stats')) {
    const q = new URLSearchParams(path.split('?')[1] ?? '')
    return mockStats(q.get('range') ?? 'week', Number(q.get('offset') ?? 0))
  }
  if (method === 'GET' && a === 'games' && c === 'stats') return mockGameStats(games.findIndex((g) => g.id === b))
  if (method === 'GET' && a === 'media' && !b) {
    const t = tracks[player.track]
    return {
      title: t.title, artist: t.artist, album: t.album, app: 'Spotify.exe', playing: player.playing,
      canPlayPause: true, canNext: true, canPrevious: true,
      positionSeconds: playerPosition(), durationSeconds: t.duration, readAt: new Date().toISOString(), artVersion: null,
    }
  }
  if (method === 'POST' && a === 'media') return mediaCommand(b)
  if (a === 'captures') {
    if (!captureStatus()) throw new Error('Le catture sono spente nelle impostazioni di CouchPilot.')
    if (method === 'GET' && !b) return { items: captures, bytes: captures.reduce((t, x) => t + x.bytes, 0), folders: ['C:\\Users\\pc\\Videos\\Captures'] }
    if (method === 'POST' && c === 'trim') {
      const source = captures.find((x) => x.id === b)
      if (!source) throw new Error('Clip non trovata.')
      await new Promise((r) => setTimeout(r, 2500))
      const gif = body?.format === 'gif'
      const length = Math.max(0.5, (body?.to ?? 0) - (body?.from ?? 0))
      const at = new Date(new Date(source.takenAt).getTime() + (body?.from ?? 0) * 1000 + 500)
      const made: Capture = {
        ...mockCapture(source.gameId ? games.findIndex((g) => g.id === source.gameId) : null, at, gif ? 'photo' : 'video', gif ? null : length),
        edited: true, sourceId: source.id,
        fileName: source.fileName.replace(/\.mp4$/, ` (0.${String(Math.floor(body?.from ?? 0)).padStart(2, '0')}-0.${String(Math.floor(body?.to ?? 0)).padStart(2, '0')}).${gif ? 'gif' : 'mp4'}`),
        bytes: gif ? 2_400_000 : Math.round(length * 900_000),
      }
      captures.push(made)
      captures.sort((x, y) => y.takenAt.localeCompare(x.takenAt))
      captureRevision++
      return made
    }
    if (method === 'POST') return captureCommand(b === 'record' ? c : b)
    if (method === 'DELETE') {
      const i = captures.findIndex((x) => x.id === b)
      if (i < 0) throw new Error('Cattura non trovata.')
      captures.splice(i, 1)
      captureRevision++
      return
    }
  }
  if (method === 'GET' && a === 'audio' && b === 'outputs') {
    const kinds = ['speakers', 'headphones', 'display'] as const
    return outputs.map((d, i) => ({ id: d.id, name: d.name, detail: d.detail, kind: kinds[d.kind] ?? 'speakers', default: i === defaultOutput }))
  }
  if (method === 'GET' && a === 'games' && !b) return games.map((g) => ({ ...g, running: running.some((r) => r.id === g.id) }))
  const game = games.find((g) => g.id === b)
  if (method === 'GET' && a === 'games' && game) return { ...game, description: 'Descrizione finta del gioco, quella vera arriva da Playnite.', developers: ['Studio'] }
  if (method === 'POST' && a === 'games' && game && c === 'start') {
    if (!game.installed) throw new Error('Il gioco non è installato.')
    running.push({ id: game.id, name: game.name, source: game.source, startedAt: new Date().toISOString(), hasCover: false, hasBackground: false })
    return
  }
  if (method === 'POST' && a === 'games' && game && c === 'install') {
    if (game.installed) throw new Error('Il gioco è già installato.')
    return void libraryJob(game, 'installing')
  }
  if (method === 'POST' && a === 'games' && game && c === 'uninstall') {
    if (running.some((r) => r.id === game.id)) throw new Error('Chiudi il gioco prima di disinstallarlo.')
    return void libraryJob(game, 'uninstalling')
  }
  if (method === 'POST' && a === 'games' && game && c === 'stop-waiting') {
    if (!game.installing && !game.uninstalling) throw new Error('Playnite non lo sta installando né disinstallando.')
    clearTimeout(libraryJobs.get(game.id))
    libraryJobs.delete(game.id)
    game.installing = game.uninstalling = false
    libraryRevision++
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
      const p = { ...presetAudio, id: `p${Date.now()}`, name: body?.name || `Preset ${presets.length + 1}`, monitors: current }
      presets.push(p)
      return p
    }
    if (method === 'PATCH' && preset) return void (preset.name = body.name)
    if (method === 'PUT' && preset && path.endsWith('/audio')) {
      preset.audioOutputId = body.outputId
      preset.audioOutputName = body.outputId ? body.outputName : null
      return
    }
    if (method === 'DELETE' && preset) return void presets.splice(presets.indexOf(preset), 1)
    if (method === 'POST' && preset && path.endsWith('/apply')) {
      const previousPresetId = pending ? pending.previousPresetId : (presets.find((p) => p.monitors === current)?.id ?? null)
      if (!pending) previous = current
      current = preset.monitors
      pending = { presetId: preset.id, previousPresetId, deadline: Date.now() + 15000 }
      // Like AudioFollowsDisplays.cs, minus the rollback of the audio.
      const audioIndex = outputs.findIndex((o) => o.id === preset.audioOutputId)
      if (audioIndex >= 0) defaultOutput = audioIndex
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

