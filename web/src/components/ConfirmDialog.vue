<script setup lang="ts">
import {
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogOverlay,
  AlertDialogPortal,
  AlertDialogRoot,
  AlertDialogTitle,
} from 'reka-ui'
import { t } from '../i18n'
import AppIcon, { type IconName } from './AppIcon.vue'

defineProps<{ title: string; description: string; confirmLabel: string; icon?: IconName }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ confirm: [] }>()

const confirm = () => {
  emit('confirm')
  open.value = false
}
</script>

<template>
  <AlertDialogRoot v-model:open="open">
    <AlertDialogPortal>
      <AlertDialogOverlay class="sheet-overlay" />
      <AlertDialogContent class="dialog card">
        <span v-if="icon" class="badge"><AppIcon :name="icon" :size="22" /></span>
        <AlertDialogTitle class="title">{{ title }}</AlertDialogTitle>
        <AlertDialogDescription class="desc">{{ description }}</AlertDialogDescription>
        <div class="actions">
          <AlertDialogCancel class="btn btn-ghost">{{ t.app.cancel }}</AlertDialogCancel>
          <!-- Not AlertDialogAction: it closes the dialog before our click handler runs, and the
               parent clears what's being confirmed on close, so `confirm` came too late. -->
          <button class="btn btn-danger" @click="confirm">{{ confirmLabel }}</button>
        </div>
      </AlertDialogContent>
    </AlertDialogPortal>
  </AlertDialogRoot>
</template>
