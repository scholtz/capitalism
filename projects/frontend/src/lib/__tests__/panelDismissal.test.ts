import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  PRODUCTION_PANEL_DISMISSED_KEY,
  SALES_PANEL_DISMISSED_KEY,
  loadDismissedIds,
  saveDismissedIds,
  isBuildingPanelDismissed,
  dismissBuildingPanel,
  shouldShowPanel,
} from '../panelDismissal'

// ---------------------------------------------------------------------------
// localStorage mock (vitest environment: 'node' has no browser globals)
// ---------------------------------------------------------------------------

function createLocalStorageMock() {
  const store: Record<string, string> = {}
  return {
    getItem: vi.fn((key: string) => store[key] ?? null),
    setItem: vi.fn((key: string, value: string) => {
      store[key] = value
    }),
    removeItem: vi.fn((key: string) => {
      delete store[key]
    }),
    clear: vi.fn(() => {
      Object.keys(store).forEach((k) => delete store[k])
    }),
    get length() {
      return Object.keys(store).length
    },
    key: vi.fn((index: number) => Object.keys(store)[index] ?? null),
  }
}

const localStorageMock = createLocalStorageMock()
vi.stubGlobal('localStorage', localStorageMock)

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

const BUILDING_A = 'building-abc-123'
const BUILDING_B = 'building-xyz-456'

function clearStorage() {
  localStorageMock.clear()
}

// ---------------------------------------------------------------------------
// loadDismissedIds
// ---------------------------------------------------------------------------

describe('loadDismissedIds', () => {
  beforeEach(clearStorage)

  it('returns empty array when key is absent', () => {
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toEqual([])
  })

  it('returns stored array when key exists', () => {
    localStorageMock.setItem(PRODUCTION_PANEL_DISMISSED_KEY, JSON.stringify([BUILDING_A, BUILDING_B]))
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toEqual([BUILDING_A, BUILDING_B])
  })

  it('returns empty array when stored value is invalid JSON', () => {
    localStorageMock.setItem(PRODUCTION_PANEL_DISMISSED_KEY, '{not-valid-json')
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toEqual([])
  })

  it('returns empty array when key value is empty string', () => {
    localStorageMock.setItem(PRODUCTION_PANEL_DISMISSED_KEY, '')
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toEqual([])
  })
})

// ---------------------------------------------------------------------------
// saveDismissedIds
// ---------------------------------------------------------------------------

describe('saveDismissedIds', () => {
  beforeEach(clearStorage)

  it('persists the array under the given key', () => {
    saveDismissedIds(SALES_PANEL_DISMISSED_KEY, [BUILDING_A])
    const raw = localStorageMock.getItem(SALES_PANEL_DISMISSED_KEY)
    expect(JSON.parse(raw!)).toEqual([BUILDING_A])
  })

  it('overwrites a previous value', () => {
    saveDismissedIds(SALES_PANEL_DISMISSED_KEY, [BUILDING_A])
    saveDismissedIds(SALES_PANEL_DISMISSED_KEY, [BUILDING_B])
    expect(loadDismissedIds(SALES_PANEL_DISMISSED_KEY)).toEqual([BUILDING_B])
  })

  it('persists an empty array', () => {
    saveDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY, [])
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toEqual([])
  })
})

// ---------------------------------------------------------------------------
// isBuildingPanelDismissed
// ---------------------------------------------------------------------------

describe('isBuildingPanelDismissed', () => {
  beforeEach(clearStorage)

  it('returns false when no buildings are dismissed', () => {
    expect(isBuildingPanelDismissed(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(false)
  })

  it('returns true for a building that has been dismissed', () => {
    saveDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY, [BUILDING_A])
    expect(isBuildingPanelDismissed(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(true)
  })

  it('returns false for a building not in the dismissed list', () => {
    saveDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY, [BUILDING_A])
    expect(isBuildingPanelDismissed(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_B)).toBe(false)
  })

  it('checks each key independently (production vs sales)', () => {
    saveDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY, [BUILDING_A])
    // Sales key untouched — should report not dismissed
    expect(isBuildingPanelDismissed(SALES_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(false)
  })
})

// ---------------------------------------------------------------------------
// dismissBuildingPanel
// ---------------------------------------------------------------------------

describe('dismissBuildingPanel', () => {
  beforeEach(clearStorage)

  it('adds the building ID to the dismissed list', () => {
    dismissBuildingPanel(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)
    expect(isBuildingPanelDismissed(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(true)
  })

  it('is idempotent — calling twice does not duplicate the building ID', () => {
    dismissBuildingPanel(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)
    dismissBuildingPanel(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)
    expect(loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)).toHaveLength(1)
  })

  it('preserves existing dismissed buildings when adding a new one', () => {
    saveDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY, [BUILDING_A])
    dismissBuildingPanel(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_B)
    const ids = loadDismissedIds(PRODUCTION_PANEL_DISMISSED_KEY)
    expect(ids).toContain(BUILDING_A)
    expect(ids).toContain(BUILDING_B)
    expect(ids).toHaveLength(2)
  })

  it('operates independently per key (production vs sales)', () => {
    dismissBuildingPanel(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)
    expect(isBuildingPanelDismissed(SALES_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(false)
    expect(isBuildingPanelDismissed(PRODUCTION_PANEL_DISMISSED_KEY, BUILDING_A)).toBe(true)
  })
})

// ---------------------------------------------------------------------------
// shouldShowPanel — the core display-gate logic
// ---------------------------------------------------------------------------

describe('shouldShowPanel', () => {
  it('shows panel when it has never been dismissed', () => {
    expect(shouldShowPanel(false, true)).toBe(true)
    expect(shouldShowPanel(false, false)).toBe(true)
  })

  it('hides panel when dismissed and chain is complete (normal play)', () => {
    // Player dismissed the panel; chain is healthy → respect the choice.
    expect(shouldShowPanel(true, true)).toBe(false)
  })

  it('re-shows panel when dismissed but chain is incomplete (error override)', () => {
    // Chain broke after dismissal → panel must reappear to alert the player.
    expect(shouldShowPanel(true, false)).toBe(true)
  })

  it('handles edge case: not dismissed and chain not complete → still shows', () => {
    // First-run unconfigured building: panel should guide the player.
    expect(shouldShowPanel(false, false)).toBe(true)
  })
})

