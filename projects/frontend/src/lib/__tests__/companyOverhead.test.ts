import { describe, it, expect } from 'vitest'
import {
  getOverheadStatus,
  OVERHEAD_MEDIUM_THRESHOLD,
  OVERHEAD_HIGH_THRESHOLD,
} from '../companyOverhead'

describe('getOverheadStatus', () => {
  it('returns low for zero overhead', () => {
    expect(getOverheadStatus(0)).toBe('low')
  })

  it('returns low for rates below the medium threshold', () => {
    expect(getOverheadStatus(0.14)).toBe('low')
    expect(getOverheadStatus(OVERHEAD_MEDIUM_THRESHOLD - 0.001)).toBe('low')
  })

  it('returns medium at exactly the medium threshold', () => {
    expect(getOverheadStatus(OVERHEAD_MEDIUM_THRESHOLD)).toBe('medium')
  })

  it('returns medium for rates between medium and high thresholds', () => {
    expect(getOverheadStatus(0.20)).toBe('medium')
    expect(getOverheadStatus(OVERHEAD_HIGH_THRESHOLD - 0.001)).toBe('medium')
  })

  it('returns high at exactly the high threshold', () => {
    expect(getOverheadStatus(OVERHEAD_HIGH_THRESHOLD)).toBe('high')
  })

  it('returns high for rates at maximum (50%)', () => {
    expect(getOverheadStatus(0.5)).toBe('high')
  })

  it('returns high for rates above high threshold', () => {
    expect(getOverheadStatus(0.40)).toBe('high')
  })
})
