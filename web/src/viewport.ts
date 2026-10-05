import { onScopeDispose, ref } from 'vue'

// Where the phone's on-screen keyboard is. Browsers mostly overlay it on the page instead of
// shrinking it, so fixed elements at the bottom end up underneath; the visual viewport is
// what's still visible above it.
export function useVisualViewport() {
  const vv = window.visualViewport
  const height = ref(vv?.height ?? window.innerHeight)
  // Where the visible area starts, in layout-viewport pixels: iOS pans it to show the field.
  const offsetTop = ref(0)
  // How far above the layout viewport's bottom edge the visible area ends.
  const bottomOffset = ref(0)
  const keyboardHeight = ref(0)
  // The tallest the visible area has been at this width: the height with no keyboard.
  let baseline = height.value
  let width = vv?.width ?? window.innerWidth

  function update() {
    if (!vv) return
    if (Math.abs(vv.width - width) > 1) {
      // Rotated: the old baseline means nothing now.
      width = vv.width
      baseline = vv.height
    }
    baseline = Math.max(baseline, vv.height)
    height.value = vv.height
    offsetTop.value = vv.offsetTop
    bottomOffset.value = Math.max(0, window.innerHeight - vv.height - vv.offsetTop)
    keyboardHeight.value = Math.max(0, baseline - vv.height)
  }

  vv?.addEventListener('resize', update)
  vv?.addEventListener('scroll', update)
  onScopeDispose(() => {
    vv?.removeEventListener('resize', update)
    vv?.removeEventListener('scroll', update)
  })
  update()

  return { height, offsetTop, bottomOffset, keyboardHeight }
}
