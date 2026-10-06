<script setup lang="ts">
import { DialogClose, DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle } from 'reka-ui'
import { ref, watch } from 'vue'
import { t } from '../i18n'

const props = defineProps<{ title: string; description?: string; initial?: string; confirmLabel: string }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ submit: [name: string] }>()

const name = ref('')
watch(open, (o) => o && (name.value = props.initial ?? ''))

function submit() {
  if (!name.value.trim()) return
  emit('submit', name.value.trim())
  open.value = false
}
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="sheet-overlay" />
      <DialogContent class="dialog card" @open-auto-focus.prevent="($event.target as HTMLElement)?.querySelector('input')?.focus()">
        <DialogTitle class="title">{{ title }}</DialogTitle>
        <DialogDescription class="desc">{{ description ?? t.app.namePrompt.description }}</DialogDescription>
        <form @submit.prevent="submit">
          <input v-model="name" class="field" :placeholder="t.app.namePrompt.placeholder" maxlength="40" autocomplete="off" />
          <div class="actions">
            <DialogClose class="btn btn-ghost" type="button">{{ t.app.cancel }}</DialogClose>
            <button class="btn btn-ink" type="submit" :disabled="!name.trim()">{{ confirmLabel }}</button>
          </div>
        </form>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
.field {
  display: block;
  inline-size: 100%;
  margin-top: var(--size-4);
  padding: 0.75rem var(--size-3);
  border: 0;
  border-radius: var(--r-field);
  background: var(--well);
  box-shadow: inset 0 0 0 1.5px var(--line);
  font-size: 1rem;
  outline: none;
}
.field:focus { box-shadow: inset 0 0 0 2px var(--wait); }
</style>
