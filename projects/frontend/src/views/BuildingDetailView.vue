<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AdvancedItemSelector from '@/components/buildings/AdvancedItemSelector.vue'
import BuildingFinancialTimelineChart from '@/components/buildings/BuildingFinancialTimelineChart.vue'
import UnitResourceHistoryPanel from '@/components/buildings/UnitResourceHistoryPanel.vue'
import { getInventorySourcingCostPerUnit, getPlannedUnitConstructionCost, getTotalInventorySourcingCost, getUnitConstructionCost, sumPlannedConfigurationCost } from '@/lib/buildingUnitEconomics'
import { isProductLocked } from '@/lib/productAccess'
import { formatPercent, formatUnitQuantity, getFillBucket, getUnitConfiguredItemId, getUnitPriceMetric } from '@/lib/gridTileHelpers'
import {
  applyDiagonalLinkCycle,
  applyHorizontalLinkCycle,
  applyVerticalLinkCycle,
  getDiagonalLinkLabel,
  getDiagonalLinkState,
  getHorizontalLinkArrow,
  getHorizontalLinkState,
  getVerticalLinkArrow,
  getVerticalLinkState,
} from '@/lib/linkHelpers'
import { annotateExchangeOffers, selectOptimalOffer, sortExchangeOffers, detectLogisticsTrap, type AnnotatedExchangeOffer, type ExchangeSortBy } from '@/lib/globalExchange'
import { getLocalizedProductDescription, getLocalizedProductName, getLocalizedResourceDescription, getLocalizedResourceName, getProductImageUrl, getResourceImageUrl } from '@/lib/catalogPresentation'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useScrollPreservation } from '@/composables/useScrollPreservation'
import { gqlRequest, GraphQLError } from '@/lib/graphql'
import {
  isMasterConnected,
  fetchMasterLayouts,
  saveMasterLayout,
  deleteMasterLayout,
  saveLocalLayout,
  deleteLocalLayout,
  getLocalLayoutsForType,
  type BuildingLayoutTemplate,
  type LayoutUnit,
} from '@/lib/masterLayoutApi'
import { deepEqual } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth'
import { useGameStateStore } from '@/stores/gameState'
import { getUnitResourceHistoryItemKey, type UnitResourceHistoryItemOption } from '@/lib/unitResourceHistory'
import { buildPurchaseVendorOptions, collectSameCityVendorItemKeys, getPurchaseSelectorItemKey, sortPurchaseSelectorItems } from '@/lib/purchaseSelector'
import ProductPicker from '@/components/buildings/ProductPicker.vue'
import type {
  Building,
  BuildingConfigurationPlanRemoval,
  BuildingConfigurationPlanUnit,
  BuildingFinancialTimeline,
  BuildingUnit,
  BuildingUnitInventory,
  BuildingUnitInventorySummary,
  BuildingUnitResourceHistoryPoint,
  BuildingUnitOperationalStatus,
  BuildingRecentActivityEvent,
  City,
  Company,
  GlobalExchangeOffer,
  ProcurementPreview,
  ProductType,
  PublicSalesAnalytics,
  RankedProductResult,
  ResearchBrandState,
  ResourceType,
  CityMediaHouseInfo,
  SourcingCandidate,
  UnitProductAnalytics,
} from '@/types'
import type { HorizontalLinkState, VerticalLinkState } from '@/lib/linkHelpers'

type GridUnit = BuildingUnit | BuildingConfigurationPlanUnit | EditableGridUnit
type ItemSelection = { kind: 'resource' | 'product'; id: string } | null
type SelectorItem = {
  kind: 'resource' | 'product'
  id: string
  name: string
  imageUrl?: string | null
  description?: string | null
  helperText?: string | null
  groupLabel: string
  unitSymbol?: string | null
  badge?: string | null
  disabled?: boolean
}

type PurchaseVendorOption = {
  companyId: string
  companyName: string
  buildingId: string
  buildingName: string
  cityId: string
  distanceKm: number
  pricePerUnit: number | null
  transitCostPerUnit: number
}

type PurchaseVendorCompanyData = {
  id: string
  name: string
  buildings: Array<{
    id: string
    name: string
    cityId: string
    latitude: number
    longitude: number
    units: Array<{
      id: string
      unitType: string
      resourceTypeId: string | null
      productTypeId: string | null
      minPrice: number | null
    }>
  }>
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
  lockedCityId: string | null
  isReverting?: boolean
}

const LINK_CHANGE_TICKS = 1
const UNIT_PLAN_CHANGE_TICKS = 3
const gridIndexes = [0, 1, 2, 3] as const

const { t, locale } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()

const buildingId = computed(() => route.params.id as string)
const building = ref<Building | null>(null)
const currentTick = ref(0)
const loading = ref(true)
const saving = ref(false)
/** Page-level error (building not found, load failed). Shown as a full-page error state. */
const error = ref<string | null>(null)
/** Inline save error (e.g. RECIPE_INPUT_MISMATCH). Shown within the planning section. */
const saveError = ref<string | null>(null)
const companyCash = ref<number | null>(null)
const isEditing = ref(false)
type GridCellSelection = { x: number; y: number }

const selectedCell = ref<GridCellSelection | null>(null)
const showUnitPicker = ref(false)
const draftUnits = ref<EditableGridUnit[]>([])
const editBaselineUnits = ref<EditableGridUnit[]>([])
const resourceTypes = ref<ResourceType[]>([])
const productTypes = ref<ProductType[]>([])
const rankedProducts = ref<RankedProductResult[]>([])
const rankedProductsLoading = ref(false)
const cities = ref<City[]>([])
const unitInventorySummaries = ref<BuildingUnitInventorySummary[]>([])
const unitInventories = ref<BuildingUnitInventory[]>([])
const unitResourceHistories = ref<BuildingUnitResourceHistoryPoint[]>([])
const exchangeOffers = ref<GlobalExchangeOffer[]>([])
const exchangeOffersLoading = ref(false)
const exchangeSortBy = ref<ExchangeSortBy>('deliveredPrice')

function parseUnitQuery(value: unknown): GridCellSelection | null {
  if (typeof value !== 'string') return null
  const match = value.match(/^([0-3]),([0-3])$/)
  if (!match) return null

  const x = Number(match[1])
  const y = Number(match[2])
  if (!Number.isInteger(x) || !Number.isInteger(y)) return null

  return { x, y }
}

function syncSelectedCellQuery(cell: GridCellSelection | null) {
  const nextUnit = cell ? `${cell.x},${cell.y}` : undefined
  const currentUnit = typeof route.query.unit === 'string' ? route.query.unit : undefined
  if (currentUnit === nextUnit) return

  void router.replace({
    query: {
      ...route.query,
      unit: nextUnit,
    },
  })
}

function setReadOnlySelectedCell(cell: GridCellSelection | null) {
  selectedCell.value = cell
  syncSelectedCellQuery(cell)
}

function restoreReadOnlySelectedCell(units: GridUnit[]) {
  const requestedCell = parseUnitQuery(route.query.unit)
  if (!requestedCell) {
    selectedCell.value = null
    return
  }

  const hasUnit = !!getUnitAtFrom(units, requestedCell.x, requestedCell.y)
  if (!hasUnit) {
    selectedCell.value = null
    syncSelectedCellQuery(null)
    return
  }

  selectedCell.value = requestedCell
}

function clickReadOnlyCell(x: number, y: number) {
  setReadOnlySelectedCell(getUnitAtFrom(activeUnits.value, x, y) ? { x, y } : null)
}

// Procurement preview (next-tick execution preview for PURCHASE units)
const procurementPreview = ref<ProcurementPreview | null>(null)
const procurementPreviewLoading = ref(false)
let activeProcurementPreviewRequest = 0

// Sourcing comparison (all candidates ranked by landed cost for PURCHASE units)
const sourcingCandidates = ref<SourcingCandidate[]>([])
const sourcingCandidatesLoading = ref(false)
let activeSourcingCandidatesRequest = 0
const purchaseVendorCompanies = ref<PurchaseVendorCompanyData[]>([])
const showPurchaseSelector = ref(false)

// Operational status per unit (ACTIVE/IDLE/BLOCKED/FULL/UNCONFIGURED)
const unitOperationalStatuses = ref<BuildingUnitOperationalStatus[]>([])
const unitOperationalStatusesLoading = ref(false)

// Recent tick-by-tick activity feed for the building
const recentActivity = ref<BuildingRecentActivityEvent[]>([])
const recentActivityLoading = ref(false)
const buildingFinancialTimeline = ref<BuildingFinancialTimeline | null>(null)
const buildingFinancialTimelineLoading = ref(false)

// R&D research progress state
const researchBrands = ref<ResearchBrandState[]>([])
const researchBrandsLoading = ref(false)

// City media houses — loaded lazily when a MARKETING unit is selected
const cityMediaHouses = ref<CityMediaHouseInfo[]>([])
const cityMediaHousesLoading = ref(false)

/** The media house selected in the current draft marketing unit (if any). */
const selectedDraftMediaHouse = computed(() => {
  if (!selectedCell.value) return null
  const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
  if (!unit?.mediaHouseBuildingId) return null
  return cityMediaHouses.value.find((mh) => mh.id === unit.mediaHouseBuildingId) ?? null
})

// Public Sales market intelligence analytics
const publicSalesAnalytics = ref<PublicSalesAnalytics | null>(null)
const publicSalesAnalyticsLoading = ref(false)
// Manufacturing unit product analytics
const unitProductAnalytics = ref<UnitProductAnalytics | null>(null)
const unitProductAnalyticsLoading = ref(false)
// Quick price update (instant, no tick delay)
const quickPriceInput = ref<number | null>(null)
const quickPriceSaving = ref(false)
const quickPriceSuccess = ref(false)
const quickPriceError = ref<string | null>(null)
const showSaleDialog = ref(false)
const salePrice = ref<number | null>(null)
const savingSale = ref(false)
const cancellingPlan = ref(false)
const cancelPlanError = ref<string | null>(null)
const layoutName = ref('')
const layoutDescription = ref('')
// Master-API layout state
const masterLayouts = ref<BuildingLayoutTemplate[]>([])
const masterLayoutsLoading = ref(false)
const masterLayoutsError = ref<string | null>(null)
const localLayouts = ref<BuildingLayoutTemplate[]>([])
const layoutSaving = ref(false)
const layoutSaveError = ref<string | null>(null)
const layoutSaveSuccess = ref(false)
const layoutDeleteError = ref<string | null>(null)
const overwriteConfirmPending = ref<BuildingLayoutTemplate | null>(null)
const masterConnected = computed(() => isMasterConnected())
const masterUserEmail = computed(() => auth.player?.email ?? '')
const selectedHistoryItemKey = ref<string | null>(null)

const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()

// Property management (APARTMENT / COMMERCIAL)
const showRentDialog = ref(false)
const newRentPerSqm = ref<number | null>(null)
const savingRent = ref(false)
const rentSaveError = ref<string | null>(null)

// Flush storage
const showFlushConfirmDialog = ref(false)
const flushingStorage = ref(false)
const flushStorageError = ref<string | null>(null)
const flushStorageSuccess = ref(false)

// Unit upgrade
const schedulingUpgrade = ref(false)
const unitUpgradeError = ref<string | null>(null)
const unitUpgradeInfoCache = ref<import('@/types').UnitUpgradeInfo | null>(null)

let activeBuildingLoadRequest = 0
let activeExchangeOffersRequest = 0

const allowedUnitsMap: Record<string, string[]> = {
  MINE: ['MINING', 'STORAGE', 'B2B_SALES'],
  FACTORY: ['PURCHASE', 'MANUFACTURING', 'BRANDING', 'STORAGE', 'B2B_SALES'],
  SALES_SHOP: ['PURCHASE', 'MARKETING', 'STORAGE', 'PUBLIC_SALES'],
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
const showStarterSetupBanner = computed(() => building.value?.type === 'FACTORY' && activeUnits.value.length === 0 && pendingConfiguration.value === null && !isEditing.value)
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
    imageUrl: getResourceImageUrl(resource),
    description: getLocalizedResourceDescription(resource, locale.value),
    groupLabel: t('buildingDetail.selector.rawMaterials'),
    unitSymbol: resource.unitSymbol,
  })),
  ...productTypes.value.map((product) => ({
    kind: 'product' as const,
    id: product.id,
    name: getLocalizedProductName(product, locale.value),
    imageUrl: getProductImageUrl(product),
    description: getLocalizedProductDescription(product, locale.value),
    helperText: isProductLocked(product) ? t('catalog.proDetail') : null,
    groupLabel: t('buildingDetail.selector.products'),
    unitSymbol: product.unitSymbol,
    badge: product.isProOnly ? t('catalog.proBadge') : null,
    disabled: isProductLocked(product),
  })),
])
const lockedConfiguredProducts = computed(() => {
  const configuredProductIds = new Set([...activeUnits.value, ...pendingUnits.value].map((unit) => unit.productTypeId).filter((value): value is string => !!value))

  return productTypes.value.filter((product) => configuredProductIds.has(product.id) && isProductLocked(product))
})
const lockedConfiguredProductNames = computed(() => lockedConfiguredProducts.value.map((product) => getLocalizedProductName(product, locale.value)).join(', '))
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
const draftConstructionCost = computed(() => sumPlannedConfigurationCost(activeUnits.value, draftUnits.value))
const projectedCompanyCashAfterApply = computed(() => {
  if (companyCash.value == null) return null
  return companyCash.value - draftConstructionCost.value
})

// ── Production chain status ──

/**
 * Returns the PURCHASE, MANUFACTURING and STORAGE units from the best available
 * source: active live units first, pending-configuration units second.
 * Shown in the production-chain status panel.
 */
const chainDisplayUnits = computed(() => {
  const units = activeUnits.value.length > 0 ? activeUnits.value : pendingUnits.value
  return {
    purchase: units.find((u) => u.unitType === 'PURCHASE') ?? null,
    manufacturing: units.find((u) => u.unitType === 'MANUFACTURING') ?? null,
    storage: units.find((u) => u.unitType === 'STORAGE') ?? null,
  }
})

const chainStatus = computed(() => {
  const { purchase, manufacturing, storage } = chainDisplayUnits.value
  const isPurchaseConfigured = !!(purchase && (purchase.resourceTypeId || purchase.productTypeId))
  const isManufacturingConfigured = !!(manufacturing && manufacturing.productTypeId)
  const isStoragePresent = !!storage
  return {
    isPurchaseConfigured,
    isManufacturingConfigured,
    isStoragePresent,
    isChainComplete: isPurchaseConfigured && isManufacturingConfigured && isStoragePresent,
  }
})

/**
 * Shows the production-chain status panel for a factory that already has units
 * saved (active or pending) but is not currently in edit mode.
 */
const showProductionChainPanel = computed(
  () => !isEditing.value && building.value?.type === 'FACTORY' && (activeUnits.value.length > 0 || pendingConfiguration.value !== null) && !showStarterSetupBanner.value,
)

/**
 * Mirrors showStarterSetupBanner but for SALES_SHOP buildings.
 */
const showSalesShopStarterBanner = computed(() => building.value?.type === 'SALES_SHOP' && activeUnits.value.length === 0 && pendingConfiguration.value === null && !isEditing.value)

const shopChainDisplayUnits = computed(() => {
  const units = activeUnits.value.length > 0 ? activeUnits.value : pendingUnits.value
  return {
    purchase: units.find((u) => u.unitType === 'PURCHASE') ?? null,
    publicSales: units.find((u) => u.unitType === 'PUBLIC_SALES') ?? null,
  }
})

const shopChainStatus = computed(() => {
  const { purchase, publicSales } = shopChainDisplayUnits.value
  const isPurchaseConfigured = !!(purchase && (purchase.resourceTypeId || purchase.productTypeId))
  const isPublicSalesConfigured = !!(publicSales && publicSales.productTypeId && publicSales.minPrice !== null && publicSales.minPrice !== undefined)
  return {
    isPurchaseConfigured,
    isPublicSalesConfigured,
    isChainComplete: isPurchaseConfigured && isPublicSalesConfigured,
  }
})

/**
 * Shows the sales-chain status panel for a sales shop that already has units
 * saved (active or pending) but is not currently in edit mode.
 */
const showSalesChainPanel = computed(
  () => !isEditing.value && building.value?.type === 'SALES_SHOP' && (activeUnits.value.length > 0 || pendingConfiguration.value !== null) && !showSalesShopStarterBanner.value,
)

type LinkChangeSummaryEntry = {
  description: string
  changeType: 'added' | 'removed'
}

/**
 * Computes a list of individual directional link changes between the edit baseline
 * and the current draft layout, for display in the submission summary panel.
 */
const draftLinkChanges = computed<LinkChangeSummaryEntry[]>(() => {
  if (!isEditing.value) return []
  const changes: LinkChangeSummaryEntry[] = []

  const baselineByPos = new Map(editBaselineUnits.value.map((u) => [`${u.gridX},${u.gridY}`, u]))
  const draftByPos = new Map(draftUnits.value.map((u) => [`${u.gridX},${u.gridY}`, u]))

  // Check each grid position that appears in baseline or draft
  const allPositions = new Set([...Array.from(baselineByPos.keys()), ...Array.from(draftByPos.keys())])
  for (const pos of Array.from(allPositions)) {
    const baseline = baselineByPos.get(pos)
    const draft = draftByPos.get(pos)
    const linkDirs = [
      { flag: 'linkRight', dx: 1, dy: 0, labelKey: 'buildingDetail.linkRight' },
      { flag: 'linkLeft', dx: -1, dy: 0, labelKey: 'buildingDetail.linkLeft' },
      { flag: 'linkDown', dx: 0, dy: 1, labelKey: 'buildingDetail.linkDown' },
      { flag: 'linkUp', dx: 0, dy: -1, labelKey: 'buildingDetail.linkUp' },
      { flag: 'linkDownRight', dx: 1, dy: 1, labelKey: 'buildingDetail.linkDownRight' },
      { flag: 'linkDownLeft', dx: -1, dy: 1, labelKey: 'buildingDetail.linkDownLeft' },
      { flag: 'linkUpRight', dx: 1, dy: -1, labelKey: 'buildingDetail.linkUpRight' },
      { flag: 'linkUpLeft', dx: -1, dy: -1, labelKey: 'buildingDetail.linkUpLeft' },
    ] as const

    const [bx = 0, by = 0] = pos.split(',').map(Number)
    for (const { flag, dx, dy, labelKey } of linkDirs) {
      const wasActive = !!baseline?.[flag as keyof typeof baseline]
      const isActive = !!draft?.[flag as keyof typeof draft]
      if (wasActive === isActive) continue

      const srcType = draft?.unitType ?? baseline?.unitType ?? '?'
      const targetPos = `${bx + dx},${by + dy}`
      const targetUnit = draftByPos.get(targetPos) ?? baselineByPos.get(targetPos)
      const tgtType = targetUnit?.unitType ?? '?'
      const dirLabel = t(labelKey)
      const src = `${t(`buildingDetail.unitTypes.${srcType}`)} (${bx},${by})`
      const tgt = `${t(`buildingDetail.unitTypes.${tgtType}`)} (${bx + dx},${by + dy})`
      changes.push({
        description: `${src} ${dirLabel.toLowerCase()} → ${tgt}`,
        changeType: isActive ? 'added' : 'removed',
      })
    }
  }

  return changes
})

type UnitChangeSummaryEntry = {
  changeType: 'added' | 'removed' | 'replaced'
  gridX: number
  gridY: number
  unitType: string
  previousUnitType?: string
  ticks: number
  cost: number
}

/**
 * Computes a list of individual unit structural changes (additions, removals, type
 * changes) between the edit baseline and the current draft layout.  Link-only
 * adjustments on unchanged units are excluded here — they appear in draftLinkChanges.
 */
const draftUnitChanges = computed<UnitChangeSummaryEntry[]>(() => {
  if (!isEditing.value) return []
  const entries: UnitChangeSummaryEntry[] = []

  const baselineByPos = new Map(editBaselineUnits.value.map((u) => [`${u.gridX},${u.gridY}`, u]))
  const draftByPos = new Map(draftUnits.value.map((u) => [`${u.gridX},${u.gridY}`, u]))
  const allPositions = new Set([...Array.from(baselineByPos.keys()), ...Array.from(draftByPos.keys())])

  for (const pos of Array.from(allPositions)) {
    const baseline = baselineByPos.get(pos)
    const draft = draftByPos.get(pos)
    const [gx = 0, gy = 0] = pos.split(',').map(Number)

    if (!baseline && draft) {
      // New unit added
      entries.push({
        changeType: 'added',
        gridX: gx,
        gridY: gy,
        unitType: draft.unitType,
        ticks: UNIT_PLAN_CHANGE_TICKS,
        cost: getUnitConstructionCost(draft.unitType),
      })
    } else if (baseline && !draft) {
      // Existing unit removed
      entries.push({
        changeType: 'removed',
        gridX: gx,
        gridY: gy,
        unitType: baseline.unitType,
        ticks: UNIT_PLAN_CHANGE_TICKS,
        cost: 0,
      })
    } else if (baseline && draft && baseline.unitType !== draft.unitType) {
      // Unit type replaced
      entries.push({
        changeType: 'replaced',
        gridX: gx,
        gridY: gy,
        unitType: draft.unitType,
        previousUnitType: baseline.unitType,
        ticks: UNIT_PLAN_CHANGE_TICKS,
        cost: getUnitConstructionCost(draft.unitType),
      })
    }
  }

  entries.sort((a, b) => a.gridY - b.gridY || a.gridX - b.gridX)
  return entries
})

const selectedDisplayUnit = computed<GridUnit | undefined>(() => {
  if (!selectedCell.value) return undefined

  if (isEditing.value) {
    return getUnitAtFrom(plannedUnits.value, selectedCell.value.x, selectedCell.value.y)
  }

  return getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
})
const selectedPurchaseUnit = computed(() => (selectedDisplayUnit.value?.unitType === 'PURCHASE' ? selectedDisplayUnit.value : undefined))
const selectedPublicSalesUnit = computed(() => (!isEditing.value && selectedDisplayUnit.value?.unitType === 'PUBLIC_SALES' ? selectedDisplayUnit.value : undefined))
const selectedManufacturingUnit = computed(() => (!isEditing.value && selectedDisplayUnit.value?.unitType === 'MANUFACTURING' ? selectedDisplayUnit.value : undefined))
const selectedDraftPurchaseUnit = computed(() => (isEditing.value && selectedDisplayUnit.value?.unitType === 'PURCHASE' ? (selectedDisplayUnit.value as EditableGridUnit) : undefined))

/**
 * Returns the set of product type IDs that are valid selections for the currently
 * selected STORAGE unit, based on:
 *   1. Products configured on units directly connected to this storage unit (draft layout).
 *   2. Products currently in inventory of this storage unit (active layout).
 * When this set is non-empty, the picker is filtered to only those products.
 * When empty (no links yet), the full catalog is shown as a fallback.
 */
const storageConnectedProductIds = computed<Set<string>>(() => {
  const ids = new Set<string>()
  if (!selectedCell.value || !isEditing.value) return ids

  const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
  if (!unit || unit.unitType !== 'STORAGE') return ids

  // Products from directly linked units (in draft configuration)
  for (const connected of getDirectlyConnectedUnits(unit, draftUnits.value)) {
    if (connected.productTypeId) ids.add(connected.productTypeId)
  }

  // Products already in stock in this storage unit (from active unit's inventory)
  const activeUnit = getUnitAtFrom(activeUnits.value, unit.gridX, unit.gridY)
  if (activeUnit && 'inventoryItems' in activeUnit) {
    const items = (activeUnit as { inventoryItems?: Array<{ productTypeId?: string | null }> }).inventoryItems ?? []
    for (const item of items) {
      if (item.productTypeId) ids.add(item.productTypeId)
    }
  }

  return ids
})

/**
 * Filtered ranked products for the STORAGE unit picker:
 * - If connected products are found, only show those (ranked as 'connected') plus the current selection.
 * - Otherwise fall back to the full rankedProducts list so the player is never stuck.
 */
const storageFilteredRankedProducts = computed<import('@/types').RankedProductResult[]>(() => {
  const unit = selectedCell.value && isEditing.value ? getDraftUnitAt(selectedCell.value.x, selectedCell.value.y) : null
  if (!unit || unit.unitType !== 'STORAGE') return rankedProducts.value

  const connectedIds = storageConnectedProductIds.value
  if (connectedIds.size === 0) {
    // No connections yet – show full list so player can still configure the unit
    return rankedProducts.value
  }

  // Always include the currently selected product (avoid stale-selection warning)
  const filtered = rankedProducts.value.filter(
    (r) => connectedIds.has(r.productType.id) || r.productType.id === unit.productTypeId,
  )
  // If filtering left nothing (e.g. selected product not in connected), fall back to full list
  return filtered.length > 0 ? filtered : rankedProducts.value
})

const selectedHistoryItemOptions = computed<UnitResourceHistoryItemOption[]>(() => getUnitResourceHistoryItemOptions(selectedDisplayUnit.value))
const selectedUnitResourceHistory = computed(() => getSelectedUnitResourceHistory(selectedDisplayUnit.value))
const buildingOverviewCityName = computed(() => getCityName(building.value?.cityId))
const buildingOverviewMapRoute = computed(() => {
  if (!building.value) return null

  return {
    name: 'city-map',
    params: { id: building.value.cityId },
    query: { building: building.value.id },
  }
})
const buildingFinancialSnapshots = computed(() => buildingFinancialTimeline.value?.timeline ?? [])
const buildingFinancialHasActivity = computed(() => buildingFinancialSnapshots.value.some((snapshot) => snapshot.sales > 0 || snapshot.costs > 0 || snapshot.profit !== 0))

/** Computed competitive price suggestion for the currently selected B2B_SALES draft unit. */
const b2bSuggestedPrice = computed<number | null>(() => {
  if (!selectedCell.value || !isEditing.value) return null
  const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
  if (!unit || unit.unitType !== 'B2B_SALES') return null
  return getB2BSuggestedPrice(unit)
})

/** Max revenue across all history ticks – used to normalise the revenue bar chart heights. */
const miMaxRevenue = computed(() => publicSalesAnalytics.value?.revenueHistory.reduce((m, s) => Math.max(m, s.revenue), 0) ?? 0)
/** Max quantity across all history ticks – used to normalise the quantity bar chart heights. */
const miMaxQuantitySold = computed(() => publicSalesAnalytics.value?.revenueHistory.reduce((m, s) => Math.max(m, s.quantitySold), 0) ?? 0)
/** Max price per unit across all price history ticks – used to normalise the price bar chart heights. */
const miMaxPricePerUnit = computed(() => publicSalesAnalytics.value?.priceHistory.reduce((m, s) => Math.max(m, s.pricePerUnit), 0) ?? 0)
/** Max absolute profit value across profit history – used to normalise the profit bar chart heights. */
const miMaxAbsProfit = computed(() => publicSalesAnalytics.value?.profitHistory?.reduce((m, p) => Math.max(m, Math.abs(p.profit)), 0) ?? 0)
/** Max absolute estimated profit for manufacturing unit analytics chart normalisation. */
const upaMaxAbsProfit = computed(() => unitProductAnalytics.value?.snapshots.reduce((m, s) => Math.max(m, Math.abs(s.estimatedProfit ?? 0)), 0) ?? 0)
/** Max total cost for manufacturing unit analytics chart normalisation. */
const upaMaxCost = computed(() => unitProductAnalytics.value?.snapshots.reduce((m, s) => Math.max(m, s.totalCost), 0) ?? 0)
/** Max estimated revenue for manufacturing unit analytics chart normalisation. */
const upaMaxEstRevenue = computed(() => unitProductAnalytics.value?.snapshots.reduce((m, s) => Math.max(m, s.estimatedRevenue ?? 0), 0) ?? 0)
// Current configured min price for the selected PUBLIC_SALES unit (0 if not set)
const currentPublicSalesMinPrice = computed(() => (typeof selectedPublicSalesUnit.value?.minPrice === 'number' ? selectedPublicSalesUnit.value.minPrice : 0))

let activeBuildingFinancialTimelineRequest = 0

type ExchangeOfferItem = AnnotatedExchangeOffer

const annotatedExchangeOffers = computed<ExchangeOfferItem[]>(() => {
  const maxPrice = selectedPurchaseUnit.value?.maxPrice ?? null
  const minQuality = selectedPurchaseUnit.value?.minQuality ?? null
  return annotateExchangeOffers(exchangeOffers.value, maxPrice, minQuality)
})

const exchangeOfferItems = computed<ExchangeOfferItem[]>(() => sortExchangeOffers(annotatedExchangeOffers.value, exchangeSortBy.value))

const allExchangeOffersBlocked = computed(() => exchangeOfferItems.value.length > 0 && exchangeOfferItems.value.every((o) => o.blocked))

const bestExchangeOfferCityId = computed<string | null>(() => {
  return selectOptimalOffer(annotatedExchangeOffers.value)?.cityId ?? null
})

const logisticsTrapWarning = computed(() => detectLogisticsTrap(annotatedExchangeOffers.value))

// Whether sticker price (before transit) differs from landed-cost ranking in the comparison.
const sourcingCheapestStickerDiffersFromBestLanded = computed(() => {
  const candidates = sourcingCandidates.value.filter((c) => c.isEligible)
  if (candidates.length < 2) return false
  const byLanded = [...candidates].sort((a, b) => (a.deliveredPricePerUnit ?? 0) - (b.deliveredPricePerUnit ?? 0))
  const bySticker = [...candidates].sort((a, b) => (a.exchangePricePerUnit ?? a.deliveredPricePerUnit ?? 0) - (b.exchangePricePerUnit ?? b.deliveredPricePerUnit ?? 0))
  return byLanded[0]?.sourceCityId !== bySticker[0]?.sourceCityId
})

const selectedPurchaseResourceSlug = computed<string | null>(() => {
  const resourceId = selectedPurchaseUnit.value?.resourceTypeId ?? null
  if (!resourceId) return null
  return resourceTypes.value.find((r) => r.id === resourceId)?.slug ?? null
})

const purchaseSelectorItems = computed<SelectorItem[]>(() => {
  const preferredHint = t('buildingDetail.purchaseSelector.ownSupplyHint')
  const annotatePreferred = (items: SelectorItem[]) =>
    items.map((item) =>
      sameCityVendorItemKeys.value.has(getPurchaseSelectorItemKey(item.kind, item.id))
        ? {
            ...item,
            helperText: item.helperText ? `${preferredHint} ${item.helperText}` : preferredHint,
          }
        : item,
    )

  if (building.value?.type === 'FACTORY') {
    return sortPurchaseSelectorItems(
      annotatePreferred(getFactoryPurchaseSelectableItems()),
      building.value?.type ?? null,
      sameCityVendorItemKeys.value,
    )
  }

  if (building.value?.type === 'SALES_SHOP') {
    return sortPurchaseSelectorItems(
      annotatePreferred([
        ...allSelectableItems.value.filter((item) => item.kind === 'product'),
        ...allSelectableItems.value.filter((item) => item.kind === 'resource'),
      ]),
      building.value?.type ?? null,
      sameCityVendorItemKeys.value,
    )
  }

  return sortPurchaseSelectorItems(
    annotatePreferred(allSelectableItems.value),
    building.value?.type ?? null,
    sameCityVendorItemKeys.value,
  )
})

const selectedPurchaseSelection = computed<ItemSelection>(() => getItemSelection(selectedDraftPurchaseUnit.value))

const sameCityVendorItemKeys = computed(() =>
  collectSameCityVendorItemKeys(
    purchaseVendorCompanies.value,
    building.value?.cityId ?? null,
    building.value?.id ?? null,
  ),
)

const resourceTypesById = computed(() => new Map(resourceTypes.value.map((resource) => [resource.id, resource])))
const productTypesById = computed(() => new Map(productTypes.value.map((product) => [product.id, product])))

const purchaseVendorOptions = computed<PurchaseVendorOption[]>(() => {
  return buildPurchaseVendorOptions(
    purchaseVendorCompanies.value,
    selectedPurchaseSelection.value,
    building.value?.cityId ?? null,
    building.value?.id ?? null,
    building.value ? { latitude: building.value.latitude, longitude: building.value.longitude } : null,
    resourceTypesById.value,
    productTypesById.value,
  )
})

const selectedPurchaseVendorSummary = computed<string | null>(() => {
  const companyId = selectedDraftPurchaseUnit.value?.vendorLockCompanyId
  if (!companyId) return null
  const match = purchaseVendorOptions.value.find((option) => option.companyId === companyId)
  if (match) {
    return match.pricePerUnit != null
      ? `${match.companyName} · ${match.buildingName} · ${formatCurrency(match.pricePerUnit)}`
      : `${match.companyName} · ${match.buildingName}`
  }
  if (companyId === building.value?.companyId) return t('buildingDetail.purchaseSelector.vendorOwnCompany')
  return purchaseVendorCompanies.value.find((company) => company.id === companyId)?.name ?? null
})

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
    lockedCityId: ('lockedCityId' in unit ? unit.lockedCityId : null) ?? null,
    isReverting: ('isReverting' in unit ? unit.isReverting : undefined) ?? undefined,
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
  setReadOnlySelectedCell(null)
  showUnitPicker.value = false
  refreshLocalLayouts()
  refreshMasterLayouts()
}

function cancelEditing() {
  const sourceUnits = getEditingSourceUnits()
  setDraftUnitsFrom(sourceUnits)
  setEditBaselineFrom(sourceUnits)
  isEditing.value = false
  setReadOnlySelectedCell(null)
  showUnitPicker.value = false
  saveError.value = null
}

function applyStarterLayout() {
  // Pre-populate the draft with a PURCHASE → MANUFACTURING → STORAGE → B2B_SALES chain at y=0
  const starterUnits: EditableGridUnit[] = [
    {
      id: 'draft-starter-0-0',
      unitType: 'PURCHASE',
      gridX: 0,
      gridY: 0,
      level: 1,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
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
      lockedCityId: null,
    },
    {
      id: 'draft-starter-1-0',
      unitType: 'MANUFACTURING',
      gridX: 1,
      gridY: 0,
      level: 1,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
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
      lockedCityId: null,
    },
    {
      id: 'draft-starter-2-0',
      unitType: 'STORAGE',
      gridX: 2,
      gridY: 0,
      level: 1,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
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
      lockedCityId: null,
    },
    {
      id: 'draft-starter-3-0',
      unitType: 'B2B_SALES',
      gridX: 3,
      gridY: 0,
      level: 1,
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
      saleVisibility: 'PUBLIC',
      budget: null,
      mediaHouseBuildingId: null,
      minQuality: null,
      brandScope: null,
      vendorLockCompanyId: null,
      lockedCityId: null,
    },
  ]
  setDraftUnitsFrom(starterUnits)
  setEditBaselineFrom([])
  isEditing.value = true
  setReadOnlySelectedCell(null)
  showUnitPicker.value = false
  refreshLocalLayouts()
  refreshMasterLayouts()
}

function applyShopStarterLayout() {
  const shopStarterUnits: EditableGridUnit[] = [
    {
      id: 'draft-shop-starter-0-0',
      unitType: 'PURCHASE',
      gridX: 0,
      gridY: 0,
      level: 1,
      linkUp: false,
      linkDown: false,
      linkLeft: false,
      linkRight: true,
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
      lockedCityId: null,
    },
    {
      id: 'draft-shop-starter-1-0',
      unitType: 'PUBLIC_SALES',
      gridX: 1,
      gridY: 0,
      level: 1,
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
      lockedCityId: null,
    },
  ]
  setDraftUnitsFrom(shopStarterUnits)
  setEditBaselineFrom([])
  isEditing.value = true
  setReadOnlySelectedCell(null)
  showUnitPicker.value = false
  refreshLocalLayouts()
  refreshMasterLayouts()
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
    lockedCityId: null,
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

/** Wraps the imported getHorizontalLinkState to use the local GridUnit array type. */
function getHorizontalLinkStateFor(units: GridUnit[], x: number, y: number): HorizontalLinkState {
  return getHorizontalLinkState(units, x, y)
}

/** Wraps the imported getVerticalLinkState to use the local GridUnit array type. */
function getVerticalLinkStateFor(units: GridUnit[], x: number, y: number): VerticalLinkState {
  return getVerticalLinkState(units, x, y)
}

/**
 * Cycles horizontal link through: none → forward (A→B) → backward (B→A) → both → none.
 * Links are directional: each unit's flag is set independently.
 */
function toggleHorizontalLink(x: number, y: number) {
  if (!isEditing.value) return

  const left = getDraftUnitAt(x, y)
  const right = getDraftUnitAt(x + 1, y)
  if (!left || !right) return

  applyHorizontalLinkCycle(left, right, getHorizontalLinkStateFor(draftUnits.value, x, y))
}

/**
 * Cycles vertical link through: none → forward (A→B) → backward (B→A) → both → none.
 */
function toggleVerticalLink(x: number, y: number) {
  if (!isEditing.value) return

  const top = getDraftUnitAt(x, y)
  const bottom = getDraftUnitAt(x, y + 1)
  if (!top || !bottom) return

  applyVerticalLinkCycle(top, bottom, getVerticalLinkStateFor(draftUnits.value, x, y))
}

function getDiagonalStateFor(units: GridUnit[], x: number, y: number) {
  return getDiagonalLinkState(units, x, y)
}

function toggleDiagonalLink(x: number, y: number) {
  if (!isEditing.value) return

  const topLeft = getDraftUnitAt(x, y)
  const topRight = getDraftUnitAt(x + 1, y)
  const bottomLeft = getDraftUnitAt(x, y + 1)
  const bottomRight = getDraftUnitAt(x + 1, y + 1)
  if (!topLeft || !topRight || !bottomLeft || !bottomRight) return

  applyDiagonalLinkCycle(topLeft, topRight, bottomLeft, bottomRight, getDiagonalStateFor(draftUnits.value, x, y))
}

function isHorizontalLinkActiveFor(units: GridUnit[], x: number, y: number): boolean {
  return getHorizontalLinkStateFor(units, x, y) !== 'none'
}

function isVerticalLinkActiveFor(units: GridUnit[], x: number, y: number): boolean {
  return getVerticalLinkStateFor(units, x, y) !== 'none'
}

function canToggleHorizontalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y) && !!getUnitAtFrom(units, x + 1, y)
}

function canToggleVerticalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y) && !!getUnitAtFrom(units, x, y + 1)
}

function canToggleDiagonalLink(units: GridUnit[], x: number, y: number): boolean {
  return !!getUnitAtFrom(units, x, y) && !!getUnitAtFrom(units, x + 1, y) && !!getUnitAtFrom(units, x, y + 1) && !!getUnitAtFrom(units, x + 1, y + 1)
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
    activeUnit.linkUp !== unit.linkUp ||
    activeUnit.linkDown !== unit.linkDown ||
    activeUnit.linkLeft !== unit.linkLeft ||
    activeUnit.linkRight !== unit.linkRight ||
    activeUnit.linkUpLeft !== unit.linkUpLeft ||
    activeUnit.linkUpRight !== unit.linkUpRight ||
    activeUnit.linkDownLeft !== unit.linkDownLeft ||
    activeUnit.linkDownRight !== unit.linkDownRight
  ) {
    return LINK_CHANGE_TICKS
  }

  if (
    (activeUnit.resourceTypeId ?? null) !== (unit.resourceTypeId ?? null) ||
    (activeUnit.productTypeId ?? null) !== (unit.productTypeId ?? null) ||
    (activeUnit.minPrice ?? null) !== (unit.minPrice ?? null) ||
    (activeUnit.maxPrice ?? null) !== (unit.maxPrice ?? null) ||
    (activeUnit.purchaseSource ?? null) !== (unit.purchaseSource ?? null) ||
    (activeUnit.saleVisibility ?? null) !== (unit.saleVisibility ?? null) ||
    (activeUnit.budget ?? null) !== (unit.budget ?? null) ||
    (activeUnit.mediaHouseBuildingId ?? null) !== (unit.mediaHouseBuildingId ?? null) ||
    (activeUnit.minQuality ?? null) !== (unit.minQuality ?? null) ||
    (activeUnit.brandScope ?? null) !== (unit.brandScope ?? null) ||
    (activeUnit.vendorLockCompanyId ?? null) !== (unit.vendorLockCompanyId ?? null) ||
    (activeUnit.lockedCityId ?? null) !== (unit.lockedCityId ?? null)
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

function isUnitReverting(unit: GridUnit | undefined): boolean {
  if (!unit) return false
  return 'isReverting' in unit ? !!(unit as BuildingConfigurationPlanUnit | EditableGridUnit).isReverting : false
}

type UnitComparisonKeys =
  | 'unitType'
  | 'gridX'
  | 'gridY'
  | 'linkUp'
  | 'linkDown'
  | 'linkLeft'
  | 'linkRight'
  | 'linkUpLeft'
  | 'linkUpRight'
  | 'linkDownLeft'
  | 'linkDownRight'
  | 'resourceTypeId'
  | 'productTypeId'
  | 'minPrice'
  | 'maxPrice'
  | 'purchaseSource'
  | 'saleVisibility'
  | 'budget'
  | 'mediaHouseBuildingId'
  | 'minQuality'
  | 'brandScope'
  | 'vendorLockCompanyId'
  | 'lockedCityId'

function areUnitsEquivalent(left: Pick<EditableGridUnit, UnitComparisonKeys>, right: Pick<EditableGridUnit, UnitComparisonKeys>): boolean {
  return (
    left.unitType === right.unitType &&
    left.gridX === right.gridX &&
    left.gridY === right.gridY &&
    left.linkUp === right.linkUp &&
    left.linkDown === right.linkDown &&
    left.linkLeft === right.linkLeft &&
    left.linkRight === right.linkRight &&
    left.linkUpLeft === right.linkUpLeft &&
    left.linkUpRight === right.linkUpRight &&
    left.linkDownLeft === right.linkDownLeft &&
    left.linkDownRight === right.linkDownRight &&
    (left.resourceTypeId ?? null) === (right.resourceTypeId ?? null) &&
    (left.productTypeId ?? null) === (right.productTypeId ?? null) &&
    (left.minPrice ?? null) === (right.minPrice ?? null) &&
    (left.maxPrice ?? null) === (right.maxPrice ?? null) &&
    (left.purchaseSource ?? null) === (right.purchaseSource ?? null) &&
    (left.saleVisibility ?? null) === (right.saleVisibility ?? null) &&
    (left.budget ?? null) === (right.budget ?? null) &&
    (left.mediaHouseBuildingId ?? null) === (right.mediaHouseBuildingId ?? null) &&
    (left.minQuality ?? null) === (right.minQuality ?? null) &&
    (left.brandScope ?? null) === (right.brandScope ?? null) &&
    (left.vendorLockCompanyId ?? null) === (right.vendorLockCompanyId ?? null) &&
    (left.lockedCityId ?? null) === (right.lockedCityId ?? null)
  )
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
  saveError.value = null

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
          lockedCityId: unit.lockedCityId,
        })),
      },
    },
  )
    .then(() => {
      isEditing.value = false
      return loadBuilding()
    })
    .catch((reason: unknown) => {
      saveError.value = reason instanceof Error ? reason.message : t('buildingDetail.storeUpgradeFailed')
    })
    .finally(() => {
      saving.value = false
    })
}

function cancelPlan() {
  if (!building.value || cancellingPlan.value || !pendingConfiguration.value) return

  cancellingPlan.value = true
  cancelPlanError.value = null

  gqlRequest<{
    cancelBuildingConfiguration: {
      id: string
      totalTicksRequired: number
    }
  }>(
    `mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
      cancelBuildingConfiguration(input: $input) {
        id
        totalTicksRequired
      }
    }`,
    { input: { buildingId: building.value.id } },
  )
    .then(() => {
      return loadBuilding()
    })
    .catch((reason: unknown) => {
      cancelPlanError.value = reason instanceof Error ? reason.message : t('buildingDetail.cancelPlanFailed')
    })
    .finally(() => {
      cancellingPlan.value = false
    })
}

// ── Resource path validation ──

type ValidationWarning = { key: string; params?: Record<string, unknown> }

function getLinkedUnits(unit: EditableGridUnit, units: EditableGridUnit[]): EditableGridUnit[] {
  const linked: EditableGridUnit[] = []
  const byPos = new Map(units.map((u) => [`${u.gridX},${u.gridY}`, u]))

  if (unit.linkUp) {
    const u = byPos.get(`${unit.gridX},${unit.gridY - 1}`)
    if (u) linked.push(u)
  }
  if (unit.linkDown) {
    const u = byPos.get(`${unit.gridX},${unit.gridY + 1}`)
    if (u) linked.push(u)
  }
  if (unit.linkLeft) {
    const u = byPos.get(`${unit.gridX - 1},${unit.gridY}`)
    if (u) linked.push(u)
  }
  if (unit.linkRight) {
    const u = byPos.get(`${unit.gridX + 1},${unit.gridY}`)
    if (u) linked.push(u)
  }
  if (unit.linkUpLeft) {
    const u = byPos.get(`${unit.gridX - 1},${unit.gridY - 1}`)
    if (u) linked.push(u)
  }
  if (unit.linkUpRight) {
    const u = byPos.get(`${unit.gridX + 1},${unit.gridY - 1}`)
    if (u) linked.push(u)
  }
  if (unit.linkDownLeft) {
    const u = byPos.get(`${unit.gridX - 1},${unit.gridY + 1}`)
    if (u) linked.push(u)
  }
  if (unit.linkDownRight) {
    const u = byPos.get(`${unit.gridX + 1},${unit.gridY + 1}`)
    if (u) linked.push(u)
  }

  return linked
}

const configWarnings = computed<ValidationWarning[]>(() => {
  const units = isEditing.value ? draftUnits.value : (pendingConfiguration.value?.units ?? activeUnits.value).map(cloneUnit)
  const warnings: ValidationWarning[] = []
  if (!building.value || units.length === 0) return warnings

  const purchaseUnits = units.filter((u) => u.unitType === 'PURCHASE')
  const manufacturingUnits = units.filter((u) => u.unitType === 'MANUFACTURING')
  const publicSalesUnits = units.filter((u) => u.unitType === 'PUBLIC_SALES')
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
      const hasConsumer = linked.some((u) => ['PUBLIC_SALES', 'MARKETING', 'STORAGE'].includes(u.unitType))
      if (!hasConsumer) {
        warnings.push({ key: 'buildingDetail.warnings.purchaseNotLinked', params: { x: pu.gridX, y: pu.gridY } })
      }
    }
  }

  // Check unlinked storage units
  for (const su of storageUnits) {
    const linked = getLinkedUnits(su, units)
    if (linked.length === 0) {
      warnings.push({ key: 'buildingDetail.warnings.storageNotLinked', params: { x: su.gridX, y: su.gridY } })
    }
  }

  // Check recipe compatibility: if a Manufacturing unit has a product configured,
  // ensure at least one Purchase unit in the plan supplies a matching resource/product.
  if (building.value.type === 'FACTORY' && isEditing.value) {
    for (const mu of manufacturingUnits) {
      if (!mu.productTypeId) continue
      const product = productTypes.value.find((p) => p.id === mu.productTypeId)
      if (!product || product.recipes.length === 0) continue

      const configuredPurchaseResourceIds = purchaseUnits.filter((pu) => pu.resourceTypeId).map((pu) => pu.resourceTypeId!)

      const configuredPurchaseProductIds = purchaseUnits.filter((pu) => pu.productTypeId).map((pu) => pu.productTypeId!)

      if (configuredPurchaseResourceIds.length === 0 && configuredPurchaseProductIds.length === 0) {
        continue // Incomplete (not incompatible) — missing resource is surfaced by the purchaseNoItem warning above
      }

      const anyRecipeSupplied = product.recipes.some(
        (recipe) =>
          (recipe.resourceType?.id && configuredPurchaseResourceIds.includes(recipe.resourceType.id)) ||
          (recipe.inputProductType?.id && configuredPurchaseProductIds.includes(recipe.inputProductType.id)),
      )

      if (!anyRecipeSupplied) {
        warnings.push({
          key: 'buildingDetail.warnings.recipeMismatch',
          params: { x: mu.gridX, y: mu.gridY, product: product.name },
        })
      }
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

// ── Rent management (APARTMENT / COMMERCIAL) ──

function openRentDialog() {
  newRentPerSqm.value = building.value?.pendingPricePerSqm ?? building.value?.pricePerSqm ?? null
  rentSaveError.value = null
  showRentDialog.value = true
}

function closeRentDialog() {
  showRentDialog.value = false
  rentSaveError.value = null
}

async function saveRentPerSqm() {
  if (!building.value || savingRent.value || newRentPerSqm.value === null) return
  savingRent.value = true
  rentSaveError.value = null
  try {
    const result = await gqlRequest<{
      setRentPerSqm: { id: string; pricePerSqm: number | null; pendingPricePerSqm: number | null; pendingPriceActivationTick: number | null }
    }>(
      `mutation SetRentPerSqm($input: SetRentPerSqmInput!) {
        setRentPerSqm(input: $input) {
          id pricePerSqm pendingPricePerSqm pendingPriceActivationTick
        }
      }`,
      {
        input: {
          buildingId: building.value.id,
          rentPerSqm: newRentPerSqm.value,
        },
      },
    )
    // Update local building state with returned values.
    if (building.value) {
      building.value.pendingPricePerSqm = result.setRentPerSqm.pendingPricePerSqm
      building.value.pendingPriceActivationTick = result.setRentPerSqm.pendingPriceActivationTick
    }
    showRentDialog.value = false
  } catch (reason: unknown) {
    rentSaveError.value = reason instanceof Error ? reason.message : t('property.saveFailed')
  } finally {
    savingRent.value = false
  }
}

// ── Layout save/load ──

/** Extract serialisable unit data from the draft list. */
function getDraftLayoutUnits(): LayoutUnit[] {
  return draftUnits.value.map((u) => ({
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
    lockedCityId: u.lockedCityId,
  }))
}

/** Refresh local layouts from localStorage into the reactive ref. */
function refreshLocalLayouts(): void {
  if (!building.value) return
  localLayouts.value = getLocalLayoutsForType(building.value.type)
}

/** Fetch cloud layouts from master API (no-op when not connected). */
async function refreshMasterLayouts(): Promise<void> {
  if (!masterConnected.value) {
    masterLayouts.value = []
    return
  }
  masterLayoutsLoading.value = true
  masterLayoutsError.value = null
  try {
    const all = await fetchMasterLayouts()
    masterLayouts.value = building.value
      ? all.filter((l) => l.buildingType === building.value!.type)
      : all
  } catch (err: unknown) {
    masterLayoutsError.value = err instanceof Error ? err.message : String(err)
  } finally {
    masterLayoutsLoading.value = false
  }
}

/** Save current draft as a named layout template. */
async function saveLayout(): Promise<void> {
  if (!building.value || !layoutName.value.trim()) return
  if (draftUnits.value.length === 0) {
    layoutSaveError.value = t('buildingDetail.layouts.noUnits')
    return
  }

  layoutSaving.value = true
  layoutSaveError.value = null
  layoutSaveSuccess.value = false

  const name = layoutName.value.trim()
  const description = layoutDescription.value.trim() || null
  const buildingType = building.value.type
  const units = getDraftLayoutUnits()

  try {
    if (masterConnected.value) {
      // Find if an existing cloud layout with the same name exists so we can update it
      const existing = masterLayouts.value.find((l) => l.name === name)
      const saved = await saveMasterLayout(name, description, buildingType, units, existing?.id)
      // Update local cache
      const idx = masterLayouts.value.findIndex((l) => l.id === saved.id)
      if (idx >= 0) {
        masterLayouts.value.splice(idx, 1, saved)
      } else {
        masterLayouts.value = [saved, ...masterLayouts.value]
      }
    } else {
      // Fallback: localStorage
      saveLocalLayout(name, description, buildingType, units)
      refreshLocalLayouts()
    }
    layoutName.value = ''
    layoutDescription.value = ''
    layoutSaveSuccess.value = true
    setTimeout(() => {
      layoutSaveSuccess.value = false
    }, 3000)
  } catch (err: unknown) {
    // If cloud failed, persist locally and surface a fallback warning
    if (masterConnected.value) {
      try {
        saveLocalLayout(name, description, buildingType, units)
        refreshLocalLayouts()
        layoutSaveError.value = t('buildingDetail.layouts.masterError')
        layoutSaveSuccess.value = true
        setTimeout(() => {
          layoutSaveSuccess.value = false
          layoutSaveError.value = null
        }, 4000)
      } catch {
        layoutSaveError.value = err instanceof Error ? err.message : String(err)
      }
    } else {
      layoutSaveError.value = err instanceof Error ? err.message : String(err)
    }
  } finally {
    layoutSaving.value = false
  }
}

/** Confirm and apply a layout template to the current draft. */
function requestLoadLayout(layout: BuildingLayoutTemplate): void {
  // Compatibility check: building types must match
  if (layout.buildingType !== building.value?.type) {
    layoutSaveError.value = t('buildingDetail.layouts.incompatible', {
      type: layout.buildingType,
      buildingType: building.value?.type ?? '?',
    })
    return
  }
  // If the draft is non-empty, ask for overwrite confirmation
  if (draftUnits.value.length > 0) {
    overwriteConfirmPending.value = layout
  } else {
    applyLayout(layout)
  }
}

function confirmOverwrite(): void {
  if (overwriteConfirmPending.value) {
    applyLayout(overwriteConfirmPending.value)
    overwriteConfirmPending.value = null
  }
}

function cancelOverwrite(): void {
  overwriteConfirmPending.value = null
}

function applyLayout(layout: BuildingLayoutTemplate): void {
  draftUnits.value = layout.units.map((u, i) => ({
    id: `layout-${i}-${Date.now()}`,
    ...u,
    lockedCityId: u.lockedCityId ?? null,
    level: 1,
  }))
}

/** Delete a layout template. */
async function deleteLayout(layout: BuildingLayoutTemplate): Promise<void> {
  layoutDeleteError.value = null
  try {
    if (!layout.isLocal && layout.id) {
      await deleteMasterLayout(layout.id)
      masterLayouts.value = masterLayouts.value.filter((l) => l.id !== layout.id)
    } else {
      deleteLocalLayout(layout.name, layout.buildingType)
      refreshLocalLayouts()
    }
  } catch (err: unknown) {
    layoutDeleteError.value = err instanceof Error ? err.message : String(err)
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
  if (!selection) {
    unit.vendorLockCompanyId = null
  }
}

function openPurchaseSelector() {
  showPurchaseSelector.value = true
}

function closePurchaseSelector() {
  showPurchaseSelector.value = false
}

function applyPurchaseSelection(selection: ItemSelection) {
  const unit = selectedDraftPurchaseUnit.value
  if (!unit) return
  setItemSelection(unit, selection)
}

function selectPurchaseVendor(companyId: string | null) {
  const unit = selectedDraftPurchaseUnit.value
  if (!unit) return
  unit.vendorLockCompanyId = companyId
  if (building.value?.type === 'SALES_SHOP' && companyId) {
    unit.purchaseSource = 'LOCAL'
  }
}

function getFactoryPurchaseSelectableItems(): SelectorItem[] {
  if (building.value?.type !== 'FACTORY') {
    return allSelectableItems.value
  }

  return [...allSelectableItems.value.filter((item) => item.kind === 'resource'), ...allSelectableItems.value.filter((item) => item.kind === 'product' && intermediateProductIds.value.has(item.id))]
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
    .filter((product) =>
      product.recipes.every((recipe) => {
        if (recipe.resourceType?.id) {
          return reachableInputs.has(`resource:${recipe.resourceType.id}`)
        }
        if (recipe.inputProductType?.id) {
          return reachableInputs.has(`product:${recipe.inputProductType.id}`)
        }
        return false
      }),
    )
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
  const item = getUnitConfiguredItemId(unit)
  if (!item) return null
  return item.kind === 'product' ? getProductName(item.id) : getResourceName(item.id)
}

function getUnitPrimaryMetric(unit: GridUnit | undefined): string | null {
  if (!unit) return null
  const metric = getUnitPriceMetric(unit)
  if (!metric) return null
  if (metric.kind === 'scope') {
    return formatUnitMetric(t('buildingDetail.gridMetrics.scope'), getBrandScopeLabel(metric.value as string))
  }
  return formatUnitMetric(t(`buildingDetail.gridMetrics.${metric.kind}`), `$${metric.value}`)
}

function getUnitInventorySummary(unit: GridUnit | undefined): BuildingUnitInventorySummary | undefined {
  if (!unit) return undefined
  const directSummary = unitInventorySummaries.value.find((summary) => summary.buildingUnitId === unit.id)
  if (directSummary) return directSummary

  const activeUnit = activeUnits.value.find((candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY)

  if (!activeUnit) return undefined
  return unitInventorySummaries.value.find((summary) => summary.buildingUnitId === activeUnit.id)
}

function getUnitInventories(unit: GridUnit | undefined): BuildingUnitInventory[] {
  if (!unit) return []
  const directInventories = unitInventories.value.filter((inventory) => inventory.buildingUnitId === unit.id)
  if (directInventories.length > 0) {
    return [...directInventories].sort((left, right) => right.quantity - left.quantity)
  }

  const activeUnit = activeUnits.value.find((candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY)

  if (!activeUnit) return []
  return unitInventories.value.filter((inventory) => inventory.buildingUnitId === activeUnit.id).sort((left, right) => right.quantity - left.quantity)
}

function getResolvedLiveUnitId(unit: GridUnit | undefined): string | null {
  if (!unit) return null

  const hasDirectLiveData = unitInventories.value.some((inventory) => inventory.buildingUnitId === unit.id) || unitResourceHistories.value.some((entry) => entry.buildingUnitId === unit.id)
  if (hasDirectLiveData) {
    return unit.id
  }

  const activeUnit = activeUnits.value.find((candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY)

  return activeUnit?.id ?? unit.id
}

function getUnitResourceHistory(unit: GridUnit | undefined): BuildingUnitResourceHistoryPoint[] {
  const resolvedUnitId = getResolvedLiveUnitId(unit)
  if (!resolvedUnitId) return []

  return unitResourceHistories.value.filter((entry) => entry.buildingUnitId === resolvedUnitId).sort((left, right) => left.tick - right.tick)
}

function getHistoryItemLabel(resourceTypeId: string | null | undefined, productTypeId: string | null | undefined): string {
  if (resourceTypeId) {
    return getResourceName(resourceTypeId)
  }

  if (productTypeId) {
    return getProductName(productTypeId)
  }

  return t('buildingDetail.inventory.item')
}

function getUnitResourceHistoryItemOptions(unit: GridUnit | undefined): UnitResourceHistoryItemOption[] {
  if (!unit) return []

  const options = new Map<string, UnitResourceHistoryItemOption>()
  const order = new Map<string, number>()
  let nextOrder = 0

  for (const inventory of getUnitInventories(unit)) {
    const key = getUnitResourceHistoryItemKey(inventory.resourceTypeId, inventory.productTypeId)
    if (!key || options.has(key)) {
      continue
    }

    options.set(key, {
      key,
      label: getHistoryItemLabel(inventory.resourceTypeId, inventory.productTypeId),
    })
    order.set(key, nextOrder++)
  }

  for (const entry of getUnitResourceHistory(unit)) {
    const key = getUnitResourceHistoryItemKey(entry.resourceTypeId, entry.productTypeId)
    if (!key || options.has(key)) {
      continue
    }

    options.set(key, {
      key,
      label: getHistoryItemLabel(entry.resourceTypeId, entry.productTypeId),
    })
    order.set(key, nextOrder++)
  }

  return Array.from(options.values()).sort((left, right) => (order.get(left.key) ?? Number.MAX_SAFE_INTEGER) - (order.get(right.key) ?? Number.MAX_SAFE_INTEGER))
}

function getSelectedUnitResourceHistory(unit: GridUnit | undefined): BuildingUnitResourceHistoryPoint[] {
  const selectedKey = selectedHistoryItemKey.value
  if (!selectedKey) return []

  return getUnitResourceHistory(unit).filter((entry) => getUnitResourceHistoryItemKey(entry.resourceTypeId, entry.productTypeId) === selectedKey)
}

function getUnitInventoryItemCount(unit: GridUnit | undefined): number {
  return getUnitInventories(unit).length
}

function getUnitOperationalStatus(unit: GridUnit | undefined): BuildingUnitOperationalStatus | null {
  if (!unit) return null
  return unitOperationalStatuses.value.find((s) => s.buildingUnitId === unit.id) ?? null
}

const selectedActiveUnitOperationalStatus = computed<BuildingUnitOperationalStatus | null>(() => {
  if (!selectedCell.value) return null
  const unit = getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
  return getUnitOperationalStatus(unit)
})

/** The pending upgrade plan unit for the currently selected cell, if any. */
const selectedCellPendingUpgrade = computed<{
  level: number
  ticksRemaining: number
} | null>(() => {
  if (!selectedCell.value || !building.value?.pendingConfiguration) return null
  const plan = building.value.pendingConfiguration
  // Prefer the authoritative tick from the game-state store; fall back to the
  // local `currentTick` ref which is set from the same source during loadBuilding().
  const tick = gameStateStore.gameState?.currentTick ?? currentTick.value
  const planUnit = plan.units.find(
    (u) =>
      u.gridX === selectedCell.value!.x &&
      u.gridY === selectedCell.value!.y &&
      u.isChanged,
  )
  if (!planUnit) return null
  const activeUnit = getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
  if (!activeUnit || planUnit.level <= activeUnit.level) return null
  return {
    level: planUnit.level,
    ticksRemaining: Math.max(0, planUnit.appliesAtTick - tick),
  }
})

/** Upgrade info for the currently selected cell unit (cached from last fetch). */
const selectedCellUpgradeInfo = computed<import('@/types').UnitUpgradeInfo | null>(() => {
  if (!selectedCell.value) return null
  const unit = getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
  if (!unit || !unit.id) return null
  if (unitUpgradeInfoCache.value?.unitId === unit.id) return unitUpgradeInfoCache.value
  return null
})

function formatCurrency(value: number | null | undefined): string {
  const amount = value ?? 0
  const formatter = new Intl.NumberFormat(locale.value, {
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  })
  return `$${formatter.format(amount)}`
}

function getPurchaseVendorTransitLabel(transitCostPerUnit: number): string {
  return t('buildingDetail.purchaseSelector.vendorTransit', { price: formatCurrency(transitCostPerUnit) })
}

function getCityName(cityId: string | null | undefined): string {
  if (!cityId) return t('common.notAvailable')
  return cities.value.find((city) => city.id === cityId)?.name ?? t('common.notAvailable')
}

function formatGpsLocation(latitude: number | null | undefined, longitude: number | null | undefined): string {
  if (latitude == null || longitude == null) return t('common.notAvailable')

  const latitudeDirection = latitude >= 0 ? 'N' : 'S'
  const longitudeDirection = longitude >= 0 ? 'E' : 'W'
  return `${Math.abs(latitude).toFixed(5)}°${latitudeDirection}, ${Math.abs(longitude).toFixed(5)}°${longitudeDirection}`
}

function getConfiguredItemImageUrl(unit: GridUnit | undefined): string | null {
  const resourceTypeId = unit && 'resourceTypeId' in unit ? unit.resourceTypeId : null
  if (!resourceTypeId) return null
  return resourceTypes.value.find((resource) => resource.id === resourceTypeId)?.imageUrl ?? null
}

function getConfiguredItemMonogram(unit: GridUnit | undefined): string {
  const label = getUnitConfiguredItemLabel(unit)
  if (!label) return '?'

  return label
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

function getInventoryItemImageUrl(inventory: BuildingUnitInventory): string | null {
  if (inventory.resourceTypeId) {
    return resourceTypes.value.find((resource) => resource.id === inventory.resourceTypeId)?.imageUrl ?? null
  }
  if (inventory.productTypeId) {
    return productTypes.value.find((product) => product.id === inventory.productTypeId)?.imageUrl ?? null
  }
  return null
}

function getPrimaryInventoryItem(unit: GridUnit | undefined): BuildingUnitInventory | undefined {
  return getUnitInventories(unit)[0]
}

function getUnitDisplayLabel(unit: GridUnit | undefined): string | null {
  const primaryInventory = getPrimaryInventoryItem(unit)
  if (primaryInventory) {
    const extraItems = getUnitInventoryItemCount(unit) - 1
    const primaryName = getInventoryItemName(primaryInventory)
    return extraItems > 0 ? `${primaryName} ${t('buildingDetail.inventory.moreItems', { count: extraItems })}` : primaryName
  }

  return getUnitConfiguredItemLabel(unit)
}

function getUnitDisplayImageUrl(unit: GridUnit | undefined): string | null {
  const primaryInventory = getPrimaryInventoryItem(unit)
  if (primaryInventory) {
    return getInventoryItemImageUrl(primaryInventory)
  }

  return getConfiguredItemImageUrl(unit)
}

function getUnitDisplayMonogram(unit: GridUnit | undefined): string {
  const primaryInventory = getPrimaryInventoryItem(unit)
  if (primaryInventory) {
    return getInventoryItemMonogram(primaryInventory)
  }

  return getConfiguredItemMonogram(unit)
}

function getInventoryItemName(inventory: BuildingUnitInventory): string {
  if (inventory.resourceTypeId) {
    return getResourceName(inventory.resourceTypeId)
  }
  if (inventory.productTypeId) {
    return getProductName(inventory.productTypeId)
  }
  return 'Unknown'
}

function getInventoryItemMonogram(inventory: BuildingUnitInventory): string {
  const name = getInventoryItemName(inventory)
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

function getInventoryItemSourcingCostTotal(inventory: BuildingUnitInventory): number {
  return inventory.sourcingCostTotal ?? 0
}

function getInventoryItemSourcingCostLabel(inventory: BuildingUnitInventory): string {
  return formatCurrency(getInventoryItemSourcingCostTotal(inventory))
}

function getInventoryItemSourcingCostPerUnitLabel(inventory: BuildingUnitInventory): string | null {
  const sourcingCostPerUnit = inventory.sourcingCostPerUnit ?? getInventorySourcingCostPerUnit(inventory.quantity, inventory.sourcingCostTotal)
  return sourcingCostPerUnit == null ? null : t('buildingDetail.inventory.perUnit', { value: formatCurrency(sourcingCostPerUnit) })
}

function getUnitInventoryCost(unit: GridUnit | undefined): number | null {
  if (!unit) return null
  const summary = getUnitInventorySummary(unit)
  if (summary) return summary.totalSourcingCost

  const inventories = getUnitInventories(unit)
  if (inventories.length === 0) return null

  return getTotalInventorySourcingCost(
    inventories.map((inventory) => ({
      quantity: inventory.quantity,
      sourcingCostTotal: getInventoryItemSourcingCostTotal(inventory),
    })),
  )
}

function getUnitInventoryCostLabel(unit: GridUnit | undefined): string | null {
  const value = getUnitInventoryCost(unit)
  return value == null ? null : formatCurrency(value)
}

function getUnitNextTickOperatingCost(unit: GridUnit | undefined): number | null {
  const status = getUnitOperationalStatus(unit)
  if (!status) return null
  const labor = status.nextTickLaborCost ?? 0
  const energy = status.nextTickEnergyCost ?? 0
  const total = labor + energy
  return total > 0 ? total : null
}

function getUnitNextTickOperatingCostLabel(unit: GridUnit | undefined): string | null {
  const cost = getUnitNextTickOperatingCost(unit)
  return cost == null ? null : formatCurrency(cost)
}

function getDraftUnitConstructionCost(unit: GridUnit | undefined): number {
  if (!unit) return 0
  const activeUnit = getUnitAtFrom(activeUnits.value, unit.gridX, unit.gridY)
  return getPlannedUnitConstructionCost(activeUnit, unit)
}

function getDraftUnitConstructionCostLabel(unit: GridUnit | undefined): string | null {
  const cost = getDraftUnitConstructionCost(unit)
  return cost > 0 ? formatCurrency(cost) : null
}

function getPurchaseUnitResourceTypeId(unit: GridUnit | undefined): string | null {
  return unit && 'resourceTypeId' in unit ? unit.resourceTypeId : null
}

function getPurchaseUnitSource(unit: GridUnit | undefined): string | null {
  return unit && 'purchaseSource' in unit ? unit.purchaseSource : null
}

function getGridCellAriaLabel(unit: GridUnit | undefined): string {
  if (!unit) return t('buildingDetail.cellAriaLabelEmpty')

  const typePart = t(`buildingDetail.unitTypes.${unit.unitType}`)
  const itemLabel = getUnitConfiguredItemLabel(unit)
  const metric = getUnitPrimaryMetric(unit)
  const inventory = getUnitInventorySummary(unit)

  const itemPart = itemLabel ? t('buildingDetail.cellAriaLabelItem', { item: itemLabel }) : ''
  const metricPart = metric ? t('buildingDetail.cellAriaLabelMetric', { metric }) : ''
  let fillPart = ''
  if (inventory?.capacity) {
    fillPart = t('buildingDetail.cellAriaLabelFill', {
      fill: formatPercent(inventory.fillPercent),
    })
  }
  return `${typePart}${itemPart}${metricPart}${fillPart}`
}

function updateSelectedUnitConfig(field: string, value: unknown) {
  if (!selectedCell.value || !isEditing.value) return
  const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
  if (!unit) return
  const sanitized = typeof value === 'number' && isNaN(value) ? null : value
  ;(unit as Record<string, unknown>)[field] = sanitized

  // When procurement mode changes away from EXCHANGE, clear the city lock.
  // LockedCityId only applies to EXCHANGE mode – keeping it silently restricts OPTIMAL sourcing.
  if (field === 'purchaseSource' && value !== 'EXCHANGE') {
    ;(unit as Record<string, unknown>)['lockedCityId'] = null
  }
}

async function loadUnitInventorySummaries(requestId?: number) {
  if (!auth.token) {
    if (requestId == null || requestId === activeBuildingLoadRequest) {
      unitInventorySummaries.value = []
    }
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
          totalSourcingCost
          sourcingCostPerUnit
        }
      }`,
      { buildingId: buildingId.value },
    )
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (!deepEqual(unitInventorySummaries.value, data.buildingUnitInventorySummaries)) {
      unitInventorySummaries.value = data.buildingUnitInventorySummaries
    }
  } catch {
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (unitInventorySummaries.value.length > 0) {
      unitInventorySummaries.value = []
    }
  }
}

async function loadUnitInventories(requestId?: number) {
  if (!auth.token) {
    if (requestId == null || requestId === activeBuildingLoadRequest) {
      unitInventories.value = []
    }
    return
  }

  try {
    const data = await gqlRequest<{ buildingUnitInventories: BuildingUnitInventory[] }>(
      `query BuildingUnitInventories($buildingId: UUID!) {
        buildingUnitInventories(buildingId: $buildingId) {
          id
          buildingUnitId
          resourceTypeId
          productTypeId
          quantity
          sourcingCostTotal
          sourcingCostPerUnit
          quality
        }
      }`,
      { buildingId: buildingId.value },
    )
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (!deepEqual(unitInventories.value, data.buildingUnitInventories)) {
      unitInventories.value = data.buildingUnitInventories
    }
  } catch {
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (unitInventories.value.length > 0) {
      unitInventories.value = []
    }
  }
}

async function loadUnitResourceHistories(requestId?: number) {
  if (!auth.token) {
    if (requestId == null || requestId === activeBuildingLoadRequest) {
      unitResourceHistories.value = []
    }
    return
  }

  try {
    const data = await gqlRequest<{ buildingUnitResourceHistories: BuildingUnitResourceHistoryPoint[] }>(
      `query BuildingUnitResourceHistories($buildingId: UUID!, $limit: Int) {
        buildingUnitResourceHistories(buildingId: $buildingId, limit: $limit) {
          buildingUnitId
          resourceTypeId
          productTypeId
          tick
          inflowQuantity
          outflowQuantity
          consumedQuantity
          producedQuantity
        }
      }`,
      { buildingId: buildingId.value, limit: 60 },
    )
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (!deepEqual(unitResourceHistories.value, data.buildingUnitResourceHistories)) {
      unitResourceHistories.value = data.buildingUnitResourceHistories
    }
  } catch {
    if (requestId != null && requestId !== activeBuildingLoadRequest) {
      return
    }

    if (unitResourceHistories.value.length > 0) {
      unitResourceHistories.value = []
    }
  }
}

async function loadGlobalExchangeOffers() {
  const requestId = ++activeExchangeOffersRequest
  const unit = selectedPurchaseUnit.value
  const resourceTypeId = getPurchaseUnitResourceTypeId(unit)
  const purchaseSource = getPurchaseUnitSource(unit)

  if (!building.value?.cityId || !resourceTypeId || !['EXCHANGE', 'OPTIMAL'].includes(purchaseSource ?? '')) {
    if (requestId === activeExchangeOffersRequest) {
      if (exchangeOffers.value.length > 0) {
        exchangeOffers.value = []
      }
      exchangeOffersLoading.value = false
    }
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
    if (requestId !== activeExchangeOffersRequest) {
      return
    }

    if (!deepEqual(exchangeOffers.value, data.globalExchangeOffers)) {
      exchangeOffers.value = data.globalExchangeOffers
    }
  } catch {
    if (requestId !== activeExchangeOffersRequest) {
      return
    }

    if (exchangeOffers.value.length > 0) {
      exchangeOffers.value = []
    }
  } finally {
    if (requestId === activeExchangeOffersRequest) {
      exchangeOffersLoading.value = false
    }
  }
}

async function loadProcurementPreview(isRefresh = false) {
  const unit = selectedPurchaseUnit.value
  if (!unit || !('id' in unit)) {
    procurementPreview.value = null
    procurementPreviewLoading.value = false
    return
  }
  const unitId = unit.id
  const requestId = ++activeProcurementPreviewRequest

  if (!isRefresh || procurementPreview.value == null) {
    procurementPreviewLoading.value = true
  }
  try {
    const data = await gqlRequest<{ procurementPreview: ProcurementPreview | null }>(
      `query ProcurementPreview($unitId: UUID!) {
        procurementPreview(buildingUnitId: $unitId) {
          sourceType
          sourceCityId
          sourceCityName
          sourceVendorCompanyId
          sourceVendorName
          exchangePricePerUnit
          transitCostPerUnit
          deliveredPricePerUnit
          estimatedQuality
          canExecute
          blockReason
          blockMessage
        }
      }`,
      { unitId },
    )
    if (requestId !== activeProcurementPreviewRequest) return
    procurementPreview.value = data.procurementPreview
  } catch {
    if (requestId !== activeProcurementPreviewRequest) return
    procurementPreview.value = null
  } finally {
    if (requestId === activeProcurementPreviewRequest) {
      procurementPreviewLoading.value = false
    }
  }
}

async function loadSourcingCandidates(isRefresh = false) {
  const unit = selectedPurchaseUnit.value
  if (!unit || !('id' in unit)) {
    sourcingCandidates.value = []
    sourcingCandidatesLoading.value = false
    return
  }
  const unitId = unit.id
  const requestId = ++activeSourcingCandidatesRequest

  if (!isRefresh || sourcingCandidates.value.length === 0) {
    sourcingCandidatesLoading.value = true
  }
  try {
    const data = await gqlRequest<{ sourcingCandidates: SourcingCandidate[] }>(
      `query SourcingCandidates($unitId: UUID!) {
        sourcingCandidates(buildingUnitId: $unitId) {
          sourceType
          sourceCityId
          sourceCityName
          sourceVendorCompanyId
          sourceVendorName
          exchangePricePerUnit
          transitCostPerUnit
          deliveredPricePerUnit
          estimatedQuality
          distanceKm
          isEligible
          blockReason
          blockMessage
          isRecommended
          rank
        }
      }`,
      { unitId },
    )
    if (requestId !== activeSourcingCandidatesRequest) return
    sourcingCandidates.value = data.sourcingCandidates ?? []
  } catch {
    if (requestId !== activeSourcingCandidatesRequest) return
    sourcingCandidates.value = []
  } finally {
    if (requestId === activeSourcingCandidatesRequest) {
      sourcingCandidatesLoading.value = false
    }
  }
}

async function loadResearchBrands() {
  const companyId = building.value?.companyId
  if (!companyId || building.value?.type !== 'RESEARCH_DEVELOPMENT') return

  researchBrandsLoading.value = true
  try {
    const data = await gqlRequest<{ companyBrands: ResearchBrandState[] }>(
      `query CompanyBrands($companyId: UUID!) {
        companyBrands(companyId: $companyId) {
          id
          companyId
          name
          scope
          productTypeId
          productName
          industryCategory
          awareness
          quality
          marketingEfficiencyMultiplier
        }
      }`,
      { companyId },
    )
    researchBrands.value = data.companyBrands ?? []
  } catch {
    researchBrands.value = []
  } finally {
    researchBrandsLoading.value = false
  }
}

async function loadCityMediaHouses() {
  const cityId = building.value?.cityId
  if (!cityId) return
  cityMediaHousesLoading.value = true
  try {
    const data = await gqlRequest<{ cityMediaHouses: CityMediaHouseInfo[] }>(
      `query CityMediaHouses($cityId: UUID!) {
        cityMediaHouses(cityId: $cityId) {
          id
          name
          cityId
          mediaType
          ownerCompanyId
          ownerCompanyName
          effectivenessMultiplier
          powerStatus
          isUnderConstruction
        }
      }`,
      { cityId },
    )
    cityMediaHouses.value = data.cityMediaHouses ?? []
  } catch {
    cityMediaHouses.value = []
  } finally {
    cityMediaHousesLoading.value = false
  }
}

async function loadPublicSalesAnalytics(unitId: string | null, isRefresh = false) {
  if (!unitId || !auth.token) {
    publicSalesAnalytics.value = null
    publicSalesAnalyticsLoading.value = false
    return
  }

  if (!isRefresh || publicSalesAnalytics.value == null) {
    publicSalesAnalyticsLoading.value = true
  }
  try {
    const data = await gqlRequest<{ publicSalesAnalytics: PublicSalesAnalytics | null }>(
      `query PublicSalesAnalytics($unitId: UUID!) {
        publicSalesAnalytics(unitId: $unitId) {
          buildingUnitId
          buildingId
          buildingName
          cityName
          productTypeId
          productName
          totalRevenue
          totalQuantitySold
          averagePricePerUnit
          currentSalesCapacity
          dataFromTick
          dataToTick
          demandSignal
          actionHint
          recentUtilization
          elasticityIndex
          unmetDemandShare
          populationIndex
          inventoryQuality
          brandAwareness
          totalProfit
          trendDirection
          trendFactor
          revenueHistory { tick revenue quantitySold }
          priceHistory { tick pricePerUnit }
          profitHistory { tick profit grossMarginPct }
          marketShare { label companyId share isUnmet }
          demandDrivers { factor impact score description }
        }
      }`,
      { unitId },
    )
    publicSalesAnalytics.value = data.publicSalesAnalytics ?? null
  } catch {
    publicSalesAnalytics.value = null
  } finally {
    publicSalesAnalyticsLoading.value = false
  }
}

async function loadUnitProductAnalytics(unitId: string | null, isRefresh = false) {
  if (!unitId || !auth.token) {
    unitProductAnalytics.value = null
    unitProductAnalyticsLoading.value = false
    return
  }

  if (!isRefresh || unitProductAnalytics.value == null) {
    unitProductAnalyticsLoading.value = true
  }
  try {
    const data = await gqlRequest<{ unitProductAnalytics: UnitProductAnalytics | null }>(
      `query UnitProductAnalytics($unitId: UUID!) {
        unitProductAnalytics(unitId: $unitId) {
          buildingUnitId
          unitType
          productTypeId
          productName
          dataFromTick
          dataToTick
          totalCost
          totalQuantityProduced
          estimatedRevenue
          estimatedProfit
          snapshots { tick laborCost energyCost totalCost quantityProduced estimatedRevenue estimatedProfit }
        }
      }`,
      { unitId },
    )
    unitProductAnalytics.value = data.unitProductAnalytics ?? null
  } catch {
    unitProductAnalytics.value = null
  } finally {
    unitProductAnalyticsLoading.value = false
  }
}

async function submitQuickPriceUpdate() {
  const unit = selectedPublicSalesUnit.value
  const price = quickPriceInput.value
  if (!unit || !auth.token || price == null) return
  const unitId = getResolvedLiveUnitId(unit)
  if (!unitId) return
  quickPriceSaving.value = true
  quickPriceSuccess.value = false
  quickPriceError.value = null
  try {
    const data = await gqlRequest<{ updatePublicSalesPrice: { id: string; minPrice: number } }>(
      `mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
        updatePublicSalesPrice(input: $input) { id minPrice }
      }`,
      { input: { unitId, newMinPrice: price } },
    )
    // Update local unit state immediately so UI reflects new price
    if (building.value) {
      const liveUnit = building.value.units?.find((u) => u.id === data.updatePublicSalesPrice.id)
      if (liveUnit) liveUnit.minPrice = data.updatePublicSalesPrice.minPrice
    }
    quickPriceInput.value = null
    quickPriceSuccess.value = true
    // Refresh analytics to reflect the new price
    await loadPublicSalesAnalytics(unitId)
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err)
    quickPriceError.value = msg || t('buildingDetail.marketIntelligence.priceUpdateFailed')
  } finally {
    quickPriceSaving.value = false
  }
}

async function submitFlushStorage(unitId: string) {
  if (!auth.token) return
  flushingStorage.value = true
  flushStorageError.value = null
  flushStorageSuccess.value = false
  showFlushConfirmDialog.value = false
  try {
    await gqlRequest<{ flushStorage: { discardedItemCount: number; totalDiscardedValue: number } }>(
      `mutation FlushStorage($input: FlushStorageInput!) {
        flushStorage(input: $input) {
          discardedItemCount
          totalDiscardedValue
        }
      }`,
      { input: { buildingUnitId: unitId } },
    )
    flushStorageSuccess.value = true
    // Reload building data to reflect cleared inventory
    await loadBuilding({ preserveDraft: isEditing.value })
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err)
    flushStorageError.value = msg || t('buildingDetail.flushStorage.error')
  } finally {
    flushingStorage.value = false
  }
}

/** Fetches and caches upgrade info for the given unit ID. */
async function fetchUpgradeInfo(unitId: string) {
  if (!auth.token) return
  try {
    const result = await gqlRequest<{ unitUpgradeInfo: import('@/types').UnitUpgradeInfo | null }>(
      `query UUI($unitId: UUID!) {
        unitUpgradeInfo(unitId: $unitId) {
          unitId unitType currentLevel nextLevel isMaxLevel isUpgradable
          upgradeCost upgradeTicks currentStat nextStat statLabel
        }
      }`,
      { unitId },
    )
    unitUpgradeInfoCache.value = result.unitUpgradeInfo
  } catch {
    // silently ignore — upgrade panel will remain hidden
  }
}

/** Schedules a unit level upgrade via the backend mutation. */
async function submitUnitUpgrade(unitId: string) {
  if (!auth.token || schedulingUpgrade.value) return
  schedulingUpgrade.value = true
  unitUpgradeError.value = null
  try {
    await gqlRequest<{ scheduleUnitUpgrade: { id: string } }>(
      `mutation SUU($input: ScheduleUnitUpgradeInput!) {
        scheduleUnitUpgrade(input: $input) { id appliesAtTick totalTicksRequired }
      }`,
      { input: { unitId } },
    )
    // Reload building to show the pending upgrade progress
    await loadBuilding({ preserveDraft: isEditing.value })
    // Refresh upgrade info cache so the panel transitions to pending state
    await fetchUpgradeInfo(unitId)
  } catch (err: unknown) {
    const code = err instanceof GraphQLError ? err.code : undefined
    const raw = err instanceof Error ? err.message : String(err)
    if (code === 'INSUFFICIENT_FUNDS' || raw.includes('INSUFFICIENT_FUNDS')) {
      unitUpgradeError.value = t('buildingDetail.unitUpgrade.errorInsufficientFunds')
    } else if (code === 'MAX_LEVEL_REACHED' || raw.includes('MAX_LEVEL_REACHED')) {
      unitUpgradeError.value = t('buildingDetail.unitUpgrade.errorMaxLevel')
    } else if (code === 'PENDING_CONFIGURATION_EXISTS' || raw.includes('PENDING_CONFIGURATION_EXISTS')) {
      unitUpgradeError.value = t('buildingDetail.unitUpgrade.errorPendingPlan')
    } else {
      unitUpgradeError.value = t('buildingDetail.unitUpgrade.errorGeneric')
    }
  } finally {
    schedulingUpgrade.value = false
  }
}

/**
 * Returns a competitive price suggestion for a B2B_SALES unit based on
 * the product that a linked MANUFACTURING unit will produce.
 * Falls back to the product's base price if found, otherwise null.
 */
function getB2BSuggestedPrice(unit: EditableGridUnit): number | null {
  // Find all units linked to this B2B_SALES unit (either direction)
  const byPos = new Map(draftUnits.value.map((u) => [`${u.gridX},${u.gridY}`, u]))
  const neighbors: EditableGridUnit[] = []
  const directions = [
    { dx: -1, dy: 0 },
    { dx: 1, dy: 0 },
    { dx: 0, dy: -1 },
    { dx: 0, dy: 1 },
  ]
  for (const { dx, dy } of directions) {
    const neighbor = byPos.get(`${unit.gridX + dx},${unit.gridY + dy}`)
    if (neighbor) neighbors.push(neighbor)
  }

  // Look for a connected MANUFACTURING unit with a product set
  const mfgUnit = neighbors.find((n) => n.unitType === 'MANUFACTURING' && n.productTypeId)
  if (!mfgUnit) {
    // Also check the entire building for any MANUFACTURING unit with a product
    const anyMfg = draftUnits.value.find((u) => u.unitType === 'MANUFACTURING' && u.productTypeId)
    if (anyMfg?.productTypeId) {
      return productTypes.value.find((p) => p.id === anyMfg.productTypeId)?.basePrice ?? null
    }
    return null
  }

  return productTypes.value.find((p) => p.id === mfgUnit.productTypeId)?.basePrice ?? null
}

async function loadUnitOperationalStatuses(buildingId: string) {
  if (!auth.token) return
  unitOperationalStatusesLoading.value = true
  try {
    const data = await gqlRequest<{ buildingUnitOperationalStatuses: BuildingUnitOperationalStatus[] }>(
      `query BuildingUnitOperationalStatuses($buildingId: UUID!) {
        buildingUnitOperationalStatuses(buildingId: $buildingId) {
          buildingUnitId
          status
          blockedCode
          blockedReason
          idleTicks
          nextTickLaborCost
          nextTickEnergyCost
        }
      }`,
      { buildingId },
    )
    unitOperationalStatuses.value = data.buildingUnitOperationalStatuses ?? []
  } catch {
    unitOperationalStatuses.value = []
  } finally {
    unitOperationalStatusesLoading.value = false
  }
}

async function loadRecentActivity(buildingId: string) {
  if (!auth.token) return
  recentActivityLoading.value = true
  try {
    const data = await gqlRequest<{ buildingRecentActivity: BuildingRecentActivityEvent[] }>(
      `query BuildingRecentActivity($buildingId: UUID!, $limit: Int) {
        buildingRecentActivity(buildingId: $buildingId, limit: $limit) {
          tick
          buildingUnitId
          eventType
          description
          quantity
          amount
          resourceTypeId
          productTypeId
        }
      }`,
      { buildingId, limit: 30 },
    )
    recentActivity.value = data.buildingRecentActivity ?? []
  } catch {
    recentActivity.value = []
  } finally {
    recentActivityLoading.value = false
  }
}

async function loadBuildingFinancialTimeline(buildingId: string, isRefresh = false) {
  if (!auth.token) {
    buildingFinancialTimeline.value = null
    buildingFinancialTimelineLoading.value = false
    return
  }

  const requestId = ++activeBuildingFinancialTimelineRequest
  if (!isRefresh || buildingFinancialTimeline.value == null) {
    buildingFinancialTimelineLoading.value = true
  }

  try {
    const data = await gqlRequest<{ buildingFinancialTimeline: BuildingFinancialTimeline }>(
      `query BuildingFinancialTimeline($buildingId: UUID!, $limit: Int) {
        buildingFinancialTimeline(buildingId: $buildingId, limit: $limit) {
          buildingId
          buildingName
          dataFromTick
          dataToTick
          totalSales
          totalCosts
          totalProfit
          timeline {
            tick
            sales
            costs
            profit
          }
        }
      }`,
      { buildingId, limit: 100 },
    )
    if (requestId !== activeBuildingFinancialTimelineRequest) {
      return
    }

    buildingFinancialTimeline.value = data.buildingFinancialTimeline
  } catch {
    if (requestId !== activeBuildingFinancialTimelineRequest) {
      return
    }

    if (!isRefresh) {
      buildingFinancialTimeline.value = null
    }
  } finally {
    if (requestId === activeBuildingFinancialTimelineRequest) {
      buildingFinancialTimelineLoading.value = false
    }
  }
}

async function loadBuilding(options: { preserveDraft?: boolean } = {}) {
  const requestId = ++activeBuildingLoadRequest
  const shouldShowLoading = !building.value

  try {
    if (shouldShowLoading) {
      loading.value = true
    }
    error.value = null
    const preserveDraft = options.preserveDraft === true

    const [companiesData, gameStateData, resourceData, productData, citiesData] = await Promise.all([
      gqlRequest<{ myCompanies: Company[] }>(
        `{ myCompanies {
          id
          name
          cash
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
            powerStatus
            isForSale
            askingPrice
            pricePerSqm
            pendingPricePerSqm
            pendingPriceActivationTick
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
              lockedCityId
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
                lockedCityId
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
            resourceType { id name slug unitName unitSymbol weightPerUnit }
            inputProductType { id name slug unitName unitSymbol }
          }
        }
      }`),
      gqlRequest<{ cities: City[] }>(`{ cities { id name } }`),
    ])

    if (requestId !== activeBuildingLoadRequest) {
      return
    }

    currentTick.value = gameStateData.gameState?.currentTick ?? 0
    if (!deepEqual(resourceTypes.value, resourceData.resourceTypes ?? [])) {
      resourceTypes.value = resourceData.resourceTypes ?? []
    }
    if (!deepEqual(productTypes.value, productData.productTypes ?? [])) {
      productTypes.value = productData.productTypes ?? []
    }
    if (!deepEqual(cities.value, citiesData.cities ?? [])) {
      cities.value = citiesData.cities ?? []
    }
    const nextPurchaseVendorCompanies: PurchaseVendorCompanyData[] = companiesData.myCompanies.map((company) => ({
      id: company.id,
      name: company.name,
      buildings: company.buildings.map((candidate) => ({
        id: candidate.id,
        name: candidate.name,
        cityId: candidate.cityId,
        latitude: candidate.latitude,
        longitude: candidate.longitude,
        units: candidate.units.map((unit) => ({
          id: unit.id,
          unitType: unit.unitType,
          resourceTypeId: unit.resourceTypeId,
          productTypeId: unit.productTypeId,
          minPrice: unit.minPrice,
        })),
      })),
    }))
    if (!deepEqual(purchaseVendorCompanies.value, nextPurchaseVendorCompanies)) {
      purchaseVendorCompanies.value = nextPurchaseVendorCompanies
    }

    const allBuildings = companiesData.myCompanies.flatMap((company) => company.buildings)
    const newBuilding = allBuildings.find((candidate) => candidate.id === buildingId.value) || null
    if (!deepEqual(building.value, newBuilding)) {
      building.value = newBuilding
    }

    if (!building.value) {
      companyCash.value = null
      error.value = t('buildingDetail.notFound')
      return
    }

    const newCompanyCash = companiesData.myCompanies.find((company) => company.id === building.value?.companyId)?.cash ?? null
    if (companyCash.value !== newCompanyCash) {
      companyCash.value = newCompanyCash
    }

    if (!preserveDraft) {
      // Normal (non-edit) load: sync the draft editor with the latest server state so
      // the layout editor always shows up-to-date data when the player opens it.
      // When preserveDraft is true (called from useTickRefresh while isEditing is true),
      // these lines are skipped so the player's in-progress draft is never silently
      // discarded by a background tick refresh.  See also: UX convention in copilot-instructions.
      const sourceUnits = pendingConfiguration.value?.units ?? building.value.units
      setDraftUnitsFrom(sourceUnits)
      setEditBaselineFrom(sourceUnits)
      isEditing.value = false
      restoreReadOnlySelectedCell(building.value.units)
      showUnitPicker.value = false
    }

    await Promise.all([loadUnitInventorySummaries(requestId), loadUnitInventories(requestId), loadUnitResourceHistories(requestId)])

    if (requestId !== activeBuildingLoadRequest) {
      return
    }

    await Promise.all([loadGlobalExchangeOffers(), loadResearchBrands(), loadCityMediaHouses()])
    void loadUnitOperationalStatuses(buildingId.value)
    void loadRecentActivity(buildingId.value)
    void loadBuildingFinancialTimeline(buildingId.value)
  } catch (reason: unknown) {
    if (requestId !== activeBuildingLoadRequest) {
      return
    }

    error.value = reason instanceof Error ? reason.message : t('buildingDetail.loadFailed')
  } finally {
    if (requestId === activeBuildingLoadRequest) {
      loading.value = false
    }
  }
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  await loadBuilding()
})

async function fetchRankedProducts(unitType: string) {
  if (!buildingId.value) return
  rankedProductsLoading.value = true
  try {
    const data = await gqlRequest<{ rankedProductTypes: RankedProductResult[] }>(
      `query RankedProducts($buildingId: UUID!, $unitType: String!) {
        rankedProductTypes(buildingId: $buildingId, unitType: $unitType) {
          rankingReason
          rankingScore
          productType {
            id name slug industry imageUrl basePrice baseCraftTicks outputQuantity
            energyConsumptionMwh basicLaborHours unitName unitSymbol isProOnly
            isUnlockedForCurrentPlayer description
            recipes { quantity resourceType { id name slug unitName unitSymbol weightPerUnit } inputProductType { id name slug unitName unitSymbol } }
          }
        }
      }`,
      { buildingId: buildingId.value, unitType },
    )
    rankedProducts.value = data.rankedProductTypes ?? []
  } catch {
    // Fall back to flat productTypes list if the ranked query fails
    rankedProducts.value = productTypes.value.map((pt) => ({
      productType: pt,
      rankingReason: 'catalog' as const,
      rankingScore: 10,
    }))
  } finally {
    rankedProductsLoading.value = false
  }
}

useTickRefresh(async () => {
  if (!auth.isAuthenticated || !building.value) {
    return
  }

  // Save scroll position before data update so the player's reading context is preserved
  const scrollPos = saveScrollPosition()
  try {
    await loadBuilding({ preserveDraft: isEditing.value })
  } finally {
    // Always restore scroll so the player's position is preserved regardless of errors
    await restoreScrollPosition(scrollPos)
  }

  // Refresh analytics for the selected PUBLIC_SALES unit on tick change
  const unitId = getResolvedLiveUnitId(selectedPublicSalesUnit.value)
  if (unitId) {
    void loadPublicSalesAnalytics(unitId, true)
  }
  // Refresh unit product analytics for the selected MANUFACTURING unit on tick change
  const mfgUnitId = getResolvedLiveUnitId(selectedManufacturingUnit.value)
  if (mfgUnitId) {
    void loadUnitProductAnalytics(mfgUnitId, true)
  }
  if (getResolvedLiveUnitId(selectedPurchaseUnit.value)) {
    void loadProcurementPreview(true)
    void loadSourcingCandidates(true)
  }
  void loadUnitOperationalStatuses(buildingId.value)
  void loadRecentActivity(buildingId.value)
  void loadBuildingFinancialTimeline(buildingId.value, true)
})

watch(
  () => route.query.unit,
  () => {
    if (!isEditing.value) {
      restoreReadOnlySelectedCell(activeUnits.value)
    }
  },
)

// Reset flush storage state when user navigates to a different unit
watch(
  () => selectedCell.value,
  () => {
    flushStorageError.value = null
    flushStorageSuccess.value = false
    showFlushConfirmDialog.value = false
    if (!selectedDraftPurchaseUnit.value) {
      showPurchaseSelector.value = false
    }
  },
)

// Fetch ranked products when entering a product-selection unit in edit mode
watch(
  () => {
    if (!isEditing.value || !selectedCell.value) return null
    const unit = getDraftUnitAt(selectedCell.value.x, selectedCell.value.y)
    if (!unit) return null
    const type = unit.unitType
    if (
      type === 'PUBLIC_SALES' ||
      type === 'PRODUCT_QUALITY' ||
      type === 'BRAND_QUALITY' ||
      type === 'STORAGE' ||
      type === 'B2B_SALES'
    )
      return type
    return null
  },
  (unitType) => {
    if (unitType) void fetchRankedProducts(unitType)
    else rankedProducts.value = []
  },
  { immediate: false },
)

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

// Load procurement preview when a purchase unit from the active (non-draft) layout is selected.
watch(
  () => {
    const unit = selectedPurchaseUnit.value
    if (!unit || !('id' in unit)) return null
    // Only load preview for live units (not draft-only units that aren't saved yet).
    if (isEditing.value) return null
    return unit.id
  },
  (unitId) => {
    if (unitId) {
      void loadProcurementPreview()
      void loadSourcingCandidates()
    } else {
      procurementPreview.value = null
      sourcingCandidates.value = []
    }
  },
)

watch(
  () => ({
    unitId: getResolvedLiveUnitId(selectedDisplayUnit.value),
    itemKeys: selectedHistoryItemOptions.value.map((item) => item.key).join('|'),
  }),
  () => {
    if (selectedHistoryItemOptions.value.length === 0) {
      selectedHistoryItemKey.value = null
      return
    }

    const hasSelectedItem = selectedHistoryItemOptions.value.some((item) => item.key === selectedHistoryItemKey.value)
    if (!hasSelectedItem) {
      selectedHistoryItemKey.value = selectedHistoryItemOptions.value[0]?.key ?? null
    }
  },
  { immediate: true },
)

watch(
  () => getResolvedLiveUnitId(selectedPublicSalesUnit.value),
  (unitId) => {
    void loadPublicSalesAnalytics(unitId)
  },
  { immediate: true },
)

watch(
  () => getResolvedLiveUnitId(selectedManufacturingUnit.value),
  (unitId) => {
    void loadUnitProductAnalytics(unitId ?? null)
  },
  { immediate: true },
)

watch(
  () => selectedPublicSalesUnit.value?.id,
  () => {
    quickPriceInput.value = selectedPublicSalesUnit.value?.minPrice ?? null
  },
  { immediate: true },
)

// Fetch upgrade info when a live (non-editing) unit is selected
watch(
  () => {
    if (isEditing.value || !selectedCell.value) return null
    const unit = getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
    return unit?.id ?? null
  },
  (unitId) => {
    unitUpgradeInfoCache.value = null
    unitUpgradeError.value = null
    if (unitId) {
      void fetchUpgradeInfo(unitId)
    }
  },
)
</script>

<template>
  <div class="building-detail-view container">
    <div class="page-nav">
      <RouterLink to="/dashboard" class="back-link"> <span>←</span> {{ t('buildingDetail.backToDashboard') }} </RouterLink>
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
          <span
            v-if="building.powerStatus"
            class="meta-pill power-status-pill"
            :class="{
              'power-status-powered': building.powerStatus === 'POWERED',
              'power-status-constrained': building.powerStatus === 'CONSTRAINED',
              'power-status-offline': building.powerStatus === 'OFFLINE',
            }"
            :title="t(`powerGrid.buildingStatusHint.${building.powerStatus}`, { percent: 50 })"
            role="status"
          >
            <span class="meta-label">{{ t('powerGrid.powerCardTitle') }}</span>
            <span class="meta-value">{{ t(`powerGrid.buildingStatus.${building.powerStatus}`) }}</span>
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
            <button class="btn btn-primary" :disabled="savingSale || !salePrice || salePrice <= 0" @click="setBuildingForSale(true)">
              {{ t('buildingDetail.listForSale') }}
            </button>
            <button v-if="building.isForSale" class="btn btn-danger" :disabled="savingSale" @click="setBuildingForSale(false)">
              {{ t('buildingDetail.cancelSale') }}
            </button>
          </div>
        </div>
      </div>

      <div v-if="showPurchaseSelector" class="purchase-selector-page" role="dialog" :aria-label="t('buildingDetail.purchaseSelector.title')">
        <div class="purchase-selector-shell">
          <div class="purchase-selector-header">
            <div>
              <p class="purchase-selector-eyebrow">{{ t('buildingDetail.purchaseSelector.eyebrow') }}</p>
              <h2>{{ t('buildingDetail.purchaseSelector.title') }}</h2>
            </div>
            <button class="btn btn-ghost" @click="closePurchaseSelector">{{ t('common.close') }}</button>
          </div>

          <div class="purchase-selector-grid">
            <section class="purchase-selector-card">
              <AdvancedItemSelector
                :model-value="selectedPurchaseSelection"
                :items="purchaseSelectorItems"
                :label="t('buildingDetail.config.inputItem')"
                :placeholder="t('buildingDetail.selector.searchPlaceholder')"
                :empty-text="t('buildingDetail.selector.noItems')"
                @update:model-value="applyPurchaseSelection"
              />
            </section>

            <section class="purchase-selector-card">
              <h3>{{ t('buildingDetail.purchaseSelector.vendorTitle') }}</h3>
              <p class="config-help">{{ t('buildingDetail.purchaseSelector.vendorHelp') }}</p>

              <button type="button" class="purchase-vendor-card" :class="{ selected: selectedDraftPurchaseUnit?.vendorLockCompanyId == null }" @click="selectPurchaseVendor(null)">
                <strong>{{ t('buildingDetail.purchaseSelector.vendorAutoTitle') }}</strong>
                <span>{{ t('buildingDetail.purchaseSelector.vendorAuto') }}</span>
              </button>

              <button
                type="button"
                class="purchase-vendor-card"
                :class="{ selected: selectedDraftPurchaseUnit?.vendorLockCompanyId === building.companyId }"
                @click="selectPurchaseVendor(building.companyId)"
              >
                <strong>{{ t('buildingDetail.purchaseSelector.vendorOwnCompany') }}</strong>
                <span>{{ t('buildingDetail.purchaseSelector.vendorOwnCompanyHelp') }}</span>
              </button>

              <div v-if="purchaseVendorOptions.length > 0" class="purchase-vendor-list">
                <button
                  v-for="option in purchaseVendorOptions"
                  :key="`${option.companyId}-${option.buildingId}`"
                  type="button"
                  class="purchase-vendor-card"
                  :class="{ selected: selectedDraftPurchaseUnit?.vendorLockCompanyId === option.companyId }"
                  @click="selectPurchaseVendor(option.companyId)"
                >
                  <strong>{{ option.companyName }}</strong>
                  <span>{{ option.buildingName }}</span>
                  <span v-if="option.pricePerUnit != null" class="purchase-vendor-pricing">
                    {{ t('buildingDetail.purchaseSelector.vendorPrice', { price: formatCurrency(option.pricePerUnit) }) }}
                  </span>
                  <span class="purchase-vendor-pricing">
                    {{ getPurchaseVendorTransitLabel(option.transitCostPerUnit) }}
                  </span>
                </button>
              </div>
              <p v-else class="config-help">{{ t('buildingDetail.purchaseSelector.vendorEmpty') }}</p>
            </section>
          </div>

          <div class="purchase-selector-actions">
            <button class="btn btn-primary" @click="closePurchaseSelector">{{ t('buildingDetail.purchaseSelector.done') }}</button>
          </div>
        </div>
      </div>

      <!-- Property management panel: APARTMENT / COMMERCIAL buildings -->
      <div v-if="building.type === 'APARTMENT' || building.type === 'COMMERCIAL'" class="property-panel" role="region" aria-label="property management">
        <div class="property-panel-header">
          <h2 class="property-panel-title">{{ t('property.panelTitle') }}</h2>
          <button class="btn btn-primary btn-sm" @click="openRentDialog">
            {{ t('property.setRentBtn') }}
          </button>
        </div>

        <!-- Key metrics row -->
        <div class="property-metrics">
          <div class="property-metric">
            <span class="property-metric-label">{{ t('property.totalArea') }}</span>
            <span class="property-metric-value">
              {{ building.totalAreaSqm != null ? building.totalAreaSqm.toLocaleString() + ' m²' : t('common.notAvailable') }}
            </span>
          </div>
          <div class="property-metric">
            <span class="property-metric-label">{{ t('property.occupancy') }}</span>
            <span class="property-metric-value" :class="{ 'property-metric-zero': building.occupancyPercent === 0 }">
              {{ building.occupancyPercent != null ? building.occupancyPercent.toFixed(1) + '%' : t('common.notAvailable') }}
            </span>
          </div>
          <div class="property-metric">
            <span class="property-metric-label">{{ t('property.activeRent') }}</span>
            <span class="property-metric-value">
              {{ building.pricePerSqm != null ? '€' + building.pricePerSqm.toFixed(2) + ' / m²' : t('property.noRentSet') }}
            </span>
          </div>
          <div v-if="building.occupancyPercent != null && building.totalAreaSqm != null" class="property-metric">
            <span class="property-metric-label">{{ t('property.occupiedArea') }}</span>
            <span class="property-metric-value">
              {{ Math.round(building.totalAreaSqm * (building.occupancyPercent / 100)).toLocaleString() }} m² / {{ building.totalAreaSqm.toLocaleString() }} m²
            </span>
          </div>
        </div>

        <!-- Pending rent change notice -->
        <div v-if="building.pendingPricePerSqm != null" class="pending-rent-notice" role="status">
          <span class="pending-rent-icon">⏳</span>
          <span class="pending-rent-text">
            {{
              t('property.pendingRentNotice', {
                rent: '€' + building.pendingPricePerSqm.toFixed(2),
                ticks: building.pendingPriceActivationTick != null ? Math.max(0, building.pendingPriceActivationTick - currentTick) : '—',
              })
            }}
          </span>
        </div>

        <!-- Occupancy empty-state hint -->
        <div v-if="building.occupancyPercent === 0 && building.pricePerSqm == null" class="property-empty-state">
          {{ t('property.noRentHint') }}
        </div>

        <!-- Rent dialog -->
        <div v-if="showRentDialog" class="rent-dialog">
          <div class="rent-dialog-header">
            <h3>{{ t('property.rentDialogTitle') }}</h3>
            <button class="btn btn-ghost" @click="closeRentDialog">{{ t('common.close') }}</button>
          </div>
          <div class="rent-dialog-body">
            <p class="rent-dialog-hint">{{ t('property.rentDelayHint') }}</p>
            <label class="form-label">{{ t('property.rentLabel') }}</label>
            <input
              type="number"
              class="form-input"
              :placeholder="t('property.rentPlaceholder')"
              :value="newRentPerSqm"
              @input="newRentPerSqm = isNaN(($event.target as HTMLInputElement).valueAsNumber) ? null : ($event.target as HTMLInputElement).valueAsNumber"
              min="0"
              step="0.5"
            />
            <p v-if="rentSaveError" class="rent-dialog-error">{{ rentSaveError }}</p>
            <div class="rent-dialog-actions">
              <button class="btn btn-primary" :disabled="savingRent || newRentPerSqm === null || newRentPerSqm < 0" @click="saveRentPerSqm">
                {{ savingRent ? t('common.saving') : t('property.scheduleRentBtn') }}
              </button>
              <button class="btn btn-secondary" @click="closeRentDialog">{{ t('common.cancel') }}</button>
            </div>
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

      <!-- R&D Research Progress panel: RESEARCH_DEVELOPMENT buildings -->
      <div v-if="building.type === 'RESEARCH_DEVELOPMENT'" class="research-progress-panel" role="region" aria-label="research progress">
        <div class="research-progress-header">
          <h2 class="research-progress-title">🔬 {{ t('research.panelTitle') }}</h2>
        </div>
        <p class="research-progress-intro">{{ t('research.intro') }}</p>

        <div v-if="researchBrandsLoading" class="research-loading">{{ t('common.loading') }}</div>

        <div v-else-if="researchBrands.length === 0" class="research-empty-state">
          {{ t('research.emptyState') }}
        </div>

        <div v-else class="research-brand-list">
          <div v-for="brand in researchBrands" :key="brand.id" class="research-brand-card">
            <div class="research-brand-header">
              <span class="research-brand-name">{{ brand.productName || brand.name }}</span>
              <span class="research-brand-scope-badge">
                {{
                  brand.scope === 'PRODUCT' ? t('buildingDetail.config.scopeProduct') : brand.scope === 'CATEGORY' ? t('buildingDetail.config.scopeCategory') : t('buildingDetail.config.scopeCompany')
                }}
              </span>
            </div>
            <div v-if="brand.industryCategory" class="research-brand-industry">
              {{ brand.industryCategory }}
            </div>
            <div class="research-brand-metrics">
              <!-- Product Quality metric (only shown when > 0 or scope is product-quality-relevant) -->
              <div v-if="brand.quality > 0" class="research-metric">
                <span class="research-metric-label">{{ t('research.qualityLabel') }}</span>
                <div class="research-progress-bar" :aria-label="`Product quality ${(brand.quality * 100).toFixed(1)}%`">
                  <div class="research-progress-fill research-progress-quality" :style="{ width: `${(brand.quality * 100).toFixed(1)}%` }"></div>
                </div>
                <span class="research-metric-value">{{ (brand.quality * 100).toFixed(1) }}%</span>
              </div>
              <!-- Marketing Efficiency metric (BRAND_QUALITY R&D result) -->
              <div v-if="brand.marketingEfficiencyMultiplier > 1" class="research-metric">
                <span class="research-metric-label">{{ t('research.marketingEfficiencyLabel') }}</span>
                <div class="research-progress-bar" :aria-label="`Marketing efficiency ${brand.marketingEfficiencyMultiplier.toFixed(2)}x`">
                  <div class="research-progress-fill research-progress-efficiency" :style="{ width: `${Math.min(100, (brand.marketingEfficiencyMultiplier - 1) * 100).toFixed(1)}%` }"></div>
                </div>
                <span class="research-metric-value">{{ brand.marketingEfficiencyMultiplier.toFixed(2) }}×</span>
              </div>
              <!-- Brand Awareness (from marketing spend, informational) -->
              <div v-if="brand.awareness > 0" class="research-metric">
                <span class="research-metric-label">{{ t('research.awarenessLabel') }}</span>
                <div class="research-progress-bar" :aria-label="`Brand awareness ${(brand.awareness * 100).toFixed(1)}%`">
                  <div class="research-progress-fill research-progress-awareness" :style="{ width: `${(brand.awareness * 100).toFixed(1)}%` }"></div>
                </div>
                <span class="research-metric-value">{{ (brand.awareness * 100).toFixed(1) }}%</span>
              </div>
            </div>
            <p class="research-brand-effect">
              <span v-if="brand.quality > 0">
                {{
                  /* Brand quality contributes up to 30% quality bonus to manufactured output (game formula: quality * 30). */
                  t('research.qualityEffect', { pct: (brand.quality * 30).toFixed(1) })
                }}
              </span>
              <span v-if="brand.marketingEfficiencyMultiplier > 1">
                {{ t('research.marketingEfficiencyEffect', { multiplier: brand.marketingEfficiencyMultiplier.toFixed(2) }) }}
              </span>
            </p>
          </div>
        </div>
      </div>

      <!-- Starter factory setup banner (shown for empty factories with no pending plan) -->
      <div v-if="showStarterSetupBanner" class="starter-setup-banner" role="region" aria-label="starter setup">
        <div class="starter-setup-content">
          <h2 class="starter-setup-title">🏭 {{ t('buildingDetail.starterSetup.title') }}</h2>
          <p class="starter-setup-body">{{ t('buildingDetail.starterSetup.body') }}</p>
          <p class="starter-setup-desc">{{ t('buildingDetail.starterSetup.starterLayoutDesc') }}</p>
          <p class="starter-setup-whatnext">{{ t('buildingDetail.starterSetup.whatNext') }}</p>
        </div>
        <div class="starter-setup-actions">
          <button class="btn btn-primary" @click="applyStarterLayout">
            {{ t('buildingDetail.starterSetup.applyStarter') }}
          </button>
        </div>
      </div>

      <!-- Starter sales-shop setup banner (shown for empty sales shops with no pending plan) -->
      <div v-if="showSalesShopStarterBanner" class="starter-setup-banner starter-setup-banner--shop" role="region" aria-label="shop starter setup">
        <div class="starter-setup-content">
          <h2 class="starter-setup-title">🏪 {{ t('buildingDetail.shopStarterSetup.title') }}</h2>
          <p class="starter-setup-body">{{ t('buildingDetail.shopStarterSetup.body') }}</p>
          <p class="starter-setup-desc">{{ t('buildingDetail.shopStarterSetup.starterLayoutDesc') }}</p>
          <p class="starter-setup-whatnext">{{ t('buildingDetail.shopStarterSetup.whatNext') }}</p>
        </div>
        <div class="starter-setup-actions">
          <button class="btn btn-primary" @click="applyShopStarterLayout">
            {{ t('buildingDetail.shopStarterSetup.applyStarter') }}
          </button>
        </div>
      </div>

      <div v-if="isUpgradeInProgress" class="upgrade-banner" role="status">
        <div>
          <strong>{{ t('buildingDetail.upgradeQueuedTitle') }}</strong>
          <p>{{ t('buildingDetail.upgradeQueuedBody', { ticks: remainingUpgradeTicks }) }}</p>
        </div>
        <div class="upgrade-banner-actions">
          <div class="upgrade-pill">
            {{ t('buildingDetail.upgradeAppliesAt', { tick: pendingConfiguration!.appliesAtTick }) }}
          </div>
          <button v-if="!isEditing" class="btn btn-danger btn-sm" :disabled="cancellingPlan" @click="cancelPlan">
            {{ cancellingPlan ? t('common.loading') : t('buildingDetail.cancelPlan') }}
          </button>
        </div>
      </div>
      <div v-if="cancelPlanError" class="error-banner" role="alert">{{ cancelPlanError }}</div>

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

      <!-- Production chain status panel: shown for factories with the starter layout saved -->
      <div v-if="showProductionChainPanel" class="production-chain-panel" role="region" aria-label="production chain status">
        <div class="chain-panel-header">
          <h3 class="chain-panel-title">⚙️ {{ t('buildingDetail.productionChain.title') }}</h3>
          <span v-if="chainStatus.isChainComplete" class="chain-status-badge chain-status-badge--complete">✅ {{ t('buildingDetail.productionChain.chainComplete') }}</span>
          <span v-else class="chain-status-badge chain-status-badge--incomplete">⚠️ {{ t('buildingDetail.productionChain.chainIncomplete') }}</span>
        </div>

        <div class="chain-flow" role="list" aria-label="production chain steps">
          <!-- PURCHASE step -->
          <div class="chain-step" :class="chainStatus.isPurchaseConfigured ? 'chain-step--configured' : 'chain-step--missing'" role="listitem">
            <div class="chain-step-icon">🛒</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.PURCHASE') }}</div>
            <div v-if="chainStatus.isPurchaseConfigured" class="chain-step-value">
              {{ chainDisplayUnits.purchase?.resourceTypeId ? getResourceName(chainDisplayUnits.purchase.resourceTypeId) : getProductName(chainDisplayUnits.purchase?.productTypeId ?? null) }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.productionChain.notConfigured') }}
            </div>
          </div>

          <div class="chain-arrow" aria-hidden="true">→</div>

          <!-- MANUFACTURING step -->
          <div class="chain-step" :class="chainStatus.isManufacturingConfigured ? 'chain-step--configured' : 'chain-step--missing'" role="listitem">
            <div class="chain-step-icon">🏭</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.MANUFACTURING') }}</div>
            <div v-if="chainStatus.isManufacturingConfigured" class="chain-step-value">
              {{ getProductName(chainDisplayUnits.manufacturing?.productTypeId ?? null) }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.productionChain.notConfigured') }}
            </div>
          </div>

          <div class="chain-arrow" aria-hidden="true">→</div>

          <!-- STORAGE step -->
          <div class="chain-step" :class="chainStatus.isStoragePresent ? 'chain-step--configured' : 'chain-step--missing'" role="listitem">
            <div class="chain-step-icon">📦</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.STORAGE') }}</div>
            <div class="chain-step-value">
              {{ chainStatus.isManufacturingConfigured ? getProductName(chainDisplayUnits.manufacturing?.productTypeId ?? null) : t('buildingDetail.productionChain.storageDesc') }}
            </div>
          </div>
        </div>

        <!-- Guidance when chain is incomplete -->
        <div v-if="!chainStatus.isChainComplete" class="chain-guidance">
          <h4 class="chain-guidance-title">{{ t('buildingDetail.productionChain.whatRemains') }}</h4>
          <ul class="chain-todo">
            <li v-if="!chainStatus.isPurchaseConfigured">
              {{ t('buildingDetail.productionChain.todoSelectResource') }}
            </li>
            <li v-if="!chainStatus.isManufacturingConfigured">
              {{ t('buildingDetail.productionChain.todoSelectProduct') }}
            </li>
          </ul>
          <p class="chain-action-hint">{{ t('buildingDetail.productionChain.editHint') }}</p>
        </div>

        <!-- Chain complete celebration -->
        <div v-else class="chain-complete-message">
          <p>
            {{
              t('buildingDetail.productionChain.chainCompleteDesc', {
                product: getProductName(chainDisplayUnits.manufacturing?.productTypeId ?? null),
                resource: chainDisplayUnits.purchase?.resourceTypeId ? getResourceName(chainDisplayUnits.purchase.resourceTypeId) : getProductName(chainDisplayUnits.purchase?.productTypeId ?? null),
              })
            }}
          </p>
          <p class="chain-next-step">{{ t('buildingDetail.productionChain.nextStep') }}</p>
        </div>
      </div>

      <!-- Sales chain status panel: shown for sales shops with units saved -->
      <div v-if="showSalesChainPanel" class="production-chain-panel" role="region" aria-label="sales chain status">
        <div class="chain-panel-header">
          <h3 class="chain-panel-title">🏪 {{ t('buildingDetail.salesChain.title') }}</h3>
          <span v-if="shopChainStatus.isChainComplete" class="chain-status-badge chain-status-badge--complete">✅ {{ t('buildingDetail.salesChain.chainComplete') }}</span>
          <span v-else class="chain-status-badge chain-status-badge--incomplete">⚠️ {{ t('buildingDetail.salesChain.chainIncomplete') }}</span>
        </div>

        <div class="chain-flow" role="list" aria-label="sales chain steps">
          <!-- PURCHASE step -->
          <div class="chain-step" :class="shopChainStatus.isPurchaseConfigured ? 'chain-step--configured' : 'chain-step--missing'" role="listitem">
            <div class="chain-step-icon">🛒</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.PURCHASE') }}</div>
            <div v-if="shopChainStatus.isPurchaseConfigured" class="chain-step-value">
              {{
                shopChainDisplayUnits.purchase?.resourceTypeId ? getResourceName(shopChainDisplayUnits.purchase.resourceTypeId) : getProductName(shopChainDisplayUnits.purchase?.productTypeId ?? null)
              }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.salesChain.notConfigured') }}
            </div>
          </div>

          <div class="chain-arrow" aria-hidden="true">→</div>

          <!-- PUBLIC_SALES step -->
          <div class="chain-step" :class="shopChainStatus.isPublicSalesConfigured ? 'chain-step--configured' : 'chain-step--missing'" role="listitem">
            <div class="chain-step-icon">💲</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.PUBLIC_SALES') }}</div>
            <div v-if="shopChainStatus.isPublicSalesConfigured" class="chain-step-value">
              {{ getProductName(shopChainDisplayUnits.publicSales?.productTypeId ?? null) }}
              · ${{ shopChainDisplayUnits.publicSales?.minPrice?.toFixed(2) }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.salesChain.notConfigured') }}
            </div>
          </div>
        </div>

        <!-- Guidance when chain is incomplete -->
        <div v-if="!shopChainStatus.isChainComplete" class="chain-guidance">
          <h4 class="chain-guidance-title">{{ t('buildingDetail.salesChain.whatRemains') }}</h4>
          <ul class="chain-todo">
            <li v-if="!shopChainStatus.isPurchaseConfigured">
              {{ t('buildingDetail.salesChain.todoPurchaseProduct') }}
            </li>
            <li v-if="!shopChainStatus.isPublicSalesConfigured">
              {{ t('buildingDetail.salesChain.todoPublicSalesPrice') }}
            </li>
          </ul>
          <p class="chain-action-hint">{{ t('buildingDetail.salesChain.editHint') }}</p>
        </div>

        <!-- Chain complete celebration -->
        <div v-else class="chain-complete-message">
          <p>
            {{
              t('buildingDetail.salesChain.chainCompleteDesc', {
                product: getProductName(shopChainDisplayUnits.publicSales?.productTypeId ?? null),
                price: shopChainDisplayUnits.publicSales?.minPrice?.toFixed(2) ?? '0.00',
              })
            }}
          </p>
          <p class="chain-next-step">{{ t('buildingDetail.salesChain.nextStep') }}</p>
        </div>
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
                      :style="
                        getUnitAtFrom(activeUnits, x, y)
                          ? { borderColor: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType) + '18' }
                          : {}
                      "
                      role="button"
                      :tabindex="getUnitAtFrom(activeUnits, x, y) ? 0 : -1"
                      :aria-label="getGridCellAriaLabel(getUnitAtFrom(activeUnits, x, y))"
                      @click="clickReadOnlyCell(x, y)"
                      @keydown.enter.space.prevent="clickReadOnlyCell(x, y)"
                    >
                      <template v-if="getUnitAtFrom(activeUnits, x, y)">
                        <div class="cell-heading" aria-hidden="true">
                          <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(activeUnits, x, y)!.unitType}`) }}</span>
                          <span class="cell-level">Lv.{{ getUnitAtFrom(activeUnits, x, y)!.level }}</span>
                        </div>
                        <div v-if="getUnitDisplayLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-item-block" aria-hidden="true">
                          <img v-if="getUnitDisplayImageUrl(getUnitAtFrom(activeUnits, x, y))" class="cell-item-image" :src="getUnitDisplayImageUrl(getUnitAtFrom(activeUnits, x, y))!" alt="" />
                          <span v-else class="cell-item-avatar">{{ getUnitDisplayMonogram(getUnitAtFrom(activeUnits, x, y)) }}</span>
                          <div class="cell-item-copy">
                            <span class="cell-item">{{ getUnitDisplayLabel(getUnitAtFrom(activeUnits, x, y)) }}</span>
                            <span v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))" class="cell-stock">
                              {{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.quantity) }}/{{
                                formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.capacity)
                              }}
                            </span>
                          </div>
                        </div>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y))" class="cell-metric" aria-hidden="true">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-value" aria-hidden="true">
                          {{ t('buildingDetail.inventory.sourcingCostsShort', { value: getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, x, y)) }) }}
                        </span>
                        <span v-if="getUnitNextTickOperatingCostLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-operating-cost" aria-hidden="true">
                          {{ t('buildingDetail.operatingCost.tileLabel', { cost: getUnitNextTickOperatingCostLabel(getUnitAtFrom(activeUnits, x, y)) }) }}
                        </span>
                        <div v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))?.capacity" class="cell-capacity" aria-hidden="true">
                          <span
                            class="cell-capacity-fill"
                            :data-fill="getFillBucket(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.fillPercent)"
                            :style="{ width: `${Math.round((getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.fillPercent ?? 0) * 100)}%` }"
                          ></span>
                        </div>
                      </template>
                      <template v-else>
                        <span class="cell-empty" aria-hidden="true">+</span>
                      </template>
                    </div>

                    <div
                      v-if="x < 3"
                      :key="`active-horizontal-${x}-${y}`"
                      class="link-toggle horizontal readonly"
                      :class="[
                        `link-state-${getHorizontalLinkStateFor(activeUnits, x, y)}`,
                        { active: isHorizontalLinkActiveFor(activeUnits, x, y), disabled: !canToggleHorizontalLink(activeUnits, x, y) },
                      ]"
                    >
                      <span class="link-line"></span>
                      <span v-if="getHorizontalLinkStateFor(activeUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{
                        getHorizontalLinkArrow(getHorizontalLinkStateFor(activeUnits, x, y))
                      }}</span>
                    </div>
                  </template>
                </div>

                <div v-if="y < 3" class="grid-row connector-row">
                  <template v-for="x in gridIndexes" :key="`active-connector-${x}-${y}`">
                    <div
                      class="link-toggle vertical readonly"
                      :class="[`link-state-${getVerticalLinkStateFor(activeUnits, x, y)}`, { active: isVerticalLinkActiveFor(activeUnits, x, y), disabled: !canToggleVerticalLink(activeUnits, x, y) }]"
                    >
                      <span class="link-line"></span>
                      <span v-if="getVerticalLinkStateFor(activeUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{
                        getVerticalLinkArrow(getVerticalLinkStateFor(activeUnits, x, y))
                      }}</span>
                    </div>

                    <div
                      v-if="x < 3"
                      :key="`active-diagonal-${x}-${y}`"
                      class="link-toggle diagonal readonly"
                      :class="[`state-${getDiagonalStateFor(activeUnits, x, y)}`, { disabled: !canToggleDiagonalLink(activeUnits, x, y) }]"
                    >
                      <span class="diag-line diag-line-primary"></span>
                      <span class="diag-line diag-line-secondary"></span>
                      <span v-if="getDiagonalStateFor(activeUnits, x, y) !== 'none'" class="diag-arrow" aria-hidden="true">{{ getDiagonalLinkLabel(getDiagonalStateFor(activeUnits, x, y)) }}</span>
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
                <button class="btn btn-primary" :disabled="saving || !hasDraftChanges" @click="storeConfiguration">
                  {{ saving ? t('common.loading') : t('buildingDetail.storeConfiguration') }}
                </button>
              </div>
            </div>

            <!-- Inline save error (e.g. RECIPE_INPUT_MISMATCH, PRO_SUBSCRIPTION_REQUIRED) -->
            <div v-if="saveError" class="save-error-banner" role="alert">
              ⚠️ {{ saveError }}
              <button class="btn btn-ghost btn-sm" @click="saveError = null">{{ t('common.close') }}</button>
            </div>

            <div class="upgrade-summary">
              <span class="upgrade-summary-pill">{{ t('buildingDetail.currentTickLabel', { tick: currentTick }) }}</span>
              <span class="upgrade-summary-pill">{{ t('buildingDetail.totalUpgradeTicks', { ticks: draftTotalTicks }) }}</span>
              <span class="upgrade-summary-pill">{{ t('buildingDetail.totalBuildCost', { cost: formatCurrency(draftConstructionCost) }) }}</span>
              <span v-if="projectedCompanyCashAfterApply != null" class="upgrade-summary-pill">
                {{ t('buildingDetail.cashAfterApply', { cash: formatCurrency(projectedCompanyCashAfterApply) }) }}
              </span>
            </div>

            <div v-if="draftLinkChanges.length > 0" class="link-changes-summary" role="region" :aria-label="t('buildingDetail.linkChangesSummaryTitle')">
              <h4 class="link-changes-title">{{ t('buildingDetail.linkChangesSummaryTitle') }}</h4>
              <ul class="link-changes-list">
                <li v-for="(change, i) in draftLinkChanges" :key="i" class="link-change-item" :class="change.changeType === 'added' ? 'link-change-added' : 'link-change-removed'">
                  <span class="link-change-badge" aria-hidden="true">{{ change.changeType === 'added' ? '+' : '−' }}</span>
                  <span>{{ change.description }}</span>
                </li>
              </ul>
            </div>

            <div v-if="draftUnitChanges.length > 0" class="unit-changes-summary" role="region" :aria-label="t('buildingDetail.unitChangesSummaryTitle')">
              <h4 class="unit-changes-title">{{ t('buildingDetail.unitChangesSummaryTitle') }}</h4>
              <ul class="unit-changes-list">
                <li
                  v-for="(change, i) in draftUnitChanges"
                  :key="i"
                  class="unit-change-item"
                  :class="{
                    'unit-change-added': change.changeType === 'added',
                    'unit-change-removed': change.changeType === 'removed',
                    'unit-change-replaced': change.changeType === 'replaced',
                  }"
                >
                  <span class="unit-change-badge" aria-hidden="true">
                    {{ change.changeType === 'added' ? '+' : change.changeType === 'removed' ? '−' : '↺' }}
                  </span>
                  <span class="unit-change-description">
                    <template v-if="change.changeType === 'added'">
                      {{ t('buildingDetail.unitChangeAdded', { type: t(`buildingDetail.unitTypes.${change.unitType}`), x: change.gridX, y: change.gridY }) }}
                    </template>
                    <template v-else-if="change.changeType === 'removed'">
                      {{ t('buildingDetail.unitChangeRemoved', { type: t(`buildingDetail.unitTypes.${change.unitType}`), x: change.gridX, y: change.gridY }) }}
                    </template>
                    <template v-else>
                      {{
                        t('buildingDetail.unitChangeReplaced', {
                          from: t(`buildingDetail.unitTypes.${change.previousUnitType}`),
                          to: t(`buildingDetail.unitTypes.${change.unitType}`),
                          x: change.gridX,
                          y: change.gridY,
                        })
                      }}
                    </template>
                  </span>
                  <span class="unit-change-meta">
                    <span class="unit-change-ticks">{{ t('buildingDetail.unitChangeTicks', { ticks: change.ticks }) }}</span>
                    <span v-if="change.cost > 0" class="unit-change-cost">{{ formatCurrency(change.cost) }}</span>
                  </span>
                </li>
              </ul>
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
                        reverting: isUnitReverting(getUnitAtFrom(plannedUnits, x, y)),
                      }"
                      :style="
                        getUnitAtFrom(plannedUnits, x, y)
                          ? { borderColor: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType) + '18' }
                          : {}
                      "
                      :aria-label="getGridCellAriaLabel(getUnitAtFrom(plannedUnits, x, y))"
                      @click="clickDraftCell(x, y)"
                    >
                      <template v-if="getUnitAtFrom(plannedUnits, x, y)">
                        <div class="cell-heading" aria-hidden="true">
                          <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(plannedUnits, x, y)!.unitType}`) }}</span>
                          <span class="cell-level">Lv.{{ getUnitAtFrom(plannedUnits, x, y)!.level }}</span>
                        </div>
                        <div v-if="getUnitDisplayLabel(getUnitAtFrom(plannedUnits, x, y))" class="cell-item-block" aria-hidden="true">
                          <img v-if="getUnitDisplayImageUrl(getUnitAtFrom(plannedUnits, x, y))" class="cell-item-image" :src="getUnitDisplayImageUrl(getUnitAtFrom(plannedUnits, x, y))!" alt="" />
                          <span v-else class="cell-item-avatar">{{ getUnitDisplayMonogram(getUnitAtFrom(plannedUnits, x, y)) }}</span>
                          <div class="cell-item-copy">
                            <span class="cell-item">{{ getUnitDisplayLabel(getUnitAtFrom(plannedUnits, x, y)) }}</span>
                            <span v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))" class="cell-stock">
                              {{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.quantity) }}/{{
                                formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.capacity)
                              }}
                            </span>
                          </div>
                        </div>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y))" class="cell-metric" aria-hidden="true">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, x, y))" class="cell-value" aria-hidden="true">
                          {{ t('buildingDetail.inventory.sourcingCostsShort', { value: getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, x, y)) }) }}
                        </span>
                        <div v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))?.capacity" class="cell-capacity" aria-hidden="true">
                          <span
                            class="cell-capacity-fill"
                            :data-fill="getFillBucket(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.fillPercent)"
                            :style="{ width: `${Math.round((getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.fillPercent ?? 0) * 100)}%` }"
                          ></span>
                        </div>
                        <span v-if="getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) > 0" class="cell-pending" aria-hidden="true">
                          {{ t('buildingDetail.unitUnavailableFor', { ticks: getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) }) }}
                        </span>
                        <span v-if="isUnitReverting(getUnitAtFrom(plannedUnits, x, y))" class="cell-reverting" aria-hidden="true">{{ t('buildingDetail.reverting') }}</span>
                      </template>
                      <template v-else>
                        <span class="cell-empty" aria-hidden="true">+</span>
                      </template>
                    </button>

                    <button
                      v-if="x < 3"
                      :key="`planned-horizontal-${x}-${y}`"
                      class="link-toggle horizontal"
                      :class="[
                        `link-state-${getHorizontalLinkStateFor(plannedUnits, x, y)}`,
                        { active: isHorizontalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleHorizontalLink(plannedUnits, x, y) },
                      ]"
                      :disabled="!canToggleHorizontalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkHorizontalAriaLabel', { state: getHorizontalLinkStateFor(plannedUnits, x, y) })"
                      @click="toggleHorizontalLink(x, y)"
                    >
                      <span class="link-line"></span>
                      <span v-if="getHorizontalLinkStateFor(plannedUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{
                        getHorizontalLinkArrow(getHorizontalLinkStateFor(plannedUnits, x, y))
                      }}</span>
                    </button>
                  </template>
                </div>

                <div v-if="y < 3" class="grid-row connector-row">
                  <template v-for="x in gridIndexes" :key="`planned-connector-${x}-${y}`">
                    <button
                      class="link-toggle vertical"
                      :class="[
                        `link-state-${getVerticalLinkStateFor(plannedUnits, x, y)}`,
                        { active: isVerticalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleVerticalLink(plannedUnits, x, y) },
                      ]"
                      :disabled="!canToggleVerticalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkVerticalAriaLabel', { state: getVerticalLinkStateFor(plannedUnits, x, y) })"
                      @click="toggleVerticalLink(x, y)"
                    >
                      <span class="link-line"></span>
                      <span v-if="getVerticalLinkStateFor(plannedUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{
                        getVerticalLinkArrow(getVerticalLinkStateFor(plannedUnits, x, y))
                      }}</span>
                    </button>

                    <button
                      v-if="x < 3"
                      :key="`planned-diagonal-${x}-${y}`"
                      class="link-toggle diagonal"
                      :class="[`state-${getDiagonalStateFor(plannedUnits, x, y)}`, { disabled: !canToggleDiagonalLink(plannedUnits, x, y) }]"
                      :disabled="!canToggleDiagonalLink(plannedUnits, x, y)"
                      :aria-label="`${t('buildingDetail.links')} ${getDiagonalLinkLabel(getDiagonalStateFor(plannedUnits, x, y))}`"
                      @click="toggleDiagonalLink(x, y)"
                    >
                      <span class="diag-line diag-line-primary"></span>
                      <span class="diag-line diag-line-secondary"></span>
                      <span v-if="getDiagonalStateFor(plannedUnits, x, y) !== 'none'" class="diag-arrow" aria-hidden="true">{{ getDiagonalLinkLabel(getDiagonalStateFor(plannedUnits, x, y)) }}</span>
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
                  <span class="picker-cost">{{ t('buildingDetail.unitCost', { cost: formatCurrency(getUnitConstructionCost(unitType)) }) }}</span>
                </div>
              </button>
            </div>
          </div>

          <div v-if="!showUnitPicker && getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)" class="unit-config">
            <div class="unit-config-header">
              <h3>{{ t('buildingDetail.unitConfiguration') }}</h3>
              <button class="btn btn-ghost" @click="setReadOnlySelectedCell(null)">{{ t('common.close') }}</button>
            </div>
            <div class="unit-detail">
              <h4>{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</h4>
              <p class="unit-desc">{{ t(`buildingDetail.unitDescriptions.${getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType}`) }}</p>
              <div class="unit-stats">
                <span class="stat">{{ t('common.level') }}: {{ getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.level }}</span>
                <span class="stat">{{ t('buildingDetail.gridPosition', { x: selectedCell.x, y: selectedCell.y }) }}</span>
                <span v-if="getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))" class="stat">
                  {{ t('buildingDetail.unitCost', { cost: getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)) }) }}
                </span>
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
                  <!-- Factory-specific onboarding guide for the Purchase unit -->
                  <p v-if="building?.type === 'FACTORY'" class="config-onboarding-hint">
                    {{ t('buildingDetail.config.factoryPurchaseGuide') }}
                  </p>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.inputItem') }}</label>
                    <button type="button" class="btn btn-secondary purchase-selector-trigger" @click="openPurchaseSelector">
                      {{ selectedPurchaseSelection ? t('buildingDetail.purchaseSelector.changeSelection') : t('buildingDetail.purchaseSelector.chooseSelection') }}
                    </button>
                    <div class="purchase-selection-summary">
                      <strong>
                        {{
                          selectedPurchaseSelection
                            ? selectedPurchaseSelection.kind === 'resource'
                              ? getResourceName(selectedDraftPurchaseUnit?.resourceTypeId ?? null)
                              : getProductName(selectedDraftPurchaseUnit?.productTypeId ?? null)
                            : t('buildingDetail.purchaseSelector.notSelected')
                        }}
                      </strong>
                      <span v-if="selectedPurchaseVendorSummary" class="purchase-selection-meta">
                        {{ selectedPurchaseVendorSummary }}
                      </span>
                      <span v-else class="purchase-selection-meta">
                        {{ t('buildingDetail.purchaseSelector.vendorAuto') }}
                      </span>
                    </div>
                  </div>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.maxPrice') }}</label>
                    <input
                      type="number"
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.maxPrice"
                      @input="updateSelectedUnitConfig('maxPrice', ($event.target as HTMLInputElement).valueAsNumber || null)"
                      min="0"
                      step="0.01"
                    />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minQuality') }}</label>
                    <input
                      type="number"
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minQuality"
                      @input="updateSelectedUnitConfig('minQuality', ($event.target as HTMLInputElement).valueAsNumber || null)"
                      min="0"
                      max="1"
                      step="0.01"
                    />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.procurementMode') }}</label>
                    <p class="config-help">{{ t('buildingDetail.config.procurementModeHelp') }}</p>
                    <div class="procurement-mode-options">
                      <label
                        v-for="mode in ['OPTIMAL', 'EXCHANGE', 'LOCAL']"
                        :key="mode"
                        class="procurement-mode-option"
                        :class="{ selected: (getDraftUnitAt(selectedCell.x, selectedCell.y)!.purchaseSource ?? 'OPTIMAL') === mode }"
                      >
                        <input
                          type="radio"
                          :name="`procurement-mode-${selectedCell.x}-${selectedCell.y}`"
                          :value="mode"
                          :checked="(getDraftUnitAt(selectedCell.x, selectedCell.y)!.purchaseSource ?? 'OPTIMAL') === mode"
                          @change="updateSelectedUnitConfig('purchaseSource', mode)"
                          class="procurement-mode-radio"
                        />
                        <span class="procurement-mode-label">{{ t(`buildingDetail.config.procurementMode_${mode}`) }}</span>
                        <span class="procurement-mode-desc">{{ t(`buildingDetail.config.procurementModeDesc_${mode}`) }}</span>
                      </label>
                    </div>
                  </div>

                  <!-- City lock (shown when EXCHANGE mode is selected) -->
                  <div class="config-field" v-if="(getDraftUnitAt(selectedCell.x, selectedCell.y)!.purchaseSource ?? 'OPTIMAL') === 'EXCHANGE'">
                    <label class="config-label">{{ t('buildingDetail.config.lockedCity') }}</label>
                    <p class="config-help">{{ t('buildingDetail.config.lockedCityHelp') }}</p>
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.lockedCityId ?? ''"
                      @change="updateSelectedUnitConfig('lockedCityId', ($event.target as HTMLSelectElement).value || null)"
                    >
                      <option value="">{{ t('buildingDetail.config.lockedCityAny') }}</option>
                      <option v-for="city in cities" :key="city.id" :value="city.id">{{ city.name }}</option>
                    </select>
                  </div>
                </template>

                <!-- Manufacturing unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MANUFACTURING'">
                  <!-- Factory-specific onboarding guide for the Manufacturing unit -->
                  <p v-if="building?.type === 'FACTORY'" class="config-onboarding-hint">
                    {{ t('buildingDetail.config.factoryManufacturingGuide') }}
                  </p>
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
                    <label class="config-label">{{ t('buildingDetail.config.productType') }}</label>
                    <ProductPicker
                      :model-value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? null"
                      :ranked-products="rankedProducts"
                      :loading="rankedProductsLoading"
                      :allow-none="true"
                      none-label-key="buildingDetail.config.none"
                      @update:model-value="updateSelectedUnitConfig('productTypeId', $event)"
                    />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minPrice') }}</label>
                    <input
                      type="number"
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice"
                      @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).value !== '' ? ($event.target as HTMLInputElement).valueAsNumber : null)"
                      min="0.01"
                      step="0.01"
                    />
                    <p v-if="b2bSuggestedPrice !== null" class="config-help config-price-hint">
                      {{ t('buildingDetail.config.b2bSuggestedPrice', { price: b2bSuggestedPrice!.toFixed(2) }) }}
                      <button type="button" class="btn-link" @click="updateSelectedUnitConfig('minPrice', b2bSuggestedPrice)">{{ t('buildingDetail.config.b2bUseSuggested') }}</button>
                    </p>
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.saleVisibility') }}</label>
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.saleVisibility ?? ''"
                      @change="updateSelectedUnitConfig('saleVisibility', ($event.target as HTMLSelectElement).value || null)"
                    >
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
                    <ProductPicker
                      :model-value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? null"
                      :ranked-products="rankedProducts"
                      :loading="rankedProductsLoading"
                      :allow-none="true"
                      none-label-key="buildingDetail.config.none"
                      @update:model-value="updateSelectedUnitConfig('productTypeId', $event)"
                    />
                  </div>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.minPrice') }}</label>
                    <input
                      type="number"
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice"
                      @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).value !== '' ? ($event.target as HTMLInputElement).valueAsNumber : null)"
                      min="0.01"
                      step="0.01"
                    />
                  </div>
                </template>

                <!-- Marketing unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MARKETING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.budget') }}</label>
                    <input
                      type="number"
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.budget"
                      @input="updateSelectedUnitConfig('budget', ($event.target as HTMLInputElement).valueAsNumber || null)"
                      min="0"
                      step="100"
                    />
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.mediaHouse') }}</label>
                    <div v-if="cityMediaHousesLoading" class="config-loading">{{ t('common.loading') }}</div>
                    <select
                      v-else
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.mediaHouseBuildingId ?? ''"
                      @change="updateSelectedUnitConfig('mediaHouseBuildingId', ($event.target as HTMLSelectElement).value || null)"
                    >
                      <option value="">{{ t('buildingDetail.config.noMediaHouse') }}</option>
                      <option v-for="mh in cityMediaHouses" :key="mh.id" :value="mh.id" :disabled="mh.isUnderConstruction || mh.powerStatus === 'OFFLINE'">
                        {{ mh.name }} ({{ mh.mediaType ?? '?' }}, ×{{ mh.effectivenessMultiplier.toFixed(1) }})
                        <template v-if="mh.isUnderConstruction"> – {{ t('buildingDetail.config.underConstruction') }}</template>
                        <template v-else-if="mh.powerStatus === 'OFFLINE'"> – {{ t('buildingDetail.config.offline') }}</template>
                      </option>
                    </select>
                    <p v-if="cityMediaHouses.length === 0 && !cityMediaHousesLoading" class="config-hint">
                      {{ t('buildingDetail.config.noMediaHouseAvailable') }}
                    </p>
                    <p v-else-if="selectedDraftMediaHouse" class="config-hint">{{ t('buildingDetail.config.channelEffect') }} ×{{ selectedDraftMediaHouse.effectivenessMultiplier.toFixed(1) }}</p>
                  </div>
                </template>

                <!-- Branding unit config -->
                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'BRANDING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.brandScope') }}</label>
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? ''"
                      @change="updateSelectedUnitConfig('brandScope', ($event.target as HTMLSelectElement).value || null)"
                    >
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
                    <ProductPicker
                      :model-value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? null"
                      :ranked-products="rankedProducts"
                      :loading="rankedProductsLoading"
                      :allow-none="true"
                      none-label-key="buildingDetail.config.none"
                      @update:model-value="updateSelectedUnitConfig('productTypeId', $event)"
                    />
                  </div>
                  <p class="config-help">{{ t('buildingDetail.config.researchProductHelp') }}</p>
                  <p class="config-help">{{ t('buildingDetail.proAccessHint') }}</p>
                </template>

                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'BRAND_QUALITY'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.brandScope') }}</label>
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? ''"
                      @change="updateSelectedUnitConfig('brandScope', ($event.target as HTMLSelectElement).value || null)"
                    >
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option value="PRODUCT">{{ t('buildingDetail.config.scopeProduct') }}</option>
                      <option value="CATEGORY">{{ t('buildingDetail.config.scopeCategory') }}</option>
                      <option value="COMPANY">{{ t('buildingDetail.config.scopeCompany') }}</option>
                    </select>
                  </div>
                  <div v-if="['PRODUCT', 'CATEGORY'].includes(getDraftUnitAt(selectedCell.x, selectedCell.y)!.brandScope ?? '')" class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.researchAnchorProduct') }}</label>
                    <ProductPicker
                      :model-value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? null"
                      :ranked-products="rankedProducts"
                      :loading="rankedProductsLoading"
                      :allow-none="true"
                      none-label-key="buildingDetail.config.none"
                      @update:model-value="updateSelectedUnitConfig('productTypeId', $event)"
                    />
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
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.resourceTypeId ?? ''"
                      @change="updateSelectedUnitConfig('resourceTypeId', ($event.target as HTMLSelectElement).value || null)"
                    >
                      <option value="">{{ t('buildingDetail.config.anyResource') }}</option>
                      <option v-for="rt in resourceTypes" :key="rt.id" :value="rt.id">{{ rt.name }}</option>
                    </select>
                  </div>
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.productType') }}</label>
                    <ProductPicker
                      :model-value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.productTypeId ?? null"
                      :ranked-products="storageFilteredRankedProducts"
                      :loading="rankedProductsLoading"
                      :allow-none="true"
                      none-label-key="buildingDetail.config.anyProduct"
                      @update:model-value="updateSelectedUnitConfig('productTypeId', $event)"
                    />
                    <p
                      v-if="storageConnectedProductIds.size > 0"
                      class="config-help"
                    >{{ t('productPicker.helpText') }}</p>
                    <p
                      v-else
                      class="config-help config-help-notice"
                    >{{ t('productPicker.noConnectedProducts') }}</p>
                  </div>
                </template>

                <template v-if="getDraftUnitAt(selectedCell.x, selectedCell.y)!.unitType === 'MINING'">
                  <div class="config-field">
                    <label class="config-label">{{ t('buildingDetail.config.outputResource') }}</label>
                    <select
                      class="form-input"
                      :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.resourceTypeId ?? ''"
                      @change="updateSelectedUnitConfig('resourceTypeId', ($event.target as HTMLSelectElement).value || null)"
                    >
                      <option value="">{{ t('buildingDetail.config.none') }}</option>
                      <option v-for="rt in resourceTypes" :key="rt.id" :value="rt.id">{{ rt.name }} ({{ rt.unitSymbol }})</option>
                    </select>
                  </div>
                </template>
              </div>

              <!-- Read-only unit details for non-editing mode -->
              <div class="unit-config-readonly" v-if="!isEditing && getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)">
                <template
                  v-if="getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!.unitType === 'PURCHASE' && 'resourceTypeId' in getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)!"
                >
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).resourceTypeId"
                    >{{ t('buildingDetail.config.resourceType') }}: {{ getResourceName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).resourceTypeId) }}</span
                  >
                  <span class="stat" v-if="(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId"
                    >{{ t('buildingDetail.config.productType') }}: {{ getProductName((getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y) as EditableGridUnit).productTypeId) }}</span
                  >
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
                <div class="inventory-summary-grid">
                  <div class="inventory-summary-stat">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.load') }}</span>
                    <strong>
                      {{
                        t('buildingDetail.inventory.quantity', {
                          quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.quantity),
                          capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.capacity),
                        })
                      }}
                    </strong>
                  </div>
                  <div class="inventory-summary-stat">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.distinctItems') }}</span>
                    <strong>{{ getUnitInventoryItemCount(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)) }}</strong>
                  </div>
                  <div class="inventory-summary-stat" v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.averageQuality != null">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.averageQuality') }}</span>
                    <strong>{{ formatPercent(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.averageQuality) }}</strong>
                  </div>
                  <div class="inventory-summary-stat" v-if="getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.sourcingCosts') }}</span>
                    <strong>{{ getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)) }}</strong>
                  </div>
                </div>
                <div v-if="getUnitInventories(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)).length > 0" class="inventory-table">
                  <div class="inventory-table-header">
                    <span class="inventory-col-item">{{ t('buildingDetail.inventory.item') }}</span>
                    <span class="inventory-col-quantity">{{ t('buildingDetail.inventory.amount') }}</span>
                    <span class="inventory-col-quality">{{ t('buildingDetail.inventory.quality') }}</span>
                    <span class="inventory-col-cost">{{ t('buildingDetail.inventory.sourcingCost') }}</span>
                  </div>
                  <div v-for="inventory in getUnitInventories(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))" :key="inventory.id" class="inventory-table-row">
                    <div class="inventory-col-item">
                      <img v-if="getInventoryItemImageUrl(inventory)" class="inventory-item-image" :src="getInventoryItemImageUrl(inventory)!" :alt="getInventoryItemName(inventory)" />
                      <span v-else class="inventory-item-avatar">{{ getInventoryItemMonogram(inventory) }}</span>
                      <div class="inventory-item-stack">
                        <span class="inventory-item-name">{{ getInventoryItemName(inventory) }}</span>
                      </div>
                    </div>
                    <div class="inventory-col-quantity">
                      <span class="inventory-item-quantity">{{ formatUnitQuantity(inventory.quantity) }}</span>
                    </div>
                    <div class="inventory-col-quality">
                      <span class="inventory-item-quality">{{ formatPercent(inventory.quality) }}</span>
                    </div>
                    <div class="inventory-col-cost">
                      <span class="inventory-item-cost">{{ getInventoryItemSourcingCostLabel(inventory) }}</span>
                      <span v-if="getInventoryItemSourcingCostPerUnitLabel(inventory)" class="inventory-item-secondary">
                        {{ getInventoryItemSourcingCostPerUnitLabel(inventory) }}
                      </span>
                    </div>
                  </div>
                </div>
                <p v-else class="inventory-empty">{{ t('buildingDetail.inventory.empty') }}</p>
                <div class="detail-capacity">
                  <span
                    class="detail-capacity-fill"
                    :style="{ width: `${Math.round(getUnitInventorySummary(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))!.fillPercent * 100)}%` }"
                  ></span>
                </div>
              </div>

              <UnitResourceHistoryPanel
                v-if="selectedHistoryItemOptions.length > 0"
                :items="selectedHistoryItemOptions"
                :selected-item-key="selectedHistoryItemKey"
                :history="selectedUnitResourceHistory"
                @update:selected-item-key="selectedHistoryItemKey = $event"
              />

              <div v-if="getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))" class="unit-insight-card">
                <h5>{{ t('buildingDetail.costSummaryTitle') }}</h5>
                <div class="unit-stats">
                  <span class="stat">
                    {{ t('buildingDetail.unitCost', { cost: getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)) }) }}
                  </span>
                </div>
              </div>

              <div
                v-if="
                  selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')
                "
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help exchange-selection-hint">{{ t('buildingDetail.exchange.selectionHint') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <template v-else>
                  <p v-if="allExchangeOffersBlocked" class="config-help exchange-no-valid-offers">
                    {{ t('buildingDetail.exchange.noValidOffers') }}
                  </p>
                  <!-- Logistics trap warning -->
                  <div v-if="logisticsTrapWarning" class="logistics-trap-warning" role="alert">
                    {{
                      t('buildingDetail.exchange.logisticsTrap', {
                        cheapCity: logisticsTrapWarning.cheaperStickerCityName,
                        cheapExchange: '$' + logisticsTrapWarning.cheaperStickerExchangePrice,
                        cheapDelivered: '$' + logisticsTrapWarning.cheaperStickerDeliveredPrice,
                        bestCity: logisticsTrapWarning.recommendedCityName,
                        bestDelivered: '$' + logisticsTrapWarning.recommendedDeliveredPrice,
                      })
                    }}
                  </div>
                  <!-- Sort controls -->
                  <div class="exchange-sort-controls" v-if="exchangeOfferItems.length > 1">
                    <span class="exchange-sort-label">{{ t('buildingDetail.exchange.sortBy') }}</span>
                    <button
                      v-for="dim in ['deliveredPrice', 'exchangePrice', 'quality'] as ExchangeSortBy[]"
                      :key="dim"
                      :class="['exchange-sort-btn', { active: exchangeSortBy === dim }]"
                      @click="exchangeSortBy = dim"
                    >
                      {{ t('buildingDetail.exchange.sortOption.' + dim) }}
                    </button>
                  </div>
                  <ul class="exchange-offers-list">
                    <li
                      v-for="offer in exchangeOfferItems"
                      :key="`${offer.cityId}-${offer.resourceTypeId}`"
                      :class="['exchange-offer-item', { 'offer-blocked': offer.blocked, 'offer-best': offer.cityId === bestExchangeOfferCityId }]"
                    >
                      <div class="exchange-offer-header">
                        <strong>{{ offer.cityName }}</strong>
                        <span class="offer-best-badge" v-if="offer.cityId === bestExchangeOfferCityId">{{ t('buildingDetail.exchange.bestOffer') }}</span>
                        <span>{{ t('buildingDetail.exchange.quality', { quality: formatPercent(offer.estimatedQuality) }) }}</span>
                      </div>
                      <div class="exchange-offer-metrics">
                        <span>{{ t('buildingDetail.exchange.exchangePrice', { price: '$' + offer.exchangePricePerUnit, unit: offer.unitSymbol }) }}</span>
                        <span>{{ t('buildingDetail.exchange.transit', { price: '$' + offer.transitCostPerUnit, distance: offer.distanceKm }) }}</span>
                        <span>{{ t('buildingDetail.exchange.deliveredPrice', { price: '$' + offer.deliveredPricePerUnit, unit: offer.unitSymbol }) }}</span>
                      </div>
                      <p v-if="offer.blockedReason === 'maxPrice'" class="offer-blocked-reason">
                        {{ t('buildingDetail.exchange.blockedMaxPrice', { maxPrice: selectedPurchaseUnit?.maxPrice, unit: offer.unitSymbol }) }}
                      </p>
                      <p v-else-if="offer.blockedReason === 'minQuality'" class="offer-blocked-reason">
                        {{ t('buildingDetail.exchange.blockedMinQuality', { minQuality: formatPercent(selectedPurchaseUnit?.minQuality ?? 0) }) }}
                      </p>
                    </li>
                  </ul>
                  <!-- Link to Global Exchange -->
                  <RouterLink v-if="selectedPurchaseResourceSlug" :to="{ name: 'exchange', query: { resource: selectedPurchaseResourceSlug, city: building?.cityId } }" class="exchange-view-link">
                    {{ t('buildingDetail.exchange.viewOnExchange') }}
                  </RouterLink>
                </template>
              </div>

              <div class="unit-actions" v-if="isEditing">
                <button class="btn btn-danger btn-sm" @click="removeDraftUnit(selectedCell.x, selectedCell.y)">
                  {{ t('buildingDetail.removeUnit') }}
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- Read-only unit detail sidebar (click on active grid) -->
        <div class="sidebar" v-else-if="selectedCell && !isEditing && getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)">
          <div class="unit-config">
            <div class="unit-config-header">
              <h3>{{ t('buildingDetail.unitDetails') }}</h3>
              <button class="btn btn-ghost" @click="setReadOnlySelectedCell(null)">{{ t('common.close') }}</button>
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
                  {{ t('buildingDetail.config.procurementMode') }}:
                  {{ t(`buildingDetail.config.procurementMode_${(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y) as BuildingUnit).purchaseSource}`) }}
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

              <!-- Operational status badge for active units -->
              <div
                v-if="selectedActiveUnitOperationalStatus"
                class="unit-insight-card operational-status-card"
                :data-status="selectedActiveUnitOperationalStatus.status"
                aria-label="Unit operational status"
              >
                <h5>{{ t('buildingDetail.operationalStatus.title') }}</h5>
                <div class="operational-status-row">
                  <span class="status-badge" :class="`status-${selectedActiveUnitOperationalStatus.status.toLowerCase()}`">
                    {{ t(`buildingDetail.operationalStatus.${selectedActiveUnitOperationalStatus.status}`) }}
                  </span>
                  <span v-if="selectedActiveUnitOperationalStatus.idleTicks > 0" class="idle-ticks-label">
                    {{ t('buildingDetail.operationalStatus.idleTicks', { count: selectedActiveUnitOperationalStatus.idleTicks }) }}
                  </span>
                </div>
                <p v-if="selectedActiveUnitOperationalStatus.blockedReason" class="blocked-reason-text">
                  {{ selectedActiveUnitOperationalStatus.blockedReason }}
                </p>
                <!-- Next-tick operating costs breakdown -->
                <div v-if="selectedActiveUnitOperationalStatus.nextTickLaborCost != null || selectedActiveUnitOperationalStatus.nextTickEnergyCost != null" class="operating-costs-row">
                  <span class="operating-cost-label">{{ t('buildingDetail.operatingCost.title') }}</span>
                  <span v-if="selectedActiveUnitOperationalStatus.nextTickLaborCost != null" class="operating-cost-item">
                    {{ t('buildingDetail.operatingCost.labor', { cost: formatCurrency(selectedActiveUnitOperationalStatus.nextTickLaborCost) }) }}
                  </span>
                  <span v-if="selectedActiveUnitOperationalStatus.nextTickEnergyCost != null" class="operating-cost-item">
                    {{ t('buildingDetail.operatingCost.energy', { cost: formatCurrency(selectedActiveUnitOperationalStatus.nextTickEnergyCost) }) }}
                  </span>
                </div>
              </div>

              <!-- Unit Upgrade Panel -->
              <div
                v-if="selectedCellUpgradeInfo !== null"
                class="unit-insight-card unit-upgrade-panel"
                aria-label="Unit Upgrade"
              >
                <h5>{{ t('buildingDetail.unitUpgrade.sectionTitle') }}</h5>

                <!-- Upgrade in progress (from pending configuration) -->
                <div v-if="selectedCellPendingUpgrade" class="unit-upgrade-in-progress">
                  <div class="unit-upgrade-progress-badge">⏳</div>
                  <div class="unit-upgrade-progress-body">
                    <strong>{{ t('buildingDetail.unitUpgrade.pendingTitle') }}</strong>
                    <p class="unit-upgrade-progress-desc">
                      {{ t('buildingDetail.unitUpgrade.pendingBody', {
                        level: selectedCellPendingUpgrade.level,
                        ticks: selectedCellPendingUpgrade.ticksRemaining,
                      }) }}
                    </p>
                  </div>
                </div>

                <!-- Max level state -->
                <div v-else-if="selectedCellUpgradeInfo.isMaxLevel" class="unit-upgrade-max-level">
                  <span class="unit-upgrade-max-badge">★</span>
                  <span>{{ t('buildingDetail.unitUpgrade.maxLevel') }}</span>
                  <p class="unit-upgrade-max-note">{{ t('buildingDetail.unitUpgrade.maxLevelNote') }}</p>
                </div>

                <!-- Not upgradable -->
                <div v-else-if="!selectedCellUpgradeInfo.isUpgradable" class="unit-upgrade-not-available">
                  <p>{{ t('buildingDetail.unitUpgrade.notUpgradable') }}</p>
                </div>

                <!-- Upgrade available -->
                <div v-else class="unit-upgrade-available">
                  <div class="unit-upgrade-levels">
                    <span class="unit-upgrade-level current-level">{{ t('buildingDetail.unitUpgrade.currentLevel', { level: selectedCellUpgradeInfo.currentLevel }) }}</span>
                    <span class="unit-upgrade-arrow">→</span>
                    <span class="unit-upgrade-level next-level">{{ t('buildingDetail.unitUpgrade.nextLevel', { level: selectedCellUpgradeInfo.nextLevel }) }}</span>
                  </div>
                  <div class="unit-upgrade-stats">
                    <div class="unit-upgrade-stat-row">
                      <span class="unit-upgrade-stat-label">{{ selectedCellUpgradeInfo.statLabel }}</span>
                      <span class="unit-upgrade-stat-values">
                        <span class="stat-current">{{ selectedCellUpgradeInfo.currentStat.toFixed(1) }}</span>
                        <span class="stat-arrow"> → </span>
                        <span class="stat-next">{{ selectedCellUpgradeInfo.nextStat.toFixed(1) }}</span>
                      </span>
                    </div>
                  </div>
                  <div class="unit-upgrade-meta">
                    <span class="unit-upgrade-cost">{{ t('buildingDetail.unitUpgrade.cost', { cost: formatCurrency(selectedCellUpgradeInfo.upgradeCost) }) }}</span>
                    <span class="unit-upgrade-duration">{{ t('buildingDetail.unitUpgrade.duration', { ticks: selectedCellUpgradeInfo.upgradeTicks }) }}</span>
                  </div>
                  <p v-if="unitUpgradeError" class="form-error">{{ unitUpgradeError }}</p>
                  <button
                    class="btn btn-primary btn-sm unit-upgrade-confirm-btn"
                    :disabled="schedulingUpgrade"
                    @click="submitUnitUpgrade(selectedCellUpgradeInfo!.unitId)"
                  >
                    {{ schedulingUpgrade
                      ? t('buildingDetail.unitUpgrade.confirmingButton')
                      : t('buildingDetail.unitUpgrade.confirmButton') }}
                  </button>
                </div>
              </div>

              <div v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))" class="unit-insight-card">
                <h5>{{ t('buildingDetail.inventory.title') }}</h5>
                <div class="inventory-summary-grid">
                  <div class="inventory-summary-stat">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.load') }}</span>
                    <strong>
                      {{
                        t('buildingDetail.inventory.quantity', {
                          quantity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.quantity),
                          capacity: formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.capacity),
                        })
                      }}
                    </strong>
                  </div>
                  <div class="inventory-summary-stat">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.distinctItems') }}</span>
                    <strong>{{ getUnitInventoryItemCount(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)) }}</strong>
                  </div>
                  <div class="inventory-summary-stat" v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.averageQuality != null">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.averageQuality') }}</span>
                    <strong>{{ formatPercent(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.averageQuality) }}</strong>
                  </div>
                  <div class="inventory-summary-stat" v-if="getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))">
                    <span class="inventory-summary-label">{{ t('buildingDetail.inventory.sourcingCosts') }}</span>
                    <strong>{{ getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)) }}</strong>
                  </div>
                </div>
                <div v-if="getUnitInventories(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)).length > 0" class="inventory-table">
                  <div class="inventory-table-header">
                    <span class="inventory-col-item">{{ t('buildingDetail.inventory.item') }}</span>
                    <span class="inventory-col-quantity">{{ t('buildingDetail.inventory.amount') }}</span>
                    <span class="inventory-col-quality">{{ t('buildingDetail.inventory.quality') }}</span>
                    <span class="inventory-col-cost">{{ t('buildingDetail.inventory.sourcingCost') }}</span>
                  </div>
                  <div v-for="inventory in getUnitInventories(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))" :key="inventory.id" class="inventory-table-row">
                    <div class="inventory-col-item">
                      <img v-if="getInventoryItemImageUrl(inventory)" class="inventory-item-image" :src="getInventoryItemImageUrl(inventory)!" :alt="getInventoryItemName(inventory)" />
                      <span v-else class="inventory-item-avatar">{{ getInventoryItemMonogram(inventory) }}</span>
                      <div class="inventory-item-stack">
                        <span class="inventory-item-name">{{ getInventoryItemName(inventory) }}</span>
                      </div>
                    </div>
                    <div class="inventory-col-quantity">
                      <span class="inventory-item-quantity">{{ formatUnitQuantity(inventory.quantity) }}</span>
                    </div>
                    <div class="inventory-col-quality">
                      <span class="inventory-item-quality">{{ formatPercent(inventory.quality) }}</span>
                    </div>
                    <div class="inventory-col-cost">
                      <span class="inventory-item-cost">{{ getInventoryItemSourcingCostLabel(inventory) }}</span>
                      <span v-if="getInventoryItemSourcingCostPerUnitLabel(inventory)" class="inventory-item-secondary">
                        {{ getInventoryItemSourcingCostPerUnitLabel(inventory) }}
                      </span>
                    </div>
                  </div>
                </div>
                <p v-else class="inventory-empty">{{ t('buildingDetail.inventory.empty') }}</p>
                <div class="detail-capacity">
                  <span
                    class="detail-capacity-fill"
                    :style="{ width: `${Math.round(getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.fillPercent * 100)}%` }"
                  ></span>
                </div>
                <!-- Flush storage action for STORAGE, MINING, and MANUFACTURING units -->
                <div v-if="['STORAGE', 'MINING', 'MANUFACTURING'].includes(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)!.unitType)" class="flush-storage-section">
                  <button
                    class="btn btn-danger btn-sm"
                    :disabled="flushingStorage || getUnitInventorySummary(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y))!.quantity === 0"
                    @click="showFlushConfirmDialog = true"
                  >
                    {{ flushingStorage ? t('buildingDetail.flushStorage.flushing') : t('buildingDetail.flushStorage.title') }}
                  </button>
                  <p v-if="flushStorageError" class="form-error">{{ flushStorageError }}</p>
                  <p v-if="flushStorageSuccess" class="form-success">{{ t('buildingDetail.flushStorage.success') }}</p>
                  <!-- Confirmation dialog -->
                  <div v-if="showFlushConfirmDialog" class="flush-confirm-dialog" role="dialog" :aria-label="t('buildingDetail.flushStorage.confirmTitle')">
                    <p class="flush-confirm-msg">{{ t('buildingDetail.flushStorage.confirmBody') }}</p>
                    <div class="flush-confirm-actions">
                      <button class="btn btn-danger btn-sm" @click="submitFlushStorage(getUnitAtFrom(activeUnits, selectedCell.x, selectedCell.y)!.id)">
                        {{ t('buildingDetail.flushStorage.confirmYes') }}
                      </button>
                      <button class="btn btn-ghost btn-sm" @click="showFlushConfirmDialog = false">{{ t('common.cancel') }}</button>
                    </div>
                  </div>
                </div>
              </div>

              <UnitResourceHistoryPanel
                v-if="selectedHistoryItemOptions.length > 0"
                :items="selectedHistoryItemOptions"
                :selected-item-key="selectedHistoryItemKey"
                :history="selectedUnitResourceHistory"
                @update:selected-item-key="selectedHistoryItemKey = $event"
              />
              <div
                v-if="
                  selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')
                "
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help exchange-selection-hint">{{ t('buildingDetail.exchange.selectionHint') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <template v-else>
                  <p v-if="allExchangeOffersBlocked" class="config-help exchange-no-valid-offers">
                    {{ t('buildingDetail.exchange.noValidOffers') }}
                  </p>
                  <!-- Logistics trap warning -->
                  <div v-if="logisticsTrapWarning" class="logistics-trap-warning" role="alert">
                    {{
                      t('buildingDetail.exchange.logisticsTrap', {
                        cheapCity: logisticsTrapWarning.cheaperStickerCityName,
                        cheapExchange: '$' + logisticsTrapWarning.cheaperStickerExchangePrice,
                        cheapDelivered: '$' + logisticsTrapWarning.cheaperStickerDeliveredPrice,
                        bestCity: logisticsTrapWarning.recommendedCityName,
                        bestDelivered: '$' + logisticsTrapWarning.recommendedDeliveredPrice,
                      })
                    }}
                  </div>
                  <!-- Sort controls -->
                  <div class="exchange-sort-controls" v-if="exchangeOfferItems.length > 1">
                    <span class="exchange-sort-label">{{ t('buildingDetail.exchange.sortBy') }}</span>
                    <button
                      v-for="dim in ['deliveredPrice', 'exchangePrice', 'quality'] as ExchangeSortBy[]"
                      :key="dim"
                      :class="['exchange-sort-btn', { active: exchangeSortBy === dim }]"
                      @click="exchangeSortBy = dim"
                    >
                      {{ t('buildingDetail.exchange.sortOption.' + dim) }}
                    </button>
                  </div>
                  <ul class="exchange-offers-list">
                    <li
                      v-for="offer in exchangeOfferItems"
                      :key="`${offer.cityId}-${offer.resourceTypeId}`"
                      :class="['exchange-offer-item', { 'offer-blocked': offer.blocked, 'offer-best': offer.cityId === bestExchangeOfferCityId }]"
                    >
                      <div class="exchange-offer-header">
                        <strong>{{ offer.cityName }}</strong>
                        <span class="offer-best-badge" v-if="offer.cityId === bestExchangeOfferCityId">{{ t('buildingDetail.exchange.bestOffer') }}</span>
                        <span>{{ t('buildingDetail.exchange.quality', { quality: formatPercent(offer.estimatedQuality) }) }}</span>
                      </div>
                      <div class="exchange-offer-metrics">
                        <span>{{ t('buildingDetail.exchange.exchangePrice', { price: '$' + offer.exchangePricePerUnit, unit: offer.unitSymbol }) }}</span>
                        <span>{{ t('buildingDetail.exchange.transit', { price: '$' + offer.transitCostPerUnit, distance: offer.distanceKm }) }}</span>
                        <span>{{ t('buildingDetail.exchange.deliveredPrice', { price: '$' + offer.deliveredPricePerUnit, unit: offer.unitSymbol }) }}</span>
                      </div>
                      <p v-if="offer.blockedReason === 'maxPrice'" class="offer-blocked-reason">
                        {{ t('buildingDetail.exchange.blockedMaxPrice', { maxPrice: selectedPurchaseUnit?.maxPrice, unit: offer.unitSymbol }) }}
                      </p>
                      <p v-else-if="offer.blockedReason === 'minQuality'" class="offer-blocked-reason">
                        {{ t('buildingDetail.exchange.blockedMinQuality', { minQuality: formatPercent(selectedPurchaseUnit?.minQuality ?? 0) }) }}
                      </p>
                    </li>
                  </ul>
                  <!-- Link to Global Exchange -->
                  <RouterLink v-if="selectedPurchaseResourceSlug" :to="{ name: 'exchange', query: { resource: selectedPurchaseResourceSlug, city: building?.cityId } }" class="exchange-view-link">
                    {{ t('buildingDetail.exchange.viewOnExchange') }}
                  </RouterLink>
                </template>
              </div>

              <!-- Procurement Preview Card (shown in view mode for PURCHASE units) -->
              <div v-if="selectedPurchaseUnit" class="procurement-preview unit-insight-card">
                <h5 class="procurement-preview-title">{{ t('buildingDetail.procurementPreview.title') }}</h5>
                <div v-if="procurementPreviewLoading" class="procurement-preview-loading">{{ t('common.loading') }}…</div>
                <div v-else-if="procurementPreview" class="procurement-preview-content">
                  <div v-if="procurementPreview.canExecute" class="procurement-preview-ok">
                    <span class="preview-status ok">✓ {{ t('buildingDetail.procurementPreview.willExecute') }}</span>
                    <div class="preview-details">
                      <div class="preview-row" v-if="procurementPreview.sourceCityName">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.source') }}</span>
                        <span class="preview-value">{{ procurementPreview.sourceCityName }} ({{ t(`buildingDetail.procurementPreview.sourceType_${procurementPreview.sourceType}`) }})</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.sourceVendorName">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.vendor') }}</span>
                        <span class="preview-value">{{ procurementPreview.sourceVendorName }}</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.exchangePricePerUnit !== null">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.exchangePrice') }}</span>
                        <span class="preview-value">${{ procurementPreview.exchangePricePerUnit?.toFixed(2) }}</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.transitCostPerUnit !== null">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.transitCost') }}</span>
                        <span class="preview-value">${{ procurementPreview.transitCostPerUnit?.toFixed(2) }}</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.deliveredPricePerUnit !== null">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.deliveredPrice') }}</span>
                        <span class="preview-value preview-delivered">${{ procurementPreview.deliveredPricePerUnit?.toFixed(2) }}</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.estimatedQuality !== null">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.quality') }}</span>
                        <span class="preview-value">{{ formatPercent(procurementPreview.estimatedQuality ?? 0) }}</span>
                      </div>
                    </div>
                  </div>
                  <div v-else class="procurement-preview-blocked">
                    <span class="preview-status blocked">✗ {{ t('buildingDetail.procurementPreview.blocked') }}</span>
                    <div class="preview-block-details">
                      <span class="preview-block-reason">{{ t(`buildingDetail.procurementPreview.blockReason_${procurementPreview.blockReason ?? 'UNKNOWN'}`) }}</span>
                      <p class="preview-block-message" v-if="procurementPreview.blockMessage">{{ procurementPreview.blockMessage }}</p>
                    </div>
                    <div class="preview-details" v-if="procurementPreview.deliveredPricePerUnit !== null">
                      <div class="preview-row">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.nearestOffer') }}</span>
                        <span class="preview-value preview-blocked-price">${{ procurementPreview.deliveredPricePerUnit?.toFixed(2) }}</span>
                      </div>
                      <div class="preview-row" v-if="procurementPreview.sourceCityName">
                        <span class="preview-label">{{ t('buildingDetail.procurementPreview.source') }}</span>
                        <span class="preview-value">{{ procurementPreview.sourceCityName }}</span>
                      </div>
                    </div>
                  </div>
                </div>
                <div v-else class="procurement-preview-empty">
                  {{ t('buildingDetail.procurementPreview.notAvailable') }}
                </div>
              </div>

              <!-- Sourcing Comparison Panel (shown in view mode for PURCHASE units with a resource configured) -->
              <div
                v-if="selectedPurchaseUnit && (selectedPurchaseUnit.resourceTypeId || selectedPurchaseUnit.productTypeId)"
                class="sourcing-comparison unit-insight-card"
                aria-label="Sourcing Comparison"
              >
                <h5 class="sourcing-comparison-title">{{ t('buildingDetail.sourcingComparison.title') }}</h5>
                <p class="sourcing-comparison-subtitle config-help">{{ t('buildingDetail.sourcingComparison.subtitle') }}</p>

                <div v-if="sourcingCandidatesLoading" class="sourcing-comparison-loading">
                  {{ t('buildingDetail.sourcingComparison.loading') }}
                </div>

                <template v-else-if="sourcingCandidates.length > 0">
                  <!-- Logistics note: cheapest sticker ≠ best landed -->
                  <p v-if="sourcingCheapestStickerDiffersFromBestLanded" class="sourcing-trap-note">ℹ️ {{ t('buildingDetail.sourcingComparison.cheapestNotBest') }}</p>

                  <!-- Candidate table -->
                  <div class="sourcing-table-wrapper">
                    <table class="sourcing-table">
                      <thead>
                        <tr>
                          <th>{{ t('buildingDetail.sourcingComparison.colSource') }}</th>
                          <th>{{ t('buildingDetail.sourcingComparison.colOfferPrice') }}</th>
                          <th>{{ t('buildingDetail.sourcingComparison.colTransit') }}</th>
                          <th class="col-landed">{{ t('buildingDetail.sourcingComparison.colLanded') }}</th>
                          <th>{{ t('buildingDetail.sourcingComparison.colQuality') }}</th>
                          <th>{{ t('buildingDetail.sourcingComparison.colStatus') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr
                          v-for="candidate in sourcingCandidates"
                          :key="`${candidate.rank}-${candidate.sourceCityId ?? candidate.sourceVendorCompanyId}`"
                          :class="['sourcing-row', candidate.isRecommended ? 'recommended' : '', !candidate.isEligible ? 'ineligible' : '']"
                        >
                          <td class="sourcing-col-source">
                            <span class="source-type-badge">{{ t(`buildingDetail.sourcingComparison.sourceType_${candidate.sourceType}`) }}</span>
                            <span class="source-name">
                              {{ candidate.sourceCityName ?? candidate.sourceVendorName ?? '—' }}
                            </span>
                            <span v-if="candidate.distanceKm && candidate.distanceKm > 0" class="source-distance">
                              {{ t('buildingDetail.sourcingComparison.distanceKm', { km: Math.round(candidate.distanceKm) }) }}
                            </span>
                          </td>
                          <td class="sourcing-col-offer">
                            <span v-if="candidate.exchangePricePerUnit !== null"> ${{ candidate.exchangePricePerUnit.toFixed(2) }} </span>
                            <span v-else-if="candidate.deliveredPricePerUnit !== null"> ${{ candidate.deliveredPricePerUnit.toFixed(2) }} </span>
                            <span v-else>—</span>
                          </td>
                          <td class="sourcing-col-transit">
                            <span v-if="candidate.transitCostPerUnit !== null" class="transit-cost"> +${{ candidate.transitCostPerUnit.toFixed(2) }} </span>
                            <span v-else>—</span>
                          </td>
                          <td class="sourcing-col-landed col-landed">
                            <strong v-if="candidate.deliveredPricePerUnit !== null"> ${{ candidate.deliveredPricePerUnit.toFixed(2) }} </strong>
                            <span v-else>—</span>
                          </td>
                          <td class="sourcing-col-quality">
                            <span v-if="candidate.estimatedQuality !== null">{{ formatPercent(candidate.estimatedQuality) }}</span>
                            <span v-else>—</span>
                          </td>
                          <td class="sourcing-col-status">
                            <span v-if="candidate.isRecommended" class="sc-badge sc-badge--recommended"> ★ {{ t('buildingDetail.sourcingComparison.recommended') }} </span>
                            <span v-else-if="candidate.isEligible" class="sc-badge sc-badge--eligible">
                              {{ t('buildingDetail.sourcingComparison.eligible') }}
                            </span>
                            <span v-else class="sc-badge sc-badge--blocked" :title="candidate.blockMessage ?? ''">
                              {{ t(`buildingDetail.sourcingComparison.blockReason_${candidate.blockReason ?? 'UNKNOWN'}`) }}
                            </span>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <!-- Filter hint when some candidates are blocked -->
                  <p v-if="sourcingCandidates.some((c) => !c.isEligible)" class="sourcing-filter-hint config-help">
                    {{ t('buildingDetail.sourcingComparison.filterHint') }}
                  </p>
                </template>

                <div v-else class="sourcing-comparison-empty">
                  {{ t('buildingDetail.sourcingComparison.empty') }}
                </div>
              </div>
              <div v-if="selectedPublicSalesUnit" class="unit-insight-card market-intelligence-panel" aria-label="Market Intelligence">
                <h5>{{ t('buildingDetail.marketIntelligence.title') }}</h5>

                <!-- Product identity + data window row -->
                <div class="mi-context-row">
                  <span v-if="publicSalesAnalytics?.productName" class="mi-product-chip" aria-label="Currently selling product">
                    {{ publicSalesAnalytics.productName }}
                  </span>
                  <span v-if="publicSalesAnalytics && publicSalesAnalytics.dataFromTick > 0" class="mi-tick-window">
                    T{{ publicSalesAnalytics.dataFromTick }}–T{{ publicSalesAnalytics.dataToTick }}
                  </span>
                </div>

                <p v-if="publicSalesAnalyticsLoading" class="config-help">{{ t('buildingDetail.marketIntelligence.loading') }}</p>

                <template v-else-if="publicSalesAnalytics">
                  <!-- Summary metrics -->
                  <div class="mi-summary-grid">
                    <div class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.totalRevenue') }}</span>
                      <strong class="mi-metric-value">{{ formatCurrency(publicSalesAnalytics.totalRevenue) }}</strong>
                    </div>
                    <div class="mi-metric" v-if="publicSalesAnalytics.totalProfit !== null">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.totalProfit') }}</span>
                      <strong
                        class="mi-metric-value"
                        :class="{
                          'building-profit-positive-text': publicSalesAnalytics.totalProfit >= 0,
                          'building-profit-negative-text': publicSalesAnalytics.totalProfit < 0,
                        }"
                      >{{ formatCurrency(publicSalesAnalytics.totalProfit) }}</strong>
                    </div>
                    <div class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.totalSold') }}</span>
                      <strong class="mi-metric-value">{{ formatUnitQuantity(publicSalesAnalytics.totalQuantitySold) }}</strong>
                    </div>
                    <div class="mi-metric" v-if="publicSalesAnalytics.averagePricePerUnit > 0">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.avgPrice') }}</span>
                      <strong class="mi-metric-value">{{ formatCurrency(publicSalesAnalytics.averagePricePerUnit) }}</strong>
                    </div>
                    <div class="mi-metric" v-if="selectedPublicSalesUnit.minPrice != null">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.configuredPrice') }}</span>
                      <strong class="mi-metric-value">{{ formatCurrency(currentPublicSalesMinPrice) }}</strong>
                    </div>
                    <div class="mi-metric" v-if="publicSalesAnalytics.revenueHistory.length > 0">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.recentUtilization') }}</span>
                      <strong class="mi-metric-value">{{ Math.round(publicSalesAnalytics.recentUtilization * 100) }}%</strong>
                    </div>
                    <!-- Trend direction (only shown when there are at least 2 ticks of history) -->
                    <div
                      v-if="publicSalesAnalytics.trendDirection && publicSalesAnalytics.trendDirection !== 'NO_DATA'"
                      class="mi-metric"
                    >
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.trend') }}</span>
                      <strong
                        class="mi-metric-value mi-trend"
                        :class="{
                          'mi-trend-up': publicSalesAnalytics.trendDirection === 'UP',
                          'mi-trend-down': publicSalesAnalytics.trendDirection === 'DOWN',
                          'mi-trend-flat': publicSalesAnalytics.trendDirection === 'FLAT',
                        }"
                      >
                        {{
                          publicSalesAnalytics.trendDirection === 'UP'
                            ? t('buildingDetail.marketIntelligence.trendUp')
                            : publicSalesAnalytics.trendDirection === 'DOWN'
                              ? t('buildingDetail.marketIntelligence.trendDown')
                              : t('buildingDetail.marketIntelligence.trendFlat')
                        }}
                      </strong>
                    </div>
                    <!-- Market trend factor (live trend multiplier from the simulation) -->
                    <div v-if="publicSalesAnalytics.trendFactor !== null" class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.marketIntelligence.trendFactor') }}</span>
                      <strong
                        class="mi-metric-value mi-trend"
                        :class="{
                          'mi-trend-up': publicSalesAnalytics.trendFactor > 1.05,
                          'mi-trend-down': publicSalesAnalytics.trendFactor < 0.95,
                          'mi-trend-flat': publicSalesAnalytics.trendFactor >= 0.95 && publicSalesAnalytics.trendFactor <= 1.05,
                        }"
                      >
                        {{ publicSalesAnalytics.trendFactor > 1 ? '+' : '' }}{{ ((publicSalesAnalytics.trendFactor - 1) * 100).toFixed(0) }}%
                      </strong>
                    </div>
                  </div>

                  <!-- No-history empty state -->
                  <p v-if="publicSalesAnalytics.revenueHistory.length === 0" class="mi-empty-state">
                    {{ t('buildingDetail.marketIntelligence.noHistory') }}
                  </p>

                  <template v-else>
                    <!-- Revenue mini chart -->
                    <div class="mi-chart-section">
                      <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.revenueChart') }}</span>
                      <div class="mi-bar-chart" role="img" :aria-label="t('buildingDetail.marketIntelligence.revenueChart')">
                        <div
                          v-for="snap in publicSalesAnalytics.revenueHistory"
                          :key="snap.tick"
                          class="mi-bar mi-bar-revenue"
                          :style="{
                            height: `${Math.max(2, miMaxRevenue > 0 ? (snap.revenue / miMaxRevenue) * 100 : 0).toFixed(1)}%`,
                          }"
                          :title="`T${snap.tick}: ${formatCurrency(snap.revenue)}`"
                        ></div>
                      </div>
                    </div>

                    <!-- Quantity mini chart -->
                    <div class="mi-chart-section">
                      <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.quantityChart') }}</span>
                      <div class="mi-bar-chart" role="img" :aria-label="t('buildingDetail.marketIntelligence.quantityChart')">
                        <div
                          v-for="snap in publicSalesAnalytics.revenueHistory"
                          :key="snap.tick"
                          class="mi-bar mi-bar-quantity"
                          :style="{
                            height: `${Math.max(2, miMaxQuantitySold > 0 ? (snap.quantitySold / miMaxQuantitySold) * 100 : 0).toFixed(1)}%`,
                          }"
                          :title="`T${snap.tick}: ${formatUnitQuantity(snap.quantitySold)}`"
                        ></div>
                      </div>
                    </div>

                    <!-- Price history chart -->
                    <div v-if="publicSalesAnalytics.priceHistory.length > 0" class="mi-chart-section">
                      <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.priceChart') }}</span>
                      <div class="mi-bar-chart mi-bar-chart-price" role="img" :aria-label="t('buildingDetail.marketIntelligence.priceChart')">
                        <div
                          v-for="snap in publicSalesAnalytics.priceHistory"
                          :key="snap.tick"
                          class="mi-bar mi-bar-price"
                          :style="{
                            height: `${Math.max(2, miMaxPricePerUnit > 0 ? (snap.pricePerUnit / miMaxPricePerUnit) * 100 : 0).toFixed(1)}%`,
                          }"
                          :title="`T${snap.tick}: ${formatCurrency(snap.pricePerUnit)}`"
                        ></div>
                      </div>
                    </div>

                    <!-- Profit history chart -->
                    <div v-if="publicSalesAnalytics.profitHistory && publicSalesAnalytics.profitHistory.length > 0" class="mi-chart-section">
                      <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.profitChart') }}</span>
                      <div class="mi-bar-chart mi-bar-chart-profit" role="img" :aria-label="t('buildingDetail.marketIntelligence.profitChart')">
                        <div
                          v-for="snap in publicSalesAnalytics.profitHistory"
                          :key="snap.tick"
                          class="mi-bar"
                          :class="snap.profit >= 0 ? 'mi-bar-profit-positive' : 'mi-bar-profit-negative'"
                          :style="{
                            height: `${Math.max(2, miMaxAbsProfit > 0 ? (Math.abs(snap.profit) / miMaxAbsProfit) * 100 : 0).toFixed(1)}%`,
                          }"
                          :title="`T${snap.tick}: ${formatCurrency(snap.profit)}${snap.grossMarginPct !== null ? ` (${snap.grossMarginPct.toFixed(1)}% margin)` : ''}`"
                        ></div>
                      </div>
                    </div>
                  </template>

                  <!-- Market share -->
                  <div class="mi-section">
                    <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.marketShare') }}</span>
                    <p v-if="publicSalesAnalytics.marketShare.length === 0" class="config-help">
                      {{ t('buildingDetail.marketIntelligence.noMarketShare') }}
                    </p>
                    <div v-else class="mi-market-share">
                      <div
                        v-for="entry in publicSalesAnalytics.marketShare"
                        :key="entry.label"
                        class="mi-share-row"
                        :class="{ 'mi-share-row-you': entry.companyId === building?.companyId, 'mi-share-row-unmet': entry.isUnmet }"
                      >
                        <span class="mi-share-label"> {{ entry.label }}{{ entry.companyId === building?.companyId ? ' ★' : '' }}{{ entry.isUnmet ? ' ⬚' : '' }} </span>
                        <div class="mi-share-bar-wrap">
                          <div class="mi-share-bar" :class="{ 'mi-share-bar-unmet': entry.isUnmet }" :style="{ width: `${(entry.share * 100).toFixed(1)}%` }"></div>
                        </div>
                        <span class="mi-share-pct">{{ (entry.share * 100).toFixed(1) }}%</span>
                      </div>
                    </div>
                  </div>

                  <!-- Demand Drivers -->
                  <div v-if="publicSalesAnalytics.demandDrivers.length > 0" class="mi-demand-drivers" aria-label="Demand Drivers">
                    <span class="mi-chart-label">{{ t('buildingDetail.marketIntelligence.demandDrivers.title') }}</span>
                    <div class="mi-driver-list">
                      <div
                        v-for="driver in publicSalesAnalytics.demandDrivers"
                        :key="driver.factor"
                        class="mi-driver-entry"
                        :class="`mi-driver-${driver.impact.toLowerCase()}`"
                      >
                        <span class="mi-driver-icon">
                          {{ driver.impact === 'POSITIVE' ? '↑' : driver.impact === 'NEGATIVE' ? '↓' : '→' }}
                        </span>
                        <div class="mi-driver-content">
                          <strong class="mi-driver-factor">{{ t(`buildingDetail.marketIntelligence.demandDrivers.factor_${driver.factor}`) }}</strong>
                          <span class="mi-driver-desc">{{ driver.description }}</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  <!-- Elasticity index + context card -->
                  <div class="mi-context-card">
                    <div class="mi-context-grid">
                      <div v-if="publicSalesAnalytics.elasticityIndex !== null" class="mi-context-item">
                        <span class="mi-context-label">{{ t('buildingDetail.marketIntelligence.elasticityIndex') }}</span>
                        <strong
                          class="mi-context-value"
                          :class="{ 'mi-elastic-high': (publicSalesAnalytics.elasticityIndex ?? 0) < -1.5, 'mi-elastic-low': (publicSalesAnalytics.elasticityIndex ?? 0) > -0.5 }"
                        >
                          {{ publicSalesAnalytics.elasticityIndex.toFixed(2) }}
                        </strong>
                        <span class="mi-context-hint">{{ t('buildingDetail.marketIntelligence.elasticityHint') }}</span>
                      </div>
                      <div v-if="publicSalesAnalytics.populationIndex !== null" class="mi-context-item">
                        <span class="mi-context-label">{{ t('buildingDetail.marketIntelligence.populationIndex') }}</span>
                        <strong class="mi-context-value">{{ publicSalesAnalytics.populationIndex.toFixed(2) }}×</strong>
                        <span class="mi-context-hint">{{ t('buildingDetail.marketIntelligence.populationIndexHint') }}</span>
                      </div>
                      <div v-if="publicSalesAnalytics.inventoryQuality !== null" class="mi-context-item">
                        <span class="mi-context-label">{{ t('buildingDetail.marketIntelligence.productQuality') }}</span>
                        <strong class="mi-context-value" :class="{ 'mi-quality-high': publicSalesAnalytics.inventoryQuality >= 0.7, 'mi-quality-low': publicSalesAnalytics.inventoryQuality < 0.4 }">
                          {{ Math.round(publicSalesAnalytics.inventoryQuality * 100) }}%
                        </strong>
                        <span class="mi-context-hint">{{ t('buildingDetail.marketIntelligence.productQualityHint') }}</span>
                      </div>
                      <div v-if="publicSalesAnalytics.brandAwareness !== null" class="mi-context-item">
                        <span class="mi-context-label">{{ t('buildingDetail.marketIntelligence.brandAwareness') }}</span>
                        <strong class="mi-context-value" :class="{ 'mi-quality-high': publicSalesAnalytics.brandAwareness >= 0.6 }">
                          {{ Math.round(publicSalesAnalytics.brandAwareness * 100) }}%
                        </strong>
                        <span class="mi-context-hint">{{ t('buildingDetail.marketIntelligence.brandAwarenessHint') }}</span>
                      </div>
                    </div>
                  </div>

                  <!-- Demand signal -->
                  <div class="mi-demand-card" :class="`mi-demand-${publicSalesAnalytics.demandSignal.toLowerCase().replace(/_/g, '-')}`">
                    <div class="mi-demand-header">
                      <span class="mi-demand-title">{{ t('buildingDetail.marketIntelligence.demandSignal.title') }}</span>
                      <span class="mi-demand-badge">{{ t(`buildingDetail.marketIntelligence.demandSignal.${publicSalesAnalytics.demandSignal}`) }}</span>
                    </div>
                    <p class="mi-action-hint" v-if="publicSalesAnalytics.actionHint">
                      <strong>{{ t('buildingDetail.marketIntelligence.actionHint') }}:</strong>
                      {{ publicSalesAnalytics.actionHint }}
                    </p>
                  </div>

                  <!-- Quick price update -->
                  <div class="mi-price-update-panel" aria-label="Quick Price Update">
                    <h6 class="mi-price-update-title">{{ t('buildingDetail.marketIntelligence.priceUpdate.title') }}</h6>
                    <p class="mi-price-update-desc">{{ t('buildingDetail.marketIntelligence.priceUpdate.desc') }}</p>

                    <!-- Directional impact hint derived from elasticity -->
                    <div
                      v-if="publicSalesAnalytics.elasticityIndex !== null && quickPriceInput !== null && currentPublicSalesMinPrice > 0"
                      class="mi-price-impact-hint"
                      :class="{
                        'mi-price-impact-raise': quickPriceInput > currentPublicSalesMinPrice,
                        'mi-price-impact-lower': quickPriceInput < currentPublicSalesMinPrice,
                      }"
                    >
                      <template v-if="quickPriceInput > currentPublicSalesMinPrice">
                        {{
                          t('buildingDetail.marketIntelligence.priceUpdate.raisingHint', {
                            elasticity: Math.abs(publicSalesAnalytics.elasticityIndex).toFixed(1),
                          })
                        }}
                      </template>
                      <template v-else-if="quickPriceInput < currentPublicSalesMinPrice">
                        {{
                          t('buildingDetail.marketIntelligence.priceUpdate.loweringHint', {
                            elasticity: Math.abs(publicSalesAnalytics.elasticityIndex).toFixed(1),
                          })
                        }}
                      </template>
                    </div>

                    <div class="mi-price-update-row">
                      <label class="mi-price-update-label" for="quick-price-input">
                        {{ t('buildingDetail.marketIntelligence.priceUpdate.newPrice') }}
                      </label>
                      <input
                        id="quick-price-input"
                        type="number"
                        class="mi-price-input"
                        :placeholder="selectedPublicSalesUnit?.minPrice?.toString() ?? ''"
                        :min="0.01"
                        :step="0.01"
                        v-model.number="quickPriceInput"
                      />
                      <button class="btn btn-primary mi-price-update-btn" :disabled="quickPriceSaving || quickPriceInput === null || quickPriceInput <= 0" @click="submitQuickPriceUpdate">
                        {{ quickPriceSaving ? t('buildingDetail.marketIntelligence.priceUpdate.saving') : t('buildingDetail.marketIntelligence.priceUpdate.apply') }}
                      </button>
                    </div>
                    <p v-if="quickPriceSuccess" class="mi-price-success">
                      {{ t('buildingDetail.marketIntelligence.priceUpdate.success') }}
                    </p>
                    <p v-if="quickPriceError" class="mi-price-error">{{ quickPriceError }}</p>
                  </div>
                </template>

                <p v-else class="config-help">{{ t('buildingDetail.marketIntelligence.loadFailed') }}</p>
              </div>

              <!-- Manufacturing Unit Product Analytics Panel -->
              <div
                v-if="selectedManufacturingUnit && (selectedManufacturingUnit.productTypeId || unitProductAnalytics)"
                class="unit-insight-card unit-product-analytics-panel"
                aria-label="Product Performance Analytics"
              >
                <h5>{{ t('buildingDetail.unitProductAnalytics.title') }}</h5>

                <!-- Product identity + data window row -->
                <div class="mi-context-row">
                  <span v-if="unitProductAnalytics?.productName" class="mi-product-chip" aria-label="Currently producing product">
                    {{ unitProductAnalytics.productName }}
                  </span>
                  <span v-else-if="selectedManufacturingUnit.productTypeId" class="mi-product-chip">
                    {{ t('buildingDetail.unitProductAnalytics.productConfigured') }}
                  </span>
                  <span v-if="unitProductAnalytics && unitProductAnalytics.dataFromTick > 0" class="mi-tick-window">
                    T{{ unitProductAnalytics.dataFromTick }}–T{{ unitProductAnalytics.dataToTick }}
                  </span>
                </div>

                <p v-if="unitProductAnalyticsLoading" class="config-help">{{ t('buildingDetail.unitProductAnalytics.loading') }}</p>

                <template v-else-if="unitProductAnalytics && unitProductAnalytics.snapshots.length > 0">
                  <!-- Summary metrics -->
                  <div class="mi-summary-grid">
                    <div class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.unitProductAnalytics.totalProduced') }}</span>
                      <strong class="mi-metric-value">{{ formatUnitQuantity(unitProductAnalytics.totalQuantityProduced) }}</strong>
                    </div>
                    <div class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.unitProductAnalytics.totalCost') }}</span>
                      <strong class="mi-metric-value building-profit-negative-text">{{ formatCurrency(unitProductAnalytics.totalCost) }}</strong>
                    </div>
                    <div v-if="unitProductAnalytics.estimatedRevenue !== null" class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.unitProductAnalytics.estimatedRevenue') }}</span>
                      <strong class="mi-metric-value">{{ formatCurrency(unitProductAnalytics.estimatedRevenue) }}</strong>
                    </div>
                    <div v-if="unitProductAnalytics.estimatedProfit !== null" class="mi-metric">
                      <span class="mi-metric-label">{{ t('buildingDetail.unitProductAnalytics.estimatedProfit') }}</span>
                      <strong
                        class="mi-metric-value"
                        :class="{
                          'building-profit-positive-text': unitProductAnalytics.estimatedProfit >= 0,
                          'building-profit-negative-text': unitProductAnalytics.estimatedProfit < 0,
                        }"
                      >{{ formatCurrency(unitProductAnalytics.estimatedProfit) }}</strong>
                    </div>
                  </div>

                  <!-- Cost history chart -->
                  <div v-if="unitProductAnalytics.snapshots.length > 0" class="mi-chart-section">
                    <span class="mi-chart-label">{{ t('buildingDetail.unitProductAnalytics.costChart') }}</span>
                    <div class="mi-bar-chart mi-bar-chart-cost" role="img" :aria-label="t('buildingDetail.unitProductAnalytics.costChart')">
                      <div
                        v-for="snap in unitProductAnalytics.snapshots"
                        :key="snap.tick"
                        class="mi-bar mi-bar-cost"
                        :style="{
                          height: `${Math.max(2, upaMaxCost > 0 ? (snap.totalCost / upaMaxCost) * 100 : 0).toFixed(1)}%`,
                        }"
                        :title="`T${snap.tick}: ${formatCurrency(snap.totalCost)} (${t('buildingDetail.unitProductAnalytics.labor')}: ${formatCurrency(snap.laborCost)}, ${t('buildingDetail.unitProductAnalytics.energy')}: ${formatCurrency(snap.energyCost)})`"
                      ></div>
                    </div>
                  </div>

                  <!-- Estimated revenue chart -->
                  <div v-if="unitProductAnalytics.snapshots.some((s) => s.estimatedRevenue !== null && s.estimatedRevenue > 0)" class="mi-chart-section">
                    <span class="mi-chart-label">{{ t('buildingDetail.unitProductAnalytics.estimatedRevenueChart') }}</span>
                    <div class="mi-bar-chart mi-bar-chart-revenue" role="img" :aria-label="t('buildingDetail.unitProductAnalytics.estimatedRevenueChart')">
                      <div
                        v-for="snap in unitProductAnalytics.snapshots"
                        :key="snap.tick"
                        class="mi-bar mi-bar-revenue"
                        :style="{
                          height: `${Math.max(2, upaMaxEstRevenue > 0 ? ((snap.estimatedRevenue ?? 0) / upaMaxEstRevenue) * 100 : 0).toFixed(1)}%`,
                        }"
                        :title="`T${snap.tick}: ${formatCurrency(snap.estimatedRevenue ?? 0)}`"
                      ></div>
                    </div>
                  </div>

                  <!-- Estimated profit chart -->
                  <div v-if="unitProductAnalytics.snapshots.some((s) => s.estimatedProfit !== null)" class="mi-chart-section">
                    <span class="mi-chart-label">{{ t('buildingDetail.unitProductAnalytics.estimatedProfitChart') }}</span>
                    <div class="mi-bar-chart mi-bar-chart-profit" role="img" :aria-label="t('buildingDetail.unitProductAnalytics.estimatedProfitChart')">
                      <div
                        v-for="snap in unitProductAnalytics.snapshots"
                        :key="snap.tick"
                        class="mi-bar"
                        :class="(snap.estimatedProfit ?? 0) >= 0 ? 'mi-bar-profit-positive' : 'mi-bar-profit-negative'"
                        :style="{
                          height: `${Math.max(2, upaMaxAbsProfit > 0 ? (Math.abs(snap.estimatedProfit ?? 0) / upaMaxAbsProfit) * 100 : 0).toFixed(1)}%`,
                        }"
                        :title="`T${snap.tick}: ${formatCurrency(snap.estimatedProfit ?? 0)}`"
                      ></div>
                    </div>
                  </div>

                  <!-- Profitability note -->
                  <p class="config-help mi-hint">{{ t('buildingDetail.unitProductAnalytics.profitNote') }}</p>
                </template>

                <template v-else-if="unitProductAnalytics && unitProductAnalytics.snapshots.length === 0">
                  <p class="config-help">{{ t('buildingDetail.unitProductAnalytics.noData') }}</p>
                </template>

                <template v-else-if="!unitProductAnalytics && !unitProductAnalyticsLoading">
                  <p class="config-help">{{ t('buildingDetail.unitProductAnalytics.noProduct') }}</p>
                </template>
              </div>

              <!-- Recent Activity feed for all unit types -->
              <div class="unit-insight-card recent-activity-panel" aria-label="Recent Activity">
                <h5>{{ t('buildingDetail.recentActivity.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.recentActivity.subtitle') }}</p>
                <p v-if="recentActivityLoading" class="config-help">…</p>
                <template v-else-if="recentActivity.length > 0">
                  <ul class="activity-list">
                    <li
                      v-for="(event, idx) in recentActivity"
                      :key="`${event.tick}-${event.buildingUnitId}-${event.eventType}-${idx}`"
                      class="activity-item"
                      :class="`activity-${event.eventType.toLowerCase()}`"
                    >
                      <span class="activity-tick">{{ t('buildingDetail.recentActivity.tickLabel', { tick: event.tick }) }}</span>
                      <span class="activity-desc">{{ event.description }}</span>
                    </li>
                  </ul>
                </template>
                <p v-else class="config-help">{{ t('buildingDetail.recentActivity.empty') }}</p>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="sidebar sidebar-placeholder">
          <div class="unit-config">
            <div class="unit-config-header">
              <h3>{{ isEditing ? t('buildingDetail.unitDetails') : t('buildingDetail.overview.title') }}</h3>
            </div>
            <div v-if="isEditing" class="unit-detail placeholder-detail">
              <h4>{{ t('buildingDetail.sidebarPlaceholderTitle') }}</h4>
              <p class="unit-desc">
                {{ t('buildingDetail.sidebarPlaceholderBodyEditing') }}
              </p>
              <div class="unit-insight-card placeholder-summary-card">
                <h5>{{ t('buildingDetail.costSummaryTitle') }}</h5>
                <div class="unit-stats">
                  <span class="stat">{{ t('buildingDetail.totalBuildCost', { cost: formatCurrency(draftConstructionCost) }) }}</span>
                  <span v-if="projectedCompanyCashAfterApply != null" class="stat">
                    {{ t('buildingDetail.cashAfterApply', { cash: formatCurrency(projectedCompanyCashAfterApply) }) }}
                  </span>
                </div>
              </div>

              <!-- ── Building Layouts panel ── -->
              <div class="layout-section" aria-label="Building Layouts">
                <div class="layout-header">
                  <h4>{{ t('buildingDetail.layouts.title') }}</h4>
                </div>

                <!-- Overwrite confirmation dialog -->
                <div v-if="overwriteConfirmPending" class="layout-overwrite-confirm" role="alertdialog" aria-modal="true">
                  <p class="layout-confirm-text">{{ t('buildingDetail.layouts.overwriteConfirm') }}</p>
                  <div class="layout-confirm-actions">
                    <button class="btn btn-danger btn-sm" @click="confirmOverwrite">{{ t('common.confirm') }}</button>
                    <button class="btn btn-ghost btn-sm" @click="cancelOverwrite">{{ t('common.cancel') }}</button>
                  </div>
                </div>

                <!-- Save form -->
                <div class="layout-save" v-if="!overwriteConfirmPending">
                  <input
                    type="text"
                    class="form-input"
                    v-model="layoutName"
                    :placeholder="t('buildingDetail.layouts.namePlaceholder')"
                    :aria-label="t('buildingDetail.layouts.namePlaceholder')"
                  />
                  <input
                    type="text"
                    class="form-input layout-desc-input"
                    v-model="layoutDescription"
                    :placeholder="t('buildingDetail.layouts.descriptionPlaceholder')"
                    :aria-label="t('buildingDetail.layouts.descriptionPlaceholder')"
                  />
                  <button
                    class="btn btn-secondary btn-sm"
                    :disabled="!layoutName.trim() || layoutSaving"
                    @click="saveLayout"
                  >
                    {{ layoutSaving ? t('buildingDetail.layouts.masterSaving') : t('buildingDetail.layouts.save') }}
                  </button>
                  <p v-if="layoutSaveSuccess" class="layout-save-success">✓ {{ t('buildingDetail.layouts.saveSuccess') }}</p>
                  <p v-if="layoutSaveError" class="layout-save-error">{{ layoutSaveError }}</p>
                  <p v-if="layoutDeleteError" class="layout-save-error">{{ layoutDeleteError }}</p>
                </div>

                <!-- Cloud layouts section -->
                <template v-if="masterConnected">
                  <div class="layout-cloud-header">
                    <span class="layout-cloud-badge">☁ {{ t('buildingDetail.layouts.cloudBadge') }}</span>
                    <span class="layout-connected-email">{{ t('buildingDetail.layouts.masterConnected', { email: masterUserEmail }) }}</span>
                  </div>
                  <p v-if="masterLayoutsLoading" class="layout-empty">{{ t('common.loading') }}</p>
                  <p v-else-if="masterLayoutsError" class="layout-save-error">{{ masterLayoutsError }}</p>
                  <div v-else-if="masterLayouts.length > 0" class="layout-list">
                    <div v-for="layout in masterLayouts" :key="layout.id ?? layout.name" class="layout-item">
                      <div class="layout-item-info">
                        <span class="layout-name">{{ layout.name }}</span>
                        <span v-if="layout.description" class="layout-desc">{{ layout.description }}</span>
                        <span class="layout-meta">{{ layout.units.length }} {{ t('buildingDetail.layouts.units') }}</span>
                      </div>
                      <div class="layout-item-actions">
                        <button class="btn btn-ghost btn-sm" @click="requestLoadLayout(layout)">{{ t('buildingDetail.layouts.load') }}</button>
                        <button class="btn btn-ghost btn-sm" @click="deleteLayout(layout)">{{ t('buildingDetail.layouts.delete') }}</button>
                      </div>
                    </div>
                  </div>
                  <p v-else class="layout-empty">{{ t('buildingDetail.layouts.empty') }}</p>
                  <p class="layout-sync-hint">{{ t('buildingDetail.layouts.cloudSyncHint') }}</p>
                </template>

                <!-- Shared-session sign-in prompt -->
                <template v-else>
                  <div class="layout-master-connect">
                    <p class="layout-connect-body">{{ t('buildingDetail.layouts.masterConnectBody') }}</p>
                    <button class="btn btn-secondary btn-sm" @click="router.push('/login')">
                      {{ t('buildingDetail.layouts.masterConnect') }}
                    </button>
                  </div>

                  <!-- Local-only fallback -->
                  <div class="layout-local-section">
                    <div class="layout-local-header">
                      <span class="layout-local-badge">{{ t('buildingDetail.layouts.localBadge') }}</span>
                    </div>
                    <div v-if="localLayouts.length > 0" class="layout-list">
                      <div v-for="layout in localLayouts" :key="layout.name" class="layout-item">
                        <div class="layout-item-info">
                          <span class="layout-name">{{ layout.name }}</span>
                          <span v-if="layout.description" class="layout-desc">{{ layout.description }}</span>
                          <span class="layout-meta">{{ layout.units.length }} {{ t('buildingDetail.layouts.units') }}</span>
                        </div>
                        <div class="layout-item-actions">
                          <button class="btn btn-ghost btn-sm" @click="requestLoadLayout(layout)">{{ t('buildingDetail.layouts.load') }}</button>
                          <button class="btn btn-ghost btn-sm" @click="deleteLayout(layout)">{{ t('buildingDetail.layouts.delete') }}</button>
                        </div>
                      </div>
                    </div>
                    <p v-else class="layout-empty">{{ t('buildingDetail.layouts.empty') }}</p>
                  </div>
                </template>
              </div>
              <!-- ── End Building Layouts panel ── -->
            </div>
            <div v-else class="unit-detail building-overview-detail">
              <p class="building-overview-name">{{ building.name }}</p>
              <p class="unit-desc">{{ t('buildingDetail.overview.subtitle', { type: formatBuildingType(building.type) }) }}</p>

              <div class="unit-insight-card building-location-card">
                <h5>{{ t('buildingDetail.overview.locationTitle') }}</h5>
                <div class="building-overview-location-grid">
                  <div class="building-overview-location-row">
                    <span class="building-overview-label">{{ t('buildingDetail.overview.city') }}</span>
                    <strong>{{ buildingOverviewCityName }}</strong>
                  </div>
                  <div class="building-overview-location-row">
                    <span class="building-overview-label">{{ t('buildingDetail.overview.gps') }}</span>
                    <strong>{{ formatGpsLocation(building.latitude, building.longitude) }}</strong>
                  </div>
                </div>
                <RouterLink v-if="buildingOverviewMapRoute" :to="buildingOverviewMapRoute" class="btn btn-secondary btn-sm building-overview-map-link">
                  {{ t('buildingDetail.overview.showOnMap') }}
                </RouterLink>
              </div>

              <div class="unit-insight-card building-financial-card">
                <h5>{{ t('buildingDetail.overview.statsTitle') }}</h5>
                <p v-if="buildingFinancialTimeline" class="config-help">
                  {{ t('buildingDetail.overview.tickWindow', { start: buildingFinancialTimeline.dataFromTick, end: buildingFinancialTimeline.dataToTick }) }}
                </p>
                <p v-else-if="buildingFinancialTimelineLoading" class="config-help">{{ t('common.loading') }}</p>

                <div class="mi-summary-grid">
                  <div class="mi-metric">
                    <span class="mi-metric-label">{{ t('buildingDetail.overview.sales') }}</span>
                    <strong class="mi-metric-value">{{ formatCurrency(buildingFinancialTimeline?.totalSales ?? 0) }}</strong>
                  </div>
                  <div class="mi-metric">
                    <span class="mi-metric-label">{{ t('buildingDetail.overview.costs') }}</span>
                    <strong class="mi-metric-value">{{ formatCurrency(buildingFinancialTimeline?.totalCosts ?? 0) }}</strong>
                  </div>
                  <div class="mi-metric">
                    <span class="mi-metric-label">{{ t('buildingDetail.overview.profit') }}</span>
                    <strong
                      class="mi-metric-value"
                      :class="{
                        'building-profit-positive-text': (buildingFinancialTimeline?.totalProfit ?? 0) >= 0,
                        'building-profit-negative-text': (buildingFinancialTimeline?.totalProfit ?? 0) < 0,
                      }"
                    >
                      {{ formatCurrency(buildingFinancialTimeline?.totalProfit ?? 0) }}
                    </strong>
                  </div>
                </div>

                <template v-if="buildingFinancialTimeline">
                  <BuildingFinancialTimelineChart v-if="buildingFinancialHasActivity" :timeline="buildingFinancialSnapshots" />

                  <p v-else class="mi-empty-state">
                    {{ t('buildingDetail.overview.noFinancialData') }}
                  </p>
                </template>

                <p v-else-if="!buildingFinancialTimelineLoading" class="mi-empty-state">
                  {{ t('buildingDetail.overview.loadFailed') }}
                </p>
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
  grid-template-columns: minmax(0, 1.05fr) minmax(360px, 0.95fr);
  gap: 2rem;
  align-items: start;
}

@media (min-width: 1320px) {
  .main-content {
    grid-template-columns: minmax(0, 1fr) minmax(420px, 1fr);
  }
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

.upgrade-summary {
  margin-top: 1rem;
  margin-bottom: 0.5rem;
}

/* Link changes summary panel shown before submission */
.link-changes-summary {
  margin-top: 0.75rem;
  padding: 0.75rem 1rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
}

.link-changes-title {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  margin: 0 0 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.link-changes-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.link-change-item {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.link-change-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  font-size: 0.75rem;
  font-weight: 700;
  flex-shrink: 0;
}

.link-change-added .link-change-badge {
  background: rgba(0, 200, 83, 0.12);
  color: var(--color-secondary, #00c853);
}

.link-change-removed .link-change-badge {
  background: rgba(220, 38, 38, 0.12);
  color: var(--color-danger, #dc2626);
}

/* Unit-level planned changes summary panel */
.unit-changes-summary {
  margin-top: 0.75rem;
  padding: 0.75rem 1rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
}

.unit-changes-title {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  margin: 0 0 0.5rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.unit-changes-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.unit-change-item {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.unit-change-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  font-size: 0.75rem;
  font-weight: 700;
  flex-shrink: 0;
}

.unit-change-added .unit-change-badge {
  background: rgba(0, 200, 83, 0.12);
  color: var(--color-secondary, #00c853);
}

.unit-change-removed .unit-change-badge {
  background: rgba(220, 38, 38, 0.12);
  color: var(--color-danger, #dc2626);
}

.unit-change-replaced .unit-change-badge {
  background: rgba(234, 179, 8, 0.12);
  color: var(--color-warning, #ca8a04);
}

.unit-change-description {
  flex: 1;
}

.unit-change-meta {
  display: flex;
  gap: 0.5rem;
  flex-shrink: 0;
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.unit-change-ticks {
  white-space: nowrap;
}

.unit-change-cost {
  white-space: nowrap;
  font-weight: 600;
  color: var(--color-text-secondary);
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

.power-status-pill.power-status-powered {
  background: rgba(34, 197, 94, 0.1);
  color: var(--color-secondary);
}

.power-status-pill.power-status-constrained {
  background: rgba(251, 191, 36, 0.15);
  color: #f59e0b;
}

.power-status-pill.power-status-offline {
  background: rgba(248, 113, 113, 0.1);
  color: var(--color-danger);
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
  margin-top: 1rem;
  margin-bottom: 1.5rem;
  background: linear-gradient(135deg, rgba(19, 127, 236, 0.09), rgba(0, 200, 83, 0.08));
  border: 1px solid rgba(19, 127, 236, 0.18);
  border-radius: var(--radius-lg, 12px);
}

.upgrade-banner-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-shrink: 0;
}

.error-banner {
  padding: 0.75rem 1.25rem;
  margin-bottom: 1rem;
  border: 1px solid rgba(220, 38, 38, 0.3);
  border-radius: var(--radius-lg);
  background: rgba(220, 38, 38, 0.08);
  color: #dc2626;
  font-size: 0.875rem;
}

.save-error-banner {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  border: 1px solid rgba(220, 38, 38, 0.35);
  border-radius: var(--radius-lg);
  background: rgba(220, 38, 38, 0.08);
  color: #dc2626;
  font-size: 0.875rem;
  line-height: 1.4;
}

.pro-access-banner {
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
  border: 1px solid rgba(255, 109, 0, 0.3);
  border-radius: var(--radius-lg);
  background: rgba(255, 109, 0, 0.08);
}

.starter-setup-banner {
  margin-bottom: 1.5rem;
  padding: 1.25rem;
  border: 1px solid rgba(0, 200, 83, 0.3);
  border-radius: var(--radius-lg);
  background: linear-gradient(135deg, rgba(0, 200, 83, 0.07), rgba(19, 127, 236, 0.05));
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.starter-setup-content {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.starter-setup-title {
  font-size: 1.0625rem;
  font-weight: 700;
  color: var(--color-secondary);
  margin: 0;
}

.starter-setup-body {
  font-size: 0.9375rem;
  margin: 0;
}

.starter-setup-desc {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  padding: 0.5rem 0.75rem;
  border-left: 3px solid rgba(0, 200, 83, 0.4);
  margin: 0;
  background: rgba(0, 200, 83, 0.04);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}

.starter-setup-whatnext {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin: 0;
}

.starter-setup-actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
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

/* ── Production chain status panel ─────────────────────────────────────── */

.production-chain-panel {
  background: var(--color-surface-raised);
  border: 1px solid var(--color-border);
  border-radius: 12px;
  padding: 1.25rem 1.5rem;
  margin-top: 1.25rem;
  margin-bottom: 1.5rem;
}

.chain-panel-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
  flex-wrap: wrap;
}

.chain-panel-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.chain-status-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.2rem 0.6rem;
  border-radius: 999px;
}

.chain-status-badge--complete {
  background: rgba(52, 211, 153, 0.15);
  color: #4ade80;
}

.chain-status-badge--incomplete {
  background: rgba(251, 191, 36, 0.15);
  color: #fbbf24;
}

.chain-flow {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.chain-step {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 110px;
  padding: 0.75rem;
  border-radius: 8px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  text-align: center;
  gap: 0.25rem;
}

.chain-step--configured {
  border-color: #34d399;
  background: rgba(52, 211, 153, 0.1);
}

.chain-step--missing {
  border-color: #fbbf24;
  background: rgba(251, 191, 36, 0.1);
}

.chain-step-icon {
  font-size: 1.5rem;
  line-height: 1;
}

.chain-step-type {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.chain-step-value {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text);
  word-break: break-word;
}

.chain-step-missing-label {
  font-size: 0.8rem;
  color: var(--color-text-tertiary, var(--color-text-secondary));
  font-style: italic;
}

.chain-arrow {
  font-size: 1.5rem;
  color: var(--color-text-secondary);
  flex-shrink: 0;
}

.chain-guidance {
  border-top: 1px solid var(--color-border);
  padding-top: 1rem;
  margin-top: 0.5rem;
}

.chain-guidance-title {
  margin: 0 0 0.5rem;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.chain-todo {
  margin: 0 0 0.75rem;
  padding-left: 1.25rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.chain-todo li {
  margin-bottom: 0.3rem;
}

.chain-action-hint {
  margin: 0;
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.chain-complete-message {
  border-top: 1px solid var(--color-border);
  padding-top: 1rem;
  margin-top: 0.5rem;
}

.chain-complete-message p {
  margin: 0 0 0.4rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.chain-next-step {
  font-weight: 600;
  color: var(--color-text-primary) !important;
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
  min-height: 96px;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  justify-content: flex-start;
  gap: 0.35rem;
  padding: 0.5rem;
  border: 2px solid var(--color-border);
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.76), rgba(244, 247, 251, 0.92));
  color: var(--color-text);
  transition:
    border-color 0.15s ease,
    box-shadow 0.15s ease,
    transform 0.15s ease;
}

.cell-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.5rem;
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
  text-align: left;
  line-height: 1.2;
}

.cell-item-block {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr);
  gap: 0.45rem;
  align-items: center;
}

.cell-item-image,
.cell-item-avatar {
  width: 28px;
  height: 28px;
  border-radius: 8px;
}

.cell-item-image {
  object-fit: cover;
  border: 1px solid color-mix(in srgb, var(--color-border) 80%, transparent);
}

.cell-item-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 0.6875rem;
  font-weight: 800;
  background: color-mix(in srgb, var(--color-primary) 10%, white 90%);
  color: var(--color-primary);
}

.cell-item-copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.cell-item,
.cell-metric,
.cell-value,
.cell-operating-cost,
.cell-stock {
  font-size: 0.5625rem;
  text-align: left;
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

.cell-stock,
.cell-value {
  font-weight: 600;
}

.cell-operating-cost {
  opacity: 0.75;
}

.cell-level,
.cell-pending,
.cell-reverting {
  font-size: 0.625rem;
  flex-shrink: 0;
}

.cell-pending {
  color: #f59e0b;
  text-align: center;
}

.cell-reverting {
  color: #c084fc;
  text-align: center;
  font-style: italic;
}

.grid-cell.reverting {
  border-style: dashed !important;
  opacity: 0.85;
}

.cell-inventory-indicator {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  margin-top: 0.25rem;
}

.inventory-icon {
  font-size: 1rem;
}

.cell-empty {
  font-size: 1.35rem;
  opacity: 0.45;
  margin: auto;
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
  transition: background 0.2s ease;
}

.cell-capacity-fill[data-fill='medium'] {
  background: linear-gradient(90deg, #3b82f6, #38bdf8);
}

.cell-capacity-fill[data-fill='high'] {
  background: linear-gradient(90deg, #f59e0b, #ef4444);
}

.link-toggle {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 94%, white 6%);
  transition:
    border-color 0.15s ease,
    background 0.15s ease,
    box-shadow 0.15s ease;
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

/* Directional arrow indicator inside link toggle buttons */
.link-arrow {
  position: absolute;
  font-size: 7px;
  line-height: 1;
  color: var(--color-primary);
  pointer-events: none;
  font-weight: 700;
}

.link-toggle.horizontal .link-arrow {
  right: 1px;
  top: 50%;
  transform: translateY(-50%);
}

.link-toggle.horizontal.link-state-backward .link-arrow {
  right: auto;
  left: 1px;
}

.link-toggle.horizontal.link-state-both .link-arrow {
  right: auto;
  left: 50%;
  transform: translate(-50%, -50%);
}

.link-toggle.vertical .link-arrow {
  bottom: 1px;
  left: 50%;
  transform: translateX(-50%);
}

.link-toggle.vertical.link-state-backward .link-arrow {
  bottom: auto;
  top: 1px;
}

.link-toggle.vertical.link-state-both .link-arrow {
  top: 50%;
  bottom: auto;
  transform: translate(-50%, -50%);
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
.diagonal.state-br-tl .diag-line-primary,
.diagonal.state-tr-bl .diag-line-secondary,
.diagonal.state-bl-tr .diag-line-secondary,
.diagonal.state-cross .diag-line-primary,
.diagonal.state-cross .diag-line-secondary {
  opacity: 1;
  background: var(--color-primary);
}

/* Arrow indicator overlay for diagonal link buttons */
.diag-arrow {
  position: absolute;
  font-size: 9px;
  line-height: 1;
  color: var(--color-primary);
  pointer-events: none;
  font-weight: 700;
  z-index: 1;
}

/* config-help-notice is a softer variant of config-help for guidance messages */
.config-help-notice {
  color: var(--color-text-muted, #6b7280);
  font-style: italic;
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
  transition:
    border-color 0.15s ease,
    background 0.15s ease;
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

.picker-cost {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--color-text);
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
.property-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}

.property-panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.25rem;
}

.property-panel-title {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
}

.property-metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.property-metric {
  background: var(--color-bg);
  border: 1px solid color-mix(in srgb, var(--color-border) 80%, transparent);
  border-radius: var(--radius-md, 8px);
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.property-metric-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-weight: 500;
}

.property-metric-value {
  font-size: 1rem;
  font-weight: 600;
  color: var(--color-text);
}

.property-metric-zero {
  color: var(--color-text-secondary);
}

.pending-rent-notice {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: rgba(59, 130, 246, 0.08);
  border: 1px solid rgba(59, 130, 246, 0.3);
  border-radius: var(--radius-md, 8px);
  padding: 0.75rem 1rem;
  font-size: 0.875rem;
  color: #60a5fa;
  margin-bottom: 0.75rem;
}

.pending-rent-icon {
  font-size: 1rem;
  flex-shrink: 0;
}

.property-empty-state {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  padding: 0.5rem 0;
}

.rent-dialog {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.25rem;
  margin-top: 1rem;
}

.rent-dialog-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.rent-dialog-header h3 {
  font-size: 1rem;
  margin: 0;
}

.rent-dialog-body {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.rent-dialog-hint {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.rent-dialog-error {
  font-size: 0.8125rem;
  color: #dc2626;
  margin: 0;
}

.rent-dialog-actions {
  display: flex;
  gap: 0.75rem;
  margin-top: 0.25rem;
}

.config-warnings {
  background: rgba(255, 109, 0, 0.08);
  border: 1px solid rgba(255, 109, 0, 0.3);
  border-radius: var(--radius-lg);
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
  color: #f59e0b;
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

.purchase-selector-trigger {
  width: 100%;
  justify-content: center;
}

.purchase-selection-summary {
  margin-top: 0.5rem;
  padding: 0.75rem;
  border-top: 1px solid var(--color-border);
  display: grid;
  gap: 0.2rem;
}

.purchase-selection-meta {
  color: var(--color-text-secondary);
  font-size: 0.82rem;
}

.purchase-selector-page {
  position: fixed;
  inset: 0;
  z-index: 110;
  background: rgba(15, 23, 42, 0.82);
  padding: 2rem;
  overflow: auto;
}

.purchase-selector-shell {
  max-width: 1100px;
  margin: 0 auto;
  background: var(--color-bg);
  border-radius: 18px;
  border: 1px solid var(--color-border);
  padding: 1.5rem;
  display: grid;
  gap: 1rem;
}

.purchase-selector-header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.purchase-selector-header h2 {
  margin: 0.2rem 0 0;
}

.purchase-selector-eyebrow {
  margin: 0;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--color-primary);
  font-weight: 700;
}

.purchase-selector-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: 1.2fr 0.8fr;
}

.purchase-selector-card {
  border: 1px solid var(--color-border);
  border-radius: 14px;
  padding: 1rem;
  background: var(--color-surface-raised);
}

.purchase-vendor-list {
  display: grid;
  gap: 0.75rem;
  margin-top: 0.75rem;
}

.purchase-vendor-card {
  width: 100%;
  text-align: left;
  display: grid;
  gap: 0.2rem;
  border-radius: 12px;
  border: 1px solid var(--color-border);
  background: var(--color-bg);
  color: var(--color-text);
  padding: 0.85rem 0.95rem;
  cursor: pointer;
  margin-top: 0.75rem;
}

.purchase-vendor-card.selected {
  border-color: var(--color-primary);
  background: rgba(37, 99, 235, 0.08);
}

.purchase-vendor-card span {
  color: var(--color-text-secondary);
  font-size: 0.82rem;
}

.purchase-vendor-pricing {
  font-variant-numeric: tabular-nums;
}

.purchase-selector-actions {
  display: flex;
  justify-content: flex-end;
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

.config-onboarding-hint {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  background: var(--color-surface);
  border-left: 3px solid #60a5fa;
  padding: 0.5rem 0.75rem;
  border-radius: 0 6px 6px 0;
  margin-bottom: 0.75rem;
  line-height: 1.4;
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

.inventory-summary-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 0.75rem;
  margin-bottom: 0.9rem;
}

.inventory-summary-stat {
  padding: 0.8rem 0.9rem;
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  border-radius: var(--radius-md, 8px);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 94%, white 6%);
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.inventory-summary-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.inventory-summary-stat strong {
  font-size: 0.95rem;
  color: var(--color-text);
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

.offer-blocked {
  opacity: 0.5;
}

.offer-blocked-reason {
  margin-top: 0.35rem;
  font-size: 0.7rem;
  color: var(--color-error, #ef4444);
}

.exchange-no-valid-offers {
  color: var(--color-error, #ef4444);
  font-size: 0.8rem;
}

.exchange-selection-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-style: italic;
  margin-bottom: 0.5rem;
}

.offer-best {
  border-color: var(--color-primary, #3b82f6);
  background: color-mix(in srgb, var(--color-primary, #3b82f6) 8%, var(--color-surface-raised, var(--color-surface)));
}

.offer-best-badge {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-primary, #3b82f6);
  background: color-mix(in srgb, var(--color-primary, #3b82f6) 15%, transparent);
  border: 1px solid color-mix(in srgb, var(--color-primary, #3b82f6) 40%, transparent);
  border-radius: var(--radius-sm, 4px);
  padding: 0.1rem 0.4rem;
  white-space: nowrap;
}

.logistics-trap-warning {
  font-size: 0.75rem;
  color: var(--color-warning-text, #92400e);
  background: var(--color-warning-bg, #fef3c7);
  border: 1px solid var(--color-warning-border, #f59e0b);
  border-radius: var(--radius-md, 8px);
  padding: 0.5rem 0.75rem;
  margin-bottom: 0.5rem;
}

.exchange-sort-controls {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  flex-wrap: wrap;
  margin-bottom: 0.5rem;
}

.exchange-sort-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.exchange-sort-btn {
  font-size: 0.7rem;
  padding: 0.1rem 0.5rem;
  border-radius: var(--radius-sm, 4px);
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-text-secondary);
  cursor: pointer;
  transition:
    background 0.15s,
    color 0.15s,
    border-color 0.15s;
}

.exchange-sort-btn.active {
  background: color-mix(in srgb, var(--color-primary, #3b82f6) 15%, transparent);
  border-color: var(--color-primary, #3b82f6);
  color: var(--color-primary, #3b82f6);
  font-weight: 600;
}

.exchange-sort-btn:hover:not(.active) {
  background: color-mix(in srgb, var(--color-border) 30%, transparent);
  color: var(--color-text);
}

.exchange-view-link {
  display: inline-block;
  margin-top: 0.5rem;
  font-size: 0.75rem;
  color: var(--color-primary, #3b82f6);
  text-decoration: none;
  font-weight: 500;
}

.exchange-view-link:hover {
  text-decoration: underline;
}

/* Inventory table */
.inventory-table {
  margin-top: 0.75rem;
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  border-radius: var(--radius-md, 8px);
  overflow: hidden;
}

.inventory-table-header,
.inventory-table-row {
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) 90px 90px minmax(110px, 0.9fr);
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  align-items: center;
}

.inventory-table-header {
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 95%, white 5%);
  border-bottom: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.inventory-table-row {
  border-bottom: 1px solid color-mix(in srgb, var(--color-border) 95%, transparent);
}

.inventory-table-row:last-child {
  border-bottom: none;
}

.inventory-col-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-width: 0;
}

.inventory-col-quantity,
.inventory-col-quality,
.inventory-col-cost {
  font-size: 0.8125rem;
}

.inventory-col-cost {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.125rem;
}

.inventory-item-stack {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.inventory-item-image {
  width: 32px;
  height: 32px;
  border-radius: 6px;
  object-fit: cover;
  flex-shrink: 0;
}

.inventory-item-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 6px;
  background: var(--color-primary);
  color: white;
  font-size: 0.875rem;
  font-weight: 700;
  flex-shrink: 0;
}

.inventory-item-name {
  font-weight: 600;
  color: var(--color-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.inventory-item-cost {
  font-weight: 700;
  color: var(--color-text);
}

.inventory-item-secondary {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.inventory-empty {
  margin: 0;
  padding: 0.85rem 0.95rem;
  border-radius: var(--radius-md, 8px);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 94%, white 6%);
  color: var(--color-text-secondary);
  font-size: 0.8125rem;
}

.inventory-item-quantity,
.inventory-item-quality {
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
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.layout-desc-input {
  font-size: 0.8125rem;
}

.layout-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.layout-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 0.5rem;
  background: var(--color-bg);
  border-radius: var(--radius-md, 8px);
  gap: 0.5rem;
}

.layout-item-info {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  min-width: 0;
}

.layout-name {
  font-size: 0.8125rem;
  font-weight: 600;
}

.layout-desc {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.layout-meta {
  font-size: 0.75rem;
  color: var(--color-text-tertiary, var(--color-text-secondary));
}

.layout-item-actions {
  display: flex;
  gap: 0.25rem;
  flex-shrink: 0;
}

.layout-empty {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0.5rem 0;
}

.layout-save-success {
  font-size: 0.8125rem;
  color: var(--color-success, #22c55e);
  margin: 0.25rem 0 0;
}

.layout-save-error {
  font-size: 0.8125rem;
  color: var(--color-danger, #ef4444);
  margin: 0.25rem 0 0;
}

.layout-overwrite-confirm {
  background: color-mix(in srgb, var(--color-warning, #f59e0b) 12%, transparent);
  border: 1px solid var(--color-warning, #f59e0b);
  border-radius: var(--radius-md, 8px);
  padding: 0.75rem;
  margin-bottom: 0.75rem;
}

.layout-confirm-text {
  font-size: 0.875rem;
  margin: 0 0 0.5rem;
}

.layout-confirm-actions {
  display: flex;
  gap: 0.5rem;
}

.layout-cloud-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  flex-wrap: wrap;
}

.layout-cloud-badge {
  font-size: 0.75rem;
  font-weight: 600;
  background: color-mix(in srgb, var(--color-primary, #3b82f6) 15%, transparent);
  color: var(--color-primary, #3b82f6);
  border-radius: var(--radius-sm, 4px);
  padding: 0.125rem 0.375rem;
}

.layout-local-badge {
  font-size: 0.75rem;
  font-weight: 600;
  background: color-mix(in srgb, var(--color-border) 40%, transparent);
  color: var(--color-text-secondary);
  border-radius: var(--radius-sm, 4px);
  padding: 0.125rem 0.375rem;
}

.layout-connected-email {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}

.layout-master-connect {
  margin-bottom: 0.75rem;
}

.layout-connect-body {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0 0 0.75rem;
}

.layout-login-form {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.layout-form-toggle {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0.25rem 0 0;
}

.layout-local-section {
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--color-border);
}

.layout-local-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.layout-sync-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  margin: 0.5rem 0 0;
  font-style: italic;
}

.btn-link {
  background: none;
  border: none;
  padding: 0;
  color: var(--color-primary, #3b82f6);
  font-size: inherit;
  cursor: pointer;
  text-decoration: underline;
}

.btn-xs {
  padding: 0.125rem 0.375rem;
  font-size: 0.75rem;
}

.placeholder-detail {
  min-height: 240px;
}

.building-overview-detail {
  min-height: 240px;
}

.building-overview-name {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 700;
  color: var(--color-text);
}

.placeholder-summary-card {
  border-top-style: dashed;
}

.building-overview-location-grid {
  display: grid;
  gap: 0.75rem;
  margin-bottom: 0.9rem;
}

.building-overview-location-row {
  padding: 0.8rem 0.9rem;
  border: 1px solid color-mix(in srgb, var(--color-border) 88%, transparent);
  border-radius: var(--radius-md, 8px);
  background: color-mix(in srgb, var(--color-surface-raised, var(--color-surface)) 94%, white 6%);
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.building-overview-label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.building-overview-map-link {
  width: fit-content;
}

.grid-cell.clickable {
  cursor: pointer;
}

/* R&D Research Progress Panel */
.research-progress-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  padding: 1.25rem;
  margin-bottom: 1rem;
}

.research-progress-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.5rem;
}

.research-progress-title {
  font-size: 1.125rem;
  font-weight: 600;
  margin: 0;
  color: var(--color-text);
}

.research-progress-intro {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  margin: 0 0 1rem;
}

.research-loading {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.research-empty-state {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.research-brand-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.research-brand-card {
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm, 6px);
  padding: 0.875rem;
}

.research-brand-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.25rem;
}

.research-brand-name {
  font-weight: 600;
  font-size: 0.9375rem;
  color: var(--color-text);
}

.research-brand-scope-badge {
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.125rem 0.375rem;
  border-radius: 9999px;
  background: var(--color-primary-subtle, rgba(0, 71, 255, 0.1));
  color: var(--color-primary);
}

.research-brand-industry {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.5rem;
}

.research-brand-metrics {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin: 0.5rem 0;
}

.research-metric {
  display: grid;
  grid-template-columns: 8rem 1fr 3.5rem;
  align-items: center;
  gap: 0.5rem;
}

.research-metric-label {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  white-space: nowrap;
}

.research-metric-value {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--color-text);
  text-align: right;
}

.research-progress-bar {
  height: 0.5rem;
  background: var(--color-border);
  border-radius: 9999px;
  overflow: hidden;
}

.research-progress-fill {
  height: 100%;
  border-radius: 9999px;
  transition: width 0.3s ease;
}

.research-progress-quality {
  background: #0047ff;
}

.research-progress-awareness {
  background: #9333ea;
}

.research-progress-efficiency {
  background: #16a34a;
}

.research-brand-effect {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0.5rem 0 0;
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

/* ── Market Intelligence panel ── */
.market-intelligence-panel {
  margin-top: 1.25rem;
}

.unit-product-analytics-panel {
  margin-top: 1.25rem;
}

/* Product context row: chip + tick window label */
.mi-context-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
  flex-wrap: wrap;
}

.mi-product-chip {
  display: inline-block;
  background: var(--color-primary-light, rgba(59, 130, 246, 0.12));
  color: var(--color-primary, #3b82f6);
  border: 1px solid var(--color-primary-light, rgba(59, 130, 246, 0.3));
  border-radius: 999px;
  padding: 0.15rem 0.65rem;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.mi-tick-window {
  font-size: 0.72rem;
  color: var(--color-text-secondary);
  font-variant-numeric: tabular-nums;
}

/* Trend direction colours */
.mi-trend-up {
  color: #4ade80;
}

.mi-trend-down {
  color: #f87171;
}

.mi-trend-flat {
  color: var(--color-text-secondary);
}

.mi-summary-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
  gap: 0.6rem;
  margin: 0.75rem 0 1rem;
}

.mi-metric {
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.55rem 0.7rem;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.mi-metric-label {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.mi-metric-value {
  font-size: 0.9375rem;
  color: var(--color-text);
}

.mi-empty-state {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  background: var(--color-surface);
  border: 1px dashed var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.75rem;
  margin: 0.5rem 0 0.75rem;
}

.building-profit-positive-text {
  color: #4ade80;
}

.building-profit-negative-text {
  color: #f87171;
}

@media (max-width: 640px) {
  .building-overview-map-link {
    width: 100%;
    justify-content: center;
  }
}

.mi-section {
  margin-bottom: 1rem;
}

.mi-market-share {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-top: 0.4rem;
}

.mi-share-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
}

.mi-share-row-you .mi-share-label {
  font-weight: 600;
  color: var(--color-primary, #0047ff);
}

.mi-share-label {
  width: 6.5rem;
  flex-shrink: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text);
}

.mi-share-bar-wrap {
  flex: 1;
  height: 10px;
  background: var(--color-border);
  border-radius: 9999px;
  overflow: hidden;
}

.mi-share-bar {
  height: 100%;
  background: var(--color-primary, #0047ff);
  border-radius: 9999px;
  transition: width 0.3s ease;
}

.mi-share-row-you .mi-share-bar {
  background: #16a34a;
}

.mi-share-pct {
  width: 3rem;
  text-align: right;
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
  color: var(--color-text-secondary);
}

.mi-demand-card {
  margin-top: 0.75rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  padding: 0.75rem;
}

.mi-demand-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
}

.mi-demand-title {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.mi-demand-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.55rem;
  border-radius: 9999px;
}

/* Demand signal color themes */
.mi-demand-no-data .mi-demand-badge {
  background: var(--color-border);
  color: var(--color-text-secondary);
}

.mi-demand-strong .mi-demand-badge {
  background: rgba(52, 211, 153, 0.15);
  color: #4ade80;
}

.mi-demand-moderate .mi-demand-badge {
  background: rgba(96, 165, 250, 0.15);
  color: #60a5fa;
}

.mi-demand-weak .mi-demand-badge {
  background: rgba(251, 191, 36, 0.15);
  color: #fbbf24;
}

.mi-demand-supply-constrained .mi-demand-badge {
  background: rgba(248, 113, 113, 0.15);
  color: #f87171;
}

.mi-share-row-unmet .mi-share-label {
  color: var(--color-text-secondary);
  font-style: italic;
}

.mi-share-bar-unmet {
  background: #94a3b8 !important;
  opacity: 0.7;
}

/* Context card for elasticity, quality, brand */
.mi-context-card {
  margin-bottom: 0.75rem;
  border-radius: var(--radius-sm);
  border: 1px solid var(--color-border);
  padding: 0.6rem;
  background: var(--color-surface);
}

.mi-context-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 0.5rem;
}

.mi-context-item {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.mi-context-label {
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.mi-context-value {
  font-size: 0.9375rem;
  font-variant-numeric: tabular-nums;
}

.mi-context-hint {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
  line-height: 1.3;
}

.mi-elastic-high {
  color: #f87171;
}

.mi-elastic-low {
  color: #4ade80;
}

.mi-quality-high {
  color: #4ade80;
}

.mi-quality-low {
  color: #f59e0b;
}

/* ─── Bar chart layout ─── */
.mi-chart-section {
  margin-bottom: 0.9rem;
}

.mi-chart-label {
  display: block;
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 0.3rem;
}

.mi-bar-chart {
  display: flex;
  align-items: flex-end;
  gap: 1px;
  height: 60px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  padding: 4px;
  overflow: hidden;
}

.mi-bar {
  flex: 1;
  min-width: 1px;
  border-radius: 2px 2px 0 0;
  background: var(--color-primary, #3b82f6);
  transition: height 0.2s ease;
}

.mi-bar-revenue {
  background: #3b82f6;
}

.mi-bar-quantity {
  background: #8b5cf6;
}

.mi-bar-price {
  background: #f59e0b;
}

.mi-bar-cost {
  background: #ef4444;
}

.mi-hint {
  font-size: 0.72rem;
  color: var(--color-text-secondary);
  margin-top: 0.4rem;
}

/* ─── Profit chart bars ─── */
.mi-bar-profit-positive {
  background: #16a34a;
}

.mi-bar-profit-negative {
  background: #dc2626;
  align-self: flex-end;
}

/* ─── Demand Drivers ─── */
.mi-demand-drivers {
  margin-top: 0.75rem;
  margin-bottom: 0.5rem;
}

.mi-driver-list {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-top: 0.4rem;
}

.mi-driver-entry {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  padding: 0.4rem 0.6rem;
  border-radius: var(--radius-sm, 6px);
  border: 1px solid var(--color-border);
  font-size: 0.8rem;
}

.mi-driver-positive {
  border-left: 3px solid #16a34a;
  background: rgba(22, 163, 74, 0.12);
}

.mi-driver-neutral {
  border-left: 3px solid #94a3b8;
  background: rgba(148, 163, 184, 0.1);
}

.mi-driver-negative {
  border-left: 3px solid #dc2626;
  background: rgba(220, 38, 38, 0.12);
}

.mi-driver-icon {
  font-weight: 700;
  font-size: 0.9rem;
  flex-shrink: 0;
  margin-top: 0.05rem;
}

.mi-driver-positive .mi-driver-icon {
  color: #4ade80;
}

.mi-driver-neutral .mi-driver-icon {
  color: #94a3b8;
}

.mi-driver-negative .mi-driver-icon {
  color: #f87171;
}

.mi-driver-content {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.mi-driver-factor {
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text);
}

.mi-driver-desc {
  color: var(--color-text-secondary);
  font-size: 0.78rem;
  line-height: 1.3;
}

@media (max-width: 640px) {
  .mi-summary-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .mi-share-label {
    width: 5rem;
  }

  .mi-context-grid {
    grid-template-columns: 1fr;
  }
}

/* ─── Operational Status Card ─── */
.operational-status-card {
  margin-top: 0.75rem;
}

.operational-status-row {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-bottom: 0.35rem;
}

.status-badge {
  display: inline-block;
  padding: 0.15rem 0.55rem;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.status-active {
  background: rgba(52, 211, 153, 0.15);
  color: #4ade80;
}

.status-idle {
  background: rgba(148, 163, 184, 0.12);
  color: #94a3b8;
}

.status-blocked {
  background: rgba(248, 113, 113, 0.15);
  color: #f87171;
}

.status-full {
  background: rgba(251, 191, 36, 0.15);
  color: #fbbf24;
}

.status-unconfigured {
  background: rgba(148, 163, 184, 0.12);
  color: #94a3b8;
  font-style: italic;
}

.idle-ticks-label {
  font-size: 0.72rem;
  color: var(--color-text-secondary);
}

.blocked-reason-text {
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  line-height: 1.45;
  margin: 0;
}

.operating-costs-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem;
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--color-border);
}

.operating-cost-label {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  flex-shrink: 0;
}

.operating-cost-item {
  font-size: 0.72rem;
  color: var(--color-text-secondary);
  background: color-mix(in srgb, var(--color-border) 40%, transparent);
  border-radius: 0.25rem;
  padding: 0.1rem 0.35rem;
}

/* ─── Recent Activity Panel ─── */
.recent-activity-panel {
  margin-top: 0.75rem;
}

.activity-list {
  list-style: none;
  padding: 0;
  margin: 0.5rem 0 0;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.activity-item {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  font-size: 0.78rem;
  line-height: 1.4;
}

.activity-tick {
  flex-shrink: 0;
  font-size: 0.68rem;
  font-weight: 700;
  color: var(--color-text-secondary);
  min-width: 3.5rem;
}

.activity-desc {
  color: var(--color-text);
}

.activity-purchased .activity-tick {
  color: #60a5fa;
}
.activity-manufactured .activity-tick {
  color: #4ade80;
}
.activity-sold .activity-tick {
  color: #c084fc;
}
.activity-moved .activity-tick {
  color: #fbbf24;
}

/* ── Procurement Mode Selector ── */
.procurement-mode-options {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.25rem;
}

.procurement-mode-option {
  display: flex;
  flex-direction: column;
  padding: 0.6rem 0.75rem;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 6px;
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
  gap: 0.15rem;
}

.procurement-mode-option:hover {
  border-color: var(--color-primary, #2563eb);
  background: var(--color-bg-hover, rgba(59, 130, 246, 0.1));
}

.procurement-mode-option.selected {
  border-color: var(--color-primary, #2563eb);
  background: var(--color-bg-selected, rgba(59, 130, 246, 0.15));
}

.procurement-mode-radio {
  position: absolute;
  opacity: 0;
  width: 0;
  height: 0;
  pointer-events: none;
}

.procurement-mode-label {
  font-weight: 600;
  font-size: 0.88rem;
  color: var(--color-text);
}

.procurement-mode-desc {
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  line-height: 1.35;
}

/* ── Procurement Preview Card ── */
.procurement-preview {
  margin-top: 1rem;
  padding: 0.85rem 1rem;
  border-radius: 8px;
  border: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface, #f8fafc);
}

.procurement-preview-title {
  font-size: 0.82rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-text-secondary);
  margin: 0 0 0.6rem;
}

.procurement-preview-loading {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
}

.procurement-preview-empty {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
}

.preview-status {
  display: inline-block;
  font-size: 0.82rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.preview-status.ok {
  color: #4ade80;
}

.preview-status.blocked {
  color: #dc2626;
}

.preview-details {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.preview-row {
  display: flex;
  gap: 0.5rem;
  font-size: 0.82rem;
}

.preview-label {
  color: var(--color-text-secondary);
  min-width: 7rem;
  flex-shrink: 0;
}

.preview-value {
  color: var(--color-text);
  font-weight: 500;
}

.preview-delivered {
  color: #4ade80;
  font-weight: 700;
}

.preview-blocked-price {
  color: #dc2626;
}

.preview-block-details {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.5rem;
}

.preview-block-reason {
  font-size: 0.82rem;
  font-weight: 600;
  color: #dc2626;
}

.preview-block-message {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  margin: 0.15rem 0 0.35rem;
  line-height: 1.45;
}

/* ── Sourcing Comparison Panel ── */
.sourcing-comparison {
  margin-top: 1rem;
  padding: 0.85rem 1rem;
  border-radius: 8px;
  border: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface, #f8fafc);
}

.sourcing-comparison-title {
  font-size: 0.82rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-text-secondary);
  margin: 0 0 0.25rem;
}

.sourcing-comparison-subtitle {
  margin: 0 0 0.75rem;
  font-size: 0.78rem;
}

.sourcing-comparison-loading,
.sourcing-comparison-empty {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
}

.sourcing-trap-note {
  font-size: 0.78rem;
  color: #fbbf24;
  background: rgba(251, 191, 36, 0.12);
  border: 1px solid rgba(251, 191, 36, 0.3);
  border-radius: 4px;
  padding: 0.35rem 0.6rem;
  margin-bottom: 0.6rem;
  line-height: 1.4;
}

.sourcing-table-wrapper {
  overflow-x: auto;
  margin: 0 -0.5rem;
}

.sourcing-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
}

.sourcing-table th {
  text-align: left;
  font-weight: 600;
  color: var(--color-text-secondary);
  padding: 0.3rem 0.5rem;
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  white-space: nowrap;
}

.sourcing-table td {
  padding: 0.35rem 0.5rem;
  border-bottom: 1px solid var(--color-border-muted, #f0f4f8);
  vertical-align: top;
}

.sourcing-row.recommended {
  background: rgba(52, 211, 153, 0.08);
}

.sourcing-row.ineligible {
  opacity: 0.65;
}

.sourcing-row.recommended td {
  border-bottom-color: rgba(52, 211, 153, 0.2);
}

.sourcing-col-source {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  min-width: 7rem;
}

.source-type-badge {
  font-size: 0.68rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
}

.source-name {
  font-weight: 500;
  color: var(--color-text);
}

.source-distance {
  font-size: 0.68rem;
  color: var(--color-text-secondary);
}

.sourcing-col-transit .transit-cost {
  color: #f59e0b;
  font-weight: 500;
}

.col-landed {
  font-weight: 700;
  color: var(--color-text);
}

.sourcing-row.recommended .col-landed strong {
  color: #34d399;
}

.sourcing-row.ineligible .col-landed strong {
  color: #dc2626;
}

.sc-badge {
  display: inline-block;
  padding: 0.15rem 0.45rem;
  border-radius: 3px;
  font-size: 0.68rem;
  font-weight: 600;
  white-space: nowrap;
}

.sc-badge--recommended {
  background: rgba(52, 211, 153, 0.15);
  color: #4ade80;
}

.sc-badge--eligible {
  background: rgba(96, 165, 250, 0.12);
  color: #60a5fa;
}

.sc-badge--blocked {
  background: rgba(248, 113, 113, 0.15);
  color: #f87171;
  cursor: help;
}

.sourcing-filter-hint {
  margin-top: 0.6rem;
  font-size: 0.75rem;
}

/* ── Quick Price Update Panel ── */
.mi-price-update-panel {
  margin-top: 1rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  padding: 0.75rem;
  background: var(--color-surface-raised, #f9fafb);
}

.mi-price-update-title {
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
  margin: 0 0 0.2rem;
}

.mi-price-update-desc {
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  margin: 0 0 0.6rem;
  line-height: 1.4;
}

.mi-price-update-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.mi-price-update-label {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  white-space: nowrap;
}

.mi-price-input {
  width: 6rem;
  padding: 0.3rem 0.5rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
  background: var(--color-background);
  color: var(--color-text);
}

.mi-price-update-btn {
  font-size: 0.8rem;
  padding: 0.3rem 0.75rem;
  white-space: nowrap;
}

.mi-price-impact-hint {
  font-size: 0.78rem;
  margin: 0 0 0.5rem;
  padding: 0.35rem 0.5rem;
  border-radius: var(--radius-sm);
  line-height: 1.4;
}

.mi-price-impact-raise {
  background: rgba(251, 191, 36, 0.12);
  color: #fbbf24;
  border: 1px solid rgba(251, 191, 36, 0.3);
}

.mi-price-impact-lower {
  background: rgba(52, 211, 153, 0.12);
  color: #4ade80;
  border: 1px solid rgba(52, 211, 153, 0.3);
}

.mi-price-success {
  font-size: 0.8rem;
  color: #4ade80;
  margin: 0.4rem 0 0;
}

.mi-price-error {
  font-size: 0.8rem;
  color: var(--color-danger, #dc2626);
  margin: 0.4rem 0 0;
}

/* ── B2B Competitive Price Hint ── */
.config-price-hint {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  margin: 0.25rem 0 0;
}

.btn-link {
  background: none;
  border: none;
  padding: 0;
  font-size: inherit;
  color: var(--color-primary, #3b82f6);
  cursor: pointer;
  text-decoration: underline;
  font-weight: 500;
}

.btn-link:hover {
  opacity: 0.8;
}

/* ── Flush Storage Section ── */
.flush-storage-section {
  margin-top: 0.75rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--color-border);
}

/* ── Unit upgrade panel ─────────────────────────────────────────────── */
.unit-upgrade-panel h5 {
  margin-bottom: 0.5rem;
}

.unit-upgrade-in-progress {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
}

.unit-upgrade-progress-badge {
  font-size: 1.25rem;
  line-height: 1;
}

.unit-upgrade-progress-body strong {
  font-size: 0.85rem;
  color: var(--color-text-primary);
}

.unit-upgrade-progress-desc {
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  margin: 0.2rem 0 0;
}

.unit-upgrade-max-level {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
}

.unit-upgrade-max-badge {
  color: #f59e0b;
  font-size: 1.1rem;
}

.unit-upgrade-max-note {
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  margin: 0.3rem 0 0;
}

.unit-upgrade-not-available p {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.unit-upgrade-levels {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  margin-bottom: 0.5rem;
}

.unit-upgrade-level {
  font-size: 0.85rem;
  font-weight: 600;
  padding: 0.2rem 0.55rem;
  border-radius: var(--radius-sm);
}

.current-level {
  background: var(--color-surface-secondary, #f3f4f6);
  color: var(--color-text-primary);
}

.next-level {
  background: rgba(52, 211, 153, 0.15);
  color: #4ade80;
}

.unit-upgrade-arrow {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
}

.unit-upgrade-stats {
  margin-bottom: 0.5rem;
}

.unit-upgrade-stat-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.8rem;
  padding: 0.2rem 0;
}

.unit-upgrade-stat-label {
  color: var(--color-text-secondary);
}

.stat-current {
  color: var(--color-text-secondary);
}

.stat-next {
  color: #4ade80;
  font-weight: 600;
}

.unit-upgrade-meta {
  display: flex;
  gap: 1rem;
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.6rem;
}

.unit-upgrade-cost {
  font-weight: 500;
}

.unit-upgrade-confirm-btn {
  width: 100%;
}

.flush-confirm-dialog {
  margin-top: 0.5rem;
  padding: 0.75rem;
  background: rgba(251, 191, 36, 0.08);
  border: 1px solid rgba(251, 191, 36, 0.3);
  border-radius: var(--radius-sm);
}

.flush-confirm-msg {
  font-size: 0.82rem;
  color: #fbbf24;
  margin: 0 0 0.6rem;
  line-height: 1.4;
}

.flush-confirm-actions {
  display: flex;
  gap: 0.5rem;
}

.form-success {
  font-size: 0.8rem;
  color: #4ade80;
  margin: 0.35rem 0 0;
}

@media (max-width: 900px) {
  .purchase-selector-page {
    padding: 1rem;
  }

  .purchase-selector-grid {
    grid-template-columns: 1fr;
  }
}
</style>
