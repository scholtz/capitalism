import { describe, it, expect } from 'vitest'
import {
  computeExchangePrice,
  computeExchangeQuality,
  computeDistanceKm,
  computeTransitCostPerUnit,
  computeDeliveredPrice,
  DEFAULT_MISSING_ABUNDANCE,
} from '../globalExchange'

describe('computeExchangePrice', () => {
  it('returns a positive price for typical inputs', () => {
    const price = computeExchangePrice(10, 0.5, 15)
    expect(price).toBeGreaterThan(0)
  })

  it('produces higher price when abundance is low (scarce)', () => {
    const cheapCity = computeExchangePrice(10, 0.8, 15) // high abundance = cheap
    const expensiveCity = computeExchangePrice(10, 0.1, 15) // low abundance = expensive
    expect(expensiveCity).toBeGreaterThan(cheapCity)
  })

  it('produces higher price in expensive cities (higher rent)', () => {
    const cheapCity = computeExchangePrice(10, 0.5, 5)
    const expensiveCity = computeExchangePrice(10, 0.5, 50)
    expect(expensiveCity).toBeGreaterThan(cheapCity)
  })

  it('clamps abundance to [0, 1] range', () => {
    const clampedLow = computeExchangePrice(10, -0.5, 15)
    const zeroAbundance = computeExchangePrice(10, 0, 15)
    expect(clampedLow).toEqual(zeroAbundance)

    const clampedHigh = computeExchangePrice(10, 1.5, 15)
    const fullAbundance = computeExchangePrice(10, 1, 15)
    expect(clampedHigh).toEqual(fullAbundance)
  })

  it('rounds to 2 decimal places', () => {
    const price = computeExchangePrice(7.77, 0.333, 12.5)
    expect(price.toString()).toMatch(/^\d+\.\d{1,2}$/)
  })

  it('uses DEFAULT_MISSING_ABUNDANCE for fallback computations', () => {
    const fallbackPrice = computeExchangePrice(10, DEFAULT_MISSING_ABUNDANCE, 15)
    expect(fallbackPrice).toBeGreaterThan(0)
    expect(DEFAULT_MISSING_ABUNDANCE).toBe(0.05)
  })
})

describe('computeExchangeQuality', () => {
  it('returns quality in [0.35, 0.95] for all valid inputs', () => {
    for (const abundance of [0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0]) {
      const quality = computeExchangeQuality(abundance)
      expect(quality).toBeGreaterThanOrEqual(0.35)
      expect(quality).toBeLessThanOrEqual(0.95)
    }
  })

  it('returns minimum quality (0.35) for zero abundance', () => {
    expect(computeExchangeQuality(0)).toBe(0.35)
  })

  it('returns maximum quality (0.95) for full abundance', () => {
    expect(computeExchangeQuality(1)).toBe(0.95)
  })

  it('clamps values below 0 to minimum quality', () => {
    expect(computeExchangeQuality(-1)).toBe(0.35)
  })

  it('clamps values above 1 to maximum quality', () => {
    expect(computeExchangeQuality(2)).toBe(0.95)
  })

  it('quality increases with abundance', () => {
    const low = computeExchangeQuality(0.2)
    const mid = computeExchangeQuality(0.5)
    const high = computeExchangeQuality(0.8)
    expect(low).toBeLessThan(mid)
    expect(mid).toBeLessThan(high)
  })

  it('rounds to 4 decimal places', () => {
    const quality = computeExchangeQuality(0.333)
    const decimals = quality.toString().split('.')[1]?.length ?? 0
    expect(decimals).toBeLessThanOrEqual(4)
  })
})

describe('computeDistanceKm', () => {
  it('returns 0 for identical coordinates', () => {
    expect(computeDistanceKm(48.15, 17.11, 48.15, 17.11)).toBe(0)
  })

  it('returns a positive distance for different cities', () => {
    // Bratislava (48.15, 17.11) → Prague (50.08, 14.43)
    const dist = computeDistanceKm(48.15, 17.11, 50.08, 14.43)
    expect(dist).toBeGreaterThan(0)
  })

  it('returns approximately the right distance for Bratislava → Prague', () => {
    // Actual great-circle distance is roughly 277 km
    const dist = computeDistanceKm(48.15, 17.11, 50.08, 14.43)
    expect(dist).toBeGreaterThan(250)
    expect(dist).toBeLessThan(320)
  })

  it('returns approximately the right distance for Bratislava → Vienna', () => {
    // Bratislava → Vienna is roughly 55-60 km
    const dist = computeDistanceKm(48.15, 17.11, 48.21, 16.37)
    expect(dist).toBeGreaterThan(40)
    expect(dist).toBeLessThan(80)
  })

  it('is symmetric (A→B distance equals B→A distance)', () => {
    const distAB = computeDistanceKm(48.15, 17.11, 50.08, 14.43)
    const distBA = computeDistanceKm(50.08, 14.43, 48.15, 17.11)
    expect(distAB).toBeCloseTo(distBA, 5)
  })
})

describe('computeTransitCostPerUnit', () => {
  // Same coordinates = same city = zero transit cost
  it('returns 0 for same-city (zero distance)', () => {
    const cost = computeTransitCostPerUnit(48.15, 17.11, 48.15, 17.11, 1.0)
    expect(cost).toBe(0)
  })

  it('returns a positive cost for cross-city transit', () => {
    const cost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 1.0)
    expect(cost).toBeGreaterThan(0)
  })

  it('enforces minimum transit cost of 0.05 for cross-city transit', () => {
    // Very light resource (0.01 weight) over a short distance should still charge minimum
    const cost = computeTransitCostPerUnit(48.15, 17.11, 48.20, 17.15, 0.01)
    expect(cost).toBeGreaterThanOrEqual(0.05)
  })

  it('scales with resource weight', () => {
    const lightCost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 0.5)
    const heavyCost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 5.0)
    expect(heavyCost).toBeGreaterThan(lightCost)
  })

  it('scales with distance', () => {
    // Short route (BA → Vienna ~55 km)
    const shortCost = computeTransitCostPerUnit(48.15, 17.11, 48.21, 16.37, 1.0)
    // Long route (BA → Prague ~277 km)
    const longCost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 1.0)
    expect(longCost).toBeGreaterThan(shortCost)
  })

  it('uses minimum effective weight of 0.1 for very light resources', () => {
    const lightCost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 0.01)
    const minWeightCost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 0.1)
    expect(lightCost).toEqual(minWeightCost)
  })

  it('rounds to 2 decimal places', () => {
    const cost = computeTransitCostPerUnit(48.15, 17.11, 50.08, 14.43, 1.5)
    const decimals = cost.toString().split('.')[1]?.length ?? 0
    expect(decimals).toBeLessThanOrEqual(2)
  })
})

describe('computeDeliveredPrice', () => {
  it('returns the sum of exchange price and transit cost', () => {
    expect(computeDeliveredPrice(10.5, 2.3)).toBeCloseTo(12.8, 2)
  })

  it('equals exchange price when transit cost is zero (same city)', () => {
    expect(computeDeliveredPrice(15.75, 0)).toBe(15.75)
  })

  it('rounds to 2 decimal places', () => {
    const price = computeDeliveredPrice(10.123, 2.789)
    const decimals = price.toString().split('.')[1]?.length ?? 0
    expect(decimals).toBeLessThanOrEqual(2)
  })

  it('delivered price is always >= exchange price', () => {
    const exchangePrice = 8.5
    for (const transitCost of [0, 0.5, 1.23, 5.0]) {
      expect(computeDeliveredPrice(exchangePrice, transitCost)).toBeGreaterThanOrEqual(exchangePrice)
    }
  })
})

describe('exchange landed-cost integration', () => {
  it('correctly computes delivered price from city coordinates and resource metadata', () => {
    // Bratislava as destination, Prague as source
    const sourceAbundance = 0.6
    const basePricePerUnit = 10
    const weightPerUnit = 1.0
    const sourceRentPerSqm = 12

    const exchangePrice = computeExchangePrice(basePricePerUnit, sourceAbundance, sourceRentPerSqm)
    const transitCost = computeTransitCostPerUnit(50.08, 14.43, 48.15, 17.11, weightPerUnit) // Prague → Bratislava
    const delivered = computeDeliveredPrice(exchangePrice, transitCost)
    const quality = computeExchangeQuality(sourceAbundance)

    expect(exchangePrice).toBeGreaterThan(0)
    expect(transitCost).toBeGreaterThan(0)
    expect(delivered).toBeGreaterThan(exchangePrice)
    expect(quality).toBeGreaterThanOrEqual(0.35)
    expect(quality).toBeLessThanOrEqual(0.95)
  })

  it('same-city delivered price equals exchange price', () => {
    const abundance = 0.7
    const basePrice = 12
    const rentPerSqm = 18

    const exchangePrice = computeExchangePrice(basePrice, abundance, rentPerSqm)
    const transitCost = computeTransitCostPerUnit(48.15, 17.11, 48.15, 17.11, 1.0)
    const delivered = computeDeliveredPrice(exchangePrice, transitCost)

    expect(transitCost).toBe(0)
    expect(delivered).toBe(exchangePrice)
  })
})
