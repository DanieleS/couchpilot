<template>
  <div class="page">
    <header class="page-header"><h1>Controllo</h1></header>
    <section class="soon card">
      <svg viewBox="0 0 120 60" aria-hidden="true">
        <circle cx="22" cy="30" r="18" fill="var(--accent)" />
        <rect x="48" y="12" width="36" height="36" rx="6" fill="var(--wait)" />
        <polygon points="104,12 120,48 88,48" fill="var(--run)" />
      </svg>
      <p class="title">Touchpad e tastiera in arrivo</p>
      <p class="muted">Sono il prossimo passo, dopo libreria e schermi.</p>
    </section>
  </div>
</template>

<style scoped>
.soon { margin-top: var(--size-5); padding: var(--size-6) var(--size-4); text-align: center; }
.soon svg { inline-size: 7.5rem; margin: 0 auto var(--size-4); }
.title { font-weight: 800; }
.muted { font-size: 0.8125rem; margin-top: 4px; }
</style>
