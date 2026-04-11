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
  latitude: number
  longitude: number
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
  distanceKm: number
  pricePerUnit: number | null
  transitCostPerUnit: number
}

export type PurchaseSelectorWeightedResource = {
  id: string
  weightPerUnit: number
}

export type PurchaseSelectorWeightedProduct = {
  id: string
  outputQuantity: number
  recipes: Array<{
    quantity: number
    resourceType: { id: string } | null
    inputProductType: { id: string } | null
  }>
}

const MINIMUM_WEIGHT_PER_UNIT = 0.1
const TRANSIT_COST_RATE_PER_KM_PER_WEIGHT_UNIT = 0.0025
const MINIMUM_TRANSIT_COST_PER_UNIT = 0.01

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
  destinationBuilding:
    | {
        latitude: number
        longitude: number
      }
    | null
    | undefined,
  resourceTypesById: Map<string, PurchaseSelectorWeightedResource>,
  productTypesById: Map<string, PurchaseSelectorWeightedProduct>,
): PurchaseVendorOption[] {
  if (!selection || !cityId || !destinationBuilding) return []

  const itemWeightPerUnit = computeItemWeightPerUnit(selection, resourceTypesById, productTypesById)

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
      const distanceKm = computeDistanceKm(
        building.latitude,
        building.longitude,
        destinationBuilding.latitude,
        destinationBuilding.longitude,
      )
      const transitCostPerUnit = computeTransitCostPerUnit(distanceKm, itemWeightPerUnit)

      options.push({
        companyId: company.id,
        companyName: company.name,
        buildingId: building.id,
        buildingName: building.name,
        cityId: building.cityId,
        distanceKm,
        pricePerUnit,
        transitCostPerUnit,
      })
    }
  }

  return options.sort((left, right) => {
    const leftDelivered = (left.pricePerUnit ?? Number.POSITIVE_INFINITY) + left.transitCostPerUnit
    const rightDelivered = (right.pricePerUnit ?? Number.POSITIVE_INFINITY) + right.transitCostPerUnit
    if (leftDelivered !== rightDelivered) {
      return leftDelivered - rightDelivered
    }
    if (left.pricePerUnit != null && right.pricePerUnit == null) return -1
    if (left.pricePerUnit == null && right.pricePerUnit != null) return 1
    const companyCompare = left.companyName.localeCompare(right.companyName)
    return companyCompare !== 0 ? companyCompare : left.buildingName.localeCompare(right.buildingName)
  })
}

function computeItemWeightPerUnit(
  selection: PurchaseSelectorSelection,
  resourceTypesById: Map<string, PurchaseSelectorWeightedResource>,
  productTypesById: Map<string, PurchaseSelectorWeightedProduct>,
): number {
  if (selection.kind === 'resource') {
    return Math.max(resourceTypesById.get(selection.id)?.weightPerUnit ?? MINIMUM_WEIGHT_PER_UNIT, MINIMUM_WEIGHT_PER_UNIT)
  }

  return computeProductWeightPerUnit(selection.id, resourceTypesById, productTypesById, new Map())
}

function computeProductWeightPerUnit(
  productId: string,
  resourceTypesById: Map<string, PurchaseSelectorWeightedResource>,
  productTypesById: Map<string, PurchaseSelectorWeightedProduct>,
  cache: Map<string, number>,
): number {
  const cached = cache.get(productId)
  if (cached != null) return cached

  const product = productTypesById.get(productId)
  if (!product || product.recipes.length === 0) {
    return MINIMUM_WEIGHT_PER_UNIT
  }

  let totalInputWeight = 0
  for (const recipe of product.recipes) {
    if (recipe.resourceType?.id) {
      totalInputWeight += recipe.quantity * Math.max(resourceTypesById.get(recipe.resourceType.id)?.weightPerUnit ?? MINIMUM_WEIGHT_PER_UNIT, MINIMUM_WEIGHT_PER_UNIT)
      continue
    }

    if (recipe.inputProductType?.id) {
      totalInputWeight += recipe.quantity * computeProductWeightPerUnit(recipe.inputProductType.id, resourceTypesById, productTypesById, cache)
    }
  }

  const outputQuantity = Math.max(product.outputQuantity ?? 1, 1)
  const computed = Math.max(totalInputWeight / outputQuantity, MINIMUM_WEIGHT_PER_UNIT)
  cache.set(productId, computed)
  return computed
}

function computeTransitCostPerUnit(distanceKm: number, itemWeightPerUnit: number): number {
  const rawTransitCost = distanceKm * Math.max(itemWeightPerUnit, MINIMUM_WEIGHT_PER_UNIT) * TRANSIT_COST_RATE_PER_KM_PER_WEIGHT_UNIT
  return Number(Math.max(rawTransitCost, MINIMUM_TRANSIT_COST_PER_UNIT).toFixed(2))
}

function computeDistanceKm(latitudeA: number, longitudeA: number, latitudeB: number, longitudeB: number): number {
  const earthRadiusKm = 6371
  const deltaLatitude = degreesToRadians(latitudeB - latitudeA)
  const deltaLongitude = degreesToRadians(longitudeB - longitudeA)
  const originLatitude = degreesToRadians(latitudeA)
  const destinationLatitude = degreesToRadians(latitudeB)

  const sinLatitude = Math.sin(deltaLatitude / 2)
  const sinLongitude = Math.sin(deltaLongitude / 2)
  const haversine = (sinLatitude * sinLatitude)
    + (Math.cos(originLatitude) * Math.cos(destinationLatitude) * sinLongitude * sinLongitude)
  const arc = 2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine))
  return Number((earthRadiusKm * arc).toFixed(2))
}

function degreesToRadians(degrees: number): number {
  return degrees * Math.PI / 180
}
