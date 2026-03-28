<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AdvancedItemSelector from '@/components/buildings/AdvancedItemSelector.vue'
import { isProductLocked } from '@/lib/productAccess'
import {
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
} from '@/lib/catalogPresentation'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import type {
  Building,
  BuildingConfigurationPlanRemoval,
  BuildingConfigurationPlanUnit,
  BuildingUnit,
  BuildingUnitInventorySummary,
  GlobalExchangeOffer,
  ProductType,
  ResourceType,
} from '@/types'

type GridUnit = BuildingUnit | BuildingConfigurationPlanUnit | EditableGridUnit
type ItemSelection = { kind: 'resource' | 'product'; id: string } | null
type SelectorItem = {
  kind: 'resource' | 'product'
  id: string
  name: string
  description?: string | null
  helperText?: string | null
  groupLabel: string
  unitSymbol?: string | null
  badge?: string | null
  disabled?: boolean
}

type EditableGridUnit = {
  id: string
  unitType: string
  gridX: number
  gridY: number
  level: number
  linkUp: boolean
  linkDown: boolean
  linkLeft: boolean
  linkRight: boolean
  linkUpLeft: boolean
  linkUpRight: boolean
  linkDownLeft: boolean
  linkDownRight: boolean
  resourceTypeId: string | null
  productTypeId: string | null
  minPrice: number | null
  maxPrice: number | null
  purchaseSource: string | null
  saleVisibility: string | null
  budget: number | null
  mediaHouseBuildingId: string | null
  minQuality: number | null
  brandScope: string | null
  vendorLockCompanyId: string | null
}

const LINK_CHANGE_TICKS = 1
const UNIT_PLAN_CHANGE_TICKS = 3
const gridIndexes = [0, 1, 2, 3] as const

const { t, locale } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const buildingId = computed(() => route.params.id as string)
const building = ref<Building | null>(null)
const currentTick = ref(0)
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const isEditing = ref(false)
const selectedCell = ref<{ x: number; y: number } | null>(null)
const showUnitPicker = ref(false)
const draftUnits = ref<EditableGridUnit[]>([])
const editBaselineUnits = ref<EditableGridUnit[]>([])
const resourceTypes = ref<ResourceType[]>([])
const productTypes = ref<ProductType[]>([])
const unitInventorySummaries = ref<BuildingUnitInventorySummary[]>([])
const exchangeOffers = ref<GlobalExchangeOffer[]>([])
const exchangeOffersLoading = ref(false)
const showSaleDialog = ref(false)
const salePrice = ref<number | null>(null)
const savingSale = ref(false)
const layoutName = ref('')
const showLayoutDialog = ref(false)

const LAYOUT_STORAGE_KEY = 'capitalism_building_layouts'

const allowedUnitsMap: Record<string, string[]> = {
  MINE: ['MINING', 'STORAGE', 'B2B_SALES'],
  FACTORY: ['PURCHASE', 'MANUFACTURING', 'BRANDING', 'STORAGE', 'B2B_SALES'],
  SALES_SHOP: ['PURCHASE', 'MARKETING', 'PUBLIC_SALES'],
  RESEARCH_DEVELOPMENT: ['PRODUCT_QUALITY', 'BRAND_QUALITY'],
}

const unitColors: Record<string, string> = {
  MINING: '#ff6d00',
  STORAGE: '#8b949e',
  B2B_SALES: '#00c853',
  PURCHASE: '#0047ff',
  MANUFACTURING: '#ff6d00',
  BRANDING: '#9333ea',
  MARKETING: '#ec4899',
  PUBLIC_SALES: '#00c853',
  PRODUCT_QUALITY: '#0047ff',
  BRAND_QUALITY: '#9333ea',
}

const activeUnits = computed(() => building.value?.units ?? [])
const pendingConfiguration = computed(() => building.value?.pendingConfiguration ?? null)
const pendingUnits = computed(() => pendingConfiguration.value?.units ?? [])
const pendingRemovals = computed(() => pendingConfiguration.value?.removals ?? [])
const plannedUnits = computed<GridUnit[]>(() => (isEditing.value ? draftUnits.value : pendingUnits.value))
const allowedUnits = computed(() => {
  if (!building.value) return []
  return allowedUnitsMap[building.value.type] || []
})
const intermediateProductIds = computed(() => {
  const ids = new Set<string>()
  for (const product of productTypes.value) {
    for (const recipe of product.recipes) {
      if (recipe.inputProductType?.id) {
        ids.add(recipe.inputProductType.id)
      }
    }
  }
  return ids
})
const allSelectableItems = computed<SelectorItem[]>(() => [
  ...resourceTypes.value.map((resource) => ({
    kind: 'resource' as const,
    id: resource.id,
    name: getLocalizedResourceName(resource, locale.value),
    description: getLocalizedResourceDescription(resource, locale.value),
    groupLabel: t('buildingDetail.selector.rawMaterials'),
    unitSymbol: resource.unitSymbol,
  })),
  ...productTypes.value.map((product) => ({
    kind: 'product' as const,
    id: product.id,
    name: getLocalizedProductName(product, locale.value),
    description: getLocalizedProductDescription(product, locale.value),
    helperText: isProductLocked(product) ? t('catalog.proDetail') : null,
    groupLabel: t('buildingDetail.selector.products'),
    unitSymbol: product.unitSymbol,
    badge: product.isProOnly ? t('catalog.proBadge') : null,
    disabled: isProductLocked(product),
  })),
])
const lockedConfiguredProducts = computed(() => {
  const configuredProductIds = new Set(
    [...activeUnits.value, ...pendingUnits.value]
      .map((unit) => unit.productTypeId)
      .filter((value): value is string => !!value),
  )

  return productTypes.value.filter((product) => configuredProductIds.has(product.id) && isProductLocked(product))
})
const lockedConfiguredProductNames = computed(() =>
  lockedConfiguredProducts.value.map((product) => getLocalizedProductName(product, locale.value)).join(', '),
)
const isUpgradeInProgress = computed(() => pendingConfiguration.value !== null)
const showPlanningSection = computed(() => isEditing.value)
const remainingUpgradeTicks = computed(() => {
  if (!pendingConfiguration.value) return 0
  return Math.max(pendingConfiguration.value.appliesAtTick - currentTick.value, 0)
})
const draftTotalTicks = computed(() => {
  const positions = new Set<string>()

  for (const unit of activeUnits.value) positions.add(`${unit.gridX},${unit.gridY}`)
  for (const unit of draftUnits.value) positions.add(`${unit.gridX},${unit.gridY}`)
  for (const unit of pendingUnits.value) positions.add(`${unit.gridX},${unit.gridY}`)
  for (const removal of pendingRemovals.value) positions.add(`${removal.gridX},${removal.gridY}`)

  return Array.from(positions).reduce((maxTicks, position) => {
    const [gridX = 0, gridY = 0] = position.split(',').map(Number)
    return Math.max(maxTicks, getDraftTicksAt(gridX, gridY))
  }, 0)
})
const hasDraftChanges = computed(() => !areUnitCollectionsEqual(draftUnits.value, editBaselineUnits.value))
const selectedDisplayUnit = computed<GridUnit | undefined>(() => {
  if (!selectedCell.value) return undefined

  if (isEditing.value) {
    return getUnitAtFrom(plannedUnits.value, selectedCell.value.x, selectedCell.value.y)
  }

  return getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
})
const selectedPurchaseUnit = computed(() => selectedDisplayUnit.value?.unitType === 'PURCHASE' ? selectedDisplayUnit.value : undefined)

function formatBuildingType(type: string): string {
  return type.replace(/_/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase())
}

function getUnitColor(unitType: string): string {
  return unitColors[unitType] || '#8b949e'
}

function getUnitAtFrom(units: GridUnit[], x: number, y: number): GridUnit | undefined {
  return units.find((unit) => unit.gridX === x && unit.gridY === y)
}

function getDraftUnitAt(x: number, y: number): EditableGridUnit | undefined {
  return draftUnits.value.find((unit) => unit.gridX === x && unit.gridY === y)
}

function getPendingUnitAt(x: number, y: number): BuildingConfigurationPlanUnit | undefined {
  return pendingUnits.value.find((unit) => unit.gridX === x && unit.gridY === y)
}

function getPendingRemovalAt(x: number, y: number): BuildingConfigurationPlanRemoval | undefined {
  return pendingRemovals.value.find((removal) => removal.gridX === x && removal.gridY === y)
}

function cloneUnit(unit: GridUnit): EditableGridUnit {
  return {
    id: unit.id,
    unitType: unit.unitType,
    gridX: unit.gridX,
    gridY: unit.gridY,
    level: unit.level,
    linkUp: unit.linkUp,
    linkDown: unit.linkDown,
    linkLeft: unit.linkLeft,
    linkRight: unit.linkRight,
    linkUpLeft: unit.linkUpLeft,
    linkUpRight: unit.linkUpRight,
    linkDownLeft: unit.linkDownLeft,
    linkDownRight: unit.linkDownRight,
    resourceTypeId: ('resourceTypeId' in unit ? unit.resourceTypeId : null) ?? null,
    productTypeId: ('productTypeId' in unit ? unit.productTypeId : null) ?? null,
    minPrice: ('minPrice' in unit ? unit.minPrice : null) ?? null,
    maxPrice: ('maxPrice' in unit ? unit.maxPrice : null) ?? null,
    purchaseSource: ('purchaseSource' in unit ? unit.purchaseSource : null) ?? null,
    saleVisibility: ('saleVisibility' in unit ? unit.saleVisibility : null) ?? null,
    budget: ('budget' in unit ? unit.budget : null) ?? null,
    mediaHouseBuildingId: ('mediaHouseBuildingId' in unit ? unit.mediaHouseBuildingId : null) ?? null,
    minQuality: ('minQuality' in unit ? unit.minQuality : null) ?? null,
    brandScope: ('brandScope' in unit ? unit.brandScope : null) ?? null,
    vendorLockCompanyId: ('vendorLockCompanyId' in unit ? unit.vendorLockCompanyId : null) ?? null,
  }
}

function setDraftUnitsFrom(sourceUnits: GridUnit[]) {
  draftUnits.value = sourceUnits.map((unit) => cloneUnit(unit))
}

function setEditBaselineFrom(sourceUnits: GridUnit[]) {
  editBaselineUnits.value = sourceUnits.map((unit) => cloneUnit(unit))
}

function getEditingSourceUnits(): GridUnit[] {
  return pendingConfiguration.value?.units ?? activeUnits.value
}

function startEditing() {
  const sourceUnits = getEditingSourceUnits()
  setDraftUnitsFrom(sourceUnits)
  setEditBaselineFrom(sourceUnits)
  isEditing.value = true
  selectedCell.value = null
  showUnitPicker.value = false
}

function cancelEditing() {
  const sourceUnits = getEditingSourceUnits()
  setDraftUnitsFrom(sourceUnits)
  setEditBaselineFrom(sourceUnits)
  isEditing.value = false
  selectedCell.value = null
  showUnitPicker.value = false
}

function clickDraftCell(x: number, y: number) {
  if (!isEditing.value) {
    return
  }

  const existing = getDraftUnitAt(x, y)
  selectedCell.value = { x, y }
  showUnitPicker.value = !existing
}

function placeUnit(unitType: string) {
  if (!selectedCell.value || !isEditing.value) return

  const newUnit: EditableGridUnit = {
    id: `draft-${selectedCell.value.x}-${selectedCell.value.y}-${Date.now()}`,
    unitType,
    gridX: selectedCell.value.x,
    gridY: selectedCell.value.y,
    level: getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)?.level ?? 1,
    linkUp: false,
    linkDown: false,
    linkLeft: false,
    linkRight: false,
    linkUpLeft: false,
    linkUpRight: false,
    linkDownLeft: false,
    linkDownRight: false,
    resourceTypeId: null,
    productTypeId: null,
    minPrice: null,
    maxPrice: null,
    purchaseSource: null,
    saleVisibility: null,
    budget: null,
    mediaHouseBuildingId: null,
    minQuality: null,
    brandScope: null,
    vendorLockCompanyId: null,
  }

  draftUnits.value = [...draftUnits.value.filter((unit) => !(unit.gridX === newUnit.gridX && unit.gridY === newUnit.gridY)), newUnit]
  selectedCell.value = null
  showUnitPicker.value = false
}

function clearConnectionsAround(x: number, y: number) {
  const left = getDraftUnitAt(x - 1, y)
  const right = getDraftUnitAt(x + 1, y)
  const up = getDraftUnitAt(x, y - 1)
  const down = getDraftUnitAt(x, y + 1)
  const upLeft = getDraftUnitAt(x - 1, y - 1)
  const upRight = getDraftUnitAt(x + 1, y - 1)
  const downLeft = getDraftUnitAt(x - 1, y + 1)
  const downRight = getDraftUnitAt(x + 1, y + 1)

  if (left) left.linkRight = false
  if (right) right.linkLeft = false
  if (up) up.linkDown = false
  if (down) down.linkUp = false
  if (upLeft) upLeft.linkDownRight = false
  if (upRight) upRight.linkDownLeft = false
  if (downLeft) downLeft.linkUpRight = false
  if (downRight) downRight.linkUpLeft = false
}

function removeDraftUnit(x: number, y: number) {
  if (!isEditing.value) return

  clearConnectionsAround(x, y)
  draftUnits.value = draftUnits.value.filter((unit) => !(unit.gridX === x && unit.gridY === y))
  selectedCell.value = null
  showUnitPicker.value = false
}

function toggleHorizontalLink(x: number, y: number) {
  if (!isEditing.value) return

  const left = getDraftUnitAt(x, y)
  const right = getDraftUnitAt(x + 1, y)
  if (!left || !right) return

  const next = !left.linkRight
  left.linkRight = next
  right.linkLeft = next
}

function toggleVerticalLink(x: number, y: number) {
  if (!isEditing.value) return

  const top = getDraftUnitAt(x, y)
  const bottom = getDraftUnitAt(x, y + 1)
  if (!top || !bottom) return

  const next = !top.linkDown
  top.linkDown = next
  bottom.linkUp = next
}

function clearDiagonalState(units: EditableGridUnit[], x: number, y: number) {
  const topLeft = getUnitAtFrom(units, x, y) as EditableGridUnit | undefined
  const topRight = getUnitAtFrom(units, x + 1, y) as EditableGridUnit | undefined
  const bottomLeft = getUnitAtFrom(units, x, y + 1) as EditableGridUnit | undefined
  const bottomRight = getUnitAtFrom(units, x + 1, y + 1) as EditableGridUnit | undefined

  if (topLeft) topLeft.linkDownRight = false
  if (bottomRight) bottomRight.linkUpLeft = false
  if (topRight) topRight.linkDownLeft = false
  if (bottomLeft) bottomLeft.linkUpRight = false
}

function getDiagonalStateFor(units: GridUnit[], x: number, y: number): 'none' | 'tl-br' | 'tr-bl' | 'cross' {
  const topLeft = getUnitAtFrom(units, x, y)
  const topRight = getUnitAtFrom(units, x + 1, y)
  const hasTopLeftToBottomRight = !!topLeft?.linkDownRight
  const hasTopRightToBottomLeft = !!topRight?.linkDownLeft

  if (hasTopLeftToBottomRight && hasTopRightToBottomLeft) return 'cross'
  if (hasTopLeftToBottomRight) return 'tl-br'
  if (hasTopRightToBottomLeft) return 'tr-bl'
  return 'none'
}

function toggleDiagonalLink(x: number, y: number) {
  if (!isEditing.value) return

  const topLeft = getDraftUnitAt(x, y)
  const topRight = getDraftUnitAt(x + 1, y)
  const bottomLeft = getDraftUnitAt(x, y + 1)
  const bottomRight = getDraftUnitAt(x + 1, y + 1)
  if (!topLeft || !topRight || !bottomLeft || !bottomRight) return

  const currentState = getDiagonalStateFor(draftUnits.value, x, y)
  clearDiagonalState(draftUnits.value, x, y)

  if (currentState === 'none') {
    topLeft.linkDownRight = true
    bottomRight.linkUpLeft = true
    return
  }

  if (currentState === 'tl-br') {
    topRight.linkDownLeft = true
    bottomLeft.linkUpRight = true
    return
  }

  if (currentState === 'tr-bl') {
    topLeft.linkDownRight = true
    bottomRight.linkUpLeft = true
    topRight.linkDownLeft = true
    bottomLeft.linkUpRight = true
  }
}

function isHorizontalLinkActiveFor(units: GridUnit[], x: number, y: number): boolean {
  return getUnitAtFrom(units, x, y)?.linkRight || false
}

function isVerticalLinkActiveFor(units: GridUnit[], x: number, y: number): boolean {
  return getUnitAtFrom(units, x, y)?.linkDown || false
}

function canToggleHorizontalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y) && !!getUnitAtFrom(units, x + 1, y)
}

function canToggleVerticalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y) && !!getUnitAtFrom(units, x, y + 1)
}

function canToggleDiagonalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y)
    && !!getUnitAtFrom(units, x + 1, y)
    && !!getUnitAtFrom(units, x, y + 1)
    && !!getUnitAtFrom(units, x + 1, y + 1)
}

function getDraftTicksForUnit(unit: EditableGridUnit): number {
  const baselinePendingUnit = getPendingUnitAt(unit.gridX, unit.gridY)
  const baselinePendingRemoval = getPendingRemovalAt(unit.gridX, unit.gridY)
  const activeUnit = getUnitAtFrom(activeUnits.value, unit.gridX, unit.gridY) as BuildingUnit | undefined

  if (baselinePendingUnit && areUnitsEquivalent(baselinePendingUnit, unit)) {
    return getRemainingTicksFromApplyTick(baselinePendingUnit.appliesAtTick)
  }

  if (baselinePendingRemoval && activeUnit && areUnitsEquivalent(activeUnit, unit)) {
    return getCancelTicks(baselinePendingRemoval.ticksRequired)
  }

  if (baselinePendingUnit && activeUnit && areUnitsEquivalent(activeUnit, unit)) {
    return getCancelTicks(baselinePendingUnit.ticksRequired)
  }

  if (!activeUnit) return UNIT_PLAN_CHANGE_TICKS

  if (activeUnit.unitType !== unit.unitType) {
    return UNIT_PLAN_CHANGE_TICKS
  }

  if (
    activeUnit.linkUp !== unit.linkUp
    || activeUnit.linkDown !== unit.linkDown
    || activeUnit.linkLeft !== unit.linkLeft
    || activeUnit.linkRight !== unit.linkRight
    || activeUnit.linkUpLeft !== unit.linkUpLeft
    || activeUnit.linkUpRight !== unit.linkUpRight
    || activeUnit.linkDownLeft !== unit.linkDownLeft
    || activeUnit.linkDownRight !== unit.linkDownRight
  ) {
    return LINK_CHANGE_TICKS
  }

  if (
    (activeUnit.resourceTypeId ?? null) !== (unit.resourceTypeId ?? null)
    || (activeUnit.productTypeId ?? null) !== (unit.productTypeId ?? null)
    || (activeUnit.minPrice ?? null) !== (unit.minPrice ?? null)
    || (activeUnit.maxPrice ?? null) !== (unit.maxPrice ?? null)
    || (activeUnit.purchaseSource ?? null) !== (unit.purchaseSource ?? null)
    || (activeUnit.saleVisibility ?? null) !== (unit.saleVisibility ?? null)
    || (activeUnit.budget ?? null) !== (unit.budget ?? null)
    || (activeUnit.mediaHouseBuildingId ?? null) !== (unit.mediaHouseBuildingId ?? null)
    || (activeUnit.minQuality ?? null) !== (unit.minQuality ?? null)
    || (activeUnit.brandScope ?? null) !== (unit.brandScope ?? null)
    || (activeUnit.vendorLockCompanyId ?? null) !== (unit.vendorLockCompanyId ?? null)
  ) {
    return LINK_CHANGE_TICKS
  }

  return 0
}

function getDraftTicksAt(x: number, y: number): number {
  const draftUnit = getDraftUnitAt(x, y)
  if (draftUnit) {
    return getDraftTicksForUnit(draftUnit)
  }

  const pendingRemoval = getPendingRemovalAt(x, y)
  if (pendingRemoval) {
    return getRemainingTicksFromApplyTick(pendingRemoval.appliesAtTick)
  }

  const pendingUnit = getPendingUnitAt(x, y)
  if (pendingUnit) {
    return getCancelTicks(pendingUnit.ticksRequired)
  }

  return getUnitAtFrom(activeUnits.value, x, y) ? UNIT_PLAN_CHANGE_TICKS : 0
}

function getDisplayedTicks(unit: GridUnit): number {
  if ('appliesAtTick' in unit && 'isChanged' in unit && unit.isChanged) {
    return getRemainingTicksFromApplyTick(unit.appliesAtTick)
  }

  return getDraftTicksForUnit(unit as EditableGridUnit)
}

function getRemainingTicksFromApplyTick(appliesAtTick: number): number {
  return Math.max(appliesAtTick - currentTick.value, 0)
}

function getCancelTicks(baseTicks: number): number {
  return Math.max(Math.ceil(baseTicks * 0.1), 1)
}

type UnitComparisonKeys = 'unitType' | 'gridX' | 'gridY' | 'linkUp' | 'linkDown' | 'linkLeft' | 'linkRight' | 'linkUpLeft' | 'linkUpRight' | 'linkDownLeft' | 'linkDownRight' | 'resourceTypeId' | 'productTypeId' | 'minPrice' | 'maxPrice' | 'purchaseSource' | 'saleVisibility' | 'budget' | 'mediaHouseBuildingId' | 'minQuality' | 'brandScope' | 'vendorLockCompanyId'

function areUnitsEquivalent(
  left: Pick<EditableGridUnit, UnitComparisonKeys>,
  right: Pick<EditableGridUnit, UnitComparisonKeys>,
): boolean {
  return left.unitType === right.unitType
    && left.gridX === right.gridX
    && left.gridY === right.gridY
    && left.linkUp === right.linkUp
    && left.linkDown === right.linkDown
    && left.linkLeft === right.linkLeft
    && left.linkRight === right.linkRight
    && left.linkUpLeft === right.linkUpLeft
    && left.linkUpRight === right.linkUpRight
    && left.linkDownLeft === right.linkDownLeft
    && left.linkDownRight === right.linkDownRight
    && (left.resourceTypeId ?? null) === (right.resourceTypeId ?? null)
    && (left.productTypeId ?? null) === (right.productTypeId ?? null)
    && (left.minPrice ?? null) === (right.minPrice ?? null)
    && (left.maxPrice ?? null) === (right.maxPrice ?? null)
    && (left.purchaseSource ?? null) === (right.purchaseSource ?? null)
    && (left.saleVisibility ?? null) === (right.saleVisibility ?? null)
    && (left.budget ?? null) === (right.budget ?? null)
    && (left.mediaHouseBuildingId ?? null) === (right.mediaHouseBuildingId ?? null)
    && (left.minQuality ?? null) === (right.minQuality ?? null)
    && (left.brandScope ?? null) === (right.brandScope ?? null)
    && (left.vendorLockCompanyId ?? null) === (right.vendorLockCompanyId ?? null)
}

function areUnitCollectionsEqual(left: EditableGridUnit[], right: EditableGridUnit[]): boolean {
  if (left.length !== right.length) {
    return false
  }

  const sortedLeft = [...left].sort(compareUnits)
  const sortedRight = [...right].sort(compareUnits)

  return sortedLeft.every((unit, index) => areUnitsEquivalent(unit, sortedRight[index]!))
}

function compareUnits(left: EditableGridUnit, right: EditableGridUnit): number {
  if (left.gridY !== right.gridY) return left.gridY - right.gridY
  if (left.gridX !== right.gridX) return left.gridX - right.gridX
  return left.unitType.localeCompare(right.unitType)
}

function storeConfiguration() {
  if (!building.value || saving.value || !hasDraftChanges.value) return

  saving.value = true
  error.value = null

  gqlRequest<{
    storeBuildingConfiguration: {
      id: string
      appliesAtTick: number
      totalTicksRequired: number
    }
  }>(
    `mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
      storeBuildingConfiguration(input: $input) {
        id
        appliesAtTick
        totalTicksRequired
      }
    }`,
    {
      input: {
        buildingId: building.value.id,
        units: draftUnits.value.map((unit) => ({
          unitType: unit.unitType,
          gridX: unit.gridX,
          gridY: unit.gridY,
          linkUp: unit.linkUp,
          linkDown: unit.linkDown,
          linkLeft: unit.linkLeft,
          linkRight: unit.linkRight,
          linkUpLeft: unit.linkUpLeft,
          linkUpRight: unit.linkUpRight,
          linkDownLeft: unit.linkDownLeft,
          linkDownRight: unit.linkDownRight,
          resourceTypeId: unit.resourceTypeId,
          productTypeId: unit.productTypeId,
          minPrice: unit.minPrice,
          maxPrice: unit.maxPrice,
          purchaseSource: unit.purchaseSource,
          saleVisibility: unit.saleVisibility,
          budget: unit.budget,
          mediaHouseBuildingId: unit.mediaHouseBuildingId,
          minQuality: unit.minQuality,
          brandScope: unit.brandScope,
          vendorLockCompanyId: unit.vendorLockCompanyId,
        })),
      },
    },
  )
    .then(() => {
      isEditing.value = false
      return loadBuilding()
    })
    .catch((reason: unknown) => {
      error.value = reason instanceof Error ? reason.message : t('buildingDetail.storeUpgradeFailed')
    })
    .finally(() => {
      saving.value = false
    })
}

// ── Resource path validation ──

type ValidationWarning = { key: string; params?: Record<string, unknown> }

function getLinkedUnits(unit: EditableGridUnit, units: EditableGridUnit[]): EditableGridUnit[] {
  const linked: EditableGridUnit[] = []
  const byPos = new Map(units.map((u) => [`${u.gridX},${u.gridY}`, u]))

  if (unit.linkUp) { const u = byPos.get(`${unit.gridX},${unit.gridY - 1}`); if (u) linked.push(u) }
  if (unit.linkDown) { const u = byPos.get(`${unit.gridX},${unit.gridY + 1}`); if (u) linked.push(u) }
  if (unit.linkLeft) { const u = byPos.get(`${unit.gridX - 1},${unit.gridY}`); if (u) linked.push(u) }
  if (unit.linkRight) { const u = byPos.get(`${unit.gridX + 1},${unit.gridY}`); if (u) linked.push(u) }
  if (unit.linkUpLeft) { const u = byPos.get(`${unit.gridX - 1},${unit.gridY - 1}`); if (u) linked.push(u) }
  if (unit.linkUpRight) { const u = byPos.get(`${unit.gridX + 1},${unit.gridY - 1}`); if (u) linked.push(u) }
  if (unit.linkDownLeft) { const u = byPos.get(`${unit.gridX - 1},${unit.gridY + 1}`); if (u) linked.push(u) }
  if (unit.linkDownRight) { const u = byPos.get(`${unit.gridX + 1},${unit.gridY + 1}`); if (u) linked.push(u) }

  return linked
}

const configWarnings = computed<ValidationWarning[]>(() => {
  const units = isEditing.value ? draftUnits.value : (pendingConfiguration.value?.units ?? activeUnits.value).map(cloneUnit)
  const warnings: ValidationWarning[] = []
  if (!building.value || units.length === 0) return warnings

  const purchaseUnits = units.filter((u) => u.unitType === 'PURCHASE')
  const manufacturingUnits = units.filter((u) => u.unitType === 'MANUFACTURING')
  const publicSalesUnits = units.filter((u) => u.unitType === 'PUBLIC_SALES')
  const b2bSalesUnits = units.filter((u) => u.unitType === 'B2B_SALES')
  const marketingUnits = units.filter((u) => u.unitType === 'MARKETING')
  const brandingUnits = units.filter((u) => u.unitType === 'BRANDING')
  const miningUnits = units.filter((u) => u.unitType === 'MINING')
  const storageUnits = units.filter((u) => u.unitType === 'STORAGE')
  const productQualityUnits = units.filter((u) => u.unitType === 'PRODUCT_QUALITY')
  const brandQualityUnits = units.filter((u) => u.unitType === 'BRAND_QUALITY')

  // Check unit-specific configuration
  for (const unit of purchaseUnits) {
    if (!unit.resourceTypeId && !unit.productTypeId) {
      warnings.push({ key: 'buildingDetail.warnings.purchaseNoItem', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of manufacturingUnits) {
    if (!unit.productTypeId) {
      warnings.push({ key: 'buildingDetail.warnings.manufacturingNoProduct', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of publicSalesUnits) {
    if (!unit.productTypeId && !unit.resourceTypeId) {
      warnings.push({ key: 'buildingDetail.warnings.salesNoItem', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of marketingUnits) {
    if (!unit.budget || unit.budget <= 0) {
      warnings.push({ key: 'buildingDetail.warnings.marketingNoBudget', params: { x: unit.gridX, y: unit.gridY } })
    }
    if (!unit.mediaHouseBuildingId) {
      warnings.push({ key: 'buildingDetail.warnings.marketingNoMediaHouse', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of brandingUnits) {
    if (!unit.brandScope) {
      warnings.push({ key: 'buildingDetail.warnings.brandingNoScope', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of productQualityUnits) {
    if (!unit.productTypeId) {
      warnings.push({ key: 'buildingDetail.warnings.productQualityNoProduct', params: { x: unit.gridX, y: unit.gridY } })
    }
  }
  for (const unit of brandQualityUnits) {
    if (!unit.brandScope) {
      warnings.push({ key: 'buildingDetail.warnings.brandQualityNoScope', params: { x: unit.gridX, y: unit.gridY } })
      continue
    }

    if (['PRODUCT', 'CATEGORY'].includes(unit.brandScope) && !unit.productTypeId) {
      warnings.push({ key: 'buildingDetail.warnings.brandQualityNoProduct', params: { x: unit.gridX, y: unit.gridY } })
    }
  }

  // Check resource flow connectivity
  if (building.value.type === 'FACTORY') {
    for (const pu of purchaseUnits) {
      const linked = getLinkedUnits(pu, units)
      const hasConsumer = linked.some((u) => ['MANUFACTURING', 'STORAGE'].includes(u.unitType))
      if (!hasConsumer) {
        warnings.push({ key: 'buildingDetail.warnings.purchaseNotLinked', params: { x: pu.gridX, y: pu.gridY } })
      }
    }
    for (const mu of manufacturingUnits) {
      const linked = getLinkedUnits(mu, units)
      const hasOutput = linked.some((u) => ['STORAGE', 'B2B_SALES'].includes(u.unitType))
      if (!hasOutput) {
        warnings.push({ key: 'buildingDetail.warnings.manufacturingNotLinked', params: { x: mu.gridX, y: mu.gridY } })
      }
    }
  }

  if (building.value.type === 'MINE') {
    for (const mu of miningUnits) {
      const linked = getLinkedUnits(mu, units)
      const hasOutput = linked.some((u) => ['STORAGE', 'B2B_SALES'].includes(u.unitType))
      if (!hasOutput) {
        warnings.push({ key: 'buildingDetail.warnings.miningNotLinked', params: { x: mu.gridX, y: mu.gridY } })
      }
    }
  }

  if (building.value.type === 'SALES_SHOP') {
    for (const pu of purchaseUnits) {
      const linked = getLinkedUnits(pu, units)
      const hasConsumer = linked.some((u) => ['PUBLIC_SALES', 'MARKETING'].includes(u.unitType))
      if (!hasConsumer) {
        warnings.push({ key: 'buildingDetail.warnings.purchaseNotLinked', params: { x: pu.gridX, y: pu.gridY } })
      }
    }
  }

  // Check unlinked storage/sales units
  for (const su of storageUnits) {
    const linked = getLinkedUnits(su, units)
    if (linked.length === 0) {
      warnings.push({ key: 'buildingDetail.warnings.storageNotLinked', params: { x: su.gridX, y: su.gridY } })
    }
  }
  for (const su of b2bSalesUnits) {
    const linked = getLinkedUnits(su, units)
    if (linked.length === 0) {
      warnings.push({ key: 'buildingDetail.warnings.b2bSalesNotLinked', params: { x: su.gridX, y: su.gridY } })
    }
  }

  return warnings
})

// ── Building sale ──

function openSaleDialog() {
  salePrice.value = building.value?.askingPrice ?? null
  showSaleDialog.value = true
}

function closeSaleDialog() {
  showSaleDialog.value = false
}

async function setBuildingForSale(forSale: boolean) {
  if (!building.value || savingSale.value) return
  savingSale.value = true
  try {
    await gqlRequest<{ setBuildingForSale: { id: string } }>(
      `mutation SetBuildingForSale($input: SetBuildingForSaleInput!) {
        setBuildingForSale(input: $input) { id isForSale askingPrice }
      }`,
      {
        input: {
          buildingId: building.value.id,
          isForSale: forSale,
          askingPrice: forSale ? salePrice.value : null,
        },
      },
    )
    showSaleDialog.value = false
    await loadBuilding()
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('buildingDetail.saleFailed')
  } finally {
    savingSale.value = false
  }
}

// ── Layout save/load ──

type SavedLayout = {
  name: string
  buildingType: string
  units: Array<{
    unitType: string
    gridX: number
    gridY: number
    linkUp: boolean
    linkDown: boolean
    linkLeft: boolean
    linkRight: boolean
    linkUpLeft: boolean
    linkUpRight: boolean
    linkDownLeft: boolean
    linkDownRight: boolean
    resourceTypeId: string | null
    productTypeId: string | null
    minPrice: number | null
    maxPrice: number | null
    purchaseSource: string | null
    saleVisibility: string | null
    budget: number | null
    mediaHouseBuildingId: string | null
    minQuality: number | null
    brandScope: string | null
    vendorLockCompanyId: string | null
  }>
}

function getSavedLayouts(): SavedLayout[] {
  try {
    return JSON.parse(localStorage.getItem(LAYOUT_STORAGE_KEY) || '[]') as SavedLayout[]
  } catch {
    return []
  }
}

const savedLayouts = computed(() => {
  if (!building.value) return []
  return getSavedLayouts().filter((layout) => layout.buildingType === building.value!.type)
})

function saveLayout() {
  if (!building.value || !layoutName.value.trim()) return
  const layout: SavedLayout = {
    name: layoutName.value.trim(),
    buildingType: building.value.type,
    units: draftUnits.value.map((u) => ({
      unitType: u.unitType,
      gridX: u.gridX,
      gridY: u.gridY,
      linkUp: u.linkUp,
      linkDown: u.linkDown,
      linkLeft: u.linkLeft,
      linkRight: u.linkRight,
      linkUpLeft: u.linkUpLeft,
      linkUpRight: u.linkUpRight,
      linkDownLeft: u.linkDownLeft,
      linkDownRight: u.linkDownRight,
      resourceTypeId: u.resourceTypeId,
      productTypeId: u.productTypeId,
      minPrice: u.minPrice,
      maxPrice: u.maxPrice,
      purchaseSource: u.purchaseSource,
      saleVisibility: u.saleVisibility,
      budget: u.budget,
      mediaHouseBuildingId: u.mediaHouseBuildingId,
      minQuality: u.minQuality,
      brandScope: u.brandScope,
      vendorLockCompanyId: u.vendorLockCompanyId,
    })),
  }
  const layouts = getSavedLayouts()
  layouts.push(layout)
  localStorage.setItem(LAYOUT_STORAGE_KEY, JSON.stringify(layouts))
  layoutName.value = ''
  showLayoutDialog.value = false
}

function loadLayout(layout: SavedLayout) {
  draftUnits.value = layout.units.map((u, i) => ({
    id: `layout-${i}-${Date.now()}`,
    ...u,
    level: 1,
  }))
}

function deleteLayout(index: number) {
  const allLayouts = getSavedLayouts()
  const filtered = allLayouts.filter((l) => l.buildingType === building.value?.type)
  const toDelete = filtered[index]
  if (!toDelete) return
  const globalIndex = allLayouts.indexOf(toDelete)
  if (globalIndex !== -1) {
    allLayouts.splice(globalIndex, 1)
    localStorage.setItem(LAYOUT_STORAGE_KEY, JSON.stringify(allLayouts))
  }
}

// ── Unit config helpers ──

function getResourceName(id: string | null): string {
  if (!id) return '—'
  const resource = resourceTypes.value.find((candidate) => candidate.id === id)
  return resource ? getLocalizedResourceName(resource, locale.value) : id
}

function getProductName(id: string | null): string {
  if (!id) return '—'
  const product = productTypes.value.find((candidate) => candidate.id === id)
  return product ? getLocalizedProductName(product, locale.value) : id
}

function getItemSelection(unit: EditableGridUnit | undefined): ItemSelection {
  if (!unit) return null
  if (unit.productTypeId) {
    return { kind: 'product', id: unit.productTypeId }
  }
  if (unit.resourceTypeId) {
    return { kind: 'resource', id: unit.resourceTypeId }
  }
  return null
}

function setItemSelection(unit: EditableGridUnit | undefined, selection: ItemSelection) {
  if (!unit) return
  unit.resourceTypeId = selection?.kind === 'resource' ? selection.id : null
  unit.productTypeId = selection?.kind === 'product' ? selection.id : null
}

function getFactoryPurchaseSelectableItems(): SelectorItem[] {
  if (building.value?.type !== 'FACTORY') {
    return allSelectableItems.value
  }

  return [
    ...allSelectableItems.value.filter((item) => item.kind === 'resource'),
    ...allSelectableItems.value.filter((item) => item.kind === 'product' && intermediateProductIds.value.has(item.id)),
  ]
}

function getDirectlyConnectedUnits(unit: EditableGridUnit, units: EditableGridUnit[]): EditableGridUnit[] {
  return units.filter((candidate) => {
    if (candidate.id === unit.id) return false

    if (candidate.gridX === unit.gridX && candidate.gridY === unit.gridY - 1) {
      return unit.linkUp || candidate.linkDown
    }
    if (candidate.gridX === unit.gridX && candidate.gridY === unit.gridY + 1) {
      return unit.linkDown || candidate.linkUp
    }
    if (candidate.gridX === unit.gridX - 1 && candidate.gridY === unit.gridY) {
      return unit.linkLeft || candidate.linkRight
    }
    if (candidate.gridX === unit.gridX + 1 && candidate.gridY === unit.gridY) {
      return unit.linkRight || candidate.linkLeft
    }
    if (candidate.gridX === unit.gridX - 1 && candidate.gridY === unit.gridY - 1) {
      return unit.linkUpLeft || candidate.linkDownRight
    }
    if (candidate.gridX === unit.gridX + 1 && candidate.gridY === unit.gridY - 1) {
      return unit.linkUpRight || candidate.linkDownLeft
    }
    if (candidate.gridX === unit.gridX - 1 && candidate.gridY === unit.gridY + 1) {
      return unit.linkDownLeft || candidate.linkUpRight
    }
    if (candidate.gridX === unit.gridX + 1 && candidate.gridY === unit.gridY + 1) {
      return unit.linkDownRight || candidate.linkUpLeft
    }
    return false
  })
}

function getReachableInputSelections(unit: EditableGridUnit | undefined): Set<string> {
  const selected = new Set<string>()
  if (!unit) return selected

  const queue = [unit]
  const visited = new Set<string>()

  while (queue.length > 0) {
    const current = queue.shift()!
    const key = `${current.gridX},${current.gridY}`
    if (visited.has(key)) continue
    visited.add(key)

    if (current.id !== unit.id && ['PURCHASE', 'MINING', 'STORAGE'].includes(current.unitType)) {
      if (current.resourceTypeId) selected.add(`resource:${current.resourceTypeId}`)
      if (current.productTypeId) selected.add(`product:${current.productTypeId}`)
    }

    for (const next of getDirectlyConnectedUnits(current, draftUnits.value)) {
      queue.push(next)
    }
  }

  return selected
}

function getManufacturingSelectableItems(unit: EditableGridUnit | undefined): SelectorItem[] {
  if (!unit) return []
  const reachableInputs = getReachableInputSelections(unit)
  return productTypes.value
    .filter((product) => product.recipes.length > 0)
    .filter((product) => product.recipes.every((recipe) => {
      if (recipe.resourceType?.id) {
        return reachableInputs.has(`resource:${recipe.resourceType.id}`)
      }
      if (recipe.inputProductType?.id) {
        return reachableInputs.has(`product:${recipe.inputProductType.id}`)
      }
      return false
    }))
    .map((product) => ({
      kind: 'product' as const,
      id: product.id,
      name: getLocalizedProductName(product, locale.value),
      description: getLocalizedProductDescription(product, locale.value),
      helperText: isProductLocked(product) ? t('catalog.proDetail') : null,
      groupLabel: t('buildingDetail.selector.availableOutputs'),
      unitSymbol: product.unitSymbol,
      badge: product.isProOnly ? t('catalog.proBadge') : null,
      disabled: isProductLocked(product),
    }))
}

function getProductOptionLabel(product: ProductType) {
  return product.isProOnly ? `${product.name} · ${t('catalog.proBadge')}` : product.name
}

function getBrandScopeLabel(scope: string | null): string {
  if (!scope) return t('buildingDetail.config.none')

  switch (scope) {
    case 'PRODUCT':
      return t('buildingDetail.config.scopeProduct')
    case 'CATEGORY':
      return t('buildingDetail.config.scopeCategory')
    case 'COMPANY':
      return t('buildingDetail.config.scopeCompany')
    default:
      return scope
  }
}

function formatUnitMetric(label: string, value: string): string {
  return `${label}: ${value}`
}

function getUnitConfiguredItemLabel(unit: GridUnit | undefined): string | null {
  if (!unit) return null

  if ('productTypeId' in unit && unit.productTypeId) {
    return getProductName(unit.productTypeId)
  }

  if ('resourceTypeId' in unit && unit.resourceTypeId) {
    return getResourceName(unit.resourceTypeId)
  }

  return null
}

function getUnitPrimaryMetric(unit: GridUnit | undefined): string | null {
  if (!unit) return null

  if ('minPrice' in unit && unit.minPrice != null) {
    return formatUnitMetric(t('buildingDetail.gridMetrics.minPrice'), `$${unit.minPrice}`)
  }

  if ('maxPrice' in unit && unit.maxPrice != null) {
    return formatUnitMetric(t('buildingDetail.gridMetrics.maxPrice'), `$${unit.maxPrice}`)
  }

  if ('budget' in unit && unit.budget != null) {
    return formatUnitMetric(t('buildingDetail.gridMetrics.budget'), `$${unit.budget}`)
  }

  if ('brandScope' in unit && unit.brandScope) {
    return formatUnitMetric(t('buildingDetail.gridMetrics.scope'), getBrandScopeLabel(unit.brandScope))
  }

  return null
}

function getUnitInventorySummary(unit: GridUnit | undefined): BuildingUnitInventorySummary | undefined {
  if (!unit) return undefined
  return unitInventorySummaries.value.find((summary) => summary.buildingUnitId === unit.id)
}

function formatPercent(value: number | null | undefined): string {
  if (value == null) return '—'
  return `${Math.round(value * 100)}%`
}

function formatUnitQuantity(value: number): string {
  if (Number.isInteger(value)) return `${value}`
  return value.toFixed(2).replace(/\.?0+$/, '')
}

function getPurchaseUnitResourceTypeId(unit: GridUnit | undefined): string | null {
  return unit && 'resourceTypeId' in unit ? unit.resourceTypeId : null
}

function getPurchaseUnitSource(unit: GridUnit | undefined): string | null {
  return unit && 'purchaseSource' in unit ? unit.purchaseSource : null
}

function updateSelectedUnitConfig(field: string, value: unknown) {
  if (!selectedCell.value || !isEditing.value) return
  const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
  if (!unit) return
  const sanitized = typeof value === 'number' && isNaN(value) ? null : value
  ;(unit as Record<string, unknown>)[field] = sanitized
}

async function loadUnitInventorySummaries() {
  if (!auth.token) {
    unitInventorySummaries.value = []
    return
  }

  try {
    const data = await gqlRequest<{ buildingUnitInventorySummaries: BuildingUnitInventorySummary[] }>(
      `query BuildingUnitInventorySummaries($buildingId: UUID!) {
        buildingUnitInventorySummaries(buildingId: $buildingId) {
          buildingUnitId
          quantity
          capacity
          fillPercent
          averageQuality
        }
      }`,
      { buildingId: buildingId.value },
    )
    unitInventorySummaries.value = data.buildingUnitInventorySummaries
  } catch {
    unitInventorySummaries.value = []
  }
}

async function loadGlobalExchangeOffers() {
  const unit = selectedPurchaseUnit.value
  const resourceTypeId = getPurchaseUnitResourceTypeId(unit)
  const purchaseSource = getPurchaseUnitSource(unit)

  if (!building.value?.cityId || !resourceTypeId || !['EXCHANGE', 'OPTIMAL'].includes(purchaseSource ?? '')) {
    exchangeOffers.value = []
    exchangeOffersLoading.value = false
    return
  }

  exchangeOffersLoading.value = true
  try {
    const data = await gqlRequest<{ globalExchangeOffers: GlobalExchangeOffer[] }>(
      `query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
        globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
          cityId
          cityName
          resourceTypeId
          resourceName
          resourceSlug
          unitSymbol
          localAbundance
          exchangePricePerUnit
          estimatedQuality
          transitCostPerUnit
          deliveredPricePerUnit
          distanceKm
        }
      }`,
      {
        destinationCityId: building.value.cityId,
        resourceTypeId,
      },
    )
    exchangeOffers.value = data.globalExchangeOffers
  } catch {
    exchangeOffers.value = []
  } finally {
    exchangeOffersLoading.value = false
  }
}

async function loadBuilding() {
  try {
    loading.value = true
    error.value = null

    const [companiesData, gameStateData, resourceData, productData] = await Promise.all([
      gqlRequest<{ myCompanies: { buildings: Building[] }[] }>(
        `{ myCompanies {
          buildings {
            id
            companyId
            cityId
            type
            name
            latitude
            longitude
            level
            powerConsumption
            isForSale
            askingPrice
            pricePerSqm
            occupancyPercent
            totalAreaSqm
            powerPlantType
            powerOutput
            mediaType
            interestRate
            builtAtUtc
            units {
              id
              buildingId
              unitType
              gridX
              gridY
              level
              linkUp
              linkDown
              linkLeft
              linkRight
              linkUpLeft
              linkUpRight
              linkDownLeft
              linkDownRight
              resourceTypeId
              productTypeId
              minPrice
              maxPrice
              purchaseSource
              saleVisibility
              budget
              mediaHouseBuildingId
              minQuality
              brandScope
              vendorLockCompanyId
            }
            pendingConfiguration {
              id
              buildingId
              submittedAtUtc
              submittedAtTick
              appliesAtTick
              totalTicksRequired
              removals {
                id
                gridX
                gridY
                startedAtTick
                appliesAtTick
                ticksRequired
                isReverting
              }
              units {
                id
                unitType
                gridX
                gridY
                level
                linkUp
                linkDown
                linkLeft
                linkRight
                linkUpLeft
                linkUpRight
                linkDownLeft
                linkDownRight
                startedAtTick
                appliesAtTick
                ticksRequired
                isChanged
                isReverting
                resourceTypeId
                productTypeId
                minPrice
                maxPrice
                purchaseSource
                saleVisibility
                budget
                mediaHouseBuildingId
                minQuality
                brandScope
                vendorLockCompanyId
              }
            }
          }
        } }`,
      ),
      gqlRequest<{ gameState: { currentTick: number } | null }>(`{ gameState { currentTick } }`),
      gqlRequest<{ resourceTypes: ResourceType[] }>(`{
        resourceTypes {
          id
          name
          slug
          category
          basePrice
          weightPerUnit
          unitName
          unitSymbol
          imageUrl
          description
        }
      }`),
      gqlRequest<{ productTypes: ProductType[] }>(`{
        productTypes {
          id
          name
          slug
          industry
          basePrice
          baseCraftTicks
          outputQuantity
          energyConsumptionMwh
          unitName
          unitSymbol
          isProOnly
          isUnlockedForCurrentPlayer
          description
          recipes {
            quantity
            resourceType { id name slug unitName unitSymbol }
            inputProductType { id name slug unitName unitSymbol }
          }
        }
      }`),
    ])

    currentTick.value = gameStateData.gameState?.currentTick ?? 0
    resourceTypes.value = resourceData.resourceTypes ?? []
    productTypes.value = productData.productTypes ?? []

    const allBuildings = companiesData.myCompanies.flatMap((company) => company.buildings)
    building.value = allBuildings.find((candidate) => candidate.id === buildingId.value) || null

    if (!building.value) {
      error.value = t('buildingDetail.notFound')
      return
    }

    const sourceUnits = pendingConfiguration.value?.units ?? building.value.units
    setDraftUnitsFrom(sourceUnits)
    setEditBaselineFrom(sourceUnits)
    isEditing.value = false
    selectedCell.value = null
    showUnitPicker.value = false
    await loadUnitInventorySummaries()
    await loadGlobalExchangeOffers()
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('buildingDetail.loadFailed')
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  await loadBuilding()
})

watch(
  () => [
    building.value?.cityId ?? null,
    selectedCell.value?.x ?? null,
    selectedCell.value?.y ?? null,
    getPurchaseUnitResourceTypeId(selectedPurchaseUnit.value),
    getPurchaseUnitSource(selectedPurchaseUnit.value),
    isEditing.value,
  ],
  () => {
    void loadGlobalExchangeOffers()
  },
)
</script>

<template>
  <div class="building-detail-view container">
    <div class="page-nav">
      <RouterLink to="/dashboard" class="back-link">
        <span>←</span> {{ t('buildingDetail.backToDashboard') }}
      </RouterLink>
    </div>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>

    <div v-else-if="error" class="error-message" role="alert">
      {{ error }}
      <button class="btn btn-secondary" @click="router.push('/dashboard')">{{ t('buildingDetail.backToDashboard') }}</button>
    </div>

    <template v-else-if="building">
      <div class="building-header">
        <div class="building-title">
          <h1>{{ building.name }}</h1>
          <span class="building-type-badge">{{ formatBuildingType(building.type) }}</span>
        </div>
        <div class="building-meta">
          <span class="meta-pill">
            <span class="meta-label">{{ t('common.level') }}</span>
            <span class="meta-value">{{ building.level }}</span>
          </span>
          <span class="meta-pill">
            <span class="meta-label">{{ t('buildings.power') }}</span>
            <span class="meta-value">{{ building.powerConsumption }} {{ t('buildings.powerUnit') }}</span>
          </span>
          <span class="meta-pill" :class="building.isForSale ? 'for-sale' : ''">
            {{ building.isForSale ? t('buildingDetail.forSale') : t('buildingDetail.notForSale') }}
          </span>
          <button class="btn btn-secondary btn-sm" @click="openSaleDialog">
            {{ building.isForSale ? t('buildingDetail.editSale') : t('buildingDetail.sellBuilding') }}
          </button>
        </div>
      </div>

      <!-- Sale dialog -->
      <div v-if="showSaleDialog" class="sale-dialog">
        <div class="sale-dialog-header">
          <h3>{{ t('buildingDetail.sellBuilding') }}</h3>
          <button class="btn btn-ghost" @click="closeSaleDialog">{{ t('common.close') }}</button>
        </div>
        <div class="sale-dialog-body">
          <label class="form-label">{{ t('buildingDetail.askingPrice') }}</label>
          <input
            type="number"
            class="form-input"
            :placeholder="t('buildingDetail.askingPricePlaceholder')"
            :value="salePrice"
            @input="salePrice = isNaN(($event.target as HTMLInputElement).valueAsNumber) ? null : ($event.target as HTMLInputElement).valueAsNumber"
            min="0"
            step="1000"
          />
          <div class="sale-dialog-actions">
            <button
              class="btn btn-primary"
              :disabled="savingSale || !salePrice || salePrice <= 0"
              @click="setBuildingForSale(true)"
            >
              {{ t('buildingDetail.listForSale') }}
            </button>
            <button
              v-if="building.isForSale"
              class="btn btn-danger"
              :disabled="savingSale"
              @click="setBuildingForSale(false)"
            >
              {{ t('buildingDetail.cancelSale') }}
            </button>
          </div>
        </div>
      </div>

      <!-- Configuration warnings -->
      <div v-if="configWarnings.length > 0" class="config-warnings" role="alert">
        <strong>{{ t('buildingDetail.warnings.title') }}</strong>
        <ul>
          <li v-for="(warning, i) in configWarnings" :key="i">
            {{ t(warning.key, warning.params || {}) }}
          </li>
        </ul>
      </div>

      <div v-if="isUpgradeInProgress" class="upgrade-banner" role="status">
        <div>
          <strong>{{ t('buildingDetail.upgradeQueuedTitle') }}</strong>
          <p>{{ t('buildingDetail.upgradeQueuedBody', { ticks: remainingUpgradeTicks }) }}</p>
        </div>
        <div class="upgrade-pill">
          {{ t('buildingDetail.upgradeAppliesAt', { tick: pendingConfiguration!.appliesAtTick }) }}
        </div>
      </div>

      <div v-if="lockedConfiguredProducts.length > 0" class="pro-access-banner" role="status">
        <strong>{{ t('catalog.proLockedTitle') }}</strong>
        <p>
          {{
            t('buildingDetail.proAccessGrandfathered', {
              products: lockedConfiguredProductNames,
            })
          }}
        </p>
      </div>

      <div class="main-content">
        <div class="grid-container">
          <div v-if="!isEditing" class="grid-section">
            <div class="grid-header">
              <div>
                <h2>{{ t('buildingDetail.activeConfiguration') }}</h2>
                <p class="section-subtitle">{{ t('buildingDetail.activeConfigurationHelp') }}</p>
              </div>
              <button v-if="!isEditing" class="btn btn-secondary" @click="startEditing">
                {{ t('buildingDetail.editConfiguration') }}
              </button>
            </div>

            <div class="unit-grid readonly-grid">
              <template v-for="y in gridIndexes" :key="`active-row-${y}`">
                <div class="grid-row unit-row">
                  <template v-for="x in gridIndexes" :key="`active-unit-${x}-${y}`">
                    <div
                      class="grid-cell readonly clickable"
                      :class="{ occupied: !!getUnitAtFrom(activeUnits, x, y), selected: selectedCell?.x === x && selectedCell?.y === y }"
                      :style="getUnitAtFrom(activeUnits, x, y)
                        ? { borderColor: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType) + '18' }
                        : {}"
                      @click="selectedCell = getUnitAtFrom(activeUnits, x, y) ? { x, y } : null"
                    >
                      <template v-if="getUnitAtFrom(activeUnits, x, y)">
                        <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(activeUnits, x, y)!.unitType}`) }}</span>
                        <span v-if="getUnitConfiguredItemLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-item">
                          {{ getUnitConfiguredItemLabel(getUnitAtFrom(activeUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y))" class="cell-metric">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y)) }}
                        </span>
                        <span class="cell-level">Lv.{{ getUnitAtFrom(activeUnits, x, y)!.level }}</span>
                        <div
                          v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))?.capacity"
                          class="cell-capacity"
                          :aria-label="t('buildingDetail.capacityMeterLabel', {
                            quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.quantity),
                            capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.capacity),
                          })"
                        >
                          <span
                            class="cell-capacity-fill"
                            :style="{ width: `${Math.round((getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.fillPercent ?? 0) * 100)}%` }"
                          ></span>
                        </div>
                      </template>
                      <template v-else>
                        <span class="cell-empty">+</span>
                      </template>
                    </div>

                    <div
                      v-if="x < 3"
                      :key="`active-horizontal-${x}-${y}`"
                      class="link-toggle horizontal readonly"
                      :class="{ active: isHorizontalLinkActiveFor(activeUnits, x, y), disabled: !canToggleHorizontalLink(activeUnits, x, y) }"
                    >
                      <span class="link-line"></span>
                    </div>
                  </template>
                </div>

                <div v-if="y < 3" class="grid-row connector-row">
                  <template v-for="x in gridIndexes" :key="`active-connector-${x}-${y}`">
                    <div
                      class="link-toggle vertical readonly"
                      :class="{ active: isVerticalLinkActiveFor(activeUnits, x, y), disabled: !canToggleVerticalLink(activeUnits, x, y) }"
                    >
                      <span class="link-line"></span>
                    </div>

                    <div
                      v-if="x < 3"
                      :key="`active-diagonal-${x}-${y}`"
                      class="link-toggle diagonal readonly"
                      :class="[`state-${getDiagonalStateFor(activeUnits, x, y)}`, { disabled: !canToggleDiagonalLink(activeUnits, x, y) }]"
                    >
                      <span class="diag-line diag-line-primary"></span>
                      <span class="diag-line diag-line-secondary"></span>
                    </div>
                  </template>
                </div>
              </template>
            </div>
          </div>

          <div v-if="showPlanningSection" class="grid-section">
            <div class="grid-header plan-header">
              <div>
                <h2>{{ isUpgradeInProgress ? t('buildingDetail.queuedConfiguration') : t('buildingDetail.plannedConfiguration') }}</h2>
                <p class="section-subtitle">
                  {{ isUpgradeInProgress ? t('buildingDetail.queuedConfigurationHelp') : t('buildingDetail.plannedConfigurationHelp') }}
                </p>
              </div>
              <div class="grid-actions">
                <button class="btn btn-secondary" @click="cancelEditing">
                  {{ t('buildingDetail.cancelEditing') }}
                </button>
                <button
                  class="btn btn-primary"
                  :disabled="saving || !hasDraftChanges"
                  @click="storeConfiguration"
                >
                  {{ saving ? t('common.loading') : t('buildingDetail.storeConfiguration') }}
                </button>
              </div>
            </div>

            <div class="upgrade-summary">
              <span class="upgrade-summary-pill">{{ t('buildingDetail.currentTickLabel', { tick: currentTick }) }}</span>
              <span class="upgrade-summary-pill">{{ t('buildingDetail.totalUpgradeTicks', { ticks: draftTotalTicks }) }}</span>
            </div>

            <div class="unit-grid">
              <template v-for="y in gridIndexes" :key="`planned-row-${y}`">
                <div class="grid-row unit-row">
                  <template v-for="x in gridIndexes" :key="`planned-unit-${x}-${y}`">
                    <button
                      class="grid-cell"
                      :class="{
                        occupied: !!getUnitAtFrom(plannedUnits, x, y),
                        selected: selectedCell?.x === x && selectedCell?.y === y,
                        changed: !!getUnitAtFrom(plannedUnits, x, y) && getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) > 0,
                      }"
                      :style="getUnitAtFrom(plannedUnits, x, y)
                        ? { borderColor: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType) + '18' }
                        : {}"
                      @click="clickDraftCell(x, y)"
                    >
                      <template v-if="getUnitAtFrom(plannedUnits, x, y)">
                        <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(plannedUnits, x, y)!.unitType}`) }}</span>
                        <span v-if="getUnitConfiguredItemLabel(getUnitAtFrom(plannedUnits, x, y))" class="cell-item">
                          {{ getUnitConfiguredItemLabel(getUnitAtFrom(plannedUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y))" class="cell-metric">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y)) }}
                        </span>
                        <span class="cell-level">Lv.{{ getUnitAtFrom(plannedUnits, x, y)!.level }}</span>
                        <div
                          v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))?.capacity"
                          class="cell-capacity"
                          :aria-label="t('buildingDetail.capacityMeterLabel', {
                            quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.quantity),
                            capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.capacity),
                          })"
                        >
                          <span
                            class="cell-capacity-fill"
                            :style="{ width: `${Math.round((getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.fillPercent ?? 0) * 100)}%` }"
                          ></span>
                        </div>
                        <span v-if="getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) > 0" class="cell-pending">
                          {{ t('buildingDetail.unitUnavailableFor', { ticks: getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) }) }}
                        </span>
                      </template>
                      <template v-else>
                        <span class="cell-empty">+</span>
                      </template>
                    </button>

                    <button
                      v-if="x < 3"
                      :key="`planned-horizontal-${x}-${y}`"
                      class="link-toggle horizontal"
                      :class="{ active: isHorizontalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleHorizontalLink(plannedUnits, x, y) }"
                      :disabled="!canToggleHorizontalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkRight')"
                      @click="toggleHorizontalLink(x, y)"
                    >
                      <span class="link-line"></span>
                    </button>
                  </template>
                </div>

                <div v-if="y < 3" class="grid-row connector-row">
                  <template v-for="x in gridIndexes" :key="`planned-connector-${x}-${y}`">
                    <button
                      class="link-toggle vertical"
                      :class="{ active: isVerticalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleVerticalLink(plannedUnits, x, y) }"
                      :disabled="!canToggleVerticalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkDown')"
                      @click="toggleVerticalLink(x, y)"
                    >
                      <span class="link-line"></span>
                    </button>

                    <button
                      v-if="x < 3"
                      :key="`planned-diagonal-${x}-${y}`"
                      class="link-toggle diagonal"
                      :class="[`state-${getDiagonalStateFor(plannedUnits, x, y)}`, { disabled: !canToggleDiagonalLink(plannedUnits, x, y) }]"
                      :disabled="!canToggleDiagonalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.links')"
                      @click="toggleDiagonalLink(x, y)"
                    >
                      <span class="diag-line diag-line-primary"></span>
                      <span class="diag-line diag-line-secondary"></span>
                    </button>
                  </template>
                </div>
              </template>
            </div>

            <div class="grid-legend">
              <div v-for="unitType in allowedUnits" :key="unitType" class="legend-item">
                <span class="legend-color" :style="{ background: getUnitColor(unitType) }"></span>
                <span>{{ t(`buildingDetail.unitTypes.${unitType}`) }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="sidebar" v-if="selectedCell && isEditing">
          <div v-if="showUnitPicker" class="unit-picker">
            <div class="picker-header">
              <h3>{{ t('buildingDetail.selectUnitType') }}</h3>
              <button class="btn btn-ghost" @click="showUnitPicker = false">{{ t('common.close') }}</button>
            </div>
            <p class="picker-subtitle">{{ t('buildingDetail.allowedUnits') }}</p>
            <div class="picker-grid">
              <button v-for="unitType in allowedUnits" :key="unitType" class="picker-option" @click="placeUnit(unitType)">
                <span class="picker-color" :style="{ background: getUnitColor(unitType) }"></span>
                <div class="picker-info">
                  <span class="picker-name">{{ t(`buildingDetail.unitTypes.${unitType}`) }}</span>
                  <span class="picker-desc">{{ t(`buildingDetail.unitDescriptions.${unitType}`) }}</span>
                </div>
              </button>
            </div>
          </div>

          <div v-if="!showUnitPicker && getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)" class="unit-config">
            <div class="unit-config-header">
              <h3>{{ t('buildingDetail.unitConfiguration') }}</h3>
              <button class="btn btn-ghost" @click="selectedCell = null">{{ t('common.close') }}</button>
            </div>
            <div class="unit-detail">
              <h4>{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</h4>
              <p class="unit-desc">{{ t(`buildingDetail.unitDescriptions.${getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</p>
              <div class="unit-stats">
                <span class="stat">{{ t('common.level') }}: {{ getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.level }}</span>
                <span class="stat">{{ t('buildingDetail.gridPosition', { x: selectedCell.x, y: selectedCell.y }) }}</span>
                <span class="stat" v-if="getDisplayedTicks(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!) > 0">
                  {{ t('buildingDetail.unitUnavailableFor', { ticks: getDisplayedTicks(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!) }) }}
                </span>
              </div>
              <div class="unit-links">
                <span class="link-label">{{ t('buildingDetail.links') }}:</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkUp" class="link-badge">{{ t('buildingDetail.linkUp') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkDown" class="link-badge">{{ t('buildingDetail.linkDown') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkLeft" class="link-badge">{{ t('buildingDetail.linkLeft') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkRight" class="link-badge">{{ t('buildingDetail.linkRight') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkUpLeft" class="link-badge">{{ t('buildingDetail.linkUpLeft') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkUpRight" class="link-badge">{{ t('buildingDetail.linkUpRight') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkDownLeft" class="link-badge">{{ t('buildingDetail.linkDownLeft') }}</span>
                <span v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.linkDownRight" class="link-badge">{{ t('buildingDetail.linkDownRight') }}</span>
              </div>

              <!-- Unit-specific configuration -->
              <div class="unit-config-fields" v-if="isEditing && getDraftUnitAt(selectedCell.x, selectedCell.y)">
                <h5>{{ t('buildingDetail.unitSettings') }}</h5>

                <!-- Purchase unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'PURCHASE'">
                  <div class="config-field">
                    <AdvancedItemSelector
                      :model-value="getItemSelection(getDraftUnitAt(selectedCell.x, selectedCell.y))"
                      :items="building?.type === 'FACTORY' ? getFactoryPurchaseSelectableItems() : allSelectableItems"
                      :label="t('buildingDetail.config.inputItem')"
                      :placeholder="t('buildingDetail.selector.searchPlaceholder')"
                      :empty-text="t('buildingDetail.selector.noItems')"
                      @update:model-value="setItemSelection(getDraftUnitAt(selectedCell.x, selectedCell.y), $event)"
                    />
                  </div>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.maxPrice') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.maxPrice" @input="updateSelectedUnitConfig('maxPrice', ($event.target as HTMLInputElement).valueAsNumber || null)" min="0" step="0.01" />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minQuality') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minQuality" @input="updateSelectedUnitConfig('minQuality', ($event.target as HTMLInputElement).valueAsNumber || null)" min="0" max="1" step="0.01" />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.purchaseSource') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.purchaseSource ?? ''" @change="updateSelectedUnitConfig('purchaseSource', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option value="EXCHANGE">{{ t('buildingDetail.config.sourceExchange') }}</option>
                      <option value="LOCAL">{{ t('buildingDetail.config.sourceLocal') }}</option>
                      <option value="OPTIMAL">{{ t('buildingDetail.config.sourceOptimal') }}</option>
                    </select>
                  </div>
                </template>

                <!-- Manufacturing unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MANUFACTURING'">
                  <div class="config-field">
                    <AdvancedItemSelector
                      :model-value="getItemSelection(getDraftUnitAt(selectedCell.x, selectedCell.y))"
                      :items="getManufacturingSelectableItems(getDraftUnitAt(selectedCell.x, selectedCell.y))"
                      :label="t('buildingDetail.config.outputProduct')"
                      :placeholder="t('buildingDetail.selector.searchPlaceholder')"
                      :empty-text="t('buildingDetail.selector.noLinkedOutputs')"
                      @update:model-value="setItemSelection(getDraftUnitAt(selectedCell.x, selectedCell.y), $event)"
                    />
                  </div>
                  <p class="config-help">
                    {{ t('buildingDetail.config.outputProductHelp') }}
                  </p>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                </template>

                <!-- B2B Sales unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'B2B_SALES'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minPrice') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice" @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).valueAsNumber || null)" min="0" step="0.01" />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.saleVisibility') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.saleVisibility ?? ''" @change="updateSelectedUnitConfig('saleVisibility', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option value="PUBLIC">{{ t('buildingDetail.config.visibilityPublic') }}</option>
                      <option value="COMPANY">{{ t('buildingDetail.config.visibilityCompany') }}</option>
                      <option value="GROUP">{{ t('buildingDetail.config.visibilityGroup') }}</option>
                    </select>
                  </div>
                </template>

                <!-- Public Sales unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'PUBLIC_SALES'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.productType') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? ''" @change="updateSelectedUnitConfig('productTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option
                        v-for="pt in productTypes"
                        :key="pt.id"
                        :value="pt.id"
                        :disabled="isProductLocked(pt)"
                      >
                        {{ getProductOptionLabel(pt) }}
                      </option>
                    </select>
                  </div>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minPrice') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice" @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).valueAsNumber || null)" min="0" step="0.01" />
                  </div>
                </template>

                <!-- Marketing unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MARKETING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.budget') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.budget" @input="updateSelectedUnitConfig('budget', ($event.target as HTMLInputElement).valueAsNumber || null)" min="0" step="100" />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.mediaHouse') }}</label>
                    <input type="text" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.mediaHouseBuildingId ?? ''" @input="updateSelectedUnitConfig('mediaHouseBuildingId', ($event.target as HTMLInputElement).value || null)" :placeholder="t('buildingDetail.config.mediaHousePlaceholder')" />
                  </div>
                </template>

                <!-- Branding unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'BRANDING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.brandScope') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? ''" @change="updateSelectedUnitConfig('brandScope', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option value="PRODUCT">{{ t('buildingDetail.config.scopeProduct') }}</option>
                      <option value="CATEGORY">{{ t('buildingDetail.config.scopeCategory') }}</option>
                      <option value="COMPANY">{{ t('buildingDetail.config.scopeCompany') }}</option>
                    </select>
                  </div>
                </template>

                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'PRODUCT_QUALITY'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.researchProduct') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? ''" @change="updateSelectedUnitConfig('productTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option
                        v-for="pt in productTypes"
                        :key="pt.id"
                        :value="pt.id"
                        :disabled="isProductLocked(pt)"
                      >
                        {{ getProductOptionLabel(pt) }}
                      </option>
                    </select>
                  </div>
                  <p class="config-help">{{ t('buildingDetail.config.researchProductHelp') }}</p>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                </template>

                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'BRAND_QUALITY'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.brandScope') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? ''" @change="updateSelectedUnitConfig('brandScope', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option value="PRODUCT">{{ t('buildingDetail.config.scopeProduct') }}</option>
                      <option value="CATEGORY">{{ t('buildingDetail.config.scopeCategory') }}</option>
                      <option value="COMPANY">{{ t('buildingDetail.config.scopeCompany') }}</option>
                    </select>
                  </div>
                  <div v-if="['PRODUCT', 'CATEGORY'].includes(getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? '')" class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.researchAnchorProduct') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? ''" @change="updateSelectedUnitConfig('productTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option
                        v-for="pt in productTypes"
                        :key="pt.id"
                        :value="pt.id"
                        :disabled="isProductLocked(pt)"
                      >
                        {{ getProductOptionLabel(pt) }}
                      </option>
                    </select>
                  </div>
                  <p class="config-help">{{ t('buildingDetail.config.researchBrandHelp') }}</p>
                  <p class="config-help" v-if="['PRODUCT', 'CATEGORY'].includes(getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? '')">
                    {{ t('buildingDetail.config.researchAnchorProductHelp') }}
                  </p>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                </template>

                <!-- Storage unit config (read-only info) -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'STORAGE'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.resourceType') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.resourceTypeId ?? ''" @change="updateSelectedUnitConfig('resourceTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.anyResource') }}</option>
                      <option v-for="rt in resourceTypes" :key="rt.id" :value="rt.id">{{ rt.name }}</option>
                    </select>
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.productType') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? ''" @change="updateSelectedUnitConfig('productTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.anyProduct') }}</option>
                      <option
                        v-for="pt in productTypes"
                        :key="pt.id"
                        :value="pt.id"
                        :disabled="isProductLocked(pt)"
                      >
                        {{ getProductOptionLabel(pt) }}
                      </option>
                    </select>
                  </div>
                </template>

                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MINING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.outputResource') }}</label>
                    <select class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.resourceTypeId ?? ''" @change="updateSelectedUnitConfig('resourceTypeId', ($event.target as HTMLSelectElement).value || null)">
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option v-for="rt in resourceTypes" :key="rt.id" :value="rt.id">{{ rt.name }} ({{ rt.unitSymbol }})</option>
                    </select>
                  </div>
                </template>
              </div>

              <!-- Read-only unit details for non-editing mode -->
              <div class="unit-config-readonly" v-if="!isEditing && getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)">
                <template v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType === 'PURCHASE' && ('resourceTypeId' in getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!)">
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).resourceTypeId">{{ t('buildingDetail.config.resourceType') }}: {{ getResourceName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).resourceTypeId) }}</span>
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId">{{ t('buildingDetail.config.productType') }}: {{ getProductName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId) }}</span>
                </template>
                <template v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType === 'PRODUCT_QUALITY'">
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId">
                    {{ t('buildingDetail.config.researchProduct') }}: {{ getProductName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId) }}
                  </span>
                </template>
                <template v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType === 'BRAND_QUALITY'">
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).brandScope">
                    {{ t('buildingDetail.config.brandScope') }}: {{ getBrandScopeLabel((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).brandScope) }}
                  </span>
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId">
                    {{ t('buildingDetail.config.researchAnchorProduct') }}: {{ getProductName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId) }}
                  </span>
                </template>
              </div>

              <div v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))" class="unit-insight-card">
                <h5>{{ t('buildingDetail.inventory.title') }}</h5>
                <div class="unit-stats">
                  <span class="stat">
                    {{
                      t('buildingDetail.inventory.quantity', {
                        quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.quantity),
                        capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.capacity),
                      })
                    }}
                  </span>
                  <span class="stat" v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.averageQuality != null">
                    {{ t('buildingDetail.inventory.averageQuality') }}: {{ formatPercent(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.averageQuality) }}
                  </span>
                </div>
                <div class="detail-capacity">
                  <span
                    class="detail-capacity-fill"
                    :style="{ width: `${Math.round(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.fillPercent * 100)}%` }"
                  ></span>
                </div>
              </div>

              <div
                v-if="selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')"
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <ul v-else class="exchange-offers-list">
                  <li v-for="offer in exchangeOffers" :key="`${offer.cityId}-${offer.resourceTypeId}`" class="exchange-offer-item">
                    <div class="exchange-offer-header">
                      <strong>{{ offer.cityName }}</strong>
                      <span>{{ t('buildingDetail.exchange.quality', { quality: formatPercent(offer.estimatedQuality) }) }}</span>
                    </div>
                    <div class="exchange-offer-metrics">
                      <span>{{ t('buildingDetail.exchange.exchangePrice', { price: offer.exchangePricePerUnit, unit: offer.unitSymbol }) }}</span>
                      <span>{{ t('buildingDetail.exchange.transit', { price: offer.transitCostPerUnit, distance: offer.distanceKm }) }}</span>
                      <span>{{ t('buildingDetail.exchange.deliveredPrice', { price: offer.deliveredPricePerUnit, unit: offer.unitSymbol }) }}</span>
                    </div>
                  </li>
                </ul>
              </div>

              <div class="unit-actions" v-if="isEditing">
                <button class="btn btn-danger btn-sm" @click="removeDraftUnit(selectedCell.x, selectedCell.y)">
                  {{ t('buildingDetail.removeUnit') }}
                </button>
              </div>
            </div>
          </div>

          <!-- Layout save/load -->
          <div v-if="isEditing" class="layout-section">
            <div class="layout-header">
              <h4>{{ t('buildingDetail.layouts.title') }}</h4>
            </div>
            <div class="layout-save">
              <input
                type="text"
                class="form-input"
                v-model="layoutName"
                :placeholder="t('buildingDetail.layouts.namePlaceholder')"
              />
              <button class="btn btn-secondary btn-sm" :disabled="!layoutName.trim()" @click="saveLayout">
                {{ t('buildingDetail.layouts.save') }}
              </button>
            </div>
            <div v-if="savedLayouts.length > 0" class="layout-list">
              <div v-for="(layout, i) in savedLayouts" :key="i" class="layout-item">
                <span class="layout-name">{{ layout.name }} ({{ layout.units.length }} {{ t('buildingDetail.layouts.units') }})</span>
                <div class="layout-item-actions">
                  <button class="btn btn-ghost btn-sm" @click="loadLayout(layout)">{{ t('buildingDetail.layouts.load') }}</button>
                  <button class="btn btn-ghost btn-sm" @click="deleteLayout(i)">{{ t('buildingDetail.layouts.delete') }}</button>
                </div>
              </div>
            </div>
            <p v-else class="layout-empty">{{ t('buildingDetail.layouts.empty') }}</p>
          </div>
        </div>

        <!-- Read-only unit detail sidebar (click on active grid) -->
        <div class="sidebar" v-else-if="selectedCell && !isEditing && getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)">
          <div class="unit-config">
            <div class="unit-config-header">
              <h3>{{ t('buildingDetail.unitDetails') }}</h3>
              <button class="btn btn-ghost" @click="selectedCell = null">{{ t('common.close') }}</button>
            </div>
            <div class="unit-detail">
              <h4>{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</h4>
              <p class="unit-desc">{{ t(`buildingDetail.unitDescriptions.${getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</p>
              <div class="unit-stats">
                <span class="stat">{{ t('common.level') }}: {{ getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)!.level }}</span>
                <span class="stat">{{ t('buildingDetail.gridPosition', { x: selectedCell.x, y: selectedCell.y }) }}</span>
              </div>
              <div class="unit-config-readonly-details">
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).resourceTypeId">
                  {{ t('buildingDetail.config.resourceType') }}: {{ getResourceName((getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).resourceTypeId) }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).productTypeId">
                  {{ t('buildingDetail.config.productType') }}: {{ getProductName((getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).productTypeId) }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).minPrice != null">
                  {{ t('buildingDetail.config.minPrice') }}: ${{ (getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).minPrice }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).maxPrice != null">
                  {{ t('buildingDetail.config.maxPrice') }}: ${{ (getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).maxPrice }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).purchaseSource">
                  {{ t('buildingDetail.config.purchaseSource') }}: {{ (getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).purchaseSource }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).saleVisibility">
                  {{ t('buildingDetail.config.saleVisibility') }}: {{ (getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).saleVisibility }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).budget != null">
                  {{ t('buildingDetail.config.budget') }}: ${{ (getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).budget }}
                </span>
                <span class="stat" v-if="(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).brandScope">
                  {{ t('buildingDetail.config.brandScope') }}: {{ getBrandScopeLabel((getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).brandScope) }}
                </span>
              </div>
              <div v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))" class="unit-insight-card">
                <h5>{{ t('buildingDetail.inventory.title') }}</h5>
                <div class="unit-stats">
                  <span class="stat">
                    {{
                      t('buildingDetail.inventory.quantity', {
                        quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.quantity),
                        capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.capacity),
                      })
                    }}
                  </span>
                  <span class="stat" v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.averageQuality != null">
                    {{ t('buildingDetail.inventory.averageQuality') }}: {{ formatPercent(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.averageQuality) }}
                  </span>
                </div>
                <div class="detail-capacity">
                  <span
                    class="detail-capacity-fill"
                    :style="{ width: `${Math.round(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.fillPercent * 100)}%` }"
                  ></span>
                </div>
              </div>
              <div
                v-if="selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')"
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <ul v-else class="exchange-offers-list">
                  <li v-for="offer in exchangeOffers" :key="`${offer.cityId}-${offer.resourceTypeId}`" class="exchange-offer-item">
                    <div class="exchange-offer-header">
                      <strong>{{ offer.cityName }}</strong>
                      <span>{{ t('buildingDetail.exchange.quality', { quality: formatPercent(offer.estimatedQuality) }) }}</span>
                    </div>
                    <div class="exchange-offer-metrics">
                      <span>{{ t('buildingDetail.exchange.exchangePrice', { price: offer.exchangePricePerUnit, unit: offer.unitSymbol }) }}</span>
                      <span>{{ t('buildingDetail.exchange.transit', { price: offer.transitCostPerUnit, distance: offer.distanceKm }) }}</span>
                      <span>{{ t('buildingDetail.exchange.deliveredPrice', { price: offer.deliveredPricePerUnit, unit: offer.unitSymbol }) }}</span>
                    </div>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.building-detail-view {
  padding: 2rem 1rem;
  max-width: 1400px;
}

.page-nav {
  margin-bottom: 1.5rem;
}

.main-content {
  display: grid;
  grid-template-columns: 2fr 320px;
  gap: 2rem;
  align-items: start;
}

.grid-container {
  min-width: 0; /* Allow shrinking */
}

.sidebar {
  position: sticky;
  top: 2rem;
  min-width: 0; /* Allow shrinking */
}

@media (max-width: 1024px) {
  .main-content {
    grid-template-columns: 1fr;
    gap: 1.5rem;
  }

  .sidebar {
    position: static;
    order: -1; /* Show sidebar above grid on mobile */
  }
}

@media (max-width: 768px) {
  .building-detail-view {
    padding: 1rem 0.5rem;
  }

  .main-content {
    gap: 1rem;
  }
}

.page-nav {
  margin-bottom: 1.5rem;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  text-decoration: none;
}

.back-link:hover {
  color: var(--color-primary);
  text-decoration: none;
}

.unit-detail {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}

.unit-config {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  overflow: hidden;
}

.unit-config-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  padding: 1rem 1.5rem;
  background: var(--color-bg);
  border-bottom: 1px solid var(--color-border);
}

.unit-config-header h3 {
  font-size: 1.125rem;
  margin: 0;
}

.unit-detail {
  padding: 1.5rem;
}

.unit-detail h4 {
  font-size: 1rem;
  margin: 0 0 0.35rem;
}

@media (min-width: 1025px) {
  .unit-detail {
    border: none;
    border-radius: 0;
    padding: 0;
    margin-bottom: 0;
    background: transparent;
  }

  .unit-config {
    border: none;
    border-radius: 0;
    background: transparent;
  }

  .unit-config-header {
    background: transparent;
    border-bottom: none;
    padding: 0 0 1rem 0;
  }
}

.building-title {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.building-title h1 {
  font-size: 1.5rem;
}

.building-type-badge {
  background: var(--color-primary);
  color: #fff;
  padding: 0.25rem 0.75rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.building-meta,
.upgrade-summary,
.grid-legend,
.unit-stats,
.unit-links,
.unit-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.meta-pill,
.upgrade-summary-pill,
.upgrade-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.35rem 0.85rem;
  background: var(--color-bg);
  border-radius: 9999px;
  font-size: 0.8125rem;
}

.meta-pill.for-sale {
  background: rgba(0, 200, 83, 0.1);
  color: var(--color-secondary);
}

.meta-label,
.section-subtitle,
.stat,
.link-label,
.picker-subtitle,
.legend-item,
.unit-desc,
.loading {
  color: var(--color-text-secondary);
}

.meta-label {
  font-size: 0.75rem;
}

.meta-value {
  font-weight: 600;
}

.upgrade-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
  background: linear-gradient(135deg, rgba(19, 127, 236, 0.09), rgba(0, 200, 83, 0.08));
}

.pro-access-banner {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
  border: 1px solid rgba(255, 109, 0, 0.3);
  border-radius: var(--radius-lg);
  background: rgba(255, 109, 0, 0.08);
}

.upgrade-banner p {
  margin: 0.35rem 0 0;
  font-size: 0.875rem;
}

.pro-access-banner p {
  margin: 0.35rem 0 0;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.grid-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.grid-actions {
  display: flex;
  gap: 0.75rem;
}

.grid-header h2 {
  font-size: 1.125rem;
  margin: 0;
}

.section-subtitle {
  margin: 0.35rem 0 0;
  font-size: 0.875rem;
}

.unit-grid {
  display: grid;
  gap: 0.6rem;
  margin-bottom: 1rem;
}

.grid-row {
  display: grid;
  grid-template-columns: minmax(80px, 1fr) 40px minmax(80px, 1fr) 40px minmax(80px, 1fr) 40px minmax(80px, 1fr);
  align-items: center;
  justify-items: center;
}

.connector-row {
  min-height: 32px;
}

.grid-cell {
  aspect-ratio: 1;
  width: 100%;
  min-height: 60px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.25rem;
  padding: 0.4rem;
  border: 2px solid var(--color-border);
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.76), rgba(244, 247, 251, 0.92));
  color: var(--color-text);
  transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
}

.grid-cell:not(:disabled):hover {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 4px rgba(19, 127, 236, 0.1);
}

.grid-cell.selected {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 4px rgba(19, 127, 236, 0.12);
}

.grid-cell.readonly,
.readonly-grid .grid-cell {
  cursor: default;
}

.grid-cell.changed {
  box-shadow: inset 0 0 0 2px rgba(19, 127, 236, 0.14);
}

.cell-type {
  font-size: 0.6875rem;
  font-weight: 700;
  text-align: center;
  line-height: 1.2;
}

.cell-item,
.cell-metric {
  font-size: 0.5625rem;
  text-align: center;
  line-height: 1.2;
  color: var(--color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 100%;
}

.cell-item {
  font-weight: 600;
  color: var(--color-text);
}

.cell-level,
.cell-pending {
  font-size: 0.625rem;
}

.cell-pending {
  color: #b45309;
  text-align: center;
}

.cell-empty {
  font-size: 1.35rem;
  opacity: 0.45;
}

.cell-capacity {
  width: 100%;
  height: 0.3rem;
  border-radius: 999px;
  overflow: hidden;
  background: color-mix(in srgb, var(--color-border) 75%, transparent);
}

.cell-capacity-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--color-primary), #38bdf8);
}

.link-toggle {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 94%, white 6%);
  transition: border-color 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}

.link-toggle:disabled,
.link-toggle.readonly {
  cursor: default;
}

.link-toggle:not(:disabled):not(.readonly):hover {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(19, 127, 236, 0.12);
}

.link-toggle.disabled {
  opacity: 0.28;
}

.link-toggle.horizontal {
  width: 32px;
  height: 14px;
  border-radius: 999px;
}

.link-toggle.vertical {
  width: 14px;
  height: 32px;
  border-radius: 999px;
}

.link-toggle.diagonal {
  width: 32px;
  height: 32px;
  border-radius: 12px;
}

.link-line {
  display: block;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-border) 82%, transparent);
}

.horizontal .link-line {
  width: 20px;
  height: 4px;
}

.vertical .link-line {
  width: 4px;
  height: 20px;
}

.link-toggle.active .link-line {
  background: var(--color-primary);
}

.diag-line {
  position: absolute;
  width: 22px;
  height: 3px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-border) 78%, transparent);
  opacity: 0.18;
}

.diag-line-primary {
  transform: rotate(45deg);
}

.diag-line-secondary {
  transform: rotate(-45deg);
}

.diagonal.state-tl-br .diag-line-primary,
.diagonal.state-tr-bl .diag-line-secondary,
.diagonal.state-cross .diag-line-primary,
.diagonal.state-cross .diag-line-secondary {
  opacity: 1;
  background: var(--color-primary);
}

.readonly-grid .link-toggle.active,
.link-toggle.readonly.active {
  background: rgba(19, 127, 236, 0.08);
}

.unit-picker-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.58);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
}

.unit-picker {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
  width: 100%;
  max-width: 320px;
}

@media (min-width: 1025px) {
  .unit-picker-overlay {
    display: none;
  }

  .unit-picker {
    border: none;
    border-radius: 0;
    padding: 0;
    width: 100%;
    max-width: none;
    background: transparent;
  }
}

.picker-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 0.5rem;
}

.picker-header h3,
.unit-detail h3 {
  font-size: 1.125rem;
  margin: 0;
}

.picker-grid {
  display: grid;
  gap: 0.5rem;
}

.picker-option {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.8rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  color: var(--color-text);
  text-align: left;
  transition: border-color 0.15s ease, background 0.15s ease;
}

.picker-option:hover {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.04);
}

.picker-color,
.legend-color {
  flex-shrink: 0;
  border-radius: 4px;
}

.picker-color {
  width: 16px;
  height: 16px;
}

.legend-color {
  width: 12px;
  height: 12px;
}

.picker-info {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.picker-name {
  font-weight: 600;
  font-size: 0.875rem;
}

.picker-desc {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.unit-desc {
  margin: 0.35rem 0 0.85rem;
  font-size: 0.8125rem;
}

.link-badge {
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  padding: 0.15rem 0.55rem;
  border-radius: 9999px;
  font-size: 0.6875rem;
  font-weight: 600;
}

.btn-sm {
  padding: 0.375rem 0.75rem;
  font-size: 0.8125rem;
}

.error-message {
  background: rgba(248, 113, 113, 0.1);
  color: var(--color-danger);
  padding: 1rem;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  gap: 1rem;
}

.loading {
  text-align: center;
  padding: 3rem;
}

@media (max-width: 920px) {
  .building-detail-view {
    max-width: 100%;
  }

  .grid-header {
    flex-direction: column;
    align-items: stretch;
  }

  .grid-actions {
    width: 100%;
  }
}

@media (max-width: 720px) {
  .grid-row {
    grid-template-columns: minmax(62px, 1fr) 30px minmax(62px, 1fr) 30px minmax(62px, 1fr) 30px minmax(62px, 1fr);
  }

  .grid-cell {
    min-height: 68px;
    padding: 0.35rem;
  }

  .link-toggle.horizontal {
    width: 26px;
  }

  .link-toggle.vertical {
    height: 26px;
  }

  .link-toggle.diagonal {
    width: 28px;
    height: 28px;
  }

  .diag-line {
    width: 18px;
  }

  .upgrade-banner,
  .grid-header {
    flex-direction: column;
    align-items: flex-start;
  }
}

/* Sale dialog */
.sale-dialog {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}

.sale-dialog-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.sale-dialog-header h3 {
  font-size: 1.125rem;
  margin: 0;
}

.sale-dialog-body {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.sale-dialog-actions {
  display: flex;
  gap: 0.75rem;
  margin-top: 0.5rem;
}

.form-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text-secondary);
}

.form-input {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  background: var(--color-bg);
  color: var(--color-text);
  font-size: 0.875rem;
}

.form-input:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(0, 71, 255, 0.1);
}

/* Config warnings */
.config-warnings {
  background: rgba(255, 109, 0, 0.08);
  border: 1px solid rgba(255, 109, 0, 0.3);
  border-radius: var(--radius-lg);
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
  color: #b45309;
}

.config-warnings strong {
  display: block;
  margin-bottom: 0.5rem;
}

.config-warnings ul {
  margin: 0;
  padding-left: 1.25rem;
}

.config-warnings li {
  font-size: 0.875rem;
  margin-bottom: 0.25rem;
}

/* Unit config fields */
.unit-config-fields {
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--color-border);
}

.unit-config-fields h5 {
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
}

.config-field {
  margin-bottom: 0.75rem;
}

.config-label {
  display: block;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  margin-bottom: 0.25rem;
}

.config-help {
  margin: -0.25rem 0 0;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.unit-config-readonly-details {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  margin-top: 0.5rem;
}

.unit-insight-card {
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--color-border);
}

.unit-insight-card h5 {
  margin: 0 0 0.75rem;
  font-size: 0.875rem;
}

.detail-capacity {
  width: 100%;
  height: 0.5rem;
  border-radius: 999px;
  overflow: hidden;
  background: color-mix(in srgb, var(--color-border) 75%, transparent);
}

.detail-capacity-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--color-primary), #38bdf8);
}

.exchange-offers-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  list-style: none;
  padding: 0;
  margin: 0;
}

.exchange-offer-item {
  padding: 0.75rem;
  border-radius: var(--radius-md, 8px);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 92%, white 8%);
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
}

.exchange-offer-header {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
  font-size: 0.8125rem;
}

.exchange-offer-metrics {
  display: grid;
  gap: 0.25rem;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

/* Layout section */
.layout-section {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1rem;
  margin-top: 1rem;
}

.layout-header {
  margin-bottom: 0.75rem;
}

.layout-header h4 {
  font-size: 1rem;
  margin: 0;
}

.layout-save {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.layout-save .form-input {
  flex: 1;
}

.layout-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.layout-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem;
  background: var(--color-bg);
  border-radius: var(--radius-md, 8px);
}

.layout-name {
  font-size: 0.8125rem;
}

.layout-item-actions {
  display: flex;
  gap: 0.25rem;
}

.layout-empty {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.grid-cell.clickable {
  cursor: pointer;
}
</style>
