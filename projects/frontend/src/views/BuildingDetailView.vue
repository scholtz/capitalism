<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AdvancedItemSelector from '@/components/buildings/AdvancedItemSelector.vue'
import UnitResourceHistoryPanel from '@/components/buildings/UnitResourceHistoryPanel.vue'
import {
  getInventorySourcingCostPerUnit,
  getPlannedUnitConstructionCost,
  getTotalInventorySourcingCost,
  getUnitConstructionCost,
  sumPlannedConfigurationCost,
} from '@/lib/buildingUnitEconomics'
import { isProductLocked } from '@/lib/productAccess'
import {
  formatPercent,
  formatUnitQuantity,
  getFillBucket,
  getUnitConfiguredItemId,
  getUnitPriceMetric,
} from '@/lib/gridTileHelpers'
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
import {
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
} from '@/lib/catalogPresentation'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import { getUnitResourceHistoryItemKey, type UnitResourceHistoryItemOption } from '@/lib/unitResourceHistory'
import type {
  Building,
  BuildingConfigurationPlanRemoval,
  BuildingConfigurationPlanUnit,
  BuildingUnit,
  BuildingUnitInventory,
  BuildingUnitInventorySummary,
  BuildingUnitResourceHistoryPoint,
  Company,
  GlobalExchangeOffer,
  ProductType,
  ResourceType,
} from '@/types'
import type { HorizontalLinkState, VerticalLinkState } from '@/lib/linkHelpers'

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
  isReverting?: boolean
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
/** Page-level error (building not found, load failed). Shown as a full-page error state. */
const error = ref<string | null>(null)
/** Inline save error (e.g. RECIPE_INPUT_MISMATCH). Shown within the planning section. */
const saveError = ref<string | null>(null)
const companyCash = ref<number | null>(null)
const isEditing = ref(false)
const selectedCell = ref<{ x: number; y: number } | null>(null)
const showUnitPicker = ref(false)
const draftUnits = ref<EditableGridUnit[]>([])
const editBaselineUnits = ref<EditableGridUnit[]>([])
const resourceTypes = ref<ResourceType[]>([])
const productTypes = ref<ProductType[]>([])
const unitInventorySummaries = ref<BuildingUnitInventorySummary[]>([])
const unitInventories = ref<BuildingUnitInventory[]>([])
const unitResourceHistories = ref<BuildingUnitResourceHistoryPoint[]>([])
const exchangeOffers = ref<GlobalExchangeOffer[]>([])
const exchangeOffersLoading = ref(false)
const showSaleDialog = ref(false)
const salePrice = ref<number | null>(null)
const savingSale = ref(false)
const cancellingPlan = ref(false)
const cancelPlanError = ref<string | null>(null)
const layoutName = ref('')
const showLayoutDialog = ref(false)
const selectedHistoryItemKey = ref<string | null>(null)

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
const showStarterSetupBanner = computed(
  () =>
    building.value?.type === 'FACTORY' &&
    activeUnits.value.length === 0 &&
    pendingConfiguration.value === null &&
    !isEditing.value,
)
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
  () =>
    !isEditing.value &&
    building.value?.type === 'FACTORY' &&
    (activeUnits.value.length > 0 || pendingConfiguration.value !== null) &&
    !showStarterSetupBanner.value,
)

/**
 * Mirrors showStarterSetupBanner but for SALES_SHOP buildings.
 */
const showSalesShopStarterBanner = computed(
  () =>
    building.value?.type === 'SALES_SHOP' &&
    activeUnits.value.length === 0 &&
    pendingConfiguration.value === null &&
    !isEditing.value,
)

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
  const isPublicSalesConfigured = !!(
    publicSales &&
    publicSales.productTypeId &&
    publicSales.minPrice !== null &&
    publicSales.minPrice !== undefined
  )
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
  () =>
    !isEditing.value &&
    building.value?.type === 'SALES_SHOP' &&
    (activeUnits.value.length > 0 || pendingConfiguration.value !== null) &&
    !showSalesShopStarterBanner.value,
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
      const wasActive = !!(baseline?.[flag as keyof typeof baseline])
      const isActive = !!(draft?.[flag as keyof typeof draft])
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
const selectedDisplayUnit = computed<GridUnit | undefined>(() => {
  if (!selectedCell.value) return undefined

  if (isEditing.value) {
    return getUnitAtFrom(plannedUnits.value, selectedCell.value.x, selectedCell.value.y)
  }

  return getUnitAtFrom(activeUnits.value, selectedCell.value.x, selectedCell.value.y)
})
const selectedPurchaseUnit = computed(() => selectedDisplayUnit.value?.unitType === 'PURCHASE' ? selectedDisplayUnit.value : undefined)
const selectedHistoryItemOptions = computed<UnitResourceHistoryItemOption[]>(() => getUnitResourceHistoryItemOptions(selectedDisplayUnit.value))
const selectedUnitResourceHistory = computed(() => getSelectedUnitResourceHistory(selectedDisplayUnit.value))

type ExchangeOfferItem = GlobalExchangeOffer & { blocked: boolean; blockedReason: string | null }

const exchangeOfferItems = computed<ExchangeOfferItem[]>(() => {
  const maxPrice = selectedPurchaseUnit.value?.maxPrice ?? null
  const minQuality = selectedPurchaseUnit.value?.minQuality ?? null
  return exchangeOffers.value.map((offer) => {
    if (maxPrice !== null && offer.deliveredPricePerUnit > maxPrice) {
      return { ...offer, blocked: true, blockedReason: 'maxPrice' }
    }
    if (minQuality !== null && offer.estimatedQuality < minQuality) {
      return { ...offer, blocked: true, blockedReason: 'minQuality' }
    }
    return { ...offer, blocked: false, blockedReason: null }
  })
})

const allExchangeOffersBlocked = computed(
  () => exchangeOfferItems.value.length > 0 && exchangeOfferItems.value.every((o) => o.blocked),
)

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
  saveError.value = null
}

function applyStarterLayout() {
  // Pre-populate the draft with a PURCHASE → MANUFACTURING → STORAGE chain at y=0
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
    },
  ]
  setDraftUnitsFrom(starterUnits)
  setEditBaselineFrom([])
  isEditing.value = true
  selectedCell.value = null
  showUnitPicker.value = false
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
    },
  ]
  setDraftUnitsFrom(shopStarterUnits)
  setEditBaselineFrom([])
  isEditing.value = true
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

function isUnitReverting(unit: GridUnit | undefined): boolean {
  if (!unit) return false
  return 'isReverting' in unit ? !!(unit as BuildingConfigurationPlanUnit | EditableGridUnit).isReverting : false
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

      const configuredPurchaseResourceIds = purchaseUnits
        .filter((pu) => pu.resourceTypeId)
        .map((pu) => pu.resourceTypeId!)

      const configuredPurchaseProductIds = purchaseUnits
        .filter((pu) => pu.productTypeId)
        .map((pu) => pu.productTypeId!)

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

  const activeUnit = activeUnits.value.find(
    (candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY,
  )

  if (!activeUnit) return undefined
  return unitInventorySummaries.value.find((summary) => summary.buildingUnitId === activeUnit.id)
}

function getUnitInventories(unit: GridUnit | undefined): BuildingUnitInventory[] {
  if (!unit) return []
  const directInventories = unitInventories.value.filter((inventory) => inventory.buildingUnitId === unit.id)
  if (directInventories.length > 0) {
    return [...directInventories].sort((left, right) => right.quantity - left.quantity)
  }

  const activeUnit = activeUnits.value.find(
    (candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY,
  )

  if (!activeUnit) return []
  return unitInventories.value
    .filter((inventory) => inventory.buildingUnitId === activeUnit.id)
    .sort((left, right) => right.quantity - left.quantity)
}

function getResolvedLiveUnitId(unit: GridUnit | undefined): string | null {
  if (!unit) return null

  const hasDirectLiveData = unitInventories.value.some((inventory) => inventory.buildingUnitId === unit.id)
    || unitResourceHistories.value.some((entry) => entry.buildingUnitId === unit.id)
  if (hasDirectLiveData) {
    return unit.id
  }

  const activeUnit = activeUnits.value.find(
    (candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY,
  )

  return activeUnit?.id ?? unit.id
}

function getUnitResourceHistory(unit: GridUnit | undefined): BuildingUnitResourceHistoryPoint[] {
  const resolvedUnitId = getResolvedLiveUnitId(unit)
  if (!resolvedUnitId) return []

  return unitResourceHistories.value
    .filter((entry) => entry.buildingUnitId === resolvedUnitId)
    .sort((left, right) => left.tick - right.tick)
}

function getHistoryItemLabel(
  resourceTypeId: string | null | undefined,
  productTypeId: string | null | undefined,
): string {
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

  return Array.from(options.values()).sort(
    (left, right) => (order.get(left.key) ?? Number.MAX_SAFE_INTEGER) - (order.get(right.key) ?? Number.MAX_SAFE_INTEGER),
  )
}

function getSelectedUnitResourceHistory(unit: GridUnit | undefined): BuildingUnitResourceHistoryPoint[] {
  const selectedKey = selectedHistoryItemKey.value
  if (!selectedKey) return []

  return getUnitResourceHistory(unit).filter(
    (entry) => getUnitResourceHistoryItemKey(entry.resourceTypeId, entry.productTypeId) === selectedKey,
  )
}

function getUnitInventoryItemCount(unit: GridUnit | undefined): number {
  return getUnitInventories(unit).length
}

function formatCurrency(value: number | null | undefined): string {
  const amount = value ?? 0
  const formatter = new Intl.NumberFormat(locale.value, {
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  })
  return `$${formatter.format(amount)}`
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
    return extraItems > 0
      ? `${primaryName} ${t('buildingDetail.inventory.moreItems', { count: extraItems })}`
      : primaryName
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
  const sourcingCostPerUnit = inventory.sourcingCostPerUnit
    ?? getInventorySourcingCostPerUnit(inventory.quantity, inventory.sourcingCostTotal)
  return sourcingCostPerUnit == null
    ? null
    : t('buildingDetail.inventory.perUnit', { value: formatCurrency(sourcingCostPerUnit) })
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
          totalSourcingCost
          sourcingCostPerUnit
        }
      }`,
      { buildingId: buildingId.value },
    )
    unitInventorySummaries.value = data.buildingUnitInventorySummaries
  } catch {
    unitInventorySummaries.value = []
  }
}

async function loadUnitInventories() {
  if (!auth.token) {
    unitInventories.value = []
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
    unitInventories.value = data.buildingUnitInventories
  } catch {
    unitInventories.value = []
  }
}

async function loadUnitResourceHistories() {
  if (!auth.token) {
    unitResourceHistories.value = []
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
    unitResourceHistories.value = data.buildingUnitResourceHistories
  } catch {
    unitResourceHistories.value = []
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
      gqlRequest<{ myCompanies: Company[] }>(
        `{ myCompanies {
          id
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
      companyCash.value = null
      error.value = t('buildingDetail.notFound')
      return
    }

    companyCash.value = companiesData.myCompanies.find((company) => company.id === building.value?.companyId)?.cash ?? null

    const sourceUnits = pendingConfiguration.value?.units ?? building.value.units
    setDraftUnitsFrom(sourceUnits)
    setEditBaselineFrom(sourceUnits)
    isEditing.value = false
    selectedCell.value = null
    showUnitPicker.value = false
    await Promise.all([
      loadUnitInventorySummaries(),
      loadUnitInventories(),
      loadUnitResourceHistories(),
    ])
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
      <div
        v-if="showSalesShopStarterBanner"
        class="starter-setup-banner starter-setup-banner--shop"
        role="region"
        aria-label="shop starter setup"
      >
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
          <button
            v-if="!isEditing"
            class="btn btn-danger btn-sm"
            :disabled="cancellingPlan"
            @click="cancelPlan"
          >
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
      <div
        v-if="showProductionChainPanel"
        class="production-chain-panel"
        role="region"
        aria-label="production chain status"
      >
        <div class="chain-panel-header">
          <h3 class="chain-panel-title">⚙️ {{ t('buildingDetail.productionChain.title') }}</h3>
          <span
            v-if="chainStatus.isChainComplete"
            class="chain-status-badge chain-status-badge--complete"
          >✅ {{ t('buildingDetail.productionChain.chainComplete') }}</span>
          <span v-else class="chain-status-badge chain-status-badge--incomplete"
            >⚠️ {{ t('buildingDetail.productionChain.chainIncomplete') }}</span
          >
        </div>

        <div class="chain-flow" role="list" aria-label="production chain steps">
          <!-- PURCHASE step -->
          <div
            class="chain-step"
            :class="chainStatus.isPurchaseConfigured ? 'chain-step--configured' : 'chain-step--missing'"
            role="listitem"
          >
            <div class="chain-step-icon">🛒</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.PURCHASE') }}</div>
            <div v-if="chainStatus.isPurchaseConfigured" class="chain-step-value">
              {{
                chainDisplayUnits.purchase?.resourceTypeId
                  ? getResourceName(chainDisplayUnits.purchase.resourceTypeId)
                  : getProductName(chainDisplayUnits.purchase?.productTypeId ?? null)
              }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.productionChain.notConfigured') }}
            </div>
          </div>

          <div class="chain-arrow" aria-hidden="true">→</div>

          <!-- MANUFACTURING step -->
          <div
            class="chain-step"
            :class="chainStatus.isManufacturingConfigured ? 'chain-step--configured' : 'chain-step--missing'"
            role="listitem"
          >
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
          <div
            class="chain-step"
            :class="chainStatus.isStoragePresent ? 'chain-step--configured' : 'chain-step--missing'"
            role="listitem"
          >
            <div class="chain-step-icon">📦</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.STORAGE') }}</div>
            <div class="chain-step-value">
              {{
                chainStatus.isManufacturingConfigured
                  ? getProductName(chainDisplayUnits.manufacturing?.productTypeId ?? null)
                  : t('buildingDetail.productionChain.storageDesc')
              }}
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
                resource: chainDisplayUnits.purchase?.resourceTypeId
                  ? getResourceName(chainDisplayUnits.purchase.resourceTypeId)
                  : getProductName(chainDisplayUnits.purchase?.productTypeId ?? null),
              })
            }}
          </p>
          <p class="chain-next-step">{{ t('buildingDetail.productionChain.nextStep') }}</p>
        </div>
      </div>

      <!-- Sales chain status panel: shown for sales shops with units saved -->
      <div
        v-if="showSalesChainPanel"
        class="production-chain-panel"
        role="region"
        aria-label="sales chain status"
      >
        <div class="chain-panel-header">
          <h3 class="chain-panel-title">🏪 {{ t('buildingDetail.salesChain.title') }}</h3>
          <span
            v-if="shopChainStatus.isChainComplete"
            class="chain-status-badge chain-status-badge--complete"
            >✅ {{ t('buildingDetail.salesChain.chainComplete') }}</span
          >
          <span v-else class="chain-status-badge chain-status-badge--incomplete"
            >⚠️ {{ t('buildingDetail.salesChain.chainIncomplete') }}</span
          >
        </div>

        <div class="chain-flow" role="list" aria-label="sales chain steps">
          <!-- PURCHASE step -->
          <div
            class="chain-step"
            :class="shopChainStatus.isPurchaseConfigured ? 'chain-step--configured' : 'chain-step--missing'"
            role="listitem"
          >
            <div class="chain-step-icon">🛒</div>
            <div class="chain-step-type">{{ t('buildingDetail.unitTypes.PURCHASE') }}</div>
            <div v-if="shopChainStatus.isPurchaseConfigured" class="chain-step-value">
              {{
                shopChainDisplayUnits.purchase?.resourceTypeId
                  ? getResourceName(shopChainDisplayUnits.purchase.resourceTypeId)
                  : getProductName(shopChainDisplayUnits.purchase?.productTypeId ?? null)
              }}
            </div>
            <div v-else class="chain-step-missing-label">
              {{ t('buildingDetail.salesChain.notConfigured') }}
            </div>
          </div>

          <div class="chain-arrow" aria-hidden="true">→</div>

          <!-- PUBLIC_SALES step -->
          <div
            class="chain-step"
            :class="shopChainStatus.isPublicSalesConfigured ? 'chain-step--configured' : 'chain-step--missing'"
            role="listitem"
          >
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
                      :style="getUnitAtFrom(activeUnits, x, y)
                        ? { borderColor: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(activeUnits, x, y)!.unitType) + '18' }
                        : {}"
                      role="button"
                      :tabindex="getUnitAtFrom(activeUnits, x, y) ? 0 : -1"
                      :aria-label="getGridCellAriaLabel(getUnitAtFrom(activeUnits, x, y))"
                      @click="selectedCell = getUnitAtFrom(activeUnits, x, y) ? { x, y } : null"
                      @keydown.enter.space.prevent="selectedCell = getUnitAtFrom(activeUnits, x, y) ? { x, y } : null"
                    >
                      <template v-if="getUnitAtFrom(activeUnits, x, y)">
                        <div class="cell-heading" aria-hidden="true">
                          <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(activeUnits, x, y)!.unitType}`) }}</span>
                          <span class="cell-level">Lv.{{ getUnitAtFrom(activeUnits, x, y)!.level }}</span>
                        </div>
                        <div v-if="getUnitDisplayLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-item-block" aria-hidden="true">
                          <img
                            v-if="getUnitDisplayImageUrl(getUnitAtFrom(activeUnits, x, y))"
                            class="cell-item-image"
                            :src="getUnitDisplayImageUrl(getUnitAtFrom(activeUnits, x, y))!"
                            alt=""
                          />
                          <span v-else class="cell-item-avatar">{{ getUnitDisplayMonogram(getUnitAtFrom(activeUnits, x, y)) }}</span>
                          <div class="cell-item-copy">
                            <span class="cell-item">{{ getUnitDisplayLabel(getUnitAtFrom(activeUnits, x, y)) }}</span>
                            <span v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))" class="cell-stock">
                              {{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.quantity) }}/{{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))!.capacity) }}
                            </span>
                          </div>
                        </div>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y))" class="cell-metric" aria-hidden="true">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(activeUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, x, y))" class="cell-value" aria-hidden="true">
                          {{ t('buildingDetail.inventory.sourcingCostsShort', { value: getUnitInventoryCostLabel(getUnitAtFrom(activeUnits, x, y)) }) }}
                        </span>
                        <div
                          v-if="getUnitInventorySummary(getUnitAtFrom(activeUnits, x, y))?.capacity"
                          class="cell-capacity"
                          aria-hidden="true"
                        >
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
                      :class="[`link-state-${getHorizontalLinkStateFor(activeUnits, x, y)}`, { active: isHorizontalLinkActiveFor(activeUnits, x, y), disabled: !canToggleHorizontalLink(activeUnits, x, y) }]"
                    >
                      <span class="link-line"></span>
                      <span v-if="getHorizontalLinkStateFor(activeUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{ getHorizontalLinkArrow(getHorizontalLinkStateFor(activeUnits, x, y)) }}</span>
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
                      <span v-if="getVerticalLinkStateFor(activeUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{ getVerticalLinkArrow(getVerticalLinkStateFor(activeUnits, x, y)) }}</span>
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
                <li
                  v-for="(change, i) in draftLinkChanges"
                  :key="i"
                  class="link-change-item"
                  :class="change.changeType === 'added' ? 'link-change-added' : 'link-change-removed'"
                >
                  <span class="link-change-badge" aria-hidden="true">{{ change.changeType === 'added' ? '+' : '−' }}</span>
                  <span>{{ change.description }}</span>
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
                      :style="getUnitAtFrom(plannedUnits, x, y)
                        ? { borderColor: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType), background: getUnitColor(getUnitAtFrom(plannedUnits, x, y)!.unitType) + '18' }
                        : {}"
                      :aria-label="getGridCellAriaLabel(getUnitAtFrom(plannedUnits, x, y))"
                      @click="clickDraftCell(x, y)"
                    >
                      <template v-if="getUnitAtFrom(plannedUnits, x, y)">
                        <div class="cell-heading" aria-hidden="true">
                          <span class="cell-type">{{ t(`buildingDetail.unitTypes.${getUnitAtFrom(plannedUnits, x, y)!.unitType}`) }}</span>
                          <span class="cell-level">Lv.{{ getUnitAtFrom(plannedUnits, x, y)!.level }}</span>
                        </div>
                        <div v-if="getUnitDisplayLabel(getUnitAtFrom(plannedUnits, x, y))" class="cell-item-block" aria-hidden="true">
                          <img
                            v-if="getUnitDisplayImageUrl(getUnitAtFrom(plannedUnits, x, y))"
                            class="cell-item-image"
                            :src="getUnitDisplayImageUrl(getUnitAtFrom(plannedUnits, x, y))!"
                            alt=""
                          />
                          <span v-else class="cell-item-avatar">{{ getUnitDisplayMonogram(getUnitAtFrom(plannedUnits, x, y)) }}</span>
                          <div class="cell-item-copy">
                            <span class="cell-item">{{ getUnitDisplayLabel(getUnitAtFrom(plannedUnits, x, y)) }}</span>
                            <span v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))" class="cell-stock">
                              {{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.quantity) }}/{{ formatUnitQuantity(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.capacity) }}
                            </span>
                          </div>
                        </div>
                        <span v-if="getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y))" class="cell-metric" aria-hidden="true">
                          {{ getUnitPrimaryMetric(getUnitAtFrom(plannedUnits, x, y)) }}
                        </span>
                        <span v-if="getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, x, y))" class="cell-value" aria-hidden="true">
                          {{ t('buildingDetail.inventory.sourcingCostsShort', { value: getUnitInventoryCostLabel(getUnitAtFrom(plannedUnits, x, y)) }) }}
                        </span>
                        <div
                          v-if="getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))?.capacity"
                          class="cell-capacity"
                          aria-hidden="true"
                        >
                          <span
                            class="cell-capacity-fill"
                            :data-fill="getFillBucket(getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.fillPercent)"
                            :style="{ width: `${Math.round((getUnitInventorySummary(getUnitAtFrom(plannedUnits, x, y))!.fillPercent ?? 0) * 100)}%` }"
                          ></span>
                        </div>
                        <span v-if="getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) > 0" class="cell-pending" aria-hidden="true">
                          {{ t('buildingDetail.unitUnavailableFor', { ticks: getDisplayedTicks(getUnitAtFrom(plannedUnits, x, y)!) }) }}
                        </span>
                        <span
                          v-if="isUnitReverting(getUnitAtFrom(plannedUnits, x, y))"
                          class="cell-reverting"
                          aria-hidden="true"
                        >{{ t('buildingDetail.reverting') }}</span>
                      </template>
                      <template v-else>
                        <span class="cell-empty" aria-hidden="true">+</span>
                      </template>
                    </button>

                    <button
                      v-if="x < 3"
                      :key="`planned-horizontal-${x}-${y}`"
                      class="link-toggle horizontal"
                      :class="[`link-state-${getHorizontalLinkStateFor(plannedUnits, x, y)}`, { active: isHorizontalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleHorizontalLink(plannedUnits, x, y) }]"
                      :disabled="!canToggleHorizontalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkHorizontalAriaLabel', { state: getHorizontalLinkStateFor(plannedUnits, x, y) })"
                      @click="toggleHorizontalLink(x, y)"
                    >
                      <span class="link-line"></span>
                      <span v-if="getHorizontalLinkStateFor(plannedUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{ getHorizontalLinkArrow(getHorizontalLinkStateFor(plannedUnits, x, y)) }}</span>
                    </button>
                  </template>
                </div>

                <div v-if="y < 3" class="grid-row connector-row">
                  <template v-for="x in gridIndexes" :key="`planned-connector-${x}-${y}`">
                    <button
                      class="link-toggle vertical"
                      :class="[`link-state-${getVerticalLinkStateFor(plannedUnits, x, y)}`, { active: isVerticalLinkActiveFor(plannedUnits, x, y), disabled: !canToggleVerticalLink(plannedUnits, x, y) }]"
                      :disabled="!canToggleVerticalLink(plannedUnits, x, y)"
                      :aria-label="t('buildingDetail.linkVerticalAriaLabel', { state: getVerticalLinkStateFor(plannedUnits, x, y) })"
                      @click="toggleVerticalLink(x, y)"
                    >
                      <span class="link-line"></span>
                      <span v-if="getVerticalLinkStateFor(plannedUnits, x, y) !== 'none'" class="link-arrow" aria-hidden="true">{{ getVerticalLinkArrow(getVerticalLinkStateFor(plannedUnits, x, y)) }}</span>
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
              <button class="btn btn-ghost" @click="selectedCell = null">{{ t('common.close') }}</button>
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
                    <label class="config-label">{{ t('buildingDetail.config.minPrice') }}</label>
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice" @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).value !== '' ? ($event.target as HTMLInputElement).valueAsNumber : null)" min="0.01" step="0.01" />
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
                    <input type="number" class="form-input" :value="getDraftUnitAt(selectedCell.x, selectedCell.y)!.minPrice" @input="updateSelectedUnitConfig('minPrice', ($event.target as HTMLInputElement).value !== '' ? ($event.target as HTMLInputElement).valueAsNumber : null)" min="0.01" step="0.01" />
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
                      <img
                        v-if="getInventoryItemImageUrl(inventory)"
                        class="inventory-item-image"
                        :src="getInventoryItemImageUrl(inventory)!"
                        :alt="getInventoryItemName(inventory)"
                      />
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

              <div
                v-if="getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y))"
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.costSummaryTitle') }}</h5>
                <div class="unit-stats">
                  <span class="stat">
                    {{ t('buildingDetail.unitCost', { cost: getDraftUnitConstructionCostLabel(getUnitAtFrom(plannedUnits, selectedCell.x, selectedCell.y)) }) }}
                  </span>
                </div>
              </div>

              <div
                v-if="selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')"
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <template v-else>
                  <p v-if="allExchangeOffersBlocked" class="config-help exchange-no-valid-offers">
                    {{ t('buildingDetail.exchange.noValidOffers') }}
                  </p>
                  <ul class="exchange-offers-list">
                    <li v-for="offer in exchangeOfferItems" :key="`${offer.cityId}-${offer.resourceTypeId}`" :class="['exchange-offer-item', { 'offer-blocked': offer.blocked }]">
                      <div class="exchange-offer-header">
                        <strong>{{ offer.cityName }}</strong>
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
                </template>
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
                      <img
                        v-if="getInventoryItemImageUrl(inventory)"
                        class="inventory-item-image"
                        :src="getInventoryItemImageUrl(inventory)!"
                        :alt="getInventoryItemName(inventory)"
                      />
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
              </div>

              <UnitResourceHistoryPanel
                v-if="selectedHistoryItemOptions.length > 0"
                :items="selectedHistoryItemOptions"
                :selected-item-key="selectedHistoryItemKey"
                :history="selectedUnitResourceHistory"
                @update:selected-item-key="selectedHistoryItemKey = $event"
              />
              <div
                v-if="selectedPurchaseUnit && 'resourceTypeId' in selectedPurchaseUnit && selectedPurchaseUnit.resourceTypeId && ['EXCHANGE', 'OPTIMAL'].includes(selectedPurchaseUnit.purchaseSource ?? '')"
                class="unit-insight-card"
              >
                <h5>{{ t('buildingDetail.exchange.title') }}</h5>
                <p class="config-help">{{ t('buildingDetail.exchange.subtitle') }}</p>
                <p class="config-help" v-if="exchangeOffersLoading">{{ t('common.loading') }}</p>
                <template v-else>
                  <p v-if="allExchangeOffersBlocked" class="config-help exchange-no-valid-offers">
                    {{ t('buildingDetail.exchange.noValidOffers') }}
                  </p>
                  <ul class="exchange-offers-list">
                    <li v-for="offer in exchangeOfferItems" :key="`${offer.cityId}-${offer.resourceTypeId}`" :class="['exchange-offer-item', { 'offer-blocked': offer.blocked }]">
                      <div class="exchange-offer-header">
                        <strong>{{ offer.cityName }}</strong>
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
                </template>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="sidebar sidebar-placeholder">
          <div class="unit-config">
            <div class="unit-config-header">
              <h3>{{ t('buildingDetail.unitDetails') }}</h3>
            </div>
            <div class="unit-detail placeholder-detail">
              <h4>{{ t('buildingDetail.sidebarPlaceholderTitle') }}</h4>
              <p class="unit-desc">
                {{ isEditing ? t('buildingDetail.sidebarPlaceholderBodyEditing') : t('buildingDetail.sidebarPlaceholderBody') }}
              </p>
              <div v-if="isEditing" class="unit-insight-card placeholder-summary-card">
                <h5>{{ t('buildingDetail.costSummaryTitle') }}</h5>
                <div class="unit-stats">
                  <span class="stat">{{ t('buildingDetail.totalBuildCost', { cost: formatCurrency(draftConstructionCost) }) }}</span>
                  <span v-if="projectedCompanyCashAfterApply != null" class="stat">
                    {{ t('buildingDetail.cashAfterApply', { cash: formatCurrency(projectedCompanyCashAfterApply) }) }}
                  </span>
                </div>
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
  color: #15803d;
}

.power-status-pill.power-status-constrained {
  background: rgba(251, 191, 36, 0.15);
  color: #b45309;
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
  margin-bottom: 1.5rem;
  background: linear-gradient(135deg, rgba(19, 127, 236, 0.09), rgba(0, 200, 83, 0.08));
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
  background: #d1fae5;
  color: #065f46;
}

.chain-status-badge--incomplete {
  background: #fef3c7;
  color: #92400e;
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
  background: #f0fdf4;
}

.chain-step--missing {
  border-color: #fbbf24;
  background: #fffbeb;
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
  color: var(--color-text-primary);
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
  transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
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

.cell-level,
.cell-pending,
.cell-reverting {
  font-size: 0.625rem;
  flex-shrink: 0;
}

.cell-pending {
  color: #b45309;
  text-align: center;
}

.cell-reverting {
  color: #7c3aed;
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

.placeholder-detail {
  min-height: 240px;
}

.placeholder-summary-card {
  border-top-style: dashed;
}

.grid-cell.clickable {
  cursor: pointer;
}
</style>
