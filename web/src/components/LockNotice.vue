<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api'
import { t } from '../i18n'
import { useCouchPilot } from '../store'

// The PC is on the lock screen. Shown above every tab, whether or not this phone may unlock it:
// with the permission it carries the Unlock button, without it it says where to turn that on.
// On a successful unlock the status loses its locked flag and this goes away on its own.
const { status, act } = useCouchPilot()
const unlocking = ref(false)
async function unlockPc() {
  unlocking.value = true
  await act(() => api.unlock())
  unlocking.value = false
}
</script>

<template>
  <div class="notice" role="status">
    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" />
    </svg>
    <div class="body">
      <p class="title">{{ t.app.locked.title }}</p>
      <p class="text">{{ status?.unlock ? t.app.locked.canUnlock : t.app.locked.cannotUnlock }}</p>
    </div>
    <button v-if="status?.unlock" class="btn btn-ink unlock" :disabled="unlocking" @click="unlockPc">
      <span v-if="unlocking" class="spinner" aria-hidden="true" />
      {{ unlocking ? t.app.locked.unlocking : t.app.locked.button }}
    </button>
  </div>
</template>

<style scoped>
.notice {
  display: flex;
  align-items: center;
  gap: var(--size-3);
  padding: var(--size-3);
  border-radius: var(--r-card);
  background: color-mix(in srgb, var(--accent) 12%, var(--card));
  box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 45%, transparent);
}
svg { flex-shrink: 0; color: var(--accent); }
.body { flex: 1; min-width: 0; }
.title { font-size: 0.8125rem; font-weight: 800; }
.text { font-size: 0.75rem; line-height: 1.4; color: var(--ink-2); margin-top: 2px; }
.unlock { flex-shrink: 0; padding: 0.6rem 0.9rem; font-size: 0.8125rem; }
</style>
