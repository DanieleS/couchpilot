import type { AudioDeviceKind } from '../types'
import type { IconName } from './AppIcon.vue'

export const deviceIcon = (kind: AudioDeviceKind): IconName =>
  ({ speakers: 'speaker', headphones: 'headphones', display: 'tv', mic: 'mic' } as const)[kind] ?? 'speaker'
