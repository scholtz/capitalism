import { describe, expect, it } from 'vitest'
import { buildGlobalExchangeProductQuote, buildGlobalExchangeProductQuotes } from '../globalExchangeProductQuotes'

const chairProduct = {
  id: 'prod-chair',
  name: 'Wooden Chair',
  slug: 'wooden-chair',
  industry: 'FURNITURE',
  basePrice: 45,
  baseCraftTicks: 3,
  outputQuantity: 1,
  energyConsumptionMwh: 1,
  unitName: 'Chair',
  unitSymbol: 'chairs',
  isProOnly: false,
  description: 'Starter chair product',
  recipes: [],
}

describe('globalExchangeProductQuotes', () => {
  it('builds a bid/offer quote with a positive spread', () => {
    const quote = buildGlobalExchangeProductQuote(chairProduct, 42)

    expect(quote.bidPricePerUnit).toBeLessThan(quote.offerPricePerUnit)
    expect(quote.bidPricePerUnit).toBeGreaterThan(0)
    expect(quote.estimatedQuality).toBeGreaterThanOrEqual(0.5)
    expect(quote.estimatedQuality).toBeLessThanOrEqual(0.82)
  })

  it('changes quotes slightly across ticks', () => {
    const earlyQuote = buildGlobalExchangeProductQuote(chairProduct, 1)
    const laterQuote = buildGlobalExchangeProductQuote(chairProduct, 7)

    expect(earlyQuote.offerPricePerUnit).not.toBe(laterQuote.offerPricePerUnit)
  })

  it('builds quotes for every provided product', () => {
    const quotes = buildGlobalExchangeProductQuotes([
      chairProduct,
      { ...chairProduct, id: 'prod-bread', slug: 'bread', name: 'Bread', basePrice: 3, unitSymbol: 'loaves' },
    ], 5)

    expect(quotes).toHaveLength(2)
  })
})
