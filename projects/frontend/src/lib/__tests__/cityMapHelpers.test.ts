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
