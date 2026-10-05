<template>
  <div class="notice" role="status">
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M2 8V6a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-6" />
      <path d="M2 12a9 9 0 0 1 8 8" />
      <path d="M2 16a5 5 0 0 1 4 4" />
      <line x1="2" x2="2.01" y1="20" y2="20" />
    </svg>
    <div>
      <p class="title">Stream Vibepollo in corso</p>
      <p class="text">Gli schermi li sta gestendo Vibepollo. I preset tornano disponibili quando lo stream finisce.</p>
    </div>
  </div>
</template>

<style scoped>
.notice {
  display: flex;
  gap: var(--size-3);
  padding: var(--size-3);
  border-radius: var(--r-card);
  background: color-mix(in srgb, var(--wait) 12%, var(--card));
  box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--wait) 45%, transparent);
  color: var(--wait);
}
svg { flex-shrink: 0; margin-top: 1px; }
.title { font-size: 0.8125rem; font-weight: 800; }
.text { font-size: 0.75rem; line-height: 1.4; color: var(--ink-2); margin-top: 2px; }
</style>
