/**
 * Pure helper functions for the city map lot-purchase flow.
 * Extracted from CityMapView.vue for testability.
 */

/** Returns the ownership status of a lot relative to the current player's companies. */
export function getLotStatus(
  ownerCompanyId: string | null | undefined,
  playerCompanyIds: string[],
): 'available' | 'owned' | 'yours' {
  if (!ownerCompanyId) return 'available'
  if (playerCompanyIds.includes(ownerCompanyId)) return 'yours'
  return 'owned'
}

/** Maps a lot status to its marker color for the Leaflet map. */
export function getLotMarkerColor(status: 'available' | 'owned' | 'yours'): string {
  if (status === 'available') return '#00C853'
  if (status === 'yours') return '#0047FF'
  return '#6B7280'
}

/** Formats a population-index multiplier for display, e.g. 1.42 → "1.42x". */
export function formatPopulationIndex(value: number): string {
  return value.toFixed(2) + 'x'
}

/**
 * Returns the CSS class for a population-index badge.
 * Thresholds: ≥1.8 = very-high, ≥1.3 = high, ≥0.9 = medium, else = low.
 */
export function populationIndexClass(value: number): string {
  if (value >= 1.8) return 'pop-very-high'
  if (value >= 1.3) return 'pop-high'
  if (value >= 0.9) return 'pop-medium'
  return 'pop-low'
}

/**
 * Returns the i18n key suffix for a population-index tier label.
 * Callers are expected to prepend `cityMap.populationIndex`.
 */
export function populationIndexTierKey(value: number): string {
  if (value >= 1.8) return 'VeryHigh'
  if (value >= 1.3) return 'High'
  if (value >= 0.9) return 'Medium'
  return 'Low'
}

/**
 * Returns whether the purchase action should be enabled.
 * The player must be authenticated, have at least one company, and the lot must be available.
 */
export function canPurchaseLot(
  isAuthenticated: boolean,
  playerCompanyCount: number,
  ownerCompanyId: string | null | undefined,
): boolean {
  return isAuthenticated && playerCompanyCount > 0 && !ownerCompanyId
}

/**
 * Returns the construction cost for a given building type (mirrors backend GameConstants).
 * This is charged in addition to the land purchase price.
 */
export function constructionCostForType(buildingType: string): number {
  const costs: Record<string, number> = {
    MINE: 5000,
    FACTORY: 15000,
    SALES_SHOP: 8000,
    RESEARCH_DEVELOPMENT: 25000,
    APARTMENT: 40000,
    COMMERCIAL: 20000,
    MEDIA_HOUSE: 30000,
    BANK: 50000,
    EXCHANGE: 60000,
    POWER_PLANT: 80000,
  }
  return costs[buildingType] ?? 10000
}

/**
 * Returns the construction ticks for a given building type (mirrors backend GameConstants).
 * Each tick represents one in-game hour.
 */
export function constructionTicksForType(buildingType: string): number {
  const ticks: Record<string, number> = {
    MINE: 24,
    FACTORY: 48,
    SALES_SHOP: 24,
    RESEARCH_DEVELOPMENT: 72,
    APARTMENT: 96,
    COMMERCIAL: 48,
    MEDIA_HOUSE: 48,
    BANK: 72,
    EXCHANGE: 96,
    POWER_PLANT: 120,
  }
  return ticks[buildingType] ?? 24
}

/**
 * Returns remaining construction ticks for an under-construction building.
 * Returns 0 if construction is complete or no completion tick is set.
 */
export function constructionTicksRemaining(
  completesAtTick: number | null,
  currentTick: number,
): number {
  if (completesAtTick === null) return 0
  return Math.max(0, completesAtTick - currentTick)
}

/**
 * Returns whether the purchase-confirmation form can be submitted.
 * Building type, name, and company must be selected; purchase must not already be in flight.
 */
export function canSubmitPurchaseForm(
  buildingType: string,
  /** @deprecated Building name is now optional; kept for call-site compatibility. */
  _buildingName: string,
  companyId: string,
  purchasing: boolean,
): boolean {
  return !!buildingType && !!companyId && !purchasing
}
