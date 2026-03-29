import { describe, expect, it } from 'vitest'
import {
  getLocalizedCategory,
  getLocalizedIndustry,
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedRecipeSummary,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
  getLocalizedUnitName,
  getProductImageUrl,
  getResourceImageUrl,
  normalizeCatalogLocale,
} from '../catalogPresentation'
import type { ProductType, Recipe, ResourceType } from '@/types'

// ── Fixtures ─────────────────────────────────────────────────────────────────

const makeResource = (overrides: Partial<ResourceType> = {}): ResourceType => ({
  id: 'r1',
  name: 'Wood',
  slug: 'wood',
  category: 'ORGANIC',
  basePrice: 10,
  weightPerUnit: 5,
  unitName: 'Ton',
  unitSymbol: 't',
  imageUrl: null,
  description: 'Timber from managed forests.',
  ...overrides,
})

const makeProduct = (overrides: Partial<ProductType> = {}): ProductType => ({
  id: 'p1',
  name: 'Wooden Chair',
  slug: 'wooden-chair',
  industry: 'FURNITURE',
  basePrice: 120,
  baseCraftTicks: 4,
  outputQuantity: 2,
  energyConsumptionMwh: 1.5,
  unitName: 'Chair',
  unitSymbol: 'pcs',
  isProOnly: false,
  isUnlockedForCurrentPlayer: true,
  description: 'A sturdy wooden chair.',
  recipes: [],
  ...overrides,
})

const makeRecipeWithResource = (resourceOverrides: Partial<ResourceType> = {}): Recipe => ({
  resourceType: makeResource(resourceOverrides),
  inputProductType: null,
  quantity: 3,
})

const makeRecipeWithProduct = (): Recipe => ({
  resourceType: null,
  inputProductType: { id: 'p2', name: 'Wooden Board', slug: 'wooden-board', unitName: 'Board', unitSymbol: 'bds' },
  quantity: 10,
})

// ── normalizeCatalogLocale ────────────────────────────────────────────────────

describe('normalizeCatalogLocale', () => {
  it('returns en for English locale', () => {
    expect(normalizeCatalogLocale('en')).toBe('en')
    expect(normalizeCatalogLocale('en-US')).toBe('en')
    expect(normalizeCatalogLocale('en-GB')).toBe('en')
  })

  it('returns sk for Slovak locale', () => {
    expect(normalizeCatalogLocale('sk')).toBe('sk')
    expect(normalizeCatalogLocale('sk-SK')).toBe('sk')
  })

  it('returns de for German locale', () => {
    expect(normalizeCatalogLocale('de')).toBe('de')
    expect(normalizeCatalogLocale('de-AT')).toBe('de')
  })

  it('returns en for unknown locale', () => {
    expect(normalizeCatalogLocale('fr')).toBe('en')
    expect(normalizeCatalogLocale('ja')).toBe('en')
    expect(normalizeCatalogLocale('')).toBe('en')
  })
})

// ── getLocalizedIndustry ──────────────────────────────────────────────────────

describe('getLocalizedIndustry', () => {
  it('returns English industry names', () => {
    expect(getLocalizedIndustry('FURNITURE', 'en')).toBe('Furniture')
    expect(getLocalizedIndustry('FOOD_PROCESSING', 'en')).toBe('Food Processing')
    expect(getLocalizedIndustry('HEALTHCARE', 'en')).toBe('Healthcare')
    expect(getLocalizedIndustry('ELECTRONICS', 'en')).toBe('Electronics')
    expect(getLocalizedIndustry('CONSTRUCTION', 'en')).toBe('Construction')
  })

  it('returns Slovak industry names', () => {
    expect(getLocalizedIndustry('FURNITURE', 'sk')).toBe('Nábytok')
    expect(getLocalizedIndustry('FOOD_PROCESSING', 'sk')).toBe('Potravinárstvo')
    expect(getLocalizedIndustry('HEALTHCARE', 'sk')).toBe('Zdravotníctvo')
  })

  it('returns German industry names', () => {
    expect(getLocalizedIndustry('FURNITURE', 'de')).toBe('Möbel')
    expect(getLocalizedIndustry('ELECTRONICS', 'de')).toBe('Elektronik')
  })

  it('falls back to humanized identifier for unknown industry', () => {
    expect(getLocalizedIndustry('MINING', 'en')).toBe('Mining')
    expect(getLocalizedIndustry('NEW_INDUSTRY', 'en')).toBe('New Industry')
  })
})

// ── getLocalizedCategory ──────────────────────────────────────────────────────

describe('getLocalizedCategory', () => {
  it('returns English category names', () => {
    expect(getLocalizedCategory('ORGANIC', 'en')).toBe('Organic')
    expect(getLocalizedCategory('MINERAL', 'en')).toBe('Mineral')
    expect(getLocalizedCategory('RAW_MATERIAL', 'en')).toBe('Raw material')
  })

  it('returns Slovak category names', () => {
    expect(getLocalizedCategory('ORGANIC', 'sk')).toBe('Organická')
    expect(getLocalizedCategory('MINERAL', 'sk')).toBe('Minerálna')
    expect(getLocalizedCategory('RAW_MATERIAL', 'sk')).toBe('Surovina')
  })

  it('returns German category names', () => {
    expect(getLocalizedCategory('ORGANIC', 'de')).toBe('Organisch')
    expect(getLocalizedCategory('MINERAL', 'de')).toBe('Mineralisch')
  })

  it('falls back to humanized identifier for unknown category', () => {
    expect(getLocalizedCategory('SYNTHETIC', 'en')).toBe('Synthetic')
  })
})

// ── getLocalizedResourceName ──────────────────────────────────────────────────

describe('getLocalizedResourceName', () => {
  it('returns English name as-is', () => {
    expect(getLocalizedResourceName(makeResource({ name: 'Wood', slug: 'wood' }), 'en')).toBe('Wood')
  })

  it('returns Slovak names for known resources', () => {
    expect(getLocalizedResourceName(makeResource({ name: 'Wood', slug: 'wood' }), 'sk')).toBe('Drevo')
    expect(getLocalizedResourceName(makeResource({ name: 'Coal', slug: 'coal' }), 'sk')).toBe('Uhlie')
    expect(getLocalizedResourceName(makeResource({ name: 'Gold', slug: 'gold' }), 'sk')).toBe('Zlato')
    expect(getLocalizedResourceName(makeResource({ name: 'Silicon', slug: 'silicon' }), 'sk')).toBe('Kremík')
  })

  it('returns German names for known resources', () => {
    expect(getLocalizedResourceName(makeResource({ name: 'Wood', slug: 'wood' }), 'de')).toBe('Holz')
    expect(getLocalizedResourceName(makeResource({ name: 'Iron Ore', slug: 'iron-ore' }), 'de')).toBe('Eisenerz')
    expect(getLocalizedResourceName(makeResource({ name: 'Coal', slug: 'coal' }), 'de')).toBe('Kohle')
  })

  it('falls back to English name for unknown resource slug', () => {
    expect(getLocalizedResourceName(makeResource({ name: 'Plasma', slug: 'plasma' }), 'sk')).toBe('Plasma')
    expect(getLocalizedResourceName(makeResource({ name: 'Plasma', slug: 'plasma' }), 'de')).toBe('Plasma')
  })

  it('returns dash for null or undefined resource', () => {
    expect(getLocalizedResourceName(null, 'en')).toBe('—')
    expect(getLocalizedResourceName(undefined, 'en')).toBe('—')
  })
})

// ── getLocalizedProductName ───────────────────────────────────────────────────

describe('getLocalizedProductName', () => {
  it('returns English name as-is', () => {
    expect(getLocalizedProductName(makeProduct({ name: 'Wooden Chair', slug: 'wooden-chair' }), 'en')).toBe(
      'Wooden Chair',
    )
  })

  it('translates product slug tokens into Slovak', () => {
    const result = getLocalizedProductName(makeProduct({ name: 'Wooden Chair', slug: 'wooden-chair' }), 'sk')
    expect(result).toContain('Drevený')
    expect(result).toContain('Stolička')
  })

  it('translates product slug tokens into German', () => {
    const result = getLocalizedProductName(makeProduct({ name: 'Wooden Chair', slug: 'wooden-chair' }), 'de')
    expect(result).toContain('Holz')
    expect(result).toContain('Stuhl')
  })

  it('capitalizes each token', () => {
    const result = getLocalizedProductName(makeProduct({ name: 'Bread', slug: 'bread' }), 'sk')
    expect(result[0]).toBe(result[0].toUpperCase())
  })

  it('returns dash for null or undefined product', () => {
    expect(getLocalizedProductName(null, 'en')).toBe('—')
    expect(getLocalizedProductName(undefined, 'en')).toBe('—')
  })
})

// ── getLocalizedUnitName ──────────────────────────────────────────────────────

describe('getLocalizedUnitName', () => {
  it('returns unit name unchanged for English', () => {
    expect(getLocalizedUnitName('Ton', 'en')).toBe('Ton')
    expect(getLocalizedUnitName('Piece', 'en')).toBe('Piece')
  })

  it('translates unit names to Slovak', () => {
    expect(getLocalizedUnitName('Ton', 'sk')).toBe('tona')
    expect(getLocalizedUnitName('Piece', 'sk')).toBe('kus')
    expect(getLocalizedUnitName('Chair', 'sk')).toBe('stolička')
  })

  it('translates unit names to German', () => {
    expect(getLocalizedUnitName('Ton', 'de')).toBe('Tonne')
    expect(getLocalizedUnitName('Kilogram', 'de')).toBe('Kilogramm')
  })

  it('returns the original unit name if translation is missing', () => {
    expect(getLocalizedUnitName('Crate', 'sk')).toBe('Crate')
  })

  it('returns empty string for null or undefined unit', () => {
    expect(getLocalizedUnitName(null, 'en')).toBe('')
    expect(getLocalizedUnitName(undefined, 'en')).toBe('')
  })
})

// ── getLocalizedRecipeIngredientName ─────────────────────────────────────────

describe('getLocalizedRecipeIngredientName', () => {
  it('returns resource name when resourceType is present', () => {
    const recipe = makeRecipeWithResource({ name: 'Wood', slug: 'wood' })
    expect(getLocalizedRecipeIngredientName(recipe, 'en')).toBe('Wood')
    expect(getLocalizedRecipeIngredientName(recipe, 'sk')).toBe('Drevo')
  })

  it('returns product name when inputProductType is present', () => {
    const recipe = makeRecipeWithProduct()
    expect(getLocalizedRecipeIngredientName(recipe, 'en')).toBe('Wooden Board')
  })

  it('returns dash when both resourceType and inputProductType are null', () => {
    const recipe: Recipe = { resourceType: null, inputProductType: null, quantity: 1 }
    expect(getLocalizedRecipeIngredientName(recipe, 'en')).toBe('—')
  })
})

// ── getLocalizedRecipeSummary ─────────────────────────────────────────────────

describe('getLocalizedRecipeSummary', () => {
  it('returns empty string for product with no recipes', () => {
    expect(getLocalizedRecipeSummary(makeProduct({ recipes: [] }), 'en')).toBe('')
  })

  it('formats a single-ingredient recipe correctly', () => {
    const product = makeProduct({
      recipes: [makeRecipeWithResource({ unitSymbol: 't' })],
    })
    const result = getLocalizedRecipeSummary(product, 'en')
    expect(result).toContain('3')
    expect(result).toContain('t')
    expect(result).toContain('Wood')
  })

  it('joins multiple ingredients with " + "', () => {
    const product = makeProduct({
      recipes: [makeRecipeWithResource(), makeRecipeWithProduct()],
    })
    const result = getLocalizedRecipeSummary(product, 'en')
    expect(result).toContain(' + ')
    expect(result).toContain('Wood')
    expect(result).toContain('Wooden Board')
  })

  it('localizes ingredient names in recipe summary', () => {
    const product = makeProduct({
      recipes: [makeRecipeWithResource({ slug: 'wood', unitSymbol: 't' })],
    })
    const skResult = getLocalizedRecipeSummary(product, 'sk')
    expect(skResult).toContain('Drevo')
  })
})

// ── getLocalizedResourceDescription ──────────────────────────────────────────

describe('getLocalizedResourceDescription', () => {
  it('returns the English description as-is', () => {
    const resource = makeResource({ description: 'Timber from managed forests.' })
    expect(getLocalizedResourceDescription(resource, 'en')).toBe('Timber from managed forests.')
  })

  it('returns empty string when description is null and locale is English', () => {
    const resource = makeResource({ description: null })
    expect(getLocalizedResourceDescription(resource, 'en')).toBe('')
  })

  it('generates a Slovak description sentence', () => {
    const resource = makeResource({ slug: 'wood', name: 'Wood', category: 'ORGANIC', unitName: 'Ton', unitSymbol: 't' })
    const result = getLocalizedResourceDescription(resource, 'sk')
    expect(result).toContain('Drevo')
    expect(result).toContain('tona')
    expect(result).toContain('t')
  })

  it('generates a German description sentence', () => {
    const resource = makeResource({ slug: 'wood', name: 'Wood', category: 'ORGANIC', unitName: 'Ton', unitSymbol: 't' })
    const result = getLocalizedResourceDescription(resource, 'de')
    expect(result).toContain('Holz')
    expect(result).toContain('Tonne')
    expect(result).toContain('t')
  })
})

// ── getLocalizedProductDescription ───────────────────────────────────────────

describe('getLocalizedProductDescription', () => {
  it('returns the English description as-is', () => {
    const product = makeProduct({ description: 'A sturdy wooden chair.' })
    expect(getLocalizedProductDescription(product, 'en')).toBe('A sturdy wooden chair.')
  })

  it('returns empty string when description is null and locale is English', () => {
    const product = makeProduct({ description: null })
    expect(getLocalizedProductDescription(product, 'en')).toBe('')
  })

  it('generates a Slovak description sentence', () => {
    const product = makeProduct({
      slug: 'wooden-chair',
      industry: 'FURNITURE',
      outputQuantity: 2,
      energyConsumptionMwh: 1.5,
      unitName: 'Chair',
      unitSymbol: 'pcs',
      recipes: [],
    })
    const result = getLocalizedProductDescription(product, 'sk')
    // Industry name is lowercased inside the sentence template
    expect(result).toContain('nábytok')
    expect(result).toContain('2')
    expect(result).toContain('1.5')
  })

  it('generates a German description sentence', () => {
    const product = makeProduct({
      slug: 'wooden-chair',
      industry: 'FURNITURE',
      outputQuantity: 2,
      energyConsumptionMwh: 1.5,
      unitName: 'Chair',
      unitSymbol: 'pcs',
      recipes: [],
    })
    const result = getLocalizedProductDescription(product, 'de')
    // Industry name is lowercased inside the sentence template
    expect(result).toContain('möbel')
    expect(result).toContain('2')
  })

  it('includes recipe summary in generated description when recipes exist', () => {
    const product = makeProduct({
      recipes: [makeRecipeWithResource({ slug: 'wood', unitSymbol: 't' })],
    })
    const result = getLocalizedProductDescription(product, 'sk')
    expect(result).toContain('Drevo')
  })
})

// ── getResourceImageUrl ───────────────────────────────────────────────────────

describe('getResourceImageUrl', () => {
  it('returns the imageUrl from the resource', () => {
    const url = 'data:image/svg+xml;utf8,<svg/>'
    expect(getResourceImageUrl(makeResource({ imageUrl: url }))).toBe(url)
  })

  it('returns null when imageUrl is null', () => {
    expect(getResourceImageUrl(makeResource({ imageUrl: null }))).toBeNull()
  })
})

// ── getProductImageUrl ────────────────────────────────────────────────────────

describe('getProductImageUrl', () => {
  it('returns a data URL for a product', () => {
    const url = getProductImageUrl(makeProduct({ slug: 'wooden-chair', industry: 'FURNITURE' }))
    expect(url).toMatch(/^data:image\/svg\+xml/)
  })

  it('returns a data URL containing an SVG for different industries', () => {
    const industries = ['FURNITURE', 'FOOD_PROCESSING', 'HEALTHCARE', 'ELECTRONICS', 'CONSTRUCTION']
    for (const industry of industries) {
      const url = getProductImageUrl(makeProduct({ slug: 'generic-item', industry }))
      expect(url).toMatch(/^data:image\/svg\+xml/)
    }
  })

  it('different slugs produce different image URLs for the same industry', () => {
    const chairUrl = getProductImageUrl(makeProduct({ slug: 'wooden-chair', industry: 'FURNITURE' }))
    const tableUrl = getProductImageUrl(makeProduct({ slug: 'wooden-table', industry: 'FURNITURE' }))
    expect(chairUrl).not.toBe(tableUrl)
  })

  it('produces valid SVG content in the URL', () => {
    const url = getProductImageUrl(makeProduct({ slug: 'wooden-chair', industry: 'FURNITURE' }))
    const decoded = decodeURIComponent(url.replace('data:image/svg+xml;utf8,', ''))
    expect(decoded).toContain('<svg')
    expect(decoded).toContain('</svg>')
  })
})
