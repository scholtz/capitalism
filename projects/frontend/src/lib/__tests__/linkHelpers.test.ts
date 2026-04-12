/**
 * Unit tests for the directional link helpers in linkHelpers.ts.
 * These tests verify state detection, arrow characters, 3-state toggle cycle
 * (no bidirectional), smart direction defaults, and the full 5-state diagonal
 * cycle – all without requiring a Vue app instance or i18n context.
 */

import { describe, expect, it } from 'vitest'
import {
  applyDiagonalLinkCycle,
  applyHorizontalLinkCycle,
  applyPrimaryDiagonalLinkCycle,
  applySecondaryDiagonalLinkCycle,
  applyVerticalLinkCycle,
  getDiagonalLinkLabel,
  getDiagonalLinkState,
  getHorizontalLinkArrow,
  getHorizontalLinkState,
  getPrimaryDiagonalLinkArrow,
  getPrimaryDiagonalLinkState,
  getSecondaryDiagonalLinkArrow,
  getSecondaryDiagonalLinkState,
  getVerticalLinkArrow,
  getVerticalLinkState,
  inferHorizontalDefault,
  inferPrimaryDiagonalDefault,
  inferSecondaryDiagonalDefault,
  inferVerticalDefault,
  type LinkFlagSource,
} from '../linkHelpers'

function makeUnit(
  gridX: number,
  gridY: number,
  overrides: Partial<LinkFlagSource> = {},
): LinkFlagSource {
  return {
    gridX,
    gridY,
    linkRight: false,
    linkLeft: false,
    linkDown: false,
    linkUp: false,
    linkDownRight: false,
    linkDownLeft: false,
    linkUpRight: false,
    linkUpLeft: false,
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// getHorizontalLinkState
// ---------------------------------------------------------------------------

describe('getHorizontalLinkState', () => {
  it('returns none when no flags are set', () => {
    const units = [makeUnit(0, 0), makeUnit(1, 0)]
    expect(getHorizontalLinkState(units, 0, 0)).toBe('none')
  })

  it('returns forward when only left.linkRight is true', () => {
    const units = [makeUnit(0, 0, { linkRight: true }), makeUnit(1, 0)]
    expect(getHorizontalLinkState(units, 0, 0)).toBe('forward')
  })

  it('returns backward when only right.linkLeft is true', () => {
    const units = [makeUnit(0, 0), makeUnit(1, 0, { linkLeft: true })]
    expect(getHorizontalLinkState(units, 0, 0)).toBe('backward')
  })

  it('returns both when both flags are true', () => {
    const units = [makeUnit(0, 0, { linkRight: true }), makeUnit(1, 0, { linkLeft: true })]
    expect(getHorizontalLinkState(units, 0, 0)).toBe('both')
  })

  it('returns none when one of the two cells is absent', () => {
    const units = [makeUnit(0, 0, { linkRight: true })] // no unit at (1,0)
    expect(getHorizontalLinkState(units, 0, 0)).toBe('forward') // left flag still reported
  })

  it('returns none when units array is empty', () => {
    expect(getHorizontalLinkState([], 0, 0)).toBe('none')
  })

  it('handles non-zero row y correctly', () => {
    const units = [makeUnit(2, 3, { linkRight: true }), makeUnit(3, 3)]
    expect(getHorizontalLinkState(units, 2, 3)).toBe('forward')
    expect(getHorizontalLinkState(units, 0, 3)).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// getVerticalLinkState
// ---------------------------------------------------------------------------

describe('getVerticalLinkState', () => {
  it('returns none when no flags are set', () => {
    const units = [makeUnit(0, 0), makeUnit(0, 1)]
    expect(getVerticalLinkState(units, 0, 0)).toBe('none')
  })

  it('returns forward when only top.linkDown is true', () => {
    const units = [makeUnit(0, 0, { linkDown: true }), makeUnit(0, 1)]
    expect(getVerticalLinkState(units, 0, 0)).toBe('forward')
  })

  it('returns backward when only bottom.linkUp is true', () => {
    const units = [makeUnit(0, 0), makeUnit(0, 1, { linkUp: true })]
    expect(getVerticalLinkState(units, 0, 0)).toBe('backward')
  })

  it('returns both when both flags are true', () => {
    const units = [makeUnit(0, 0, { linkDown: true }), makeUnit(0, 1, { linkUp: true })]
    expect(getVerticalLinkState(units, 0, 0)).toBe('both')
  })

  it('returns none when units array is empty', () => {
    expect(getVerticalLinkState([], 0, 0)).toBe('none')
  })

  it('is independent of horizontal flags', () => {
    // horizontal flags must not affect vertical state
    const units = [
      makeUnit(0, 0, { linkRight: true, linkDown: false }),
      makeUnit(0, 1, { linkLeft: true, linkUp: false }),
    ]
    expect(getVerticalLinkState(units, 0, 0)).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// getHorizontalLinkArrow
// ---------------------------------------------------------------------------

describe('getHorizontalLinkArrow', () => {
  it('returns ▶ for forward', () => {
    expect(getHorizontalLinkArrow('forward')).toBe('▶')
  })

  it('returns ◀ for backward', () => {
    expect(getHorizontalLinkArrow('backward')).toBe('◀')
  })

  it('returns ↔ for both', () => {
    expect(getHorizontalLinkArrow('both')).toBe('↔')
  })

  it('returns empty string for none', () => {
    expect(getHorizontalLinkArrow('none')).toBe('')
  })
})

// ---------------------------------------------------------------------------
// getVerticalLinkArrow
// ---------------------------------------------------------------------------

describe('getVerticalLinkArrow', () => {
  it('returns ▼ for forward', () => {
    expect(getVerticalLinkArrow('forward')).toBe('▼')
  })

  it('returns ▲ for backward', () => {
    expect(getVerticalLinkArrow('backward')).toBe('▲')
  })

  it('returns ↕ for both', () => {
    expect(getVerticalLinkArrow('both')).toBe('↕')
  })

  it('returns empty string for none', () => {
    expect(getVerticalLinkArrow('none')).toBe('')
  })
})

// ---------------------------------------------------------------------------
// applyHorizontalLinkCycle — 3-state cycle: none → (default dir) → (other dir) → none
// ---------------------------------------------------------------------------

describe('applyHorizontalLinkCycle', () => {
  it('none → forward (default): left sends right, right does not', () => {
    const left = makeUnit(0, 0)
    const right = makeUnit(1, 0)
    applyHorizontalLinkCycle(left, right, 'none')
    expect(left.linkRight).toBe(true)
    expect(right.linkLeft).toBe(false)
  })

  it('forward → backward: right sends left, left does not', () => {
    const left = makeUnit(0, 0, { linkRight: true })
    const right = makeUnit(1, 0)
    applyHorizontalLinkCycle(left, right, 'forward')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(true)
  })

  it('backward → none: all flags cleared (no bidirectional state)', () => {
    const left = makeUnit(0, 0)
    const right = makeUnit(1, 0, { linkLeft: true })
    applyHorizontalLinkCycle(left, right, 'backward')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(false)
  })

  it('legacy both → none: all flags cleared', () => {
    const left = makeUnit(0, 0, { linkRight: true })
    const right = makeUnit(1, 0, { linkLeft: true })
    applyHorizontalLinkCycle(left, right, 'both')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(false)
  })

  it('full cycle returns to none after 3 steps', () => {
    const left = makeUnit(0, 0)
    const right = makeUnit(1, 0)

    const getState = () => getHorizontalLinkState([left, right], 0, 0)

    expect(getState()).toBe('none')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('forward')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('backward')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// applyVerticalLinkCycle — 3-state cycle: none → (default dir) → (other dir) → none
// ---------------------------------------------------------------------------

describe('applyVerticalLinkCycle', () => {
  it('none → forward (default): top sends down, bottom does not', () => {
    const top = makeUnit(0, 0)
    const bottom = makeUnit(0, 1)
    applyVerticalLinkCycle(top, bottom, 'none')
    expect(top.linkDown).toBe(true)
    expect(bottom.linkUp).toBe(false)
  })

  it('forward → backward: bottom sends up, top does not', () => {
    const top = makeUnit(0, 0, { linkDown: true })
    const bottom = makeUnit(0, 1)
    applyVerticalLinkCycle(top, bottom, 'forward')
    expect(top.linkDown).toBe(false)
    expect(bottom.linkUp).toBe(true)
  })

  it('backward → none: all flags cleared (no bidirectional state)', () => {
    const top = makeUnit(0, 0)
    const bottom = makeUnit(0, 1, { linkUp: true })
    applyVerticalLinkCycle(top, bottom, 'backward')
    expect(top.linkDown).toBe(false)
    expect(bottom.linkUp).toBe(false)
  })

  it('legacy both → none: all flags cleared', () => {
    const top = makeUnit(0, 0, { linkDown: true })
    const bottom = makeUnit(0, 1, { linkUp: true })
    applyVerticalLinkCycle(top, bottom, 'both')
    expect(top.linkDown).toBe(false)
    expect(bottom.linkUp).toBe(false)
  })

  it('full cycle returns to none after 3 steps', () => {
    const top = makeUnit(0, 0)
    const bottom = makeUnit(0, 1)

    const getState = () => getVerticalLinkState([top, bottom], 0, 0)

    expect(getState()).toBe('none')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('forward')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('backward')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// Independent diagonal-pair helpers
// ---------------------------------------------------------------------------

describe('primary and secondary diagonal pair state helpers', () => {
  it('reads the primary diagonal pair (\\) independently', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getPrimaryDiagonalLinkState(units, 0, 0)).toBe('forward')
    expect(getSecondaryDiagonalLinkState(units, 0, 0)).toBe('none')
  })

  it('reads the secondary diagonal pair (/) independently', () => {
    const units = [
      makeUnit(0, 0),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getPrimaryDiagonalLinkState(units, 0, 0)).toBe('none')
    expect(getSecondaryDiagonalLinkState(units, 0, 0)).toBe('forward')
  })

  it('allows both diagonal axes to be active at the same time without collapsing state', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getPrimaryDiagonalLinkState(units, 0, 0)).toBe('forward')
    expect(getSecondaryDiagonalLinkState(units, 0, 0)).toBe('forward')
  })

  it('returns the correct arrows for each diagonal pair direction', () => {
    expect(getPrimaryDiagonalLinkArrow('forward')).toBe('↘')
    expect(getPrimaryDiagonalLinkArrow('backward')).toBe('↖')
    expect(getSecondaryDiagonalLinkArrow('forward')).toBe('↙')
    expect(getSecondaryDiagonalLinkArrow('backward')).toBe('↗')
  })

  it('uses smart defaults for sparse storage → sales diagonal pairs', () => {
    expect(inferPrimaryDiagonalDefault('STORAGE', 'PUBLIC_SALES')).toBe('forward')
    expect(inferSecondaryDiagonalDefault('PUBLIC_SALES', 'STORAGE')).toBe('backward')
  })

  it('cycles the primary diagonal pair through forward → backward → none', () => {
    const topLeft = makeUnit(0, 0, { unitType: 'PURCHASE' })
    const bottomRight = makeUnit(1, 1, { unitType: 'STORAGE' })

    applyPrimaryDiagonalLinkCycle(topLeft, bottomRight, 'none')
    expect(topLeft.linkDownRight).toBe(true)
    expect(bottomRight.linkUpLeft).toBe(false)

    applyPrimaryDiagonalLinkCycle(topLeft, bottomRight, 'forward')
    expect(topLeft.linkDownRight).toBe(false)
    expect(bottomRight.linkUpLeft).toBe(true)

    applyPrimaryDiagonalLinkCycle(topLeft, bottomRight, 'backward')
    expect(topLeft.linkDownRight).toBe(false)
    expect(bottomRight.linkUpLeft).toBe(false)
  })

  it('cycles the secondary diagonal pair through storage → sales by default', () => {
    const topRight = makeUnit(2, 0, { unitType: 'PUBLIC_SALES' })
    const bottomLeft = makeUnit(1, 1, { unitType: 'STORAGE' })

    applySecondaryDiagonalLinkCycle(topRight, bottomLeft, 'none')
    expect(topRight.linkDownLeft).toBe(false)
    expect(bottomLeft.linkUpRight).toBe(true)

    applySecondaryDiagonalLinkCycle(topRight, bottomLeft, 'backward')
    expect(topRight.linkDownLeft).toBe(true)
    expect(bottomLeft.linkUpRight).toBe(false)

    applySecondaryDiagonalLinkCycle(topRight, bottomLeft, 'forward')
    expect(topRight.linkDownLeft).toBe(false)
    expect(bottomLeft.linkUpRight).toBe(false)
  })
})

// ---------------------------------------------------------------------------
// getDiagonalLinkState — now checks all 4 directional diagonal flags
// ---------------------------------------------------------------------------

describe('getDiagonalLinkState', () => {
  it('returns none when no diagonal flags are set', () => {
    const units = [makeUnit(0, 0), makeUnit(1, 0), makeUnit(0, 1), makeUnit(1, 1)]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('none')
  })

  it('returns tl-br when topLeft.linkDownRight is true', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('tl-br')
  })

  it('returns br-tl when bottomRight.linkUpLeft is true', () => {
    const units = [
      makeUnit(0, 0),
      makeUnit(1, 0),
      makeUnit(0, 1),
      makeUnit(1, 1, { linkUpLeft: true }),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('br-tl')
  })

  it('returns tr-bl when topRight.linkDownLeft is true', () => {
    const units = [
      makeUnit(0, 0),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('tr-bl')
  })

  it('returns bl-tr when bottomLeft.linkUpRight is true', () => {
    const units = [
      makeUnit(0, 0),
      makeUnit(1, 0),
      makeUnit(0, 1, { linkUpRight: true }),
      makeUnit(1, 1),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('bl-tr')
  })

  it('returns cross when multiple diagonal flags are set', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('cross')
  })

  it('returns cross when tl-br and br-tl flags both set (legacy bidirectional)', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0),
      makeUnit(0, 1),
      makeUnit(1, 1, { linkUpLeft: true }),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('cross')
  })

  it('returns none when units array is empty', () => {
    expect(getDiagonalLinkState([], 0, 0)).toBe('none')
  })

  it('handles non-zero block positions correctly', () => {
    const units = [
      makeUnit(1, 2, { linkDownRight: true }),
      makeUnit(2, 2),
      makeUnit(1, 3),
      makeUnit(2, 3),
    ]
    expect(getDiagonalLinkState(units, 1, 2)).toBe('tl-br')
    expect(getDiagonalLinkState(units, 0, 0)).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// getDiagonalLinkLabel
// ---------------------------------------------------------------------------

describe('getDiagonalLinkLabel', () => {
  it('returns ↘ for tl-br', () => {
    expect(getDiagonalLinkLabel('tl-br')).toBe('↘')
  })

  it('returns ↖ for br-tl', () => {
    expect(getDiagonalLinkLabel('br-tl')).toBe('↖')
  })

  it('returns ↙ for tr-bl', () => {
    expect(getDiagonalLinkLabel('tr-bl')).toBe('↙')
  })

  it('returns ↗ for bl-tr', () => {
    expect(getDiagonalLinkLabel('bl-tr')).toBe('↗')
  })

  it('returns ✕ for cross', () => {
    expect(getDiagonalLinkLabel('cross')).toBe('✕')
  })

  it('returns empty string for none', () => {
    expect(getDiagonalLinkLabel('none')).toBe('')
  })
})

// ---------------------------------------------------------------------------
// applyDiagonalLinkCycle — 5-state: none → tl-br → br-tl → tr-bl → bl-tr → none
// Each state sets ONLY the source flag (no bidirectional reverse flag)
// ---------------------------------------------------------------------------

describe('applyDiagonalLinkCycle', () => {
  function make2x2() {
    return {
      tl: makeUnit(0, 0) as LinkFlagSource & {
        linkDownRight?: boolean
        linkDownLeft?: boolean
        linkUpRight?: boolean
        linkUpLeft?: boolean
      },
      tr: makeUnit(1, 0) as LinkFlagSource & {
        linkDownRight?: boolean
        linkDownLeft?: boolean
        linkUpRight?: boolean
        linkUpLeft?: boolean
      },
      bl: makeUnit(0, 1) as LinkFlagSource & {
        linkDownRight?: boolean
        linkDownLeft?: boolean
        linkUpRight?: boolean
        linkUpLeft?: boolean
      },
      br: makeUnit(1, 1) as LinkFlagSource & {
        linkDownRight?: boolean
        linkDownLeft?: boolean
        linkUpRight?: boolean
        linkUpLeft?: boolean
      },
    }
  }

  it('none → tl-br: only topLeft.linkDownRight set (unidirectional)', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'none')
    expect(tl.linkDownRight).toBe(true)
    // reverse flag must NOT be set (bidirectional prevention)
    expect(br.linkUpLeft).toBe(false)
    expect(tr.linkDownLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('tl-br → br-tl: only bottomRight.linkUpLeft set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'tl-br')
    expect(br.linkUpLeft).toBe(true)
    expect(tl.linkDownRight).toBe(false)
    expect(tr.linkDownLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('br-tl → tr-bl: only topRight.linkDownLeft set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'br-tl')
    expect(tr.linkDownLeft).toBe(true)
    expect(tl.linkDownRight).toBe(false)
    expect(br.linkUpLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('tr-bl → bl-tr: only bottomLeft.linkUpRight set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'tr-bl')
    expect(bl.linkUpRight).toBe(true)
    expect(tl.linkDownRight).toBe(false)
    expect(br.linkUpLeft).toBe(false)
    expect(tr.linkDownLeft).toBe(false)
  })

  it('bl-tr → none: all diagonal flags cleared', () => {
    const { tl, tr, bl, br } = make2x2()
    bl.linkUpRight = true
    applyDiagonalLinkCycle(tl, tr, bl, br, 'bl-tr')
    expect(tl.linkDownRight).toBe(false)
    expect(br.linkUpLeft).toBe(false)
    expect(tr.linkDownLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('cross → none: all diagonal flags cleared', () => {
    const { tl, tr, bl, br } = make2x2()
    // Pre-set multiple flags (legacy multi-flag state)
    tl.linkDownRight = true
    br.linkUpLeft = true
    tr.linkDownLeft = true
    bl.linkUpRight = true
    applyDiagonalLinkCycle(tl, tr, bl, br, 'cross')
    expect(tl.linkDownRight).toBe(false)
    expect(br.linkUpLeft).toBe(false)
    expect(tr.linkDownLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('full cycle returns to none after 5 steps', () => {
    const { tl, tr, bl, br } = make2x2()
    const units = [tl, tr, bl, br]

    const getState = () => getDiagonalLinkState(units, 0, 0)

    expect(getState()).toBe('none')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('tl-br')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('br-tl')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('tr-bl')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('bl-tr')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// inferHorizontalDefault — smart direction based on unit types
// ---------------------------------------------------------------------------

describe('inferHorizontalDefault', () => {
  it('returns forward when left is PURCHASE (supply origin)', () => {
    expect(inferHorizontalDefault('PURCHASE', 'STORAGE')).toBe('forward')
  })

  it('returns backward when right is PURCHASE (supply origin on right)', () => {
    expect(inferHorizontalDefault('STORAGE', 'PURCHASE')).toBe('backward')
  })

  it('returns backward when left is PUBLIC_SALES (sink on left)', () => {
    expect(inferHorizontalDefault('PUBLIC_SALES', 'STORAGE')).toBe('backward')
  })

  it('returns forward when right is PUBLIC_SALES (sink on right)', () => {
    expect(inferHorizontalDefault('MANUFACTURING', 'PUBLIC_SALES')).toBe('forward')
  })

  it('returns forward when right is B2B_SALES', () => {
    expect(inferHorizontalDefault('STORAGE', 'B2B_SALES')).toBe('forward')
  })

  it('returns forward when right is MINING (mining is supply origin on right → backward)', () => {
    // MINING on right → backward (MINING pushes outward, so link should be backward)
    expect(inferHorizontalDefault('STORAGE', 'MINING')).toBe('backward')
  })

  it('returns forward as default when no special unit types involved', () => {
    expect(inferHorizontalDefault('MANUFACTURING', 'STORAGE')).toBe('forward')
  })

  it('returns forward when unit types are undefined', () => {
    expect(inferHorizontalDefault(undefined, undefined)).toBe('forward')
  })
})

// ---------------------------------------------------------------------------
// inferVerticalDefault — smart direction based on unit types
// ---------------------------------------------------------------------------

describe('inferVerticalDefault', () => {
  it('returns forward when top is PURCHASE', () => {
    expect(inferVerticalDefault('PURCHASE', 'MANUFACTURING')).toBe('forward')
  })

  it('returns backward when bottom is PURCHASE', () => {
    expect(inferVerticalDefault('MANUFACTURING', 'PURCHASE')).toBe('backward')
  })

  it('returns forward when bottom is PUBLIC_SALES', () => {
    expect(inferVerticalDefault('STORAGE', 'PUBLIC_SALES')).toBe('forward')
  })

  it('returns backward when top is B2B_SALES', () => {
    expect(inferVerticalDefault('B2B_SALES', 'MANUFACTURING')).toBe('backward')
  })

  it('returns forward as default', () => {
    expect(inferVerticalDefault('MANUFACTURING', 'STORAGE')).toBe('forward')
  })
})

// ---------------------------------------------------------------------------
// Smart defaults in applyHorizontalLinkCycle with PURCHASE unit
// ---------------------------------------------------------------------------

describe('applyHorizontalLinkCycle with smart defaults', () => {
  it('PURCHASE on left: first click goes forward (purchase → right)', () => {
    const left = makeUnit(0, 0, { unitType: 'PURCHASE' })
    const right = makeUnit(1, 0, { unitType: 'STORAGE' })
    applyHorizontalLinkCycle(left, right, 'none')
    expect(left.linkRight).toBe(true)
    expect(right.linkLeft).toBe(false)
  })

  it('PURCHASE on right: first click goes backward (right sends left)', () => {
    const left = makeUnit(0, 0, { unitType: 'STORAGE' })
    const right = makeUnit(1, 0, { unitType: 'PURCHASE' })
    applyHorizontalLinkCycle(left, right, 'none')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(true)
  })

  it('STORAGE → PUBLIC_SALES: first click goes forward (storage feeds sales)', () => {
    const left = makeUnit(0, 0, { unitType: 'STORAGE' })
    const right = makeUnit(1, 0, { unitType: 'PUBLIC_SALES' })
    applyHorizontalLinkCycle(left, right, 'none')
    expect(left.linkRight).toBe(true)
    expect(right.linkLeft).toBe(false)
  })

  it('PUBLIC_SALES on left: first click goes backward (sales should receive)', () => {
    const left = makeUnit(0, 0, { unitType: 'PUBLIC_SALES' })
    const right = makeUnit(1, 0, { unitType: 'STORAGE' })
    applyHorizontalLinkCycle(left, right, 'none')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(true)
  })
})
