import app from './app'
import control from './control'
import games from './games'
import stats from './stats'
import achievements from './achievements'
import captures from './captures'
import audio from './audio'
import displays from './displays'

// The phone's own language picks the app's: Italian if it comes first among the ones the
// browser asks for, English otherwise. The PC answers in the same one, from Accept-Language.
export type Lang = 'en' | 'it'

const preferred = (navigator.languages?.length ? navigator.languages : [navigator.language]).map((l) => l.toLowerCase())
const first = preferred.find((l) => l.startsWith('it') || l.startsWith('en'))
export const lang: Lang = first?.startsWith('it') ? 'it' : 'en'

// What dates and numbers are formatted with: the phone's own English (en-GB writes 7 October,
// en-US October 7), or Italian.
export const locale = lang === 'it' ? 'it-IT' : first ?? 'en-US'

document.documentElement.lang = lang

// Every area keeps its strings in its own file, { en, it }, with `it` typed after `en` so a
// missing translation doesn't build. A string with numbers or names in it is a function.
export const t = {
  app: app[lang],
  control: control[lang],
  games: games[lang],
  stats: stats[lang],
  achievements: achievements[lang],
  captures: captures[lang],
  audio: audio[lang],
  displays: displays[lang],
}

// "1 game", "2 games": the count goes in front of the word that agrees with it.
export const plural = (n: number, one: string, other: string) => `${n.toLocaleString(locale)} ${n === 1 ? one : other}`
