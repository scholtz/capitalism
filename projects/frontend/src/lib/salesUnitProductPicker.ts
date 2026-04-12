import type { BuildingUnitInventory, ProductAvailabilityReason, RankedProductResult } from '@/types'

export type SalesUnitProductPickerUnit = {
  id: string
  unitType: string
  gridX: number
  gridY: number
  productTypeId: string | null
  linkUp: boolean
  linkDown: boolean
  linkLeft: boolean
  linkRight: boolean
  linkUpLeft: boolean
  linkUpRight: boolean
  linkDownLeft: boolean
  linkDownRight: boolean
}

type LinkFlag =
  | 'linkUp'
  | 'linkDown'
  | 'linkLeft'
  | 'linkRight'
  | 'linkUpLeft'
  | 'linkUpRight'
  | 'linkDownLeft'
  | 'linkDownRight'

const salesSourceUnitTypes = new Set(['PURCHASE', 'MANUFACTURING', 'STORAGE', 'B2B_SALES'])
const availabilityPriority: Record<ProductAvailabilityReason, number> = {
  connected_and_stock: 120,
  connected_upstream: 110,
  current_stock: 100,
}

const adjacencyChecks: Array<{
  deltaX: number
  deltaY: number
  unitFlag: LinkFlag
  candidateFlag: LinkFlag
}> = [
  { deltaX: 0, deltaY: -1, unitFlag: 'linkUp', candidateFlag: 'linkDown' },
  { deltaX: 0, deltaY: 1, unitFlag: 'linkDown', candidateFlag: 'linkUp' },
  { deltaX: -1, deltaY: 0, unitFlag: 'linkLeft', candidateFlag: 'linkRight' },
  { deltaX: 1, deltaY: 0, unitFlag: 'linkRight', candidateFlag: 'linkLeft' },
  { deltaX: -1, deltaY: -1, unitFlag: 'linkUpLeft', candidateFlag: 'linkDownRight' },
  { deltaX: 1, deltaY: -1, unitFlag: 'linkUpRight', candidateFlag: 'linkDownLeft' },
  { deltaX: -1, deltaY: 1, unitFlag: 'linkDownLeft', candidateFlag: 'linkUpRight' },
  { deltaX: 1, deltaY: 1, unitFlag: 'linkDownRight', candidateFlag: 'linkUpLeft' },
]

function isConnectedNeighbor(
  unit: SalesUnitProductPickerUnit,
  candidate: SalesUnitProductPickerUnit,
  adjacency: (typeof adjacencyChecks)[number],
): boolean {
  return (
    candidate.gridX === unit.gridX + adjacency.deltaX &&
    candidate.gridY === unit.gridY + adjacency.deltaY &&
    (unit[adjacency.unitFlag] || candidate[adjacency.candidateFlag])
  )
}

function getDirectlyConnectedUnits(unit: SalesUnitProductPickerUnit, units: SalesUnitProductPickerUnit[]): SalesUnitProductPickerUnit[] {
  return units.filter((candidate) => {
    if (candidate.id === unit.id) return false
    return adjacencyChecks.some((adjacency) => isConnectedNeighbor(unit, candidate, adjacency))
  })
}

function getConnectedProductIds(unit: SalesUnitProductPickerUnit, units: SalesUnitProductPickerUnit[]): Set<string> {
  const productIds = new Set<string>()
  const queue = [unit]
  const visited = new Set<string>()
  let readIndex = 0

  while (readIndex < queue.length) {
    const current = queue[readIndex]!
    readIndex += 1

    const key = `${current.gridX},${current.gridY}`
    if (visited.has(key)) continue
    visited.add(key)

    if (current.id !== unit.id && salesSourceUnitTypes.has(current.unitType) && current.productTypeId) {
      productIds.add(current.productTypeId)
    }

    for (const next of getDirectlyConnectedUnits(current, units)) {
      queue.push(next)
    }
  }

  return productIds
}

function getCurrentStockProductIds(unitId: string, unitInventories: BuildingUnitInventory[]): Set<string> {
  return new Set(
    unitInventories
      .filter((inventory) => inventory.buildingUnitId === unitId && inventory.quantity > 0 && inventory.productTypeId)
      .map((inventory) => inventory.productTypeId!)
  )
}

function getAvailabilityReason(fromConnectedUpstream: boolean, fromCurrentStock: boolean): ProductAvailabilityReason | null {
  if (fromConnectedUpstream && fromCurrentStock) return 'connected_and_stock'
  if (fromConnectedUpstream) return 'connected_upstream'
  if (fromCurrentStock) return 'current_stock'
  return null
}

export function getSalesUnitProductOptions(args: {
  unit: SalesUnitProductPickerUnit | null | undefined
  draftUnits: SalesUnitProductPickerUnit[]
  rankedProducts: RankedProductResult[]
  unitInventories: BuildingUnitInventory[]
}): RankedProductResult[] {
  const { unit, draftUnits, rankedProducts, unitInventories } = args
  if (!unit || unit.unitType !== 'PUBLIC_SALES') return rankedProducts

  const connectedProductIds = getConnectedProductIds(unit, draftUnits)
  const currentStockProductIds = getCurrentStockProductIds(unit.id, unitInventories)

  if (connectedProductIds.size === 0 && currentStockProductIds.size === 0) {
    return []
  }

  return rankedProducts
    .flatMap((entry) => {
      const fromConnectedUpstream = connectedProductIds.has(entry.productType.id)
      const fromCurrentStock = currentStockProductIds.has(entry.productType.id)
      const availabilityReason = getAvailabilityReason(fromConnectedUpstream, fromCurrentStock)
      if (!availabilityReason) return []

      return [
        {
          ...entry,
          rankingReason: 'connected' as const,
          rankingScore: availabilityPriority[availabilityReason],
          availabilityReason,
        },
      ]
    })
    .sort((left, right) => {
      if (right.rankingScore !== left.rankingScore) {
        return right.rankingScore - left.rankingScore
      }
      return left.productType.name.localeCompare(right.productType.name)
    })
}
