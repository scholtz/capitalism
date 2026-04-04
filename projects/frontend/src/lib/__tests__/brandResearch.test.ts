import { describe, it, expect } from 'vitest'
import {
  getBrandScopeShortLabel,
  getBrandScopeExplanation,
  formatEfficiencyMultiplier,
  efficiencyMultiplierProgress,
  scopeRequiresProduct,
} from '../brandResearch'

describe('getBrandScopeShortLabel', () => {
  it('returns Product for PRODUCT scope', () => {
    expect(getBrandScopeShortLabel('PRODUCT')).toBe('Product')
  })

  it('returns Category for CATEGORY scope', () => {
    expect(getBrandScopeShortLabel('CATEGORY')).toBe('Category')
  })

  it('returns Company for COMPANY scope', () => {
    expect(getBrandScopeShortLabel('COMPANY')).toBe('Company')
  })

  it('returns None for null scope', () => {
    expect(getBrandScopeShortLabel(null)).toBe('None')
  })

  it('returns None for undefined scope', () => {
    expect(getBrandScopeShortLabel(undefined)).toBe('None')
  })

  it('returns the raw string for an unknown scope', () => {
    expect(getBrandScopeShortLabel('UNKNOWN')).toBe('UNKNOWN')
  })
})

describe('getBrandScopeExplanation', () => {
  it('explains PRODUCT scope as narrowest with strongest benefit', () => {
    const explanation = getBrandScopeExplanation('PRODUCT')
    expect(explanation).toContain('one specific product')
    expect(explanation).toContain('strongest benefit')
  })

  it('explains CATEGORY scope as industry-wide balance', () => {
    const explanation = getBrandScopeExplanation('CATEGORY')
    expect(explanation).toContain('industry category')
    expect(explanation).toContain('balance')
  })

  it('explains COMPANY scope as broadest with moderate benefit', () => {
    const explanation = getBrandScopeExplanation('COMPANY')
    expect(explanation).toContain('company-wide')
    expect(explanation).toContain('moderate')
  })

  it('returns a default prompt for null scope', () => {
    const explanation = getBrandScopeExplanation(null)
    expect(explanation).toContain('Select a scope')
  })

  it('returns a default prompt for undefined scope', () => {
    const explanation = getBrandScopeExplanation(undefined)
    expect(explanation).toContain('Select a scope')
  })
})

describe('formatEfficiencyMultiplier', () => {
  it('formats baseline as 1.00×', () => {
    expect(formatEfficiencyMultiplier(1)).toBe('1.00×')
  })

  it('formats a 50% bonus as 1.50×', () => {
    expect(formatEfficiencyMultiplier(1.5)).toBe('1.50×')
  })

  it('formats double efficiency as 2.00×', () => {
    expect(formatEfficiencyMultiplier(2)).toBe('2.00×')
  })

  it('clamps values below 1 to 1.00×', () => {
    expect(formatEfficiencyMultiplier(0.5)).toBe('1.00×')
    expect(formatEfficiencyMultiplier(0)).toBe('1.00×')
  })

  it('rounds to two decimal places', () => {
    expect(formatEfficiencyMultiplier(1.4567)).toBe('1.46×')
  })
})

describe('efficiencyMultiplierProgress', () => {
  it('returns 0% at baseline (1.0)', () => {
    expect(efficiencyMultiplierProgress(1)).toBe(0)
  })

  it('returns 50% at the halfway point between baseline and max', () => {
    // default max is 3, halfway is 2.0
    expect(efficiencyMultiplierProgress(2, 3)).toBe(50)
  })

  it('returns 100% at the maximum multiplier', () => {
    expect(efficiencyMultiplierProgress(3, 3)).toBe(100)
  })

  it('clamps values above the max to 100%', () => {
    expect(efficiencyMultiplierProgress(10, 3)).toBe(100)
  })

  it('returns 0% for sub-baseline values', () => {
    expect(efficiencyMultiplierProgress(0)).toBe(0)
  })

  it('works with a custom max multiplier', () => {
    // max=2: halfway is 1.5
    expect(efficiencyMultiplierProgress(1.5, 2)).toBe(50)
  })
})

describe('scopeRequiresProduct', () => {
  it('returns true for PRODUCT scope', () => {
    expect(scopeRequiresProduct('PRODUCT')).toBe(true)
  })

  it('returns true for CATEGORY scope', () => {
    expect(scopeRequiresProduct('CATEGORY')).toBe(true)
  })

  it('returns false for COMPANY scope', () => {
    expect(scopeRequiresProduct('COMPANY')).toBe(false)
  })

  it('returns false for null scope', () => {
    expect(scopeRequiresProduct(null)).toBe(false)
  })

  it('returns false for undefined scope', () => {
    expect(scopeRequiresProduct(undefined)).toBe(false)
  })
})
