<script setup lang="ts">
import {
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogOverlay,
  AlertDialogPortal,
  AlertDialogRoot,
  AlertDialogTitle,
} from 'reka-ui'
import AppIcon, { type IconName } from './AppIcon.vue'

defineProps<{ title: string; description: string; confirmLabel: string; icon?: IconName }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ confirm: [] }>()
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
          <AlertDialogCancel class="btn btn-ghost">Annulla</AlertDialogCancel>
          <AlertDialogAction class="btn btn-danger" @click="emit('confirm')">{{ confirmLabel }}</AlertDialogAction>
        </div>
      </AlertDialogContent>
    </AlertDialogPortal>
  </AlertDialogRoot>
</template>
