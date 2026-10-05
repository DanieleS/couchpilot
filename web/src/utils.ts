const hue = (s: string) => [...s].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7)

export const placeholderGradient = (name: string, light = 42, dark = 16) => {
  const h = hue(name)
  return `linear-gradient(155deg, hsl(${h} 60% ${light}%), hsl(${(h + 40) % 360} 50% ${dark}%))`
}

// Playnite descriptions are HTML; we only want the words.
export function stripHtml(html: string) {
  return new DOMParser().parseFromString(html, 'text/html').body.textContent?.trim() ?? ''
}
