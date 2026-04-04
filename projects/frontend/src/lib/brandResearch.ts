/**
 * Pure helper functions for Marketing Brand Quality research display.
 * These functions have no Vue or i18n dependencies and can be tested in isolation.
 * Note: all string return values are hardcoded English. Callers that need
 * localised output should map the result through the app's t() translation helper.
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Valid brand research scope strings matching backend BrandScope constants. */
export type BrandScopeKey = 'PRODUCT' | 'CATEGORY' | 'COMPANY'

// ---------------------------------------------------------------------------
// Scope label helpers
// ---------------------------------------------------------------------------

/** Returns a short English display label for the given brand scope key. */
export function getBrandScopeShortLabel(scope: string | null | undefined): string {
  switch (scope) {
    case 'PRODUCT':
      return 'Product'
    case 'CATEGORY':
      return 'Category'
    case 'COMPANY':
      return 'Company'
    default:
      return scope ?? 'None'
  }
}

/**
 * Returns an explanatory sentence describing what a given research scope means
 * in business terms. Used in configuration panels and tooltips.
 */
export function getBrandScopeExplanation(scope: string | null | undefined): string {
  switch (scope) {
    case 'PRODUCT':
      return 'Improves marketing efficiency for one specific product. Narrowest scope — strongest benefit for that product line.'
    case 'CATEGORY':
      return 'Improves marketing efficiency for all products in the same industry category. Good balance of focus and breadth.'
    case 'COMPANY':
      return 'Improves marketing efficiency for all products company-wide. Broadest scope — moderate benefit for every campaign.'
    default:
      return 'Select a scope to see what this research will improve.'
  }
}

// ---------------------------------------------------------------------------
// Efficiency multiplier formatting
// ---------------------------------------------------------------------------

/**
 * Formats a marketing efficiency multiplier value as a display string.
 * Values < 1 are clamped to 1.0× (baseline).
 */
export function formatEfficiencyMultiplier(value: number): string {
  const clamped = Math.max(1, value)
  return `${clamped.toFixed(2)}×`
}

/**
 * Returns a 0-100 progress percentage representing how much above baseline
 * the efficiency multiplier is, capped at the maximum of 3.0.
 */
export function efficiencyMultiplierProgress(value: number, maxMultiplier = 3): number {
  const above = Math.max(0, value - 1)
  const cap = Math.max(1, maxMultiplier - 1)
  return Math.min(100, (above / cap) * 100)
}

// ---------------------------------------------------------------------------
// Scope validation helpers
// ---------------------------------------------------------------------------

/** Returns true if the given scope requires an anchor product to be selected. */
export function scopeRequiresProduct(scope: string | null | undefined): boolean {
  return scope === 'PRODUCT' || scope === 'CATEGORY'
}
