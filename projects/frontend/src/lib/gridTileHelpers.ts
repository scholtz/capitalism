/**
 * Pure helper functions for building grid tile display.
 * These functions have no Vue or i18n dependencies and can be tested in isolation.
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Minimal interface for determining which configured item a unit holds. */
export interface TileItemSource {
  productTypeId?: string | null
  resourceTypeId?: string | null
}

/** Result type for a configured item lookup. */
export interface TileItemId {
  kind: 'product' | 'resource'
  id: string
}

/** Minimal interface for determining which price metric a unit exposes. */
export interface TilePriceSource {
  minPrice?: number | null
  maxPrice?: number | null
  budget?: number | null
  brandScope?: string | null
}

/** Discriminated-union result for a unit's primary price/metric field. */
export type TileMetricResult =
  | { kind: 'minPrice'; value: number }
  | { kind: 'maxPrice'; value: number }
  | { kind: 'budget'; value: number }
  | { kind: 'scope'; value: string }

/** Fill level bucket used for color coding. */
export type FillBucket = 'empty' | 'low' | 'medium' | 'high'

// ---------------------------------------------------------------------------
// Formatters
// ---------------------------------------------------------------------------

/**
 * Format a unit quantity number for compact display.
 * Integers are shown without decimal places; fractional values show up to 2 d.p.
 *
 * @example formatUnitQuantity(60) === '60'
 * @example formatUnitQuantity(60.5) === '60.5'
 * @example formatUnitQuantity(60.50) === '60.5'
 */
export function formatUnitQuantity(value: number): string {
  if (Number.isInteger(value)) return `${value}`
  return value.toFixed(2).replace(/\.?0+$/, '')
}

/**
 * Format a 0–1 fill ratio as a human-readable percentage string.
 * Returns `'—'` for null/undefined inputs.
 *
 * @example formatPercent(0.75) === '75%'
 * @example formatPercent(null) === '—'
 */
export function formatPercent(value: number | null | undefined): string {
  if (value == null) return '—'
  return `${Math.round(value * 100)}%`
}

// ---------------------------------------------------------------------------
// Fill level
// ---------------------------------------------------------------------------

/**
 * Classify a fill ratio (0–1) into a named bucket for colour coding.
 *
 * | Bucket   | fillPercent range |
 * |----------|-------------------|
 * | 'empty'  | null / 0          |
 * | 'low'    | 0 < x < 0.30      |
 * | 'medium' | 0.30 ≤ x < 0.75   |
 * | 'high'   | ≥ 0.75            |
 */
export function getFillBucket(fillPercent: number | null | undefined): FillBucket {
  if (fillPercent == null || fillPercent <= 0) return 'empty'
  if (fillPercent < 0.3) return 'low'
  if (fillPercent < 0.75) return 'medium'
  return 'high'
}

// ---------------------------------------------------------------------------
// Item identity
// ---------------------------------------------------------------------------

/**
 * Return the configured item id (product or resource) for a grid unit, or `null`
 * if neither is set. Product takes precedence over resource.
 */
export function getUnitConfiguredItemId(unit: TileItemSource): TileItemId | null {
  if (unit.productTypeId) return { kind: 'product', id: unit.productTypeId }
  if (unit.resourceTypeId) return { kind: 'resource', id: unit.resourceTypeId }
  return null
}

// ---------------------------------------------------------------------------
// Price / metric
// ---------------------------------------------------------------------------

/**
 * Return the most commercially important metric for a unit tile, or `null` if
 * none is set. Priority order: minPrice → maxPrice → budget → brandScope.
 */
export function getUnitPriceMetric(unit: TilePriceSource): TileMetricResult | null {
  if (unit.minPrice != null) return { kind: 'minPrice', value: unit.minPrice }
  if (unit.maxPrice != null) return { kind: 'maxPrice', value: unit.maxPrice }
  if (unit.budget != null) return { kind: 'budget', value: unit.budget }
  if (unit.brandScope) return { kind: 'scope', value: unit.brandScope }
  return null
}
