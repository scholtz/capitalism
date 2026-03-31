/** Overhead risk levels used for status badge coloring and accessible labels. */
export type OverheadStatus = 'low' | 'medium' | 'high'

/**
 * Thresholds for overhead status categories.
 * - low:    0 – 14.9%  (green)
 * - medium: 15 – 34.9% (amber)
 * - high:   35 – 50%   (red)
 */
export const OVERHEAD_MEDIUM_THRESHOLD = 0.15
export const OVERHEAD_HIGH_THRESHOLD = 0.35

/**
 * Returns a three-level risk status for a given administration overhead rate (0–0.5).
 */
export function getOverheadStatus(rate: number): OverheadStatus {
  if (rate >= OVERHEAD_HIGH_THRESHOLD) return 'high'
  if (rate >= OVERHEAD_MEDIUM_THRESHOLD) return 'medium'
  return 'low'
}
