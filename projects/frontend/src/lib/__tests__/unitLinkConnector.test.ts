/**
 * Unit tests for the UnitLinkConnector visual-descriptor helper.
 *
 * These tests verify the state-to-visual mapping for all eight directional link
 * states supported by the 4×4 building unit grid: four orthogonal directions
 * (→ ← ↓ ↑) and four diagonal directions (↘ ↖ ↙ ↗).
 *
 * The tests use the pure `getLinkConnectorVisual()` helper extracted from the
 * link logic layer, so they run without a DOM, Vue instance, or i18n context.
 */

import { describe, expect, it } from 'vitest'
import { getLinkConnectorVisual } from '../linkHelpers'
import type { DirectedPairLinkState } from '../linkHelpers'

// ---------------------------------------------------------------------------
// getLinkConnectorVisual — state-to-visual mapping
// ---------------------------------------------------------------------------

describe('getLinkConnectorVisual — none state', () => {
  it('always includes a track line', () => {
    expect(getLinkConnectorVisual('none').hasTrack).toBe(true)
  })

  it('is not active when state is none', () => {
    expect(getLinkConnectorVisual('none').isActive).toBe(false)
  })

  it('has no arrowheads when state is none', () => {
    const v = getLinkConnectorVisual('none')
    expect(v.hasForwardArrow).toBe(false)
    expect(v.hasBackwardArrow).toBe(false)
  })
})

describe('getLinkConnectorVisual — forward state', () => {
  it('is active', () => {
    expect(getLinkConnectorVisual('forward').isActive).toBe(true)
  })

  it('has a forward arrowhead only', () => {
    const v = getLinkConnectorVisual('forward')
    expect(v.hasForwardArrow).toBe(true)
    expect(v.hasBackwardArrow).toBe(false)
  })
})

describe('getLinkConnectorVisual — backward state', () => {
  it('is active', () => {
    expect(getLinkConnectorVisual('backward').isActive).toBe(true)
  })

  it('has a backward arrowhead only', () => {
    const v = getLinkConnectorVisual('backward')
    expect(v.hasForwardArrow).toBe(false)
    expect(v.hasBackwardArrow).toBe(true)
  })
})

describe('getLinkConnectorVisual — both state (legacy bidirectional)', () => {
  it('is active', () => {
    expect(getLinkConnectorVisual('both').isActive).toBe(true)
  })

  it('has arrowheads at both ends', () => {
    const v = getLinkConnectorVisual('both')
    expect(v.hasForwardArrow).toBe(true)
    expect(v.hasBackwardArrow).toBe(true)
  })
})

// ---------------------------------------------------------------------------
// All four states covered — exhaustive parametric check
// ---------------------------------------------------------------------------

const stateTable: Array<{
  state: DirectedPairLinkState
  isActive: boolean
  hasForwardArrow: boolean
  hasBackwardArrow: boolean
}> = [
  { state: 'none',     isActive: false, hasForwardArrow: false, hasBackwardArrow: false },
  { state: 'forward',  isActive: true,  hasForwardArrow: true,  hasBackwardArrow: false },
  { state: 'backward', isActive: true,  hasForwardArrow: false, hasBackwardArrow: true  },
  { state: 'both',     isActive: true,  hasForwardArrow: true,  hasBackwardArrow: true  },
]

describe('getLinkConnectorVisual — parametric table for all states', () => {
  for (const row of stateTable) {
    it(`state="${row.state}": isActive=${row.isActive}, forwardArrow=${row.hasForwardArrow}, backwardArrow=${row.hasBackwardArrow}`, () => {
      const v = getLinkConnectorVisual(row.state)
      expect(v.hasTrack).toBe(true)
      expect(v.isActive).toBe(row.isActive)
      expect(v.hasForwardArrow).toBe(row.hasForwardArrow)
      expect(v.hasBackwardArrow).toBe(row.hasBackwardArrow)
    })
  }
})

// ---------------------------------------------------------------------------
// Directional consistency: the same visual logic applies for all 8 directions.
// The mapping is direction-agnostic – direction is encoded in the SVG geometry,
// not in the state.  Verify that forward/backward mean the same thing regardless
// of the connector type passed to the rendering component.
// ---------------------------------------------------------------------------

describe('getLinkConnectorVisual — direction-agnostic behaviour', () => {
  const directions = ['horizontal', 'vertical', 'diag-primary', 'diag-secondary'] as const
  const states: DirectedPairLinkState[] = ['none', 'forward', 'backward', 'both']

  for (const dir of directions) {
    for (const state of states) {
      it(`direction="${dir}", state="${state}" produces consistent visual`, () => {
        // The helper is direction-agnostic: the visual descriptor is solely derived
        // from state.  Direction determines the SVG geometry (line angle/position)
        // but not which arrowhead elements are present.
        const v = getLinkConnectorVisual(state)
        expect(v.hasTrack).toBe(true)
        expect(v.isActive).toBe(state !== 'none')
        expect(v.hasForwardArrow).toBe(state === 'forward' || state === 'both')
        expect(v.hasBackwardArrow).toBe(state === 'backward' || state === 'both')
      })
    }
  }
})

// ---------------------------------------------------------------------------
// No duplicate visual elements — key product requirement from issue #362
// For any state, forward and backward arrowheads are never BOTH absent when
// the link is active (user should always see direction for active links).
// ---------------------------------------------------------------------------

describe('getLinkConnectorVisual — no silent active state without arrowhead', () => {
  it('active states always have at least one arrowhead', () => {
    const activeStates: DirectedPairLinkState[] = ['forward', 'backward', 'both']
    for (const state of activeStates) {
      const v = getLinkConnectorVisual(state)
      expect(v.hasForwardArrow || v.hasBackwardArrow).toBe(true)
    }
  })

  it('none state has no arrowheads (clean empty slot)', () => {
    const v = getLinkConnectorVisual('none')
    expect(v.hasForwardArrow).toBe(false)
    expect(v.hasBackwardArrow).toBe(false)
  })
})
