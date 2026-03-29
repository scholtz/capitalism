import { describe, it, expect } from 'vitest'
import { computeSimulatedProfit, trackOnboardingEvent } from '../onboardingAnalytics'

describe('trackOnboardingEvent', () => {
  it('returns without throwing when window is not defined (SSR/node)', () => {
    // In the node test environment window is undefined; the function must be a no-op.
    expect(() => trackOnboardingEvent('onboarding_start')).not.toThrow()
    expect(() => trackOnboardingEvent('industry_selected', { industry: 'FURNITURE' })).not.toThrow()
    expect(() => trackOnboardingEvent('completed')).not.toThrow()
  })
})

describe('computeSimulatedProfit', () => {
  it('uses 40% cost estimate when recipeCost is not provided', () => {
    const result = computeSimulatedProfit(100, 5)
    expect(result.revenue).toBe(500) // 100 * 5
    expect(result.cost).toBe(200) // 40% of 500
    expect(result.profit).toBe(300) // 500 - 200
  })

  it('uses provided recipeCost when given', () => {
    const result = computeSimulatedProfit(100, 5, 150)
    expect(result.revenue).toBe(500)
    expect(result.cost).toBe(150)
    expect(result.profit).toBe(350)
  })

  it('returns zero profit when cost equals revenue', () => {
    const result = computeSimulatedProfit(100, 1, 100)
    expect(result.revenue).toBe(100)
    expect(result.cost).toBe(100)
    expect(result.profit).toBe(0)
  })

  it('handles fractional base prices by rounding', () => {
    const result = computeSimulatedProfit(9.99, 3)
    // revenue = round(9.99 * 3) = round(29.97) = 30
    expect(result.revenue).toBe(30)
    expect(result.cost).toBe(Math.round(30 * 0.4)) // 12
    expect(result.profit).toBe(18)
  })

  it('handles output quantity of 1', () => {
    const result = computeSimulatedProfit(200, 1)
    expect(result.revenue).toBe(200)
    expect(result.cost).toBe(80)
    expect(result.profit).toBe(120)
  })

  it('handles zero base price', () => {
    const result = computeSimulatedProfit(0, 10)
    expect(result.revenue).toBe(0)
    expect(result.cost).toBe(0)
    expect(result.profit).toBe(0)
  })

  it('handles zero output quantity', () => {
    const result = computeSimulatedProfit(100, 0)
    expect(result.revenue).toBe(0)
    expect(result.cost).toBe(0)
    expect(result.profit).toBe(0)
  })

  it('profit can be negative when recipeCost exceeds revenue', () => {
    const result = computeSimulatedProfit(10, 1, 50)
    expect(result.revenue).toBe(10)
    expect(result.cost).toBe(50)
    expect(result.profit).toBe(-40)
  })
})
