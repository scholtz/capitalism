import { nextTick } from 'vue'

/**
 * Saves the current window scroll position and provides a function to
 * restore it after the next Vue DOM update cycle.
 *
 * Used by management screens (BuildingDetailView, etc.) to keep the
 * player's scroll context when background tick refreshes update live data.
 *
 * Usage:
 *   const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()
 *   const saved = saveScrollPosition()
 *   await loadData()
 *   await restoreScrollPosition(saved)
 */
export function useScrollPreservation() {
  function saveScrollPosition(): { x: number; y: number } {
    return { x: window.scrollX, y: window.scrollY }
  }

  async function restoreScrollPosition(position: { x: number; y: number }): Promise<void> {
    await nextTick()
    // Use ScrollToOptions with 'instant' behavior where supported; fall back to
    // the two-argument form for browsers that only support the legacy signature.
    try {
      window.scrollTo({ left: position.x, top: position.y, behavior: 'instant' })
    } catch {
      window.scrollTo(position.x, position.y)
    }
  }

  return { saveScrollPosition, restoreScrollPosition }
}
