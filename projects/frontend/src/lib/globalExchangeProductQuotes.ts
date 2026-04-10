import type { GlobalExchangeProductQuote, ProductType } from '@/types'

function hashString(value: string): number {
  return [...value].reduce((sum, char) => sum + char.charCodeAt(0), 0)
}

function computeWave(seed: number, currentTick: number, period: number): number {
  const position = ((seed + Math.max(currentTick, 0)) % period) / (period - 1)
  return position * 2 - 1
}

export function buildGlobalExchangeProductQuote(
  product: ProductType,
  currentTick: number,
): GlobalExchangeProductQuote {
  const seed = hashString(product.slug)
  const priceWave = computeWave(seed, currentTick, 11)
  const qualityWave = computeWave(seed * 3, currentTick, 9)

  const offerMultiplier = 1.04 + priceWave * 0.04
  const bidMultiplier = 0.94 + priceWave * 0.03
  const estimatedQuality = Math.min(Math.max(0.64 + qualityWave * 0.1, 0.5), 0.82)

  const offerPricePerUnit = Number((product.basePrice * offerMultiplier).toFixed(2))
  const bidPricePerUnit = Number(Math.min(product.basePrice, product.basePrice * bidMultiplier).toFixed(2))

  return {
    productTypeId: product.id,
    productName: product.name,
    productSlug: product.slug,
    productIndustry: product.industry,
    unitSymbol: product.unitSymbol,
    basePrice: product.basePrice,
    bidPricePerUnit,
    offerPricePerUnit: Math.max(offerPricePerUnit, Number((bidPricePerUnit + 0.01).toFixed(2))),
    estimatedQuality: Number(estimatedQuality.toFixed(4)),
  }
}

export function buildGlobalExchangeProductQuotes(
  products: readonly ProductType[],
  currentTick: number,
): GlobalExchangeProductQuote[] {
  return products.map((product) => buildGlobalExchangeProductQuote(product, currentTick))
}
