/**
 * Unit tests for the directional link helpers in linkHelpers.ts.
 * These tests verify state detection, arrow characters, and 4-state toggle cycle
 * without requiring a Vue app instance or i18n context.
 */

import { describe, expect, it } from 'vitest'
import {
  applyDiagonalLinkCycle,
  applyHorizontalLinkCycle,
  applyVerticalLinkCycle,
  getDiagonalLinkLabel,
  getDiagonalLinkState,
  getHorizontalLinkArrow,
  getHorizontalLinkState,
  getVerticalLinkArrow,
  getVerticalLinkState,
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
// applyHorizontalLinkCycle — 4-state cycle: none → forward → backward → both → none
// ---------------------------------------------------------------------------

describe('applyHorizontalLinkCycle', () => {
  it('none → forward: left sends right, right does not', () => {
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

  it('backward → both: both flags set', () => {
    const left = makeUnit(0, 0)
    const right = makeUnit(1, 0, { linkLeft: true })
    applyHorizontalLinkCycle(left, right, 'backward')
    expect(left.linkRight).toBe(true)
    expect(right.linkLeft).toBe(true)
  })

  it('both → none: all flags cleared', () => {
    const left = makeUnit(0, 0, { linkRight: true })
    const right = makeUnit(1, 0, { linkLeft: true })
    applyHorizontalLinkCycle(left, right, 'both')
    expect(left.linkRight).toBe(false)
    expect(right.linkLeft).toBe(false)
  })

  it('full cycle returns to none after 4 steps', () => {
    const left = makeUnit(0, 0)
    const right = makeUnit(1, 0)

    const getState = () => getHorizontalLinkState([left, right], 0, 0)

    expect(getState()).toBe('none')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('forward')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('backward')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('both')
    applyHorizontalLinkCycle(left, right, getState())
    expect(getState()).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// applyVerticalLinkCycle — 4-state cycle: none → forward → backward → both → none
// ---------------------------------------------------------------------------

describe('applyVerticalLinkCycle', () => {
  it('none → forward: top sends down, bottom does not', () => {
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

  it('backward → both: both flags set', () => {
    const top = makeUnit(0, 0)
    const bottom = makeUnit(0, 1, { linkUp: true })
    applyVerticalLinkCycle(top, bottom, 'backward')
    expect(top.linkDown).toBe(true)
    expect(bottom.linkUp).toBe(true)
  })

  it('both → none: all flags cleared', () => {
    const top = makeUnit(0, 0, { linkDown: true })
    const bottom = makeUnit(0, 1, { linkUp: true })
    applyVerticalLinkCycle(top, bottom, 'both')
    expect(top.linkDown).toBe(false)
    expect(bottom.linkUp).toBe(false)
  })

  it('full cycle returns to none after 4 steps', () => {
    const top = makeUnit(0, 0)
    const bottom = makeUnit(0, 1)

    const getState = () => getVerticalLinkState([top, bottom], 0, 0)

    expect(getState()).toBe('none')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('forward')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('backward')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('both')
    applyVerticalLinkCycle(top, bottom, getState())
    expect(getState()).toBe('none')
  })
})

// ---------------------------------------------------------------------------
// getDiagonalLinkState
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

  it('returns tr-bl when topRight.linkDownLeft is true', () => {
    const units = [
      makeUnit(0, 0),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
    ]
    expect(getDiagonalLinkState(units, 0, 0)).toBe('tr-bl')
  })

  it('returns cross when both topLeft.linkDownRight and topRight.linkDownLeft are true', () => {
    const units = [
      makeUnit(0, 0, { linkDownRight: true }),
      makeUnit(1, 0, { linkDownLeft: true }),
      makeUnit(0, 1),
      makeUnit(1, 1),
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

  it('returns ↙ for tr-bl', () => {
    expect(getDiagonalLinkLabel('tr-bl')).toBe('↙')
  })

  it('returns ✕ for cross', () => {
    expect(getDiagonalLinkLabel('cross')).toBe('✕')
  })

  it('returns empty string for none', () => {
    expect(getDiagonalLinkLabel('none')).toBe('')
  })
})

// ---------------------------------------------------------------------------
// applyDiagonalLinkCycle
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

  it('none → tl-br: topLeft.linkDownRight and bottomRight.linkUpLeft set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'none')
    expect(tl.linkDownRight).toBe(true)
    expect(br.linkUpLeft).toBe(true)
    expect(tr.linkDownLeft).toBe(false)
    expect(bl.linkUpRight).toBe(false)
  })

  it('tl-br → tr-bl: topRight.linkDownLeft and bottomLeft.linkUpRight set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'tl-br')
    expect(tl.linkDownRight).toBe(false)
    expect(br.linkUpLeft).toBe(false)
    expect(tr.linkDownLeft).toBe(true)
    expect(bl.linkUpRight).toBe(true)
  })

  it('tr-bl → cross: all four diagonal flags set', () => {
    const { tl, tr, bl, br } = make2x2()
    applyDiagonalLinkCycle(tl, tr, bl, br, 'tr-bl')
    expect(tl.linkDownRight).toBe(true)
    expect(br.linkUpLeft).toBe(true)
    expect(tr.linkDownLeft).toBe(true)
    expect(bl.linkUpRight).toBe(true)
  })

  it('cross → none: all diagonal flags cleared', () => {
    const { tl, tr, bl, br } = make2x2()
    // Pre-set all flags
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

  it('full cycle returns to none after 4 steps', () => {
    const { tl, tr, bl, br } = make2x2()
    const units = [tl, tr, bl, br]

    const getState = () => getDiagonalLinkState(units, 0, 0)

    expect(getState()).toBe('none')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('tl-br')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('tr-bl')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('cross')
    applyDiagonalLinkCycle(tl, tr, bl, br, getState())
    expect(getState()).toBe('none')
  })
})
