import { t } from './i18n'

const hue = (s: string) => [...s].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7)

export const placeholderGradient = (name: string, light = 42, dark = 16) => {
  const h = hue(name)
  return `linear-gradient(155deg, hsl(${h} 60% ${light}%), hsl(${(h + 40) % 360} 50% ${dark}%))`
}

// Playnite descriptions are HTML; we only want the words.
export function stripHtml(html: string) {
  return new DOMParser().parseFromString(html, 'text/html').body.textContent?.trim() ?? ''
}

// "9 h 40" / "9h 40m", "45 min", "0 min": how long, the way the stats speak.
export function duration(seconds: number) {
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return t.app.minutes(minutes)
  return t.app.hours(Math.floor(minutes / 60), minutes % 60)
}

// A yyyy-MM-dd day as a local date, without the UTC shift `new Date('2026-10-03')` would do.
export const parseDay = (day: string) => {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// Capitalises the first letter of a label only, as Italian wants: «Mercoledì 7 ottobre», not «7 Ottobre».
export const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)
