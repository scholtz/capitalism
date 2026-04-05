import { describe, it, expect } from 'vitest'
import {
  ticksToDays,
  ticksToYears,
  formatLoanDuration,
  computeTotalInterest,
  computeTotalRepayment,
  computePaymentAmount,
  computeTotalPayments,
  loanStatusClass,
  isLoanAtRisk,
  computeCapacityUsedPercent,
  computeTicksUntilDue,
  computeTicksUntilNextPayment,
  formatCurrency,
  formatPercent,
  TICKS_PER_YEAR,
  TICKS_PER_DAY,
} from '../loanHelpers'
import type { LoanSummary, LoanOfferSummary } from '@/types'

function makeLoan(overrides: Partial<LoanSummary> = {}): LoanSummary {
  return {
    id: 'loan-1',
    loanOfferId: 'offer-1',
    borrowerCompanyId: 'co-b',
    borrowerCompanyName: 'BorrowerCo',
    lenderCompanyId: 'co-l',
    lenderCompanyName: 'LenderCo',
    bankBuildingId: 'bank-1',
    bankBuildingName: 'My Bank',
    originalPrincipal: 10000,
    remainingPrincipal: 10000,
    annualInterestRatePercent: 12,
    durationTicks: 1440,
    startTick: 0,
    dueTick: 1440,
    nextPaymentTick: 720,
    paymentAmount: 500,
    paymentsMade: 0,
    totalPayments: 2,
    status: 'ACTIVE',
    missedPayments: 0,
    accumulatedPenalty: 0,
    acceptedAtUtc: '2026-01-01T00:00:00Z',
    closedAtUtc: null,
    ...overrides,
  }
}

function makeOffer(overrides: Partial<LoanOfferSummary> = {}): LoanOfferSummary {
  return {
    id: 'offer-1',
    bankBuildingId: 'bank-1',
    bankBuildingName: 'My Bank',
    cityId: 'city-1',
    cityName: 'Bratislava',
    lenderCompanyId: 'co-l',
    lenderCompanyName: 'LenderCo',
    annualInterestRatePercent: 10,
    maxPrincipalPerLoan: 50000,
    totalCapacity: 200000,
    usedCapacity: 0,
    remainingCapacity: 200000,
    durationTicks: 1440,
    isActive: true,
    createdAtTick: 0,
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('loanHelpers', () => {
  describe('ticksToDays', () => {
    it('converts 24 ticks to 1 day', () => expect(ticksToDays(24)).toBe(1))
    it('converts 720 ticks to 30 days', () => expect(ticksToDays(720)).toBe(30))
    it('converts 0 ticks to 0 days', () => expect(ticksToDays(0)).toBe(0))
    it('rounds fractional days', () => expect(ticksToDays(25)).toBe(1))
  })

  describe('ticksToYears', () => {
    it('converts TICKS_PER_YEAR to 1', () => expect(ticksToYears(TICKS_PER_YEAR)).toBe(1))
    it('converts 0 to 0', () => expect(ticksToYears(0)).toBe(0))
    it('handles half a year', () => expect(ticksToYears(TICKS_PER_YEAR / 2)).toBe(0.5))
  })

  describe('formatLoanDuration', () => {
    it('formats 1 day', () => expect(formatLoanDuration(TICKS_PER_DAY)).toBe('1 day'))
    it('formats 30 days', () => expect(formatLoanDuration(720)).toBe('30 days'))
    it('formats 1 year', () => expect(formatLoanDuration(TICKS_PER_YEAR)).toBe('1 year'))
    it('formats 2 years', () => expect(formatLoanDuration(TICKS_PER_YEAR * 2)).toBe('2 years'))
    it('formats 0 ticks as 0 days', () => expect(formatLoanDuration(0)).toBe('0 days'))
  })

  describe('computeTotalInterest', () => {
    it('computes interest for a 1-year loan at 10%', () => {
      const interest = computeTotalInterest(10000, 10, TICKS_PER_YEAR)
      expect(interest).toBeCloseTo(1000, 2)
    })
    it('returns 0 when rate is 0', () => {
      expect(computeTotalInterest(10000, 0, TICKS_PER_YEAR)).toBe(0)
    })
    it('returns 0 for 0 principal', () => {
      expect(computeTotalInterest(0, 10, TICKS_PER_YEAR)).toBe(0)
    })
    it('handles half-year duration', () => {
      const interest = computeTotalInterest(10000, 12, TICKS_PER_YEAR / 2)
      expect(interest).toBeCloseTo(600, 2) // 12% / 2 = 6%
    })
  })

  describe('computeTotalRepayment', () => {
    it('equals principal + interest', () => {
      const total = computeTotalRepayment(10000, 10, TICKS_PER_YEAR)
      expect(total).toBeCloseTo(11000, 2)
    })
    it('returns principal when rate is 0', () => {
      expect(computeTotalRepayment(10000, 0, TICKS_PER_YEAR)).toBe(10000)
    })
  })

  describe('computePaymentAmount', () => {
    it('returns positive payment for normal loan', () => {
      const payment = computePaymentAmount(10000, 12, 1440)
      expect(payment).toBeGreaterThan(0)
    })
    it('handles single payment (short duration)', () => {
      const payment = computePaymentAmount(1000, 10, 100)
      expect(payment).toBeGreaterThan(1000) // principal + some interest
    })
  })

  describe('computeTotalPayments', () => {
    it('returns 2 payments for 1440 ticks (2×720)', () => {
      expect(computeTotalPayments(1440)).toBe(2)
    })
    it('returns at least 1 for very short duration', () => {
      expect(computeTotalPayments(24)).toBe(1)
    })
    it('returns 0 not possible (minimum 1)', () => {
      expect(computeTotalPayments(0)).toBe(1)
    })
  })

  describe('loanStatusClass', () => {
    it('returns status-active for ACTIVE', () => {
      expect(loanStatusClass('ACTIVE')).toBe('status-active')
    })
    it('returns status-overdue for OVERDUE', () => {
      expect(loanStatusClass('OVERDUE')).toBe('status-overdue')
    })
    it('returns status-defaulted for DEFAULTED', () => {
      expect(loanStatusClass('DEFAULTED')).toBe('status-defaulted')
    })
    it('returns status-repaid for REPAID', () => {
      expect(loanStatusClass('REPAID')).toBe('status-repaid')
    })
    it('returns status-unknown for unknown value', () => {
      expect(loanStatusClass('UNKNOWN')).toBe('status-unknown')
    })
  })

  describe('isLoanAtRisk', () => {
    it('returns false for ACTIVE loan', () => {
      expect(isLoanAtRisk(makeLoan({ status: 'ACTIVE' }))).toBe(false)
    })
    it('returns true for OVERDUE loan', () => {
      expect(isLoanAtRisk(makeLoan({ status: 'OVERDUE' }))).toBe(true)
    })
    it('returns true for DEFAULTED loan', () => {
      expect(isLoanAtRisk(makeLoan({ status: 'DEFAULTED' }))).toBe(true)
    })
    it('returns false for REPAID loan', () => {
      expect(isLoanAtRisk(makeLoan({ status: 'REPAID' }))).toBe(false)
    })
  })

  describe('computeCapacityUsedPercent', () => {
    it('returns 0 for unused capacity', () => {
      expect(computeCapacityUsedPercent(makeOffer({ usedCapacity: 0, totalCapacity: 100000 }))).toBe(0)
    })
    it('returns 50 for half used', () => {
      expect(computeCapacityUsedPercent(makeOffer({ usedCapacity: 50000, totalCapacity: 100000 }))).toBe(50)
    })
    it('returns 100 for fully used', () => {
      expect(computeCapacityUsedPercent(makeOffer({ usedCapacity: 100000, totalCapacity: 100000 }))).toBe(100)
    })
    it('caps at 100 for over-used', () => {
      expect(computeCapacityUsedPercent(makeOffer({ usedCapacity: 120000, totalCapacity: 100000 }))).toBe(100)
    })
    it('returns 0 for zero totalCapacity', () => {
      expect(computeCapacityUsedPercent(makeOffer({ usedCapacity: 0, totalCapacity: 0 }))).toBe(0)
    })
  })

  describe('computeTicksUntilDue', () => {
    it('returns remaining ticks when not due', () => {
      expect(computeTicksUntilDue(makeLoan({ dueTick: 1440 }), 500)).toBe(940)
    })
    it('returns 0 when past due', () => {
      expect(computeTicksUntilDue(makeLoan({ dueTick: 1440 }), 2000)).toBe(0)
    })
    it('returns 0 when exactly due', () => {
      expect(computeTicksUntilDue(makeLoan({ dueTick: 1440 }), 1440)).toBe(0)
    })
  })

  describe('computeTicksUntilNextPayment', () => {
    it('returns remaining ticks to next payment', () => {
      expect(computeTicksUntilNextPayment(makeLoan({ nextPaymentTick: 720 }), 100)).toBe(620)
    })
    it('returns 0 when past due', () => {
      expect(computeTicksUntilNextPayment(makeLoan({ nextPaymentTick: 720 }), 800)).toBe(0)
    })
  })

  describe('formatCurrency', () => {
    it('formats positive amount', () => {
      expect(formatCurrency(12345)).toContain('12,345')
    })
    it('formats zero', () => {
      expect(formatCurrency(0)).toContain('0')
    })
  })

  describe('formatPercent', () => {
    it('formats 12.5 as 12.5%', () => {
      expect(formatPercent(12.5)).toBe('12.5%')
    })
    it('formats 0 as 0.0%', () => {
      expect(formatPercent(0)).toBe('0.0%')
    })
    it('formats integer', () => {
      expect(formatPercent(10)).toBe('10.0%')
    })
  })
})
