import { describe, it, expect } from 'vitest'
import { computeSimulatedProfit, trackOnboardingEvent } from '../onboardingAnalytics'

describe('trackOnboardingEvent', () => {
  it('returns without throwing when window is not defined (SSR/node)', () => {
    // In the node test environment window is undefined; the function must be a no-op.
    expect(() => trackOnboardingEvent('onboarding_start')).not.toThrow()
    expect(() => trackOnboardingEvent('industry_selected', { industry: 'FURNITURE' })).not.toThrow()
    expect(() => trackOnboardingEvent('completed')).not.toThrow()
  })

  it('accepts onboarding_converted event without throwing', () => {
    expect(() =>
      trackOnboardingEvent('onboarding_converted', {
        industry: 'FURNITURE',
        cityId: 'city-ba',
        authMode: 'register',
      }),
    ).not.toThrow()
  })

  it('accepts all defined event types without throwing', () => {
    const events = [
      'onboarding_start',
      'industry_selected',
      'city_selected',
      'factory_configured',
      'shop_configured',
      'save_prompt_shown',
      'first_profit_shown',
      'completed',
      'onboarding_converted',
    ] as const
    for (const evt of events) {
      expect(() => trackOnboardingEvent(evt)).not.toThrow()
    }
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

  // Industry-specific tests using actual seed data values:
  // These document the expected simulated profit for each starter industry
  // when the backend provides exact resource costs in the recipe.
  // Seed values: Wood=$10/t, Grain=$5/t, Chemical Minerals=$30/t

  it('Furniture (Wooden Chair): basePrice=45, outputQty=20, woodCost=10 → profit=890', () => {
    // Wooden Chair: 20 chairs @ $45 each = $900 revenue, 1 wood @ $10 = $10 cost
    const result = computeSimulatedProfit(45, 20, 10)
    expect(result.revenue).toBe(900)
    expect(result.cost).toBe(10)
    expect(result.profit).toBe(890)
  })

  it('Food Processing (Bread): basePrice=3, outputQty=12, grainCost=5 → profit=31', () => {
    // Bread: 12 loaves @ $3 each = $36 revenue, 1 grain @ $5 = $5 cost
    const result = computeSimulatedProfit(3, 12, 5)
    expect(result.revenue).toBe(36)
    expect(result.cost).toBe(5)
    expect(result.profit).toBe(31)
  })

  it('Healthcare (Basic Medicine): basePrice=50, outputQty=8, chemMineralsCost=30 → profit=370', () => {
    // Basic Medicine: 8 bottles @ $50 each = $400 revenue, 1 chemical minerals @ $30 = $30 cost
    const result = computeSimulatedProfit(50, 8, 30)
    expect(result.revenue).toBe(400)
    expect(result.cost).toBe(30)
    expect(result.profit).toBe(370)
  })

  it('Furniture fallback (no recipeCost): uses 40% estimate on $900 revenue → profit=540', () => {
    // When resource basePrice is not available from the API, 40% of revenue is used as cost estimate.
    // This matches the guest-mode mock behavior where resourceType.basePrice is undefined.
    const result = computeSimulatedProfit(45, 20)
    expect(result.revenue).toBe(900)
    expect(result.cost).toBe(360) // round(900 * 0.4)
    expect(result.profit).toBe(540)
  })

  it('Food Processing fallback (no recipeCost): uses 40% estimate on $36 revenue → profit=22', () => {
    // When grain basePrice is not available, cost estimate = 40% of $36 = $14 (rounded).
    const result = computeSimulatedProfit(3, 12)
    expect(result.revenue).toBe(36)
    expect(result.cost).toBe(Math.round(36 * 0.4)) // 14
    expect(result.profit).toBe(22)
  })

  it('Healthcare fallback (no recipeCost): uses 40% estimate on $400 revenue → profit=240', () => {
    // When chemical minerals basePrice is not available, cost estimate = 40% of $400 = $160.
    const result = computeSimulatedProfit(50, 8)
    expect(result.revenue).toBe(400)
    expect(result.cost).toBe(160) // round(400 * 0.4)
    expect(result.profit).toBe(240)
  })
})
