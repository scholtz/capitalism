/**
 * Pure helpers for rendering public-sales analytics data.
 * All functions are free of Vue/i18n context so they can be unit-tested in Node.
 */

/**
 * Maps a demand-signal value to the CSS modifier class suffix used by the
 * market-intelligence demand card.
 *
 * Input examples:  "STRONG" | "MODERATE" | "WEAK" | "SUPPLY_CONSTRAINED" | "NO_DATA"
 * Output examples: "mi-demand-strong" | "mi-demand-supply-constrained" | "mi-demand-no-data"
 */
export function demandSignalClass(signal: string): string {
  return `mi-demand-${signal.toLowerCase().replace(/_/g, '-')}`
}

/**
 * Returns the CSS class for the elasticity-index value display.
 * "mi-elastic-high" — highly elastic (index < −1.5), price changes have large volume impact.
 * "mi-elastic-low"  — inelastic (index > −0.5), price changes have small volume impact.
 * ""                — normal range or null.
 */
export function elasticityClass(elasticityIndex: number | null | undefined): string {
  if (elasticityIndex == null) return ''
  if (elasticityIndex < -1.5) return 'mi-elastic-high'
  if (elasticityIndex > -0.5) return 'mi-elastic-low'
  return ''
}

/**
 * Maps a demand-driver impact value to the CSS class for the driver row.
 * Input: "POSITIVE" | "NEUTRAL" | "NEGATIVE"
 * Output: "mi-driver-positive" | "mi-driver-neutral" | "mi-driver-negative"
 */
export function demandDriverClass(impact: string): string {
  return `mi-driver-${impact.toLowerCase()}`
}

/**
 * Returns the directional arrow icon for a demand-driver impact value.
 * POSITIVE → "↑", NEGATIVE → "↓", anything else → "→"
 */
export function demandDriverIcon(impact: string): string {
  if (impact === 'POSITIVE') return '↑'
  if (impact === 'NEGATIVE') return '↓'
  return '→'
}

/**
 * Returns true when the quick-price-update panel should show the "raising price"
 * directional hint (new price is higher than the currently configured price and
 * elasticity data is available).
 */
export function isRaisingPrice(
  quickPriceInput: number | null,
  currentPrice: number,
  elasticityIndex: number | null | undefined,
): boolean {
  return (
    elasticityIndex != null &&
    quickPriceInput != null &&
    currentPrice > 0 &&
    quickPriceInput > currentPrice
  )
}

/**
 * Returns true when the quick-price-update panel should show the "lowering price"
 * directional hint.
 */
export function isLoweringPrice(
  quickPriceInput: number | null,
  currentPrice: number,
  elasticityIndex: number | null | undefined,
): boolean {
  return (
    elasticityIndex != null &&
    quickPriceInput != null &&
    currentPrice > 0 &&
    quickPriceInput < currentPrice
  )
}

/**
 * Formats the elasticity index for display (e.g., "-1.23").
 * Returns an empty string when the value is null.
 */
export function formatElasticityIndex(elasticityIndex: number | null | undefined): string {
  if (elasticityIndex == null) return ''
  return elasticityIndex.toFixed(2)
}

/**
 * Computes the demand-signal utilisation percentage string for display.
 * Returns "–" when no history is available.
 */
export function formatUtilization(recentUtilization: number, hasHistory: boolean): string {
  if (!hasHistory) return '–'
  return `${Math.round(recentUtilization * 100)}%`
}
