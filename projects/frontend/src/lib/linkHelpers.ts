/**
 * Pure helper functions for directional building-unit link logic.
 * These are extracted from BuildingDetailView so they can be unit-tested
 * independently of Vue reactivity and i18n.
 */

export type HorizontalLinkState = 'none' | 'forward' | 'backward' | 'both'
export type VerticalLinkState = 'none' | 'forward' | 'backward' | 'both'

/** Minimal shape that carries the link flags used by these helpers. */
export interface LinkFlagSource {
  gridX: number
  gridY: number
  linkRight?: boolean
  linkLeft?: boolean
  linkDown?: boolean
  linkUp?: boolean
  linkDownRight?: boolean
  linkDownLeft?: boolean
  linkUpRight?: boolean
  linkUpLeft?: boolean
}

function unitAt<T extends LinkFlagSource>(
  units: T[],
  x: number,
  y: number,
): T | undefined {
  return units.find((u) => u.gridX === x && u.gridY === y)
}

/**
 * Returns the directional link state between cell (x,y) and (x+1,y):
 *   forward  – only left unit sends right (A→B)
 *   backward – only right unit sends left (B→A)
 *   both     – both flags set (bidirectional)
 *   none     – no connection
 */
export function getHorizontalLinkState<T extends LinkFlagSource>(
  units: T[],
  x: number,
  y: number,
): HorizontalLinkState {
  const left = unitAt(units, x, y)
  const right = unitAt(units, x + 1, y)
  const hasForward = !!left?.linkRight
  const hasBackward = !!right?.linkLeft
  if (hasForward && hasBackward) return 'both'
  if (hasForward) return 'forward'
  if (hasBackward) return 'backward'
  return 'none'
}

/**
 * Returns the directional link state between cell (x,y) and (x,y+1):
 *   forward  – only top unit sends down (A→B)
 *   backward – only bottom unit sends up (B→A)
 *   both     – bidirectional
 *   none     – no connection
 */
export function getVerticalLinkState<T extends LinkFlagSource>(
  units: T[],
  x: number,
  y: number,
): VerticalLinkState {
  const top = unitAt(units, x, y)
  const bottom = unitAt(units, x, y + 1)
  const hasForward = !!top?.linkDown
  const hasBackward = !!bottom?.linkUp
  if (hasForward && hasBackward) return 'both'
  if (hasForward) return 'forward'
  if (hasBackward) return 'backward'
  return 'none'
}

/** Unicode arrow character representing horizontal directional state. */
export function getHorizontalLinkArrow(state: HorizontalLinkState): string {
  if (state === 'forward') return '▶'
  if (state === 'backward') return '◀'
  if (state === 'both') return '↔'
  return ''
}

/** Unicode arrow character representing vertical directional state. */
export function getVerticalLinkArrow(state: VerticalLinkState): string {
  if (state === 'forward') return '▼'
  if (state === 'backward') return '▲'
  if (state === 'both') return '↕'
  return ''
}

/**
 * Applies the 4-state horizontal toggle cycle to a pair of mutable unit objects:
 *   none → forward (left sends right) → backward (right sends left) → both → none
 * Mutates the passed objects in place.
 */
export function applyHorizontalLinkCycle<T extends LinkFlagSource>(
  left: T,
  right: T,
  current: HorizontalLinkState,
): void {
  if (current === 'none') {
    left.linkRight = true
    right.linkLeft = false
  } else if (current === 'forward') {
    left.linkRight = false
    right.linkLeft = true
  } else if (current === 'backward') {
    left.linkRight = true
    right.linkLeft = true
  } else {
    left.linkRight = false
    right.linkLeft = false
  }
}

/**
 * Applies the 4-state vertical toggle cycle to a pair of mutable unit objects:
 *   none → forward (top sends down) → backward (bottom sends up) → both → none
 * Mutates the passed objects in place.
 */
export function applyVerticalLinkCycle<T extends LinkFlagSource>(
  top: T,
  bottom: T,
  current: VerticalLinkState,
): void {
  if (current === 'none') {
    top.linkDown = true
    bottom.linkUp = false
  } else if (current === 'forward') {
    top.linkDown = false
    bottom.linkUp = true
  } else if (current === 'backward') {
    top.linkDown = true
    bottom.linkUp = true
  } else {
    top.linkDown = false
    bottom.linkUp = false
  }
}

// ---------------------------------------------------------------------------
// Diagonal link helpers
// ---------------------------------------------------------------------------

/**
 * Represents the active diagonal link state of a 2×2 block rooted at (x,y):
 *   tl-br  – top-left sends to bottom-right (↘)
 *   tr-bl  – top-right sends to bottom-left (↙)
 *   cross  – both diagonals active
 *   none   – no diagonal connection
 */
export type DiagonalLinkState = 'none' | 'tl-br' | 'tr-bl' | 'cross'

/**
 * Returns the diagonal link state of the 2×2 block whose top-left corner is (x,y).
 * Inspects linkDownRight on (x,y) and linkDownLeft on (x+1,y).
 */
export function getDiagonalLinkState<T extends LinkFlagSource>(
  units: T[],
  x: number,
  y: number,
): DiagonalLinkState {
  const topLeft = unitAt(units, x, y)
  const topRight = unitAt(units, x + 1, y)
  const hasTlBr = !!topLeft?.linkDownRight
  const hasTrBl = !!topRight?.linkDownLeft
  if (hasTlBr && hasTrBl) return 'cross'
  if (hasTlBr) return 'tl-br'
  if (hasTrBl) return 'tr-bl'
  return 'none'
}

/** Unicode symbol describing the diagonal link state for display. */
export function getDiagonalLinkLabel(state: DiagonalLinkState): string {
  if (state === 'tl-br') return '↘'
  if (state === 'tr-bl') return '↙'
  if (state === 'cross') return '✕'
  return ''
}

/**
 * Applies the 4-state diagonal toggle cycle to a 2×2 group of mutable unit objects:
 *   none → tl-br (topLeft→bottomRight ↘) → tr-bl (topRight→bottomLeft ↙) → cross (both) → none
 * Mutates the passed objects in place.
 */
export function applyDiagonalLinkCycle<T extends LinkFlagSource>(
  topLeft: T,
  topRight: T,
  bottomLeft: T,
  bottomRight: T,
  current: DiagonalLinkState,
): void {
  // Clear all diagonal flags first
  topLeft.linkDownRight = false
  bottomRight.linkUpLeft = false
  topRight.linkDownLeft = false
  bottomLeft.linkUpRight = false

  if (current === 'none') {
    topLeft.linkDownRight = true
    bottomRight.linkUpLeft = true
  } else if (current === 'tl-br') {
    topRight.linkDownLeft = true
    bottomLeft.linkUpRight = true
  } else if (current === 'tr-bl') {
    topLeft.linkDownRight = true
    bottomRight.linkUpLeft = true
    topRight.linkDownLeft = true
    bottomLeft.linkUpRight = true
  }
  // 'cross' → stays cleared (all flags already set to false above)
}
