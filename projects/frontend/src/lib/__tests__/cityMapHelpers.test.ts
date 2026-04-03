import { describe, it, expect } from 'vitest'
import {
  getLotStatus,
  getLotMarkerColor,
  formatPopulationIndex,
  populationIndexClass,
  populationIndexTierKey,
  canPurchaseLot,
  canSubmitPurchaseForm,
} from '../cityMapHelpers'

describe('getLotStatus', () => {
  it('returns available when ownerCompanyId is null', () => {
    expect(getLotStatus(null, [])).toBe('available')
  })

  it('returns available when ownerCompanyId is undefined', () => {
    expect(getLotStatus(undefined, ['company-1'])).toBe('available')
  })

  it('returns yours when ownerCompanyId is in playerCompanyIds', () => {
    expect(getLotStatus('company-1', ['company-1', 'company-2'])).toBe('yours')
  })

  it('returns owned when ownerCompanyId is set but not owned by player', () => {
    expect(getLotStatus('other-company', ['company-1'])).toBe('owned')
  })

  it('returns owned when player has no companies', () => {
    expect(getLotStatus('other-company', [])).toBe('owned')
  })
})

describe('getLotMarkerColor', () => {
  it('returns green for available', () => {
    expect(getLotMarkerColor('available')).toBe('#00C853')
  })

  it('returns blue for yours', () => {
    expect(getLotMarkerColor('yours')).toBe('#0047FF')
  })

  it('returns gray for owned (by other)', () => {
    expect(getLotMarkerColor('owned')).toBe('#6B7280')
  })
})

describe('formatPopulationIndex', () => {
  it('formats 1.0 as 1.00x', () => {
    expect(formatPopulationIndex(1.0)).toBe('1.00x')
  })

  it('formats 1.42 as 1.42x', () => {
    expect(formatPopulationIndex(1.42)).toBe('1.42x')
  })

  it('formats 0.78 as 0.78x', () => {
    expect(formatPopulationIndex(0.78)).toBe('0.78x')
  })

  it('rounds to 2 decimal places', () => {
    expect(formatPopulationIndex(1.566)).toBe('1.57x')
  })

  it('handles zero', () => {
    expect(formatPopulationIndex(0)).toBe('0.00x')
  })
})

describe('populationIndexClass', () => {
  it('returns pop-very-high for value >= 1.8', () => {
    expect(populationIndexClass(1.8)).toBe('pop-very-high')
    expect(populationIndexClass(2.0)).toBe('pop-very-high')
  })

  it('returns pop-high for value >= 1.3 and < 1.8', () => {
    expect(populationIndexClass(1.3)).toBe('pop-high')
    expect(populationIndexClass(1.42)).toBe('pop-high')
    expect(populationIndexClass(1.79)).toBe('pop-high')
  })

  it('returns pop-medium for value >= 0.9 and < 1.3', () => {
    expect(populationIndexClass(0.9)).toBe('pop-medium')
    expect(populationIndexClass(1.0)).toBe('pop-medium')
    expect(populationIndexClass(1.29)).toBe('pop-medium')
  })

  it('returns pop-low for value < 0.9', () => {
    expect(populationIndexClass(0.78)).toBe('pop-low')
    expect(populationIndexClass(0.0)).toBe('pop-low')
    expect(populationIndexClass(0.89)).toBe('pop-low')
  })

  it('handles exact boundary 1.8', () => {
    expect(populationIndexClass(1.8)).toBe('pop-very-high')
  })

  it('handles boundary just below 1.3', () => {
    expect(populationIndexClass(1.29)).toBe('pop-medium')
  })
})

describe('populationIndexTierKey', () => {
  it('returns VeryHigh for >= 1.8', () => {
    expect(populationIndexTierKey(1.8)).toBe('VeryHigh')
    expect(populationIndexTierKey(2.5)).toBe('VeryHigh')
  })

  it('returns High for >= 1.3 and < 1.8', () => {
    expect(populationIndexTierKey(1.3)).toBe('High')
    expect(populationIndexTierKey(1.5)).toBe('High')
  })

  it('returns Medium for >= 0.9 and < 1.3', () => {
    expect(populationIndexTierKey(0.9)).toBe('Medium')
    expect(populationIndexTierKey(1.1)).toBe('Medium')
  })

  it('returns Low for < 0.9', () => {
    expect(populationIndexTierKey(0.5)).toBe('Low')
    expect(populationIndexTierKey(0.0)).toBe('Low')
  })
})

describe('canPurchaseLot', () => {
  it('returns true when authenticated, has company, lot is available', () => {
    expect(canPurchaseLot(true, 1, null)).toBe(true)
  })

  it('returns false when not authenticated', () => {
    expect(canPurchaseLot(false, 1, null)).toBe(false)
  })

  it('returns false when player has no companies', () => {
    expect(canPurchaseLot(true, 0, null)).toBe(false)
  })

  it('returns false when lot is already owned', () => {
    expect(canPurchaseLot(true, 1, 'other-company')).toBe(false)
  })

  it('returns false for all negative conditions combined', () => {
    expect(canPurchaseLot(false, 0, 'other-company')).toBe(false)
  })
})

describe('canSubmitPurchaseForm', () => {
  it('returns true when all fields are valid', () => {
    expect(canSubmitPurchaseForm('FACTORY', 'My Factory', 'company-1', false)).toBe(true)
  })

  it('returns false when buildingType is empty', () => {
    expect(canSubmitPurchaseForm('', 'My Factory', 'company-1', false)).toBe(false)
  })

  it('returns false when buildingName is empty', () => {
    expect(canSubmitPurchaseForm('FACTORY', '', 'company-1', false)).toBe(false)
  })

  it('returns false when buildingName is only whitespace', () => {
    expect(canSubmitPurchaseForm('FACTORY', '   ', 'company-1', false)).toBe(false)
  })

  it('returns false when companyId is empty', () => {
    expect(canSubmitPurchaseForm('FACTORY', 'My Factory', '', false)).toBe(false)
  })

  it('returns false when purchase is in flight', () => {
    expect(canSubmitPurchaseForm('FACTORY', 'My Factory', 'company-1', true)).toBe(false)
  })

  it('returns false for all invalid conditions', () => {
    expect(canSubmitPurchaseForm('', '', '', true)).toBe(false)
  })
})

import {
  constructionCostForType,
  constructionTicksForType,
  constructionTicksRemaining,
} from '../cityMapHelpers'

describe('constructionCostForType', () => {
  it('returns 15000 for FACTORY', () => {
    expect(constructionCostForType('FACTORY')).toBe(15000)
  })

  it('returns 5000 for MINE', () => {
    expect(constructionCostForType('MINE')).toBe(5000)
  })

  it('returns 8000 for SALES_SHOP', () => {
    expect(constructionCostForType('SALES_SHOP')).toBe(8000)
  })

  it('returns 25000 for RESEARCH_DEVELOPMENT', () => {
    expect(constructionCostForType('RESEARCH_DEVELOPMENT')).toBe(25000)
  })

  it('returns 40000 for APARTMENT', () => {
    expect(constructionCostForType('APARTMENT')).toBe(40000)
  })

  it('returns 20000 for COMMERCIAL', () => {
    expect(constructionCostForType('COMMERCIAL')).toBe(20000)
  })

  it('returns 30000 for MEDIA_HOUSE', () => {
    expect(constructionCostForType('MEDIA_HOUSE')).toBe(30000)
  })

  it('returns 50000 for BANK', () => {
    expect(constructionCostForType('BANK')).toBe(50000)
  })

  it('returns 60000 for EXCHANGE', () => {
    expect(constructionCostForType('EXCHANGE')).toBe(60000)
  })

  it('returns 80000 for POWER_PLANT', () => {
    expect(constructionCostForType('POWER_PLANT')).toBe(80000)
  })

  it('returns 10000 for unknown building type (default)', () => {
    expect(constructionCostForType('UNKNOWN')).toBe(10000)
    expect(constructionCostForType('')).toBe(10000)
  })

  it('all known building types return positive costs', () => {
    const types = [
      'MINE', 'FACTORY', 'SALES_SHOP', 'RESEARCH_DEVELOPMENT', 'APARTMENT',
      'COMMERCIAL', 'MEDIA_HOUSE', 'BANK', 'EXCHANGE', 'POWER_PLANT',
    ]
    for (const type of types) {
      expect(constructionCostForType(type)).toBeGreaterThan(0)
    }
  })
})

describe('constructionTicksForType', () => {
  it('returns 48 for FACTORY (2 days)', () => {
    expect(constructionTicksForType('FACTORY')).toBe(48)
  })

  it('returns 24 for MINE (1 day)', () => {
    expect(constructionTicksForType('MINE')).toBe(24)
  })

  it('returns 24 for SALES_SHOP (1 day)', () => {
    expect(constructionTicksForType('SALES_SHOP')).toBe(24)
  })

  it('returns 72 for RESEARCH_DEVELOPMENT (3 days)', () => {
    expect(constructionTicksForType('RESEARCH_DEVELOPMENT')).toBe(72)
  })

  it('returns 96 for APARTMENT (4 days)', () => {
    expect(constructionTicksForType('APARTMENT')).toBe(96)
  })

  it('returns 48 for COMMERCIAL (2 days)', () => {
    expect(constructionTicksForType('COMMERCIAL')).toBe(48)
  })

  it('returns 48 for MEDIA_HOUSE (2 days)', () => {
    expect(constructionTicksForType('MEDIA_HOUSE')).toBe(48)
  })

  it('returns 72 for BANK (3 days)', () => {
    expect(constructionTicksForType('BANK')).toBe(72)
  })

  it('returns 96 for EXCHANGE (4 days)', () => {
    expect(constructionTicksForType('EXCHANGE')).toBe(96)
  })

  it('returns 120 for POWER_PLANT (5 days)', () => {
    expect(constructionTicksForType('POWER_PLANT')).toBe(120)
  })

  it('returns 24 for unknown building type (default)', () => {
    expect(constructionTicksForType('UNKNOWN')).toBe(24)
    expect(constructionTicksForType('')).toBe(24)
  })

  it('all known building types return positive tick counts', () => {
    const types = [
      'MINE', 'FACTORY', 'SALES_SHOP', 'RESEARCH_DEVELOPMENT', 'APARTMENT',
      'COMMERCIAL', 'MEDIA_HOUSE', 'BANK', 'EXCHANGE', 'POWER_PLANT',
    ]
    for (const type of types) {
      expect(constructionTicksForType(type)).toBeGreaterThan(0)
    }
  })
})

describe('constructionTicksRemaining', () => {
  it('returns remaining ticks when completesAtTick is in the future', () => {
    expect(constructionTicksRemaining(100, 80)).toBe(20)
  })

  it('returns 0 when completesAtTick equals current tick (just completed)', () => {
    expect(constructionTicksRemaining(100, 100)).toBe(0)
  })

  it('returns 0 when completesAtTick is in the past (overdue)', () => {
    expect(constructionTicksRemaining(50, 100)).toBe(0)
  })

  it('returns 0 when completesAtTick is null (no construction scheduled)', () => {
    expect(constructionTicksRemaining(null, 100)).toBe(0)
  })

  it('returns exact tick count at game start (currentTick=0)', () => {
    expect(constructionTicksRemaining(48, 0)).toBe(48)
  })

  it('returns 1 when one tick away from completion', () => {
    expect(constructionTicksRemaining(101, 100)).toBe(1)
  })

  it('never returns a negative number', () => {
    expect(constructionTicksRemaining(0, 500)).toBeGreaterThanOrEqual(0)
    expect(constructionTicksRemaining(1, 500)).toBeGreaterThanOrEqual(0)
  })
})
