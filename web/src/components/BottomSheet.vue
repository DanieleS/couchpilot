<script setup lang="ts">
import { DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle, VisuallyHidden } from 'reka-ui'

defineProps<{ title: string }>()
const open = defineModel<boolean>('open', { required: true })
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="sheet-overlay" />
      <DialogContent class="sheet" :aria-describedby="undefined">
        <span class="grabber" aria-hidden="true" />
        <VisuallyHidden><DialogTitle>{{ title }}</DialogTitle><DialogDescription>{{ title }}</DialogDescription></VisuallyHidden>
        <slot />
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
