/**
 * Pure helper functions for loan/lending marketplace calculations and formatting.
 * All functions are side-effect free and testable without Vue or i18n context.
 */

import type { LoanSummary, LoanOfferSummary, LoanStatus } from '@/types'

/** Ticks per in-game year (24 ticks/day × 365 days/year). */
export const TICKS_PER_YEAR = 24 * 365

/** Ticks per in-game day. */
export const TICKS_PER_DAY = 24

/**
 * Converts a tick duration to approximate in-game days.
 * @example ticksToDays(720) // => 30
 */
export function ticksToDays(ticks: number): number {
  return Math.round(ticks / TICKS_PER_DAY)
}

/**
 * Converts a tick duration to approximate in-game years.
 * @example ticksToYears(8760) // => 1
 */
export function ticksToYears(ticks: number): number {
  return ticks / TICKS_PER_YEAR
}

/**
 * Formats a tick-duration as a human-readable in-game time string.
 * E.g. 720 ticks → "30 days", 8760 ticks → "1 year"
 */
export function formatLoanDuration(ticks: number): string {
  const years = ticks / TICKS_PER_YEAR
  if (years >= 1) {
    const rounded = Math.round(years * 10) / 10
    return rounded === 1 ? '1 year' : `${rounded} years`
  }
  const days = Math.round(ticks / TICKS_PER_DAY)
  return days === 1 ? '1 day' : `${days} days`
}

/**
 * Computes total interest cost for a loan given principal, rate, and duration.
 * Uses simple (flat) interest: interest = principal × rate × (ticks / ticksPerYear).
 */
export function computeTotalInterest(
  principal: number,
  annualRatePercent: number,
  durationTicks: number,
): number {
  return principal * (annualRatePercent / 100) * (durationTicks / TICKS_PER_YEAR)
}

/**
 * Computes total repayment (principal + interest).
 */
export function computeTotalRepayment(
  principal: number,
  annualRatePercent: number,
  durationTicks: number,
): number {
  return principal + computeTotalInterest(principal, annualRatePercent, durationTicks)
}

/**
 * Computes estimated periodic payment amount.
 * Payments are made every 720 ticks (30 in-game days).
 */
export function computePaymentAmount(
  principal: number,
  annualRatePercent: number,
  durationTicks: number,
): number {
  const ticksPerPayment = 720
  const totalPayments = Math.max(1, Math.floor(durationTicks / ticksPerPayment))
  const totalRepayment = computeTotalRepayment(principal, annualRatePercent, durationTicks)
  return totalRepayment / totalPayments
}

/**
 * Returns the number of scheduled payments for a loan.
 */
export function computeTotalPayments(durationTicks: number): number {
  const ticksPerPayment = 720
  return Math.max(1, Math.floor(durationTicks / ticksPerPayment))
}

/**
 * Returns a CSS class name for a loan status badge.
 */
export function loanStatusClass(status: LoanStatus | string): string {
  switch (status) {
    case 'ACTIVE':
      return 'status-active'
    case 'OVERDUE':
      return 'status-overdue'
    case 'DEFAULTED':
      return 'status-defaulted'
    case 'REPAID':
      return 'status-repaid'
    default:
      return 'status-unknown'
  }
}

/**
 * Returns true if the loan is in a warning state (overdue or defaulted).
 */
export function isLoanAtRisk(loan: LoanSummary): boolean {
  return loan.status === 'OVERDUE' || loan.status === 'DEFAULTED'
}

/**
 * Returns a percentage indicating how much capacity has been used.
 * @returns 0–100
 */
export function computeCapacityUsedPercent(offer: LoanOfferSummary): number {
  if (offer.totalCapacity <= 0) return 0
  return Math.min(100, (offer.usedCapacity / offer.totalCapacity) * 100)
}

/**
 * Computes remaining ticks until a loan is due.
 * Returns 0 if the loan is past due.
 */
export function computeTicksUntilDue(loan: LoanSummary, currentTick: number): number {
  return Math.max(0, loan.dueTick - currentTick)
}

/**
 * Computes remaining ticks until next payment.
 * Returns 0 if past due.
 */
export function computeTicksUntilNextPayment(loan: LoanSummary, currentTick: number): number {
  return Math.max(0, loan.nextPaymentTick - currentTick)
}

/**
 * Formats a currency amount as a compact string.
 * @example formatCurrency(12345.67) // => "$12,346"
 */
export function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(amount)
}

/**
 * Formats a percentage with 1 decimal place.
 * @example formatPercent(12.5) // => "12.5%"
 */
export function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`
}
