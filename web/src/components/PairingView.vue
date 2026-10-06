<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { usePairing } from '../pairing'
import AppIcon from './AppIcon.vue'

// The screen a phone sees until the PC knows it: ask for the PIN, read it off the TV, type it.
const { step, name, pin, error, attemptsLeft, secondsLeft, expired, requestPin, submit, reset } = usePairing()

const field = ref<HTMLInputElement | null>(null)
const focusField = () => field.value?.focus()
watch(step, async (s) => {
  if (s !== 'pin') return
  await nextTick()
  focusField()
})
// Six digits typed: no button to look for.
watch(pin, (p) => {
  pin.value = p.replace(/\D/g, '').slice(0, 6)
  if (pin.value.length === 6) submit()
})
watch(expired, (e) => e && reset())

const cells = [0, 1, 2, 3, 4, 5]
const clock = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
</script>

<template>
  <div class="pairing">
    <div class="card box">
      <span class="mark" aria-hidden="true"><span class="dot" /><span class="bar" /></span>
      <h1 class="title">Collega questo telefono</h1>

      <template v-if="step === 'start' || step === 'requesting'">
        <p class="lead">
          Il PC chiede un PIN ai telefoni nuovi. Quando lo chiedi, compare sullo schermo del PC per due minuti:
          scrivilo qui e questo telefono sarà sempre il benvenuto.
        </p>
        <label class="name">
          <span>Come chiamare questo telefono</span>
          <input v-model="name" maxlength="60" autocomplete="off" enterkeyhint="done" @keydown.enter="requestPin" />
        </label>
        <button class="btn btn-ink btn-block go" :disabled="step === 'requesting' || !name.trim()" @click="requestPin">
          <span v-if="step === 'requesting'" class="spinner" aria-hidden="true" />
          <AppIcon v-else name="tv" :size="18" />
          {{ step === 'requesting' ? 'Chiedo al PC…' : 'Mostra il PIN sul PC' }}
        </button>
      </template>

      <template v-else>
        <p class="lead">Scrivi il PIN che vedi sullo schermo del PC.</p>
        <button class="cells" :class="{ shake: !!error && step === 'pin', busy: step === 'checking' }" type="button" aria-hidden="true" @click="focusField">
          <span v-for="i in cells" :key="i" class="cell" :class="{ on: i === pin.length, full: i < pin.length }">{{ pin[i] ?? '' }}</span>
        </button>
        <input
          ref="field"
          v-model="pin"
          class="hidden-field"
          inputmode="numeric"
          pattern="[0-9]*"
          autocomplete="one-time-code"
          maxlength="6"
          aria-label="PIN di sei cifre"
          :disabled="step === 'checking'"
        />
        <p class="countdown tabular">
          <template v-if="step === 'checking'">Controllo…</template>
          <template v-else>Il PIN vale ancora {{ clock(secondsLeft) }}</template>
        </p>
        <button class="again" @click="reset">Chiedine un altro</button>
      </template>

      <p v-if="error" class="error" role="alert">
        {{ error }}
      </p>
      <p v-else-if="attemptsLeft !== null && step === 'pin'" class="hint">{{ attemptsLeft }} {{ attemptsLeft === 1 ? 'tentativo' : 'tentativi' }} rimasti</p>
    </div>
    <p class="foot">Si cambia in Playnite: Componenti aggiuntivi → Impostazioni estensioni → CouchPilot → Telefoni.</p>
  </div>
</template>

<style scoped>
.pairing {
  min-block-size: 100dvh;
  display: grid;
  grid-template-rows: 1fr auto;
  align-items: center;
  padding: var(--safe-top) var(--gutter) var(--safe-bottom);
}
.box { padding: var(--size-6) var(--size-4) var(--size-4); }
.mark { position: relative; display: block; inline-size: 2.5rem; block-size: 2.5rem; }
.dot { position: absolute; inset: 0 auto auto 0; inline-size: 1.75rem; block-size: 1.75rem; border-radius: var(--radius-round); background: var(--accent); }
.bar { position: absolute; inset: auto 0 0 auto; inline-size: 1.75rem; block-size: 0.75rem; border-radius: 3px; background: var(--wait); }
.title { margin-top: var(--size-4); font-size: 1.75rem; font-weight: 800; letter-spacing: -0.03em; line-height: 1.05; }
.lead { margin-top: var(--size-3); font-size: 0.875rem; line-height: 1.5; color: var(--ink-2); }
.name { display: grid; gap: 6px; margin-top: var(--size-4); font-size: 0.6875rem; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--ink-2); }
.name input {
  padding: 0.75rem var(--size-3);
  border: 0;
  border-radius: var(--r-field);
  background: var(--well);
  box-shadow: inset 0 0 0 1.5px var(--line);
  font-size: 1rem;
  font-weight: 500;
  letter-spacing: 0;
  text-transform: none;
  outline: none;
}
.name input:focus { box-shadow: inset 0 0 0 2px var(--wait); }
.go { margin-top: var(--size-4); padding-block: 0.9rem; font-size: 0.9375rem; }

.cells { display: grid; grid-template-columns: repeat(6, 1fr); gap: 6px; inline-size: 100%; margin-top: var(--size-4); }
.cells.busy { opacity: 0.5; }
.cell {
  display: grid;
  place-items: center;
  block-size: 3.5rem;
  border-radius: var(--r-inner);
  background: var(--well);
  box-shadow: inset 0 0 0 1.5px var(--line);
  font-size: 1.5rem;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
}
.cell.on { box-shadow: inset 0 0 0 2px var(--wait); }
.cell.full { background: var(--card); }
/* Fourth cell gets a little air: «482 913», as on the PC. */
.cell:nth-child(4) { margin-left: 6px; }
.hidden-field { position: absolute; opacity: 0; inline-size: 1px; block-size: 1px; pointer-events: none; }
.countdown { margin-top: var(--size-3); text-align: center; font-size: 0.8125rem; font-weight: 650; color: var(--ink-2); }
.again { display: block; margin: var(--size-3) auto 0; font-size: 0.75rem; font-weight: 700; color: var(--ink-2); text-decoration: underline; text-underline-offset: 3px; }
.error { margin-top: var(--size-3); padding: 10px var(--size-3); border-radius: var(--r-inner); background: color-mix(in srgb, var(--danger) 14%, transparent); color: var(--danger); font-size: 0.8125rem; font-weight: 650; line-height: 1.4; }
.hint { margin-top: var(--size-3); text-align: center; font-size: 0.75rem; font-weight: 600; color: var(--ink-2); }
.foot { margin-top: var(--size-4); font-size: 0.6875rem; line-height: 1.45; text-align: center; color: var(--ink-2); }

@keyframes shake { 20%, 60% { transform: translateX(-6px); } 40%, 80% { transform: translateX(6px); } }
.shake { animation: shake 320ms var(--ease-out-2); }
</style>
