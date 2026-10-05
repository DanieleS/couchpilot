import { onScopeDispose, ref } from 'vue'
import { api } from './api'
import { onChanged } from './live'
import { Topic } from './protocol'
import { useCouchPilot } from './store'
import type { NowPlaying } from './types'

// What's playing on the PC, while a component wants it: fetched now, and again whenever the
// live socket says the media changed (docs/protocol.md, CHANGED media).
export function useNowPlaying() {
  const { error } = useCouchPilot()
  const media = ref<NowPlaying | null>(null)
  // When `media` was received, in phone time: the position moves on from there while playing.
  const receivedAt = ref(Date.now())

  let fetching = false
  let again = false
  async function refresh() {
    if (fetching) return void (again = true)
    fetching = true
    try {
      media.value = await api.media()
      receivedAt.value = Date.now()
    } catch {
    } finally {
      fetching = false
      if (again) {
        again = false
        refresh()
      }
    }
  }
  refresh()
  const stop = onChanged((topics) => topics & Topic.media && refresh())
  onScopeDispose(stop)

  const command = (fn: () => Promise<void>) => fn().catch((e) => (error.value = (e as Error).message))

  return {
    media,
    receivedAt,
    playPause: () => command(api.mediaPlayPause),
    next: () => command(api.mediaNext),
    previous: () => command(api.mediaPrevious),
  }
}

// Windows names apps by executable or package id; turn the usual ones into words.
const known: Record<string, string> = {
  spotify: 'Spotify', msedge: 'Edge', chrome: 'Chrome', firefox: 'Firefox', zunemusic: 'Lettore multimediale',
  vlc: 'VLC', foobar2000: 'foobar2000', musicbee: 'MusicBee', tidal: 'TIDAL', applemusic: 'Apple Music',
}
export function appName(id: string | null) {
  if (!id) return null
  const base = id.split('!')[0].split('_')[0].replace(/\.exe$/i, '')
  const last = base.split('.').pop() ?? base
  return known[last.toLowerCase()] ?? last
}
