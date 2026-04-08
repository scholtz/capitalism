import { describe, it, expect } from 'vitest'
import {
  demandSignalClass,
  elasticityClass,
  demandDriverClass,
  demandDriverIcon,
  isRaisingPrice,
  isLoweringPrice,
  formatElasticityIndex,
  formatUtilization,
} from '../publicSalesHelpers'

describe('demandSignalClass', () => {
  it('converts STRONG to kebab CSS class', () => {
    expect(demandSignalClass('STRONG')).toBe('mi-demand-strong')
  })

  it('converts NO_DATA to kebab CSS class', () => {
    expect(demandSignalClass('NO_DATA')).toBe('mi-demand-no-data')
  })

  it('converts SUPPLY_CONSTRAINED to kebab CSS class', () => {
    expect(demandSignalClass('SUPPLY_CONSTRAINED')).toBe('mi-demand-supply-constrained')
  })

  it('converts MODERATE to kebab CSS class', () => {
    expect(demandSignalClass('MODERATE')).toBe('mi-demand-moderate')
  })

  it('converts WEAK to kebab CSS class', () => {
    expect(demandSignalClass('WEAK')).toBe('mi-demand-weak')
  })
})

describe('elasticityClass', () => {
  it('returns mi-elastic-high for values below -1.5', () => {
    expect(elasticityClass(-2.0)).toBe('mi-elastic-high')
    expect(elasticityClass(-1.51)).toBe('mi-elastic-high')
  })

  it('returns mi-elastic-low for values above -0.5', () => {
    expect(elasticityClass(-0.4)).toBe('mi-elastic-low')
    expect(elasticityClass(0)).toBe('mi-elastic-low')
  })

  it('returns empty string for values in the normal range', () => {
    expect(elasticityClass(-1.0)).toBe('')
    expect(elasticityClass(-0.5)).toBe('')
    expect(elasticityClass(-1.5)).toBe('')
  })

  it('returns empty string for null', () => {
    expect(elasticityClass(null)).toBe('')
  })

  it('returns empty string for undefined', () => {
    expect(elasticityClass(undefined)).toBe('')
  })
})

describe('demandDriverClass', () => {
  it('maps POSITIVE to mi-driver-positive', () => {
    expect(demandDriverClass('POSITIVE')).toBe('mi-driver-positive')
  })

  it('maps NEGATIVE to mi-driver-negative', () => {
    expect(demandDriverClass('NEGATIVE')).toBe('mi-driver-negative')
  })

  it('maps NEUTRAL to mi-driver-neutral', () => {
    expect(demandDriverClass('NEUTRAL')).toBe('mi-driver-neutral')
  })
})

describe('demandDriverIcon', () => {
  it('returns up arrow for POSITIVE', () => {
    expect(demandDriverIcon('POSITIVE')).toBe('↑')
  })

  it('returns down arrow for NEGATIVE', () => {
    expect(demandDriverIcon('NEGATIVE')).toBe('↓')
  })

  it('returns right arrow for NEUTRAL', () => {
    expect(demandDriverIcon('NEUTRAL')).toBe('→')
  })

  it('returns right arrow for unknown impact', () => {
    expect(demandDriverIcon('UNKNOWN')).toBe('→')
  })
})

describe('isRaisingPrice', () => {
  it('returns true when new price is higher than current with elasticity data', () => {
    expect(isRaisingPrice(55, 45, -1.0)).toBe(true)
  })

  it('returns false when new price is lower', () => {
    expect(isRaisingPrice(40, 45, -1.0)).toBe(false)
  })

  it('returns false when prices are equal', () => {
    expect(isRaisingPrice(45, 45, -1.0)).toBe(false)
  })

  it('returns false when elasticity is null', () => {
    expect(isRaisingPrice(55, 45, null)).toBe(false)
  })

  it('returns false when quickPriceInput is null', () => {
    expect(isRaisingPrice(null, 45, -1.0)).toBe(false)
  })

  it('returns false when current price is zero', () => {
    expect(isRaisingPrice(10, 0, -1.0)).toBe(false)
  })
})

describe('isLoweringPrice', () => {
  it('returns true when new price is lower than current with elasticity data', () => {
    expect(isLoweringPrice(35, 45, -1.0)).toBe(true)
  })

  it('returns false when new price is higher', () => {
    expect(isLoweringPrice(50, 45, -1.0)).toBe(false)
  })

  it('returns false when prices are equal', () => {
    expect(isLoweringPrice(45, 45, -1.0)).toBe(false)
  })

  it('returns false when elasticity is null', () => {
    expect(isLoweringPrice(35, 45, null)).toBe(false)
  })

  it('returns false when quickPriceInput is null', () => {
    expect(isLoweringPrice(null, 45, -1.0)).toBe(false)
  })
})

describe('formatElasticityIndex', () => {
  it('formats a negative decimal to 2 decimal places', () => {
    expect(formatElasticityIndex(-1.23456)).toBe('-1.23')
  })

  it('formats zero to 2 decimal places', () => {
    expect(formatElasticityIndex(0)).toBe('0.00')
  })

  it('returns empty string for null', () => {
    expect(formatElasticityIndex(null)).toBe('')
  })

  it('returns empty string for undefined', () => {
    expect(formatElasticityIndex(undefined)).toBe('')
  })

  it('handles positive values (edge case)', () => {
    expect(formatElasticityIndex(0.5)).toBe('0.50')
  })
})

describe('formatUtilization', () => {
  it('formats 0.83 as 83%', () => {
    expect(formatUtilization(0.83, true)).toBe('83%')
  })

  it('rounds to nearest percent', () => {
    expect(formatUtilization(0.835, true)).toBe('84%')
  })

  it('returns em-dash when no history', () => {
    expect(formatUtilization(0.5, false)).toBe('–')
  })

  it('formats 0 as 0%', () => {
    expect(formatUtilization(0, true)).toBe('0%')
  })

  it('formats 1 as 100%', () => {
    expect(formatUtilization(1.0, true)).toBe('100%')
  })
})
