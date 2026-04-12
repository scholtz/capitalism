import { describe, it, expect, beforeEach, vi } from 'vitest'

// Mock localStorage globally
const localStorageMock = (() => {
  let store: Record<string, string> = {}
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value
    },
    removeItem: (key: string) => {
      delete store[key]
    },
    clear: () => {
      store = {}
    },
  }
})()

vi.stubGlobal('localStorage', localStorageMock)

import {
  getMasterToken,
  isMasterConnected,
  saveLocalLayout,
  deleteLocalLayout,
  getLocalLayoutsForType,
  type LayoutUnit,
} from '../masterLayoutApi'

const MASTER_TOKEN_KEY = 'auth_token'
const MASTER_EXPIRES_KEY = 'auth_expires'

const mockUnit: LayoutUnit = {
  unitType: 'MANUFACTURING',
  gridX: 1,
  gridY: 0,
  linkUp: false,
  linkDown: false,
  linkLeft: false,
  linkRight: false,
  linkUpLeft: false,
  linkUpRight: false,
  linkDownLeft: false,
  linkDownRight: false,
  resourceTypeId: null,
  productTypeId: null,
  minPrice: null,
  maxPrice: null,
  purchaseSource: null,
  saleVisibility: null,
  budget: null,
  mediaHouseBuildingId: null,
  minQuality: null,
  brandScope: null,
  vendorLockCompanyId: null,
}

describe('masterLayoutApi — session helpers', () => {
  beforeEach(() => {
    localStorageMock.clear()
  })

  it('isMasterConnected returns false when no token stored', () => {
    expect(isMasterConnected()).toBe(false)
  })

  it('isMasterConnected returns false when token is expired', () => {
    localStorageMock.setItem(MASTER_TOKEN_KEY, 'tok')
    localStorageMock.setItem(MASTER_EXPIRES_KEY, new Date(Date.now() - 1000).toISOString())
    expect(isMasterConnected()).toBe(false)
    // Expired token should be cleaned up
    expect(getMasterToken()).toBeNull()
  })

  it('isMasterConnected returns true for a valid future-expiry token', () => {
    localStorageMock.setItem(MASTER_TOKEN_KEY, 'tok')
    localStorageMock.setItem(MASTER_EXPIRES_KEY, new Date(Date.now() + 3600_000).toISOString())
    expect(isMasterConnected()).toBe(true)
    expect(getMasterToken()).toBe('tok')
  })
})

describe('masterLayoutApi — localStorage fallback', () => {
  beforeEach(() => {
    localStorageMock.clear()
  })

  it('getLocalLayoutsForType returns empty array when nothing is stored', () => {
    expect(getLocalLayoutsForType('FACTORY')).toEqual([])
  })

  it('saveLocalLayout creates a new entry and retrieves it by type', () => {
    saveLocalLayout('My Factory', null, 'FACTORY', [mockUnit])
    const layouts = getLocalLayoutsForType('FACTORY')
    expect(layouts).toHaveLength(1)
    expect(layouts[0]?.name).toBe('My Factory')
    expect(layouts[0]?.buildingType).toBe('FACTORY')
    expect(layouts[0]?.units).toHaveLength(1)
    expect(layouts[0]?.isLocal).toBe(true)
  })

  it('saveLocalLayout overwrites an existing entry with the same name + type', () => {
    saveLocalLayout('My Factory', null, 'FACTORY', [mockUnit])
    const updatedUnit = { ...mockUnit, unitType: 'PURCHASE' }
    saveLocalLayout('My Factory', 'Updated', 'FACTORY', [updatedUnit])
    const layouts = getLocalLayoutsForType('FACTORY')
    expect(layouts).toHaveLength(1)
    expect(layouts[0]?.units[0]?.unitType).toBe('PURCHASE')
    expect(layouts[0]?.description).toBe('Updated')
  })

  it('saveLocalLayout stores layouts across multiple building types independently', () => {
    saveLocalLayout('Factory Layout', null, 'FACTORY', [mockUnit])
    saveLocalLayout('Shop Layout', null, 'SALES_SHOP', [mockUnit])

    expect(getLocalLayoutsForType('FACTORY')).toHaveLength(1)
    expect(getLocalLayoutsForType('SALES_SHOP')).toHaveLength(1)
    expect(getLocalLayoutsForType('MINE')).toHaveLength(0)
  })

  it('deleteLocalLayout removes the entry by name + type', () => {
    saveLocalLayout('A', null, 'FACTORY', [mockUnit])
    saveLocalLayout('B', null, 'FACTORY', [mockUnit])
    deleteLocalLayout('A', 'FACTORY')

    const layouts = getLocalLayoutsForType('FACTORY')
    expect(layouts).toHaveLength(1)
    expect(layouts[0]?.name).toBe('B')
  })

  it('deleteLocalLayout is a no-op when the entry does not exist', () => {
    saveLocalLayout('A', null, 'FACTORY', [mockUnit])
    deleteLocalLayout('NonExistent', 'FACTORY')
    expect(getLocalLayoutsForType('FACTORY')).toHaveLength(1)
  })

  it('deleteLocalLayout does not touch layouts of a different building type', () => {
    saveLocalLayout('A', null, 'FACTORY', [mockUnit])
    saveLocalLayout('A', null, 'MINE', [mockUnit])
    deleteLocalLayout('A', 'FACTORY')

    expect(getLocalLayoutsForType('FACTORY')).toHaveLength(0)
    expect(getLocalLayoutsForType('MINE')).toHaveLength(1)
  })

  it('handles malformed localStorage data gracefully', () => {
    localStorageMock.setItem('capitalism_building_layouts', 'not-valid-json')
    expect(getLocalLayoutsForType('FACTORY')).toEqual([])
  })

  it('saveLocalLayout preserves all directional link flags', () => {
    const linkedUnit: LayoutUnit = {
      unitType: 'PURCHASE',
      gridX: 0,
      gridY: 0,
      linkUp: true,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
      linkUpLeft: false,
      linkUpRight: false,
      linkDownLeft: false,
      linkDownRight: false,
      resourceTypeId: null,
      productTypeId: null,
      minPrice: null,
      maxPrice: null,
      purchaseSource: null,
      saleVisibility: null,
      budget: null,
      mediaHouseBuildingId: null,
      minQuality: null,
      brandScope: null,
      vendorLockCompanyId: null,
    }
    saveLocalLayout('Linked Factory', null, 'FACTORY', [linkedUnit])
    const layouts = getLocalLayoutsForType('FACTORY')
    const savedUnit = layouts[0]?.units[0]
    expect(savedUnit?.linkRight).toBe(true)
    expect(savedUnit?.linkUp).toBe(true)
    expect(savedUnit?.linkDown).toBe(false)
    expect(savedUnit?.linkLeft).toBe(false)
  })

  it('saveLocalLayout preserves diagonal link flags', () => {
    const diagonalUnit: LayoutUnit = {
      unitType: 'MANUFACTURING',
      gridX: 1,
      gridY: 1,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: false,
      linkUpLeft: false,
      linkUpRight: true,
      linkDownLeft: false,
      linkDownRight: false,
      resourceTypeId: null,
      productTypeId: null,
      minPrice: null,
      maxPrice: null,
      purchaseSource: null,
      saleVisibility: null,
      budget: null,
      mediaHouseBuildingId: null,
      minQuality: null,
      brandScope: null,
      vendorLockCompanyId: null,
    }
    saveLocalLayout('Diagonal Chain', 'Has diagonal link', 'FACTORY', [diagonalUnit])
    const layouts = getLocalLayoutsForType('FACTORY')
    const savedUnit = layouts[0]?.units[0]
    expect(savedUnit?.linkUpRight).toBe(true)
    expect(savedUnit?.linkDownLeft).toBe(false)
  })

  it('saveLocalLayout preserves resourceTypeId and productTypeId', () => {
    const configuredUnit: LayoutUnit = {
      unitType: 'PURCHASE',
      gridX: 0,
      gridY: 0,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
      linkUpLeft: false,
      linkUpRight: false,
      linkDownLeft: false,
      linkDownRight: false,
      resourceTypeId: 'resource-wood',
      productTypeId: null,
      minPrice: 10,
      maxPrice: 50,
      purchaseSource: null,
      saleVisibility: null,
      budget: 1000,
      mediaHouseBuildingId: null,
      minQuality: null,
      brandScope: null,
      vendorLockCompanyId: null,
    }
    saveLocalLayout('Wood Buyer', null, 'FACTORY', [configuredUnit])
    const layouts = getLocalLayoutsForType('FACTORY')
    const saved = layouts[0]?.units[0]
    expect(saved?.resourceTypeId).toBe('resource-wood')
    expect(saved?.minPrice).toBe(10)
    expect(saved?.maxPrice).toBe(50)
    expect(saved?.budget).toBe(1000)
    expect(saved?.linkRight).toBe(true)
  })
})
