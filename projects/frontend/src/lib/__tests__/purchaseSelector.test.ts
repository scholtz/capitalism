import { describe, expect, it } from 'vitest'
import {
  buildPurchaseVendorOptions,
  collectSameCityVendorItemKeys,
  getPurchaseSelectorItemKey,
  sortPurchaseSelectorItems,
  type PurchaseVendorCompany,
} from '../purchaseSelector'

describe('collectSameCityVendorItemKeys', () => {
  it('collects resource and product keys from same-city B2B units and excludes the current building', () => {
    const companies: PurchaseVendorCompany[] = [
      {
        id: 'company-1',
        name: 'Own Supply',
        buildings: [
          {
            id: 'building-current',
            name: 'Current Building',
            cityId: 'city-ba',
            latitude: 48.15,
            longitude: 17.11,
            units: [
              { unitType: 'B2B_SALES', resourceTypeId: 'res-current', productTypeId: 'prod-current', minPrice: 10 },
            ],
          },
          {
            id: 'building-factory',
            name: 'Factory One',
            cityId: 'city-ba',
            latitude: 48.16,
            longitude: 17.12,
            units: [
              { unitType: 'B2B_SALES', resourceTypeId: 'res-wood', productTypeId: 'prod-bread', minPrice: 22 },
              { unitType: 'MANUFACTURING', resourceTypeId: null, productTypeId: 'prod-chair', minPrice: null },
            ],
          },
          {
            id: 'building-prague',
            name: 'Prague Factory',
            cityId: 'city-pr',
            latitude: 50.08,
            longitude: 14.43,
            units: [{ unitType: 'B2B_SALES', resourceTypeId: 'res-iron', productTypeId: 'prod-table', minPrice: 30 }],
          },
        ],
      },
    ]

    const keys = collectSameCityVendorItemKeys(companies, 'city-ba', 'building-current')

    expect(keys.has(getPurchaseSelectorItemKey('resource', 'res-wood'))).toBe(true)
    expect(keys.has(getPurchaseSelectorItemKey('product', 'prod-bread'))).toBe(true)
    expect(keys.has(getPurchaseSelectorItemKey('resource', 'res-current'))).toBe(false)
    expect(keys.has(getPurchaseSelectorItemKey('product', 'prod-table'))).toBe(false)
  })
})

describe('sortPurchaseSelectorItems', () => {
  it('keeps sales shop products first and moves same-city own supply products to the top of the product list', () => {
    const items = [
      { kind: 'product' as const, id: 'prod-chair', name: 'Wooden Chair' },
      { kind: 'product' as const, id: 'prod-bread', name: 'Bread' },
      { kind: 'resource' as const, id: 'res-wood', name: 'Wood' },
    ]

    const sorted = sortPurchaseSelectorItems(items, 'SALES_SHOP', [getPurchaseSelectorItemKey('product', 'prod-bread')])

    expect(sorted.map((item) => item.id)).toEqual(['prod-bread', 'prod-chair', 'res-wood'])
  })

  it('keeps factory resources first while still prioritizing preferred resources within that kind', () => {
    const items = [
      { kind: 'resource' as const, id: 'res-grain', name: 'Grain' },
      { kind: 'resource' as const, id: 'res-wood', name: 'Wood' },
      { kind: 'product' as const, id: 'prod-flour', name: 'Flour' },
    ]

    const sorted = sortPurchaseSelectorItems(items, 'FACTORY', [getPurchaseSelectorItemKey('resource', 'res-wood')])

    expect(sorted.map((item) => item.id)).toEqual(['res-wood', 'res-grain', 'prod-flour'])
  })
})

describe('buildPurchaseVendorOptions', () => {
  it('returns same-city matching vendors with min price and non-zero transit sorted by delivered cost', () => {
    const companies: PurchaseVendorCompany[] = [
      {
        id: 'company-a',
        name: 'Alpha Supply',
        buildings: [
          {
            id: 'building-a',
            name: 'Alpha Factory',
            cityId: 'city-ba',
            latitude: 48.17,
            longitude: 17.13,
            units: [
              { unitType: 'B2B_SALES', resourceTypeId: 'res-wood', productTypeId: null, minPrice: 45 },
              { unitType: 'B2B_SALES', resourceTypeId: 'res-wood', productTypeId: null, minPrice: 42 },
            ],
          },
        ],
      },
      {
        id: 'company-b',
        name: 'Beta Supply',
        buildings: [
          {
            id: 'building-b',
            name: 'Beta Factory',
            cityId: 'city-ba',
            latitude: 48.151,
            longitude: 17.111,
            units: [{ unitType: 'B2B_SALES', resourceTypeId: 'res-wood', productTypeId: null, minPrice: 40 }],
          },
          {
            id: 'building-current',
            name: 'Current',
            cityId: 'city-ba',
            latitude: 48.15,
            longitude: 17.11,
            units: [{ unitType: 'B2B_SALES', resourceTypeId: 'res-wood', productTypeId: null, minPrice: 5 }],
          },
        ],
      },
    ]

    const options = buildPurchaseVendorOptions(
      companies,
      { kind: 'resource', id: 'res-wood' },
      'city-ba',
      'building-current',
      { latitude: 48.15, longitude: 17.11 },
      new Map([['res-wood', { id: 'res-wood', weightPerUnit: 10 }]]),
      new Map(),
    )

    expect(options).toHaveLength(2)
    expect(options[0]).toMatchObject({
      companyId: 'company-b',
      companyName: 'Beta Supply',
      buildingId: 'building-b',
      buildingName: 'Beta Factory',
      cityId: 'city-ba',
      pricePerUnit: 40,
    })
    expect(options[0].transitCostPerUnit).toBeGreaterThan(0)
    expect(options[0].distanceKm).toBeGreaterThan(0)
    expect(options[1]).toMatchObject({
      companyId: 'company-a',
      companyName: 'Alpha Supply',
      buildingId: 'building-a',
      buildingName: 'Alpha Factory',
      cityId: 'city-ba',
      pricePerUnit: 42,
    })
    expect(options[1].transitCostPerUnit).toBeGreaterThan(options[0].transitCostPerUnit)
  })

  it('uses product recipe weight so heavier products cost more to ship than light ones', () => {
    const companies: PurchaseVendorCompany[] = [
      {
        id: 'company-a',
        name: 'Alpha Supply',
        buildings: [
          {
            id: 'building-a',
            name: 'Alpha Factory',
            cityId: 'city-ba',
            latitude: 48.17,
            longitude: 17.13,
            units: [
              { unitType: 'B2B_SALES', resourceTypeId: null, productTypeId: 'prod-bed', minPrice: 50 },
              { unitType: 'B2B_SALES', resourceTypeId: null, productTypeId: 'prod-medicine', minPrice: 50 },
            ],
          },
        ],
      },
    ]

    const resourceTypes = new Map([
      ['res-wood', { id: 'res-wood', weightPerUnit: 8 }],
      ['res-chem', { id: 'res-chem', weightPerUnit: 1 }],
    ])
    const productTypes = new Map([
      ['prod-bed', { id: 'prod-bed', outputQuantity: 1, recipes: [{ quantity: 3, resourceType: { id: 'res-wood' }, inputProductType: null }] }],
      ['prod-medicine', { id: 'prod-medicine', outputQuantity: 1, recipes: [{ quantity: 1, resourceType: { id: 'res-chem' }, inputProductType: null }] }],
    ])

    const heavy = buildPurchaseVendorOptions(
      companies,
      { kind: 'product', id: 'prod-bed' },
      'city-ba',
      'building-current',
      { latitude: 48.15, longitude: 17.11 },
      resourceTypes,
      productTypes,
    )[0]
    const light = buildPurchaseVendorOptions(
      companies,
      { kind: 'product', id: 'prod-medicine' },
      'city-ba',
      'building-current',
      { latitude: 48.15, longitude: 17.11 },
      resourceTypes,
      productTypes,
    )[0]

    expect(heavy.transitCostPerUnit).toBeGreaterThan(light.transitCostPerUnit)
  })
})
