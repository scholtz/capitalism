/**
 * Pure helper functions for city-level global exchange pricing, quality, and
 * transit-cost calculations. These mirror the authoritative backend logic in
 * `GlobalExchangeCalculator.cs` so the frontend can display consistent values
 * before the server confirms them.
 *
 * ⚠️  These are display-only helpers.  The server is always the source of truth
 * for any value that affects a real purchase or economic decision.
 */

export const DEFAULT_MISSING_ABUNDANCE = 0.05

/**
 * Computes the city-level exchange price for a resource based on local abundance
 * and average city rent per sqm.
 *
 * Mirrors `GlobalExchangeCalculator.ComputeExchangePrice`.
 */
export function computeExchangePrice(
  basePrice: number,
  abundance: number,
  averageRentPerSqm: number,
): number {
  const normalizedAbundance = Math.min(Math.max(abundance, 0), 1)
  const scarcityMultiplier = 1.55 - normalizedAbundance * 0.75
  const cityMultiplier = 0.95 + averageRentPerSqm / 100
  return Number((basePrice * scarcityMultiplier * cityMultiplier).toFixed(2))
}

/**
 * Estimates the quality of resources sourced from the global exchange for a
 * given city abundance.
 *
 * Mirrors `GlobalExchangeCalculator.ComputeExchangeQuality`.
 */
export function computeExchangeQuality(abundance: number): number {
  const normalizedAbundance = Math.min(Math.max(abundance, 0), 1)
  const quality = 0.35 + normalizedAbundance * 0.6
  return Number(Math.min(Math.max(quality, 0.35), 0.95).toFixed(4))
}

/**
 * Computes the Haversine great-circle distance in kilometres between two
 * geographic coordinates.
 *
 * Mirrors `GlobalExchangeCalculator.ComputeDistanceKm`.
 */
export function computeDistanceKm(
  latA: number,
  lonA: number,
  latB: number,
  lonB: number,
): number {
  const earthRadiusKm = 6371
  const deltaLat = ((latB - latA) * Math.PI) / 180
  const deltaLon = ((lonB - lonA) * Math.PI) / 180
  const originLat = (latA * Math.PI) / 180
  const destLat = (latB * Math.PI) / 180

  const haversine =
    Math.sin(deltaLat / 2) ** 2 +
    Math.cos(originLat) * Math.cos(destLat) * Math.sin(deltaLon / 2) ** 2
  return earthRadiusKm * 2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine))
}

/**
 * Computes the per-unit transit cost from a source city to a destination city.
 * Same-city transfers are free. Cross-city cost scales with distance and resource
 * weight; the minimum cross-city charge is 0.05.
 *
 * Mirrors `GlobalExchangeCalculator.ComputeTransitCostPerUnit`.
 */
export function computeTransitCostPerUnit(
  sourceLat: number,
  sourceLon: number,
  destLat: number,
  destLon: number,
  weightPerUnit: number,
): number {
  const distanceKm = computeDistanceKm(sourceLat, sourceLon, destLat, destLon)
  if (distanceKm <= 0) return 0

  const effectiveWeight = Math.max(weightPerUnit, 0.1)
  const rawCost = distanceKm * effectiveWeight * 0.0025
  return Number(Math.max(rawCost, 0.05).toFixed(2))
}

/**
 * Returns the total delivered price per unit (exchange price + transit cost).
 * This is the effective cost a purchase unit will pay when sourcing from the
 * global exchange.
 */
export function computeDeliveredPrice(exchangePrice: number, transitCost: number): number {
  return Number((exchangePrice + transitCost).toFixed(2))
}

// ── Offer filtering and optimal-source selection ───────────────────────────

/**
 * Reason why a global exchange offer is blocked for a purchase unit.
 * - 'maxPrice' : delivered price exceeds the unit's maximum acceptable price.
 * - 'minQuality': estimated quality falls below the unit's minimum quality threshold.
 * - null       : the offer is eligible (not blocked).
 */
export type BlockedReason = 'maxPrice' | 'minQuality' | null

/**
 * A `GlobalExchangeOffer` annotated with whether it is blocked for a given
 * purchase-unit configuration and, if blocked, why.
 */
export interface AnnotatedExchangeOffer {
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
  blocked: boolean
  blockedReason: BlockedReason
}

/**
 * Annotates a list of global exchange offers with whether each is blocked for
 * the given purchase-unit constraints.
 *
 * Rules (applied in order):
 * 1. If `maxPrice` is set and `deliveredPricePerUnit > maxPrice` → blocked with reason 'maxPrice'.
 * 2. If `minQuality` is set and `estimatedQuality < minQuality` → blocked with reason 'minQuality'.
 * 3. Otherwise → not blocked.
 *
 * The input list is assumed to be pre-sorted by delivered price (server provides this ordering).
 * The function does not mutate or re-sort the list.
 *
 * @param offers     List of offers from `globalExchangeOffers` query.
 * @param maxPrice   Maximum acceptable delivered price per unit, or null for no cap.
 * @param minQuality Minimum acceptable quality (0–1), or null for no floor.
 */
export function annotateExchangeOffers(
  offers: readonly {
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
  }[],
  maxPrice: number | null,
  minQuality: number | null,
): AnnotatedExchangeOffer[] {
  return offers.map((offer) => {
    if (maxPrice !== null && offer.deliveredPricePerUnit > maxPrice) {
      return { ...offer, blocked: true, blockedReason: 'maxPrice' as const }
    }
    if (minQuality !== null && offer.estimatedQuality < minQuality) {
      return { ...offer, blocked: true, blockedReason: 'minQuality' as const }
    }
    return { ...offer, blocked: false, blockedReason: null }
  })
}

/**
 * Returns the optimal (cheapest eligible) exchange offer from an annotated list,
 * or `null` if all offers are blocked or the list is empty.
 *
 * "Optimal" means the first non-blocked offer in the list. Since the server
 * returns offers pre-sorted by delivered price ascending (and quality descending
 * as a tiebreaker), the first non-blocked offer is the best available source.
 *
 * This mirrors the selection logic in `PurchasingPhase.BuyFromGlobalExchange`
 * which calls `.OrderBy(o => o.deliveredPrice).ThenByDescending(o => o.quality).FirstOrDefault()`.
 */
export function selectOptimalOffer(offers: readonly AnnotatedExchangeOffer[]): AnnotatedExchangeOffer | null {
  return offers.find((o) => !o.blocked) ?? null
}

// ── Sorting ───────────────────────────────────────────────────────────────

/**
 * Dimension by which exchange offers can be sorted in the purchase-unit sourcing
 * panel. The default is 'deliveredPrice' which matches the server ordering.
 */
export type ExchangeSortBy = 'deliveredPrice' | 'exchangePrice' | 'quality'

/**
 * Returns a new sorted copy of the annotated offer list without mutating the
 * input. Blocked offers are always pushed to the end so eligible offers remain
 * prominent regardless of the chosen sort dimension.
 *
 * Sorting rules per dimension:
 * - `deliveredPrice` : ascending delivered price; quality descending as a tiebreaker
 *   so that equal delivered prices favour higher-quality sources.
 * - `exchangePrice`  : ascending raw exchange (sticker) price.
 * - `quality`        : descending estimated quality (highest quality first).
 */
export function sortExchangeOffers(
  offers: readonly AnnotatedExchangeOffer[],
  sortBy: ExchangeSortBy,
): AnnotatedExchangeOffer[] {
  return [...offers].sort((a, b) => {
    // Blocked offers sink to the bottom
    if (a.blocked !== b.blocked) return a.blocked ? 1 : -1

    switch (sortBy) {
      case 'exchangePrice':
        return a.exchangePricePerUnit - b.exchangePricePerUnit
      case 'quality':
        // Quality sorts descending (highest first)
        return b.estimatedQuality - a.estimatedQuality
      case 'deliveredPrice':
      default:
        return (
          a.deliveredPricePerUnit - b.deliveredPricePerUnit ||
          b.estimatedQuality - a.estimatedQuality
        )
    }
  })
}

// ── Logistics-trap detection ───────────────────────────────────────────────

/**
 * Describes a "logistics trap": the situation where the offer with the lowest
 * raw exchange price is NOT the recommended offer because transit costs make it
 * more expensive to deliver than an alternative city.
 *
 * This is the key player-guidance insight: a cheaper sticker price can be worse
 * after logistics, and players should be warned before committing.
 */
export interface LogisticsTrapWarning {
  /** City that has the lowest raw exchange (sticker) price. */
  cheaperStickerCityName: string
  /** Raw exchange price per unit in the cheapest-sticker city. */
  cheaperStickerExchangePrice: number
  /** Delivered price per unit in the cheapest-sticker city (exchange + transit). */
  cheaperStickerDeliveredPrice: number
  /** City of the recommended (lowest delivered cost) offer. */
  recommendedCityName: string
  /** Delivered price per unit of the recommended offer. */
  recommendedDeliveredPrice: number
}

/**
 * Detects whether a logistics trap exists in an annotated offer list.
 *
 * A logistics trap is present when the eligible offer with the lowest exchange
 * (sticker) price is different from the eligible offer with the lowest delivered
 * price. This means transit costs flip the ordering, and a player who chose by
 * sticker price alone would pay more in the end.
 *
 * Returns `null` when:
 * - There are fewer than two eligible (non-blocked) offers.
 * - The lowest sticker-price offer is also the lowest delivered-price offer.
 */
export function detectLogisticsTrap(
  offers: readonly AnnotatedExchangeOffer[],
): LogisticsTrapWarning | null {
  const eligible = offers.filter((o) => !o.blocked)
  if (eligible.length < 2) return null

  const byExchangePrice = [...eligible].sort(
    (a, b) => a.exchangePricePerUnit - b.exchangePricePerUnit,
  )
  const cheapestSticker = byExchangePrice[0]

  const byDeliveredPrice = [...eligible].sort(
    (a, b) => a.deliveredPricePerUnit - b.deliveredPricePerUnit || b.estimatedQuality - a.estimatedQuality,
  )
  const recommended = byDeliveredPrice[0]

  if (!cheapestSticker || !recommended) return null
  if (cheapestSticker.cityId === recommended.cityId) return null

  return {
    cheaperStickerCityName: cheapestSticker.cityName,
    cheaperStickerExchangePrice: cheapestSticker.exchangePricePerUnit,
    cheaperStickerDeliveredPrice: cheapestSticker.deliveredPricePerUnit,
    recommendedCityName: recommended.cityName,
    recommendedDeliveredPrice: recommended.deliveredPricePerUnit,
  }
}
