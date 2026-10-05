const hue = (s: string) => [...s].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7)

export const placeholderGradient = (name: string, light = 42, dark = 16) => {
  const h = hue(name)
  return `linear-gradient(155deg, hsl(${h} 60% ${light}%), hsl(${(h + 40) % 360} 50% ${dark}%))`
}

// Playnite descriptions are HTML; we only want the words.
export function stripHtml(html: string) {
  return new DOMParser().parseFromString(html, 'text/html').body.textContent?.trim() ?? ''
}

// "9 h 40", "45 min", "0 min": how long, the way the stats speak.
export function duration(seconds: number) {
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} min`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m ? `${h} h ${String(m).padStart(2, '0')}` : `${h} h`
}

// A yyyy-MM-dd day as a local date, without the UTC shift `new Date('2026-10-03')` would do.
export const parseDay = (day: string) => {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

// Italian capitalises the first word of a label only: «Mercoledì 7 ottobre», not «7 Ottobre».
export const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)
