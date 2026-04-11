export type PurchaseSelectorItemKind = 'resource' | 'product'

export type PurchaseSelectorSelection = {
  kind: PurchaseSelectorItemKind
  id: string
}

export type PurchaseSelectorItem = {
  kind: PurchaseSelectorItemKind
  id: string
  name: string
}

export type PurchaseVendorUnit = {
  unitType: string
  resourceTypeId: string | null
  productTypeId: string | null
  minPrice: number | null
}

export type PurchaseVendorBuilding = {
  id: string
  name: string
  cityId: string
  units: PurchaseVendorUnit[]
}

export type PurchaseVendorCompany = {
  id: string
  name: string
  buildings: PurchaseVendorBuilding[]
}

export type PurchaseVendorOption = {
  companyId: string
  companyName: string
  buildingId: string
  buildingName: string
  cityId: string
  pricePerUnit: number | null
  transitCostPerUnit: number
}

export function getPurchaseSelectorItemKey(kind: PurchaseSelectorItemKind, id: string): string {
  return `${kind}:${id}`
}

export function collectSameCityVendorItemKeys(
  companies: PurchaseVendorCompany[],
  cityId: string | null | undefined,
  currentBuildingId: string | null | undefined,
): Set<string> {
  const keys = new Set<string>()
  if (!cityId) return keys

  for (const company of companies) {
    for (const building of company.buildings) {
      if (building.cityId !== cityId || building.id === currentBuildingId) continue
      for (const unit of building.units) {
        if (unit.unitType !== 'B2B_SALES') continue
        if (unit.productTypeId) keys.add(getPurchaseSelectorItemKey('product', unit.productTypeId))
        if (unit.resourceTypeId) keys.add(getPurchaseSelectorItemKey('resource', unit.resourceTypeId))
      }
    }
  }

  return keys
}

export function sortPurchaseSelectorItems<T extends PurchaseSelectorItem>(
  items: T[],
  buildingType: string | null | undefined,
  preferredItemKeys: Iterable<string>,
): T[] {
  const preferred = preferredItemKeys instanceof Set ? preferredItemKeys : new Set(preferredItemKeys)
  const kindPriority = buildingType === 'SALES_SHOP'
    ? { product: 0, resource: 1 }
    : { resource: 0, product: 1 }

  return [...items].sort((left, right) => {
    const leftKindPriority = kindPriority[left.kind]
    const rightKindPriority = kindPriority[right.kind]
    if (leftKindPriority !== rightKindPriority) {
      return leftKindPriority - rightKindPriority
    }

    const leftPreferred = preferred.has(getPurchaseSelectorItemKey(left.kind, left.id)) ? 1 : 0
    const rightPreferred = preferred.has(getPurchaseSelectorItemKey(right.kind, right.id)) ? 1 : 0
    if (leftPreferred !== rightPreferred) {
      return rightPreferred - leftPreferred
    }

    return left.name.localeCompare(right.name)
  })
}

export function buildPurchaseVendorOptions(
  companies: PurchaseVendorCompany[],
  selection: PurchaseSelectorSelection | null,
  cityId: string | null | undefined,
  currentBuildingId: string | null | undefined,
): PurchaseVendorOption[] {
  if (!selection || !cityId) return []

  const options: PurchaseVendorOption[] = []
  for (const company of companies) {
    for (const building of company.buildings) {
      if (building.cityId !== cityId || building.id === currentBuildingId) continue

      const matchingUnits = building.units.filter(
        (unit) =>
          unit.unitType === 'B2B_SALES'
          && ((selection.kind === 'product' && unit.productTypeId === selection.id)
            || (selection.kind === 'resource' && unit.resourceTypeId === selection.id)),
      )

      if (matchingUnits.length === 0) continue

      const numericPrices = matchingUnits
        .map((unit) => unit.minPrice)
        .filter((price): price is number => typeof price === 'number')
      const pricePerUnit = numericPrices.length > 0 ? Math.min(...numericPrices) : null

      options.push({
        companyId: company.id,
        companyName: company.name,
        buildingId: building.id,
        buildingName: building.name,
        cityId: building.cityId,
        pricePerUnit,
        // Local B2B vendor candidates are restricted to the destination city.
        // That means their transit cost is intentionally always zero.
        transitCostPerUnit: 0,
      })
    }
  }

  return options.sort((left, right) => {
    if (left.pricePerUnit != null && right.pricePerUnit != null && left.pricePerUnit !== right.pricePerUnit) {
      return left.pricePerUnit - right.pricePerUnit
    }
    if (left.pricePerUnit != null && right.pricePerUnit == null) return -1
    if (left.pricePerUnit == null && right.pricePerUnit != null) return 1
    const companyCompare = left.companyName.localeCompare(right.companyName)
    return companyCompare !== 0 ? companyCompare : left.buildingName.localeCompare(right.buildingName)
  })
}
