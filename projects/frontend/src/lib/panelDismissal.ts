/**
 * Panel dismissal helpers for the BuildingDetailView guidance panels.
 *
 * Dismissal is stored per-building in localStorage so that closing a helper
 * panel is respected across navigation, page refreshes, and tick-based polling.
 * A panel is kept hidden after dismissal only when the chain is healthy (complete).
 * If the chain becomes incomplete—signalling a real building or production error
 * that requires player action—the dismissal is overridden and the panel
 * reappears automatically.
 *
 * Storage schema:
 *   Key: `bdpanel_production_dismissed` — JSON string array of building IDs whose
 *        Production Chain panel has been dismissed.
 *   Key: `bdpanel_sales_dismissed`      — JSON string array of building IDs whose
 *        Sales Loop Status panel has been dismissed.
 */

export const PRODUCTION_PANEL_DISMISSED_KEY = 'bdpanel_production_dismissed'
export const SALES_PANEL_DISMISSED_KEY = 'bdpanel_sales_dismissed'

/**
 * Read the stored array of dismissed building IDs for a given localStorage key.
 * Returns an empty array if the key is missing, empty, or contains invalid JSON.
 */
export function loadDismissedIds(key: string): string[] {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as string[]) : []
  } catch {
    return []
  }
}

/**
 * Persist the array of dismissed building IDs under the given localStorage key.
 * Silently ignores errors when localStorage is unavailable (e.g. SSR or storage
 * quota exceeded).
 */
export function saveDismissedIds(key: string, ids: string[]): void {
  try {
    localStorage.setItem(key, JSON.stringify(ids))
  } catch {
    // localStorage unavailable — ignore
  }
}

/**
 * Check whether the guidance panel identified by `key` has been dismissed for
 * the given building ID.
 */
export function isBuildingPanelDismissed(key: string, buildingId: string): boolean {
  return loadDismissedIds(key).includes(buildingId)
}

/**
 * Dismiss the guidance panel identified by `key` for the given building ID.
 * Idempotent — calling multiple times for the same building has no additional
 * effect.
 */
export function dismissBuildingPanel(key: string, buildingId: string): void {
  const ids = loadDismissedIds(key)
  if (!ids.includes(buildingId)) {
    ids.push(buildingId)
    saveDismissedIds(key, ids)
  }
}

/**
 * Determine whether a guidance panel should be shown given the current dismissal
 * and chain-completeness state.
 *
 * Rules:
 * - If not dismissed → always show (panel has never been closed for this building).
 * - If dismissed AND chain is complete → keep hidden (normal play, choice respected).
 * - If dismissed AND chain is incomplete → show (error override; player action required).
 *
 * @param dismissed  Whether the player has previously dismissed this panel.
 * @param chainComplete Whether the relevant production/sales chain is fully configured.
 */
export function shouldShowPanel(dismissed: boolean, chainComplete: boolean): boolean {
  if (!dismissed) return true
  // Override dismissal when there is a real error: the chain has broken and needs attention.
  return !chainComplete
}
