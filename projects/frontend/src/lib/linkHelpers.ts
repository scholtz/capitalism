/**
 * Pure helper functions for directional building-unit link logic.
 * These are extracted from BuildingDetailView so they can be unit-tested
 * independently of Vue reactivity and i18n.
 */

/**
 * Horizontal link state between cell (x,y) and (x+1,y).
 * 'both' is retained for reading legacy persisted data only – new edits never produce it.
 */
export type HorizontalLinkState = 'none' | 'forward' | 'backward' | 'both'

/**
 * Vertical link state between cell (x,y) and (x,y+1).
 * 'both' is retained for reading legacy persisted data only – new edits never produce it.
 */
export type VerticalLinkState = 'none' | 'forward' | 'backward' | 'both'

/** Minimal shape that carries the link flags used by these helpers. */
export interface LinkFlagSource {
  gridX: number
  gridY: number
  /** Optional unit type used for smart default-direction inference. */
  unitType?: string
  linkRight?: boolean
  linkLeft?: boolean
  linkDown?: boolean
  linkUp?: boolean
  linkDownRight?: boolean
  linkDownLeft?: boolean
  linkUpRight?: boolean
  linkUpLeft?: boolean
}

// ---------------------------------------------------------------------------
// Unit-type role constants used for smart direction defaults
// ---------------------------------------------------------------------------

/** Unit types that are supply origins – resources flow OUT from these by default. */
const SUPPLY_ORIGIN_TYPES = ['PURCHASE', 'MINING']

/** Unit types that are terminal sinks – resources flow INTO these by default. */
const SINK_TYPES = ['PUBLIC_SALES', 'B2B_SALES']

/**
 * Infers the natural direction when first creating a horizontal link.
 * Returns 'forward' (left→right) or 'backward' (right→left).
 */
export function inferHorizontalDefault(leftType?: string, rightType?: string): 'forward' | 'backward' {
  // Purchase/mining units push outward → 'forward' if on the left, 'backward' if on the right
  if (leftType && SUPPLY_ORIGIN_TYPES.includes(leftType)) return 'forward'
  if (rightType && SUPPLY_ORIGIN_TYPES.includes(rightType)) return 'backward'
  // Sales units pull inward → resources flow *into* them
  if (rightType && SINK_TYPES.includes(rightType)) return 'forward'
  if (leftType && SINK_TYPES.includes(leftType)) return 'backward'
  // Manufacturing/storage positioned left of sales-like units
  if (rightType === 'PUBLIC_SALES' || rightType === 'B2B_SALES') return 'forward'
  if (leftType === 'PUBLIC_SALES' || leftType === 'B2B_SALES') return 'backward'
  return 'forward'
}

/**
 * Infers the natural direction when first creating a vertical link.
 * Returns 'forward' (top→bottom) or 'backward' (bottom→top).
 */
export function inferVerticalDefault(topType?: string, bottomType?: string): 'forward' | 'backward' {
  if (topType && SUPPLY_ORIGIN_TYPES.includes(topType)) return 'forward'
  if (bottomType && SUPPLY_ORIGIN_TYPES.includes(bottomType)) return 'backward'
  if (bottomType && SINK_TYPES.includes(bottomType)) return 'forward'
  if (topType && SINK_TYPES.includes(topType)) return 'backward'
  return 'forward'
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
 *   both     – both flags set (legacy bidirectional – treated as forward in the editor)
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
 *   both     – legacy bidirectional
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
 * Applies the 3-state horizontal toggle cycle to a pair of mutable unit objects:
 *   none → (smart default based on unit types) → other direction → none
 *
 * The 'both' (bidirectional) state is no longer produced; if the current reading
 * shows 'both' (legacy data), the cycle treats it as forward and clears it.
 *
 * @param left        The unit at (x, y)
 * @param right       The unit at (x+1, y)
 * @param current     The currently detected link state
 */
export function applyHorizontalLinkCycle<T extends LinkFlagSource>(
  left: T,
  right: T,
  current: HorizontalLinkState,
): void {
  const defaultDir = inferHorizontalDefault(left.unitType, right.unitType)
  const altDir = defaultDir === 'forward' ? 'backward' : 'forward'

  if (current === 'none') {
    // First click: use flow-safe default direction
    left.linkRight = defaultDir === 'forward'
    right.linkLeft = defaultDir === 'backward'
  } else if (current === defaultDir) {
    // Second click: flip to the other direction
    left.linkRight = altDir === 'forward'
    right.linkLeft = altDir === 'backward'
  } else {
    // Third click (or legacy 'both'): clear the link
    left.linkRight = false
    right.linkLeft = false
  }
}

/**
 * Applies the 3-state vertical toggle cycle to a pair of mutable unit objects:
 *   none → (smart default) → other direction → none
 *
 * @param top     The unit at (x, y)
 * @param bottom  The unit at (x, y+1)
 * @param current The currently detected link state
 */
export function applyVerticalLinkCycle<T extends LinkFlagSource>(
  top: T,
  bottom: T,
  current: VerticalLinkState,
): void {
  const defaultDir = inferVerticalDefault(top.unitType, bottom.unitType)
  const altDir = defaultDir === 'forward' ? 'backward' : 'forward'

  if (current === 'none') {
    top.linkDown = defaultDir === 'forward'
    bottom.linkUp = defaultDir === 'backward'
  } else if (current === defaultDir) {
    top.linkDown = altDir === 'forward'
    bottom.linkUp = altDir === 'backward'
  } else {
    // Third click (or legacy 'both'): clear
    top.linkDown = false
    bottom.linkUp = false
  }
}

// ---------------------------------------------------------------------------
// Diagonal link helpers
// ---------------------------------------------------------------------------

/**
 * Represents the active diagonal link state of a 2×2 block rooted at (x,y).
 *
 * Each diagonal is a different unit pair:
 *   tl-br  – topLeft  → bottomRight (↘)  flag: topLeft.linkDownRight
 *   br-tl  – bottomRight → topLeft  (↖)  flag: bottomRight.linkUpLeft
 *   tr-bl  – topRight → bottomLeft  (↙)  flag: topRight.linkDownLeft
 *   bl-tr  – bottomLeft → topRight  (↗)  flag: bottomLeft.linkUpRight
 *   cross  – multiple flags set (legacy / conflicting state)
 *   none   – no diagonal connection
 */
export type DiagonalLinkState = 'none' | 'tl-br' | 'br-tl' | 'tr-bl' | 'bl-tr' | 'cross'

/**
 * Returns the diagonal link state of the 2×2 block whose top-left corner is (x,y).
 * Checks all four directional diagonal flags independently.
 */
export function getDiagonalLinkState<T extends LinkFlagSource>(
  units: T[],
  x: number,
  y: number,
): DiagonalLinkState {
  const topLeft = unitAt(units, x, y)
  const topRight = unitAt(units, x + 1, y)
  const bottomLeft = unitAt(units, x, y + 1)
  const bottomRight = unitAt(units, x + 1, y + 1)

  const hasTlBr = !!topLeft?.linkDownRight      // topLeft → bottomRight
  const hasBrTl = !!bottomRight?.linkUpLeft     // bottomRight → topLeft
  const hasTrBl = !!topRight?.linkDownLeft      // topRight → bottomLeft
  const hasBlTr = !!bottomLeft?.linkUpRight     // bottomLeft → topRight

  const activeCount = [hasTlBr, hasBrTl, hasTrBl, hasBlTr].filter(Boolean).length
  if (activeCount > 1) return 'cross'
  if (hasTlBr) return 'tl-br'
  if (hasBrTl) return 'br-tl'
  if (hasTrBl) return 'tr-bl'
  if (hasBlTr) return 'bl-tr'
  return 'none'
}

/** Unicode symbol describing the diagonal link state for display. */
export function getDiagonalLinkLabel(state: DiagonalLinkState): string {
  if (state === 'tl-br') return '↘'
  if (state === 'br-tl') return '↖'
  if (state === 'tr-bl') return '↙'
  if (state === 'bl-tr') return '↗'
  if (state === 'cross') return '✕'
  return ''
}

/**
 * Applies the 5-state diagonal toggle cycle to a 2×2 group of mutable unit objects.
 * Each state sets ONLY the source unit's outgoing flag – no reverse companion flag.
 *
 * Cycle: none → tl-br (↘) → br-tl (↖) → tr-bl (↙) → bl-tr (↗) → none
 *
 * From 'cross' (legacy multi-flag state), cycles back to none.
 *
 * Mutates the passed objects in place.
 */
export function applyDiagonalLinkCycle<T extends LinkFlagSource>(
  topLeft: T,
  topRight: T,
  bottomLeft: T,
  bottomRight: T,
  current: DiagonalLinkState,
): void {
  // Clear all four directional diagonal flags first
  topLeft.linkDownRight = false
  bottomRight.linkUpLeft = false
  topRight.linkDownLeft = false
  bottomLeft.linkUpRight = false

  if (current === 'none') {
    // First click: topLeft → bottomRight (↘)
    topLeft.linkDownRight = true
  } else if (current === 'tl-br') {
    // Second click: bottomRight → topLeft (↖)
    bottomRight.linkUpLeft = true
  } else if (current === 'br-tl') {
    // Third click: topRight → bottomLeft (↙)
    topRight.linkDownLeft = true
  } else if (current === 'tr-bl') {
    // Fourth click: bottomLeft → topRight (↗)
    bottomLeft.linkUpRight = true
  }
  // 'bl-tr' or 'cross' → stays cleared (all flags already set to false above)
}
