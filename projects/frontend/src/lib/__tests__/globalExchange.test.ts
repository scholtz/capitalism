import { describe, it, expect } from 'vitest'
import {
  computeExchangePrice,
  computeExchangeQuality,
  computeDistanceKm,
  computeTransitCostPerUnit,
  computeDeliveredPrice,
  DEFAULT_MISSING_ABUNDANCE,
  annotateExchangeOffers,
  selectOptimalOffer,
} from '../globalExchange'
import type { AnnotatedExchangeOffer } from '../globalExchange'

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

// ── Test helpers ────────────────────────────────────────────────────────────

function makeOffer(
  overrides: Partial<{
    cityId: string
    cityName: string
    deliveredPricePerUnit: number
    exchangePricePerUnit: number
    transitCostPerUnit: number
    estimatedQuality: number
    localAbundance: number
    distanceKm: number
  }> = {},
): {
  cityId: string
  cityName: string
  resourceTypeId: string
  resourceName: string
  resourceSlug: string
  unitSymbol: string
  localAbundance: number
  exchangePricePerUnit: number
  estimatedQuality: number
  transitCostPerUnit: number
  deliveredPricePerUnit: number
  distanceKm: number
} {
  return {
    cityId: 'city-ba',
    cityName: 'Bratislava',
    resourceTypeId: 'res-wood',
    resourceName: 'Wood',
    resourceSlug: 'wood',
    unitSymbol: 't',
    localAbundance: 0.7,
    exchangePricePerUnit: 11.0,
    transitCostPerUnit: 0,
    deliveredPricePerUnit: 11.0,
    estimatedQuality: 0.77,
    distanceKm: 0,
    ...overrides,
  }
}

// ── annotateExchangeOffers ───────────────────────────────────────────────────

describe('annotateExchangeOffers', () => {
  it('returns all offers as non-blocked when no constraints are set (null maxPrice and minQuality)', () => {
    const offers = [
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, estimatedQuality: 0.77 }),
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 13.5, estimatedQuality: 0.72 }),
    ]
    const result = annotateExchangeOffers(offers, null, null)
    expect(result).toHaveLength(2)
    expect(result[0].blocked).toBe(false)
    expect(result[0].blockedReason).toBeNull()
    expect(result[1].blocked).toBe(false)
    expect(result[1].blockedReason).toBeNull()
  })

  it('blocks offers whose deliveredPricePerUnit exceeds maxPrice', () => {
    const offers = [
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 10.0 }),
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 15.0 }),
      makeOffer({ cityId: 'city-vi', deliveredPricePerUnit: 12.0 }),
    ]
    const result = annotateExchangeOffers(offers, 11.0, null)
    expect(result[0].blocked).toBe(false) // $10 ≤ $11 → eligible
    expect(result[1].blocked).toBe(true) // $15 > $11 → blocked
    expect(result[1].blockedReason).toBe('maxPrice')
    expect(result[2].blocked).toBe(true) // $12 > $11 → blocked
    expect(result[2].blockedReason).toBe('maxPrice')
  })

  it('blocks offers whose estimatedQuality falls below minQuality', () => {
    const offers = [
      makeOffer({ cityId: 'city-ba', estimatedQuality: 0.77 }),
      makeOffer({ cityId: 'city-pr', estimatedQuality: 0.60 }),
      makeOffer({ cityId: 'city-vi', estimatedQuality: 0.82 }),
    ]
    const result = annotateExchangeOffers(offers, null, 0.75)
    expect(result[0].blocked).toBe(false) // 0.77 ≥ 0.75 → eligible
    expect(result[1].blocked).toBe(true) // 0.60 < 0.75 → blocked
    expect(result[1].blockedReason).toBe('minQuality')
    expect(result[2].blocked).toBe(false) // 0.82 ≥ 0.75 → eligible
  })

  it('maxPrice check takes precedence over minQuality check', () => {
    // An offer that fails both checks must be reported as blocked by 'maxPrice' first.
    const offers = [
      makeOffer({ deliveredPricePerUnit: 20.0, estimatedQuality: 0.30 }),
    ]
    const result = annotateExchangeOffers(offers, 15.0, 0.75)
    expect(result[0].blocked).toBe(true)
    expect(result[0].blockedReason).toBe('maxPrice') // maxPrice checked first
  })

  it('returns all offers blocked when maxPrice is 0', () => {
    const offers = [
      makeOffer({ deliveredPricePerUnit: 0.01 }),
      makeOffer({ deliveredPricePerUnit: 5.0 }),
    ]
    const result = annotateExchangeOffers(offers, 0, null)
    expect(result.every((o) => o.blocked)).toBe(true)
    expect(result.every((o) => o.blockedReason === 'maxPrice')).toBe(true)
  })

  it('allows offers whose deliveredPricePerUnit equals maxPrice (boundary)', () => {
    const offers = [makeOffer({ deliveredPricePerUnit: 11.0 })]
    const result = annotateExchangeOffers(offers, 11.0, null)
    expect(result[0].blocked).toBe(false)
  })

  it('allows offers whose estimatedQuality equals minQuality (boundary)', () => {
    const offers = [makeOffer({ estimatedQuality: 0.75 })]
    const result = annotateExchangeOffers(offers, null, 0.75)
    expect(result[0].blocked).toBe(false)
  })

  it('returns empty array for empty input', () => {
    expect(annotateExchangeOffers([], null, null)).toHaveLength(0)
    expect(annotateExchangeOffers([], 10.0, 0.5)).toHaveLength(0)
  })

  it('preserves original offer order', () => {
    const offers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 8.0 }),
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0 }),
      makeOffer({ cityId: 'city-vi', deliveredPricePerUnit: 14.0 }),
    ]
    const result = annotateExchangeOffers(offers, null, null)
    expect(result.map((o) => o.cityId)).toEqual(['city-pr', 'city-ba', 'city-vi'])
  })

  it('does not mutate the input offers array', () => {
    const offers = [makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0 })]
    const originalRef = offers[0]
    annotateExchangeOffers(offers, 5.0, null)
    // The original object must not have gained blocked/blockedReason properties.
    expect((originalRef as Record<string, unknown>)['blocked']).toBeUndefined()
    expect((originalRef as Record<string, unknown>)['blockedReason']).toBeUndefined()
  })
})

// ── selectOptimalOffer ───────────────────────────────────────────────────────

describe('selectOptimalOffer', () => {
  function makeAnnotated(
    overrides: Partial<AnnotatedExchangeOffer> = {},
  ): AnnotatedExchangeOffer {
    return {
      cityId: 'city-ba',
      cityName: 'Bratislava',
      resourceTypeId: 'res-wood',
      resourceName: 'Wood',
      resourceSlug: 'wood',
      unitSymbol: 't',
      localAbundance: 0.7,
      exchangePricePerUnit: 11.0,
      transitCostPerUnit: 0,
      deliveredPricePerUnit: 11.0,
      estimatedQuality: 0.77,
      distanceKm: 0,
      blocked: false,
      blockedReason: null,
      ...overrides,
    }
  }

  it('returns null for an empty offers list', () => {
    expect(selectOptimalOffer([])).toBeNull()
  })

  it('returns null when all offers are blocked', () => {
    const offers = [
      makeAnnotated({ cityId: 'city-ba', blocked: true, blockedReason: 'maxPrice' }),
      makeAnnotated({ cityId: 'city-pr', blocked: true, blockedReason: 'minQuality' }),
    ]
    expect(selectOptimalOffer(offers)).toBeNull()
  })

  it('returns the single non-blocked offer', () => {
    const offers = [makeAnnotated({ cityId: 'city-ba', blocked: false })]
    expect(selectOptimalOffer(offers)?.cityId).toBe('city-ba')
  })

  it('returns the first non-blocked offer (best delivered price in pre-sorted list)', () => {
    const offers = [
      makeAnnotated({ cityId: 'city-pr', deliveredPricePerUnit: 8.0, blocked: false }),
      makeAnnotated({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, blocked: false }),
      makeAnnotated({ cityId: 'city-vi', deliveredPricePerUnit: 14.0, blocked: false }),
    ]
    expect(selectOptimalOffer(offers)?.cityId).toBe('city-pr')
  })

  it('skips blocked offers and picks the first eligible one', () => {
    const offers = [
      makeAnnotated({ cityId: 'city-pr', deliveredPricePerUnit: 8.0, blocked: true, blockedReason: 'minQuality' }),
      makeAnnotated({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, blocked: false }),
      makeAnnotated({ cityId: 'city-vi', deliveredPricePerUnit: 14.0, blocked: false }),
    ]
    // Prague is cheapest by delivered price but blocked by quality.
    // Bratislava is the optimal eligible offer.
    expect(selectOptimalOffer(offers)?.cityId).toBe('city-ba')
  })

  it('correctly identifies optimal when nearby city is blocked by maxPrice', () => {
    // Simulates: local city is cheapest at list price but its quality is too low;
    // remote city has higher delivered price but meets constraints.
    const offers = [
      makeAnnotated({
        cityId: 'city-ba',
        deliveredPricePerUnit: 11.0,
        blocked: true,
        blockedReason: 'maxPrice',
      }),
      makeAnnotated({
        cityId: 'city-vi',
        deliveredPricePerUnit: 13.5,
        blocked: false,
      }),
    ]
    const optimal = selectOptimalOffer(offers)
    expect(optimal?.cityId).toBe('city-vi')
  })

  it('transit-cost reranking scenario: remote cheaper delivered than local', () => {
    // AC#5 proof: local city has a higher exchange price than a remote city
    // AFTER transit is added. The pre-sorted list already reflects this ordering,
    // so the first non-blocked offer IS the correct optimal regardless of which city is "closer".
    //
    // Remote city: exchange $8, transit $2 → delivered $10  (first in list)
    // Local city:  exchange $11, transit $0 → delivered $11 (second in list)
    const offers = [
      makeAnnotated({
        cityId: 'city-remote',
        exchangePricePerUnit: 8.0,
        transitCostPerUnit: 2.0,
        deliveredPricePerUnit: 10.0,
        blocked: false,
      }),
      makeAnnotated({
        cityId: 'city-local',
        exchangePricePerUnit: 11.0,
        transitCostPerUnit: 0,
        deliveredPricePerUnit: 11.0,
        blocked: false,
      }),
    ]
    const optimal = selectOptimalOffer(offers)
    expect(optimal?.cityId).toBe('city-remote')
    expect(optimal?.deliveredPricePerUnit).toBe(10.0)
  })
})

// ── annotateExchangeOffers + selectOptimalOffer integration ──────────────────

describe('annotateExchangeOffers + selectOptimalOffer (AC#5 end-to-end)', () => {
  it('AC#5: optimal-price mode selects best delivered price when no constraints', () => {
    // The server returns offers pre-sorted by delivered price.
    // annotate→select must pick city-pr (lowest at $10).
    const rawOffers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 10.0, estimatedQuality: 0.72 }),
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, estimatedQuality: 0.77 }),
      makeOffer({ cityId: 'city-vi', deliveredPricePerUnit: 14.0, estimatedQuality: 0.65 }),
    ]
    const annotated = annotateExchangeOffers(rawOffers, null, null)
    const optimal = selectOptimalOffer(annotated)
    expect(optimal?.cityId).toBe('city-pr')
    expect(optimal?.deliveredPricePerUnit).toBe(10.0)
  })

  it('AC#5: max-price constraint filters out expensive sources', () => {
    const rawOffers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 10.0, estimatedQuality: 0.72 }),
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, estimatedQuality: 0.77 }),
      makeOffer({ cityId: 'city-vi', deliveredPricePerUnit: 14.0, estimatedQuality: 0.65 }),
    ]
    // Player set max price to $10.50 — Prague ($10) is within budget, others are not.
    const annotated = annotateExchangeOffers(rawOffers, 10.5, null)
    const optimal = selectOptimalOffer(annotated)
    expect(optimal?.cityId).toBe('city-pr')
    expect(annotated.filter((o) => o.blocked)).toHaveLength(2) // Bratislava + Vienna blocked
  })

  it('AC#5: min-quality constraint picks higher-quality over cheaper source', () => {
    // Prague is cheapest but quality is too low; Bratislava is the fallback.
    const rawOffers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 10.0, estimatedQuality: 0.65 }),
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 11.0, estimatedQuality: 0.77 }),
      makeOffer({ cityId: 'city-vi', deliveredPricePerUnit: 14.0, estimatedQuality: 0.90 }),
    ]
    const annotated = annotateExchangeOffers(rawOffers, null, 0.75)
    const optimal = selectOptimalOffer(annotated)
    // Prague blocked by quality; Bratislava is next eligible
    expect(optimal?.cityId).toBe('city-ba')
  })

  it('AC#5: returns null when all sources are blocked (no eligible offer)', () => {
    const rawOffers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 20.0 }),
      makeOffer({ cityId: 'city-ba', deliveredPricePerUnit: 25.0 }),
    ]
    const annotated = annotateExchangeOffers(rawOffers, 5.0, null) // max price $5, all blocked
    const optimal = selectOptimalOffer(annotated)
    expect(optimal).toBeNull()
    expect(annotated.every((o) => o.blocked)).toBe(true)
  })

  it('AC#5: allExchangeOffersBlocked is true when every offer is blocked', () => {
    const rawOffers = [
      makeOffer({ cityId: 'city-pr', deliveredPricePerUnit: 20.0 }),
    ]
    const annotated = annotateExchangeOffers(rawOffers, 5.0, null)
    const allBlocked = annotated.length > 0 && annotated.every((o) => o.blocked)
    expect(allBlocked).toBe(true)
  })
})
