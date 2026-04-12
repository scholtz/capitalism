import { describe, expect, it } from 'vitest'
import type { BuildingUnitInventory, RankedProductResult } from '@/types'
import { getSalesUnitProductOptions, type SalesUnitProductPickerUnit } from '../salesUnitProductPicker'

function makeUnit(overrides: Partial<SalesUnitProductPickerUnit> = {}): SalesUnitProductPickerUnit {
  return {
    id: 'unit-1',
    unitType: 'PUBLIC_SALES',
    gridX: 0,
    gridY: 0,
    productTypeId: null,
    linkUp: false,
    linkDown: false,
    linkLeft: false,
    linkRight: false,
    linkUpLeft: false,
    linkUpRight: false,
    linkDownLeft: false,
    linkDownRight: false,
    ...overrides,
  }
}

function makeRankedProduct(id: string, name: string): RankedProductResult {
  return {
    rankingReason: 'catalog',
    rankingScore: 10,
    productType: {
      id,
      name,
      slug: name.toLowerCase().replace(/\s+/g, '-'),
      industry: 'TEST',
      basePrice: 1,
      baseCraftTicks: 1,
      outputQuantity: 1,
      energyConsumptionMwh: 1,
      basicLaborHours: 1,
      unitName: 'Unit',
      unitSymbol: 'u',
      isProOnly: false,
      isUnlockedForCurrentPlayer: true,
      description: null,
      recipes: [],
    },
  }
}

function makeInventoryItem(overrides: Partial<BuildingUnitInventory> = {}): BuildingUnitInventory {
  return {
    id: 'inventory-1',
    buildingUnitId: 'unit-1',
    resourceTypeId: null,
    productTypeId: 'prod-chair',
    quantity: 4,
    sourcingCostTotal: 40,
    sourcingCostPerUnit: 10,
    quality: 0.8,
    ...overrides,
  }
}

describe('getSalesUnitProductOptions', () => {
  const rankedProducts = [
    makeRankedProduct('prod-bread', 'Bread'),
    makeRankedProduct('prod-chair', 'Wooden Chair'),
    makeRankedProduct('prod-bandage', 'Bandages'),
  ]

  it('shows only products reachable through connected upstream units', () => {
    const salesUnit = makeUnit({ id: 'sales-unit', gridX: 1, linkLeft: false })
    const connectedPurchase = makeUnit({
      id: 'purchase-unit',
      unitType: 'PURCHASE',
      gridX: 0,
      productTypeId: 'prod-bread',
      linkRight: true,
    })
    const unrelatedPurchase = makeUnit({
      id: 'other-purchase',
      unitType: 'PURCHASE',
      gridX: 0,
      gridY: 1,
      productTypeId: 'prod-chair',
    })

    const options = getSalesUnitProductOptions({
      unit: salesUnit,
      draftUnits: [salesUnit, connectedPurchase, unrelatedPurchase],
      rankedProducts,
      unitInventories: [],
    })

    expect(options.map((option) => option.productType.id)).toEqual(['prod-bread'])
    expect(options[0]?.availabilityReason).toBe('connected_upstream')
  })

  it('preserves stocked products even after the upstream link is removed', () => {
    const salesUnit = makeUnit({ id: 'sales-unit', productTypeId: 'prod-chair' })

    const options = getSalesUnitProductOptions({
      unit: salesUnit,
      draftUnits: [salesUnit],
      rankedProducts,
      unitInventories: [makeInventoryItem({ buildingUnitId: 'sales-unit', productTypeId: 'prod-chair', quantity: 6 })],
    })

    expect(options.map((option) => option.productType.id)).toEqual(['prod-chair'])
    expect(options[0]?.availabilityReason).toBe('current_stock')
  })

  it('deduplicates products that are both connected upstream and already stocked', () => {
    const salesUnit = makeUnit({ id: 'sales-unit', gridX: 2 })
    const storageUnit = makeUnit({
      id: 'storage-unit',
      unitType: 'STORAGE',
      gridX: 1,
      productTypeId: 'prod-bread',
      linkRight: true,
    })

    const options = getSalesUnitProductOptions({
      unit: salesUnit,
      draftUnits: [salesUnit, storageUnit],
      rankedProducts,
      unitInventories: [makeInventoryItem({ buildingUnitId: 'sales-unit', productTypeId: 'prod-bread', quantity: 3 })],
    })

    expect(options.map((option) => option.productType.id)).toEqual(['prod-bread'])
    expect(options[0]?.availabilityReason).toBe('connected_and_stock')
  })

  it('returns an empty list when there is no connected upstream product and no stock to preserve', () => {
    const salesUnit = makeUnit({ id: 'sales-unit' })

    expect(
      getSalesUnitProductOptions({
        unit: salesUnit,
        draftUnits: [salesUnit],
        rankedProducts,
        unitInventories: [],
      }),
    ).toEqual([])
  })
})
