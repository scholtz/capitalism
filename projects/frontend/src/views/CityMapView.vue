<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useGameStateStore } from '@/stores/gameState'
import { gqlRequest, GraphQLError } from '@/lib/graphql'
import {
  getLotStatus as lotStatusFromOwnership,
  getLotMarkerColor as markerColorFromStatus,
  formatPopulationIndex,
  populationIndexClass,
  canPurchaseLot as isPurchasable,
  canSubmitPurchaseForm as isFormSubmittable,
  constructionCostForType,
  constructionTicksForType,
  constructionTicksRemaining as computeConstructionTicksRemaining,
} from '@/lib/cityMapHelpers'
import { getActiveCompany } from '@/lib/accountContext'
import type { City, BuildingLot, Company, PurchaseLotResult, CityMediaHouseInfo } from '@/types'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()

const cityId = computed(() => route.params.id as string)
const highlightedBuildingId = computed(() => (typeof route.query.building === 'string' ? route.query.building : null))

const loading = ref(true)
const error = ref<string | null>(null)
const city = ref<City | null>(null)
const lots = ref<BuildingLot[]>([])
const companies = ref<Company[]>([])
const selectedLot = ref<BuildingLot | null>(null)
const showAvailableOnly = ref(false)
const viewMode = ref<'map' | 'list'>('map')

// Purchase form state
const purchaseMode = ref(false)
const selectedBuildingType = ref('')
const buildingName = ref('')
const selectedMediaType = ref('')
const purchasing = ref(false)
const purchaseError = ref<string | null>(null)
const purchaseSuccess = ref<string | null>(null)
const justPurchasedBuildingId = ref<string | null>(null)
const justPurchasedBuildingType = ref<string | null>(null)
const justPurchasedIsUnderConstruction = ref(false)
const justPurchasedConstructionCompletesAtTick = ref<number | null>(null)

// Map reference
const mapContainer = ref<HTMLDivElement | null>(null)
let map: L.Map | null = null

// City media houses
const cityMediaHouses = ref<CityMediaHouseInfo[]>([])
const mediaHousesLoading = ref(false)
let markers: L.Marker[] = []

const filteredLots = computed(() => {
  if (showAvailableOnly.value) {
    return lots.value.filter((lot) => !lot.ownerCompanyId)
  }
  return lots.value
})

const suitableTypesForLot = computed(() => {
  if (!selectedLot.value) return []
  return selectedLot.value.suitableTypes.split(',').map((s) => s.trim())
})

const isOwnedByPlayer = computed(() => {
  if (!selectedLot.value) return false
  return companies.value.some((c) => c.id === selectedLot.value?.ownerCompanyId)
})

const activeCompany = computed(() => getActiveCompany(auth.player, companies.value))
const isCompanyAccountActive = computed(() => auth.player?.activeAccountType === 'COMPANY' && !!activeCompany.value)
const isOwnedByActiveCompany = computed(() => !!selectedLot.value?.ownerCompanyId && selectedLot.value.ownerCompanyId === activeCompany.value?.id)
const isOwnedByDifferentControlledCompany = computed(() => isOwnedByPlayer.value && !!selectedLot.value?.ownerCompanyId && selectedLot.value.ownerCompanyId !== activeCompany.value?.id)

const canPurchase = computed(() => (selectedLot.value ? isCompanyAccountActive.value && isPurchasable(auth.isAuthenticated, companies.value.length, selectedLot.value.ownerCompanyId) : false))

const canSubmitPurchase = computed(() => {
  const baseValid = isFormSubmittable(selectedBuildingType.value, buildingName.value, activeCompany.value?.id ?? '', purchasing.value)
  // Media houses require a channel type selection.
  if (selectedBuildingType.value === 'MEDIA_HOUSE' && !selectedMediaType.value) return false
  return baseValid
})

const selectedCompany = computed(() => activeCompany.value)

const cashAfterPurchase = computed(() => {
  if (!selectedCompany.value || !selectedLot.value) return null
  const constructionCost = selectedBuildingType.value ? constructionCostForType(selectedBuildingType.value) : 0
  return selectedCompany.value.cash - selectedLot.value.price - constructionCost
})

/** Returns remaining construction ticks for the current building, using the live tick from the game state store. */
function constructionTicksRemaining(completesAtTick: number | null): number {
  const currentTick = gameStateStore.gameState?.currentTick ?? 0
  return computeConstructionTicksRemaining(completesAtTick, currentTick)
}

function getLotStatus(lot: BuildingLot): 'available' | 'owned' | 'yours' {
  return lotStatusFromOwnership(
    lot.ownerCompanyId,
    companies.value.map((c) => c.id),
  )
}

function getLotMarkerColor(lot: BuildingLot): string {
  return markerColorFromStatus(getLotStatus(lot))
}

function formatCurrency(value: number): string {
  return '$' + value.toLocaleString(locale.value)
}

function formatBuildingType(type: string): string {
  const key = `buildings.types.${type}`
  const translated = t(key)
  if (translated !== key) return translated
  return type.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}

function populationIndexLabel(value: number): string {
  if (value >= 1.8) return t('cityMap.populationIndexVeryHigh')
  if (value >= 1.3) return t('cityMap.populationIndexHigh')
  if (value >= 0.9) return t('cityMap.populationIndexMedium')
  return t('cityMap.populationIndexLow')
}

/**
 * Returns a short strategic recommendation label for the lot based on its
 * population index and resource data. This implements the ROADMAP requirement:
 * "include a simple recommendation label such as 'strong for retail demand,'
 * 'balanced starter location,' or 'resource-oriented.'"
 */
function strategicRecommendation(lot: BuildingLot): { key: string; cssClass: string } {
  const suitable = lot.suitableTypes.split(',').map((s) => s.trim())
  const hasMine = suitable.includes('MINE')
  const hasRetail = suitable.includes('SALES_SHOP')
  const hasFactory = suitable.includes('FACTORY')

  if (hasMine && lot.resourceType) {
    return { key: 'recommendationResourceOriented', cssClass: 'rec-resource' }
  }
  if (hasRetail && lot.populationIndex >= 1.3) {
    return { key: 'recommendationStrongRetail', cssClass: 'rec-retail' }
  }
  if (hasFactory && lot.populationIndex < 0.9) {
    return { key: 'recommendationIndustrialEfficiency', cssClass: 'rec-industrial' }
  }
  return { key: 'recommendationBalancedStarter', cssClass: 'rec-balanced' }
}

function materialQualityLabel(quality: number): string {
  if (quality >= 0.8) return t('cityMap.rawMaterialQualityExcellent')
  if (quality >= 0.6) return t('cityMap.rawMaterialQualityGood')
  if (quality >= 0.4) return t('cityMap.rawMaterialQualityFair')
  return t('cityMap.rawMaterialQualityPoor')
}

function materialQualityClass(quality: number): string {
  if (quality >= 0.8) return 'quality-excellent'
  if (quality >= 0.6) return 'quality-good'
  if (quality >= 0.4) return 'quality-fair'
  return 'quality-poor'
}

function placementGuidanceKey(buildingType: string): string {
  const map: Record<string, string> = {
    SALES_SHOP: 'placementGuidanceSalesShop',
    COMMERCIAL: 'placementGuidanceCommercial',
    FACTORY: 'placementGuidanceFactory',
    MINE: 'placementGuidanceMine',
    APARTMENT: 'placementGuidanceApartment',
    RESEARCH_DEVELOPMENT: 'placementGuidanceResearchDevelopment',
    POWER_PLANT: 'placementGuidancePowerPlant',
    BANK: 'placementGuidanceBank',
    EXCHANGE: 'placementGuidanceExchange',
    MEDIA_HOUSE: 'placementGuidanceMediaHouse',
  }
  return map[buildingType] ?? 'placementGuidanceGeneric'
}

function postPurchaseBodyKey(buildingType: string): string {
  const map: Record<string, string> = {
    FACTORY: 'postPurchaseBodyFactory',
    MINE: 'postPurchaseBodyMine',
    SALES_SHOP: 'postPurchaseBodySalesShop',
    RESEARCH_DEVELOPMENT: 'postPurchaseBodyResearchDevelopment',
    APARTMENT: 'postPurchaseBodyApartment',
    COMMERCIAL: 'postPurchaseBodyCommercial',
    MEDIA_HOUSE: 'postPurchaseBodyMediaHouse',
    BANK: 'postPurchaseBodyBank',
    EXCHANGE: 'postPurchaseBodyExchange',
    POWER_PLANT: 'postPurchaseBodyPowerPlant',
  }
  return map[buildingType] ?? 'postPurchaseBody'
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    if (auth.isAuthenticated && !auth.player) {
      await auth.fetchMe()
    }

    const cityData = await gqlRequest<{ city: City }>(
      `query GetCity($id: UUID!) {
        city(id: $id) {
          id name countryCode latitude longitude population
          resources { resourceType { id name slug category } abundance }
        }
      }`,
      { id: cityId.value },
    )
    const lotsData = await gqlRequest<{ cityLots: BuildingLot[] }>(
      `query CityLots($cityId: UUID!) {
        cityLots(cityId: $cityId) {
          id cityId name description district latitude longitude
          populationIndex basePrice price suitableTypes
          ownerCompanyId buildingId
          ownerCompany { id name }
          building { id name type isUnderConstruction constructionCompletesAtTick constructionCost }
          resourceType { id name slug }
          materialQuality materialQuantity
        }
      }`,
      { cityId: cityId.value },
    )
    const companiesData: { myCompanies: Company[] } = auth.isAuthenticated
      ? await gqlRequest<{ myCompanies: Company[] }>(`{ myCompanies { id name cash foundedAtUtc buildings { id } } }`)
      : { myCompanies: [] }

    city.value = cityData.city
    lots.value = lotsData.cityLots
    companies.value = companiesData.myCompanies
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load city data'
  } finally {
    loading.value = false
  }
}

async function fetchMediaHouses() {
  if (!cityId.value) return
  mediaHousesLoading.value = true
  try {
    const data = await gqlRequest<{ cityMediaHouses: CityMediaHouseInfo[] }>(
      `query CityMediaHouses($cityId: UUID!) {
        cityMediaHouses(cityId: $cityId) {
          id name mediaType effectivenessMultiplier ownerCompanyName powerStatus isUnderConstruction
        }
      }`,
      { cityId: cityId.value },
    )
    cityMediaHouses.value = data.cityMediaHouses ?? []
  } catch {
    cityMediaHouses.value = []
  } finally {
    mediaHousesLoading.value = false
  }
}

function createMarkerIcon(color: string, isSelected: boolean): L.DivIcon {
  const size = isSelected ? 18 : 12
  const border = isSelected ? '3px solid #fff' : '2px solid rgba(255,255,255,0.8)'
  return L.divIcon({
    className: 'lot-marker',
    html: `<div style="
      width:${size}px;height:${size}px;
      background:${color};
      border-radius:50%;
      border:${border};
      box-shadow:0 2px 6px rgba(0,0,0,0.4);
    "></div>`,
    iconSize: [size + 6, size + 6],
    iconAnchor: [(size + 6) / 2, (size + 6) / 2],
  })
}

function initMap() {
  if (!mapContainer.value || !city.value) return

  map = L.map(mapContainer.value, {
    center: [city.value.latitude, city.value.longitude],
    zoom: 14,
    zoomControl: true,
  })

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19,
  }).addTo(map)

  updateMarkers()
}

function updateMarkers() {
  if (!map) return

  // Clear existing markers
  markers.forEach((m) => m.remove())
  markers = []

  for (const lot of filteredLots.value) {
    const color = getLotMarkerColor(lot)
    const isSelected = selectedLot.value?.id === lot.id
    const icon = createMarkerIcon(color, isSelected)

    const marker = L.marker([lot.latitude, lot.longitude], { icon }).addTo(map)

    marker.bindTooltip(lot.name, {
      direction: 'top',
      offset: [0, -10],
    })

    marker.on('click', () => {
      selectLot(lot)
    })

    markers.push(marker)
  }

  // Fit bounds if we have lots
  if (filteredLots.value.length > 0) {
    const bounds = L.latLngBounds(filteredLots.value.map((lot) => [lot.latitude, lot.longitude] as [number, number]))
    map.fitBounds(bounds.pad(0.15))
  }
}

function selectLot(lot: BuildingLot) {
  selectedLot.value = lot
  purchaseMode.value = false
  purchaseError.value = null
  purchaseSuccess.value = null
  justPurchasedBuildingId.value = null
  justPurchasedBuildingType.value = null
  justPurchasedIsUnderConstruction.value = false
  justPurchasedConstructionCompletesAtTick.value = null
  selectedBuildingType.value = ''
  buildingName.value = ''
  selectedMediaType.value = ''

  // Update markers to show selection
  updateMarkers()

  // Pan map to selected lot
  if (map) {
    map.panTo([lot.latitude, lot.longitude])
  }
}

function selectRequestedBuildingLot() {
  const buildingId = highlightedBuildingId.value
  if (!buildingId) return

  const matchingLot = lots.value.find((lot) => lot.buildingId === buildingId)
  if (!matchingLot) return

  if (selectedLot.value?.id === matchingLot.id) {
    if (map) {
      map.panTo([matchingLot.latitude, matchingLot.longitude])
    }
    return
  }

  selectLot(matchingLot)
}

function startPurchase() {
  purchaseMode.value = true
  purchaseError.value = null
  purchaseSuccess.value = null
}

async function confirmPurchase() {
  if (!selectedLot.value || !canSubmitPurchase.value || !activeCompany.value) return

  purchasing.value = true
  purchaseError.value = null

  try {
    const data = await gqlRequest<{ purchaseLot: PurchaseLotResult }>(
      `mutation PurchaseLot($input: PurchaseLotInput!) {
        purchaseLot(input: $input) {
          lot {
            id cityId name description district latitude longitude price suitableTypes
            ownerCompanyId buildingId
            ownerCompany { id name }
            building { id name type isUnderConstruction constructionCompletesAtTick constructionCost }
          }
          building { id name type isUnderConstruction constructionCompletesAtTick constructionCost }
          company { id name cash }
        }
      }`,
      {
        input: {
          companyId: activeCompany.value.id,
          lotId: selectedLot.value.id,
          buildingType: selectedBuildingType.value,
          buildingName: buildingName.value.trim() || null,
          mediaType: selectedBuildingType.value === 'MEDIA_HOUSE' ? selectedMediaType.value || null : null,
        },
      },
    )

    // Update the lot in our local state
    const idx = lots.value.findIndex((l) => l.id === data.purchaseLot.lot.id)
    if (idx >= 0) {
      lots.value[idx] = data.purchaseLot.lot
    }
    selectedLot.value = data.purchaseLot.lot

    // Update company cash
    const companyIdx = companies.value.findIndex((c) => c.id === data.purchaseLot.company.id)
    if (companyIdx >= 0) {
      companies.value[companyIdx]!.cash = data.purchaseLot.company.cash
    }

    purchaseSuccess.value = t('cityMap.purchaseSuccess')
    justPurchasedBuildingId.value = data.purchaseLot.building.id
    justPurchasedBuildingType.value = data.purchaseLot.building.type
    justPurchasedIsUnderConstruction.value = data.purchaseLot.building.isUnderConstruction ?? false
    justPurchasedConstructionCompletesAtTick.value = data.purchaseLot.building.constructionCompletesAtTick ?? null
    purchaseMode.value = false
    updateMarkers()
  } catch (e: unknown) {
    if (e instanceof GraphQLError) {
      if (e.code === 'LOT_ALREADY_OWNED') {
        // Stale lot: another player claimed this lot after the player opened the form.
        // Re-fetch just this single lot so the UI reflects new ownership immediately
        // without fetching the full city list.
        purchaseError.value = t('cityMap.purchaseErrorAlreadyOwned')
        purchaseMode.value = false
        try {
          const refreshedLot = await gqlRequest<{ lot: BuildingLot | null }>(
            `query GetLot($id: UUID!) {
              lot(id: $id) {
                id cityId name description district latitude longitude price suitableTypes
                ownerCompanyId buildingId
                ownerCompany { id name }
                building { id name type isUnderConstruction constructionCompletesAtTick constructionCost }
              }
            }`,
            { id: selectedLot.value?.id },
          )
          if (refreshedLot.lot) {
            const idx = lots.value.findIndex((l) => l.id === refreshedLot.lot!.id)
            if (idx >= 0) lots.value[idx] = refreshedLot.lot
            selectedLot.value = refreshedLot.lot
            updateMarkers()
          }
        } catch {
          // Silently ignore refresh errors; the stale-lot error message is already shown
        }
      } else if (e.code === 'INSUFFICIENT_FUNDS') {
        purchaseError.value = t('cityMap.purchaseErrorInsufficientFunds')
      } else if (e.code === 'UNSUITABLE_BUILDING_TYPE') {
        purchaseError.value = t('cityMap.purchaseErrorUnsuitable')
      } else {
        purchaseError.value = e.message
      }
    } else {
      purchaseError.value = e instanceof Error ? e.message : t('cityMap.purchaseError')
    }
  } finally {
    purchasing.value = false
  }
}

watch(filteredLots, () => {
  if (map) {
    updateMarkers()
  }
})

watch(
  () => [highlightedBuildingId.value, lots.value.map((lot) => `${lot.id}:${lot.buildingId ?? ''}`).join('|')],
  () => {
    if (highlightedBuildingId.value) {
      selectRequestedBuildingLot()
    }
  },
)

onMounted(async () => {
  await fetchData()
  void fetchMediaHouses()
  await nextTick()
  if (viewMode.value === 'map') {
    initMap()
  }
  selectRequestedBuildingLot()
})

onUnmounted(() => {
  if (map) {
    map.remove()
    map = null
  }
})

watch(viewMode, async (mode) => {
  if (mode === 'map') {
    await nextTick()
    if (!map) {
      initMap()
    } else {
      map.invalidateSize()
    }
  }
})
</script>

<template>
  <div class="city-map-view container">
    <!-- Header -->
    <div class="page-header">
      <div>
        <button class="btn btn-secondary btn-sm" @click="router.push('/dashboard')">← {{ t('cityMap.backToDashboard') }}</button>
        <h1 v-if="city">🗺️ {{ city.name }} — {{ t('cityMap.title') }}</h1>
        <p class="subtitle">{{ t('cityMap.subtitle') }}</p>
      </div>
      <div class="header-controls">
        <div class="view-toggle">
          <button class="toggle-btn" :class="{ active: viewMode === 'map' }" @click="viewMode = 'map'">🗺️ {{ t('cityMap.mapView') }}</button>
          <button class="toggle-btn" :class="{ active: viewMode === 'list' }" @click="viewMode = 'list'">📋 {{ t('cityMap.listView') }}</button>
        </div>
        <div class="filter-toggle">
          <button class="toggle-btn" :class="{ active: !showAvailableOnly }" @click="showAvailableOnly = false">
            {{ t('cityMap.filterAll') }}
          </button>
          <button class="toggle-btn" :class="{ active: showAvailableOnly }" @click="showAvailableOnly = true">
            {{ t('cityMap.filterAvailable') }}
          </button>
        </div>
        <span class="lot-count">{{ t('cityMap.lotCount', { count: filteredLots.length }) }}</span>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>

    <!-- Error -->
    <div v-else-if="error" class="error-message" role="alert">
      {{ error }}
      <button class="btn btn-secondary" @click="fetchData()">{{ t('common.tryAgain') }}</button>
    </div>

    <!-- Content -->
    <template v-else-if="city">
      <div class="city-content" :class="{ 'has-selection': !!selectedLot }">
        <!-- Map / List -->
        <div class="map-area">
          <div v-if="viewMode === 'map'" ref="mapContainer" class="map-container"></div>
          <div v-else class="lot-list">
            <button
              v-for="lot in filteredLots"
              :key="lot.id"
              class="lot-list-item"
              :class="{
                selected: selectedLot?.id === lot.id,
                available: getLotStatus(lot) === 'available',
                owned: getLotStatus(lot) === 'owned',
                yours: getLotStatus(lot) === 'yours',
              }"
              @click="selectLot(lot)"
            >
              <div class="lot-status-dot" :style="{ background: getLotMarkerColor(lot) }"></div>
              <div class="lot-list-info">
                <span class="lot-list-name">{{ lot.name }}</span>
                <span class="lot-list-district">{{ lot.district }}</span>
              </div>
              <div class="lot-list-meta">
                <span class="lot-list-price">{{ formatCurrency(lot.price) }}</span>
                <span class="lot-list-status" :class="getLotStatus(lot)">
                  {{ getLotStatus(lot) === 'available' ? t('cityMap.available') : getLotStatus(lot) === 'yours' ? t('cityMap.yourProperty') : t('cityMap.owned') }}
                </span>
              </div>
            </button>
            <div v-if="filteredLots.length === 0" class="empty-state">
              {{ t('cityMap.noLotsAvailable') }}
            </div>
          </div>
        </div>

        <!-- Detail Panel -->
        <aside v-if="selectedLot" class="detail-panel">
          <div class="detail-header">
            <h2>{{ selectedLot.name }}</h2>
            <span class="status-badge" :class="getLotStatus(selectedLot)">
              {{ getLotStatus(selectedLot) === 'available' ? t('cityMap.available') : getLotStatus(selectedLot) === 'yours' ? t('cityMap.yourProperty') : t('cityMap.owned') }}
            </span>
          </div>

          <p class="lot-description">{{ selectedLot.description }}</p>

          <!-- Strategic recommendation badge -->
          <div class="strategic-recommendation" :class="strategicRecommendation(selectedLot).cssClass" data-testid="strategic-recommendation">
            <span class="rec-icon">🎯</span>
            <span class="rec-label">{{ t(`cityMap.${strategicRecommendation(selectedLot).key}`) }}</span>
          </div>

          <div class="detail-grid">
            <div class="detail-item">
              <span class="detail-label">{{ t('cityMap.district') }}</span>
              <span class="detail-value">{{ selectedLot.district }}</span>
            </div>
            <div class="detail-item">
              <span class="detail-label">{{ t('cityMap.appraisedValue') }}</span>
              <span class="detail-value" data-testid="appraised-value">{{ formatCurrency(selectedLot.basePrice) }}</span>
            </div>
            <div class="detail-item">
              <span class="detail-label">{{ t('cityMap.price') }}</span>
              <span class="detail-value price" data-testid="asking-price">
                {{ formatCurrency(selectedLot.price) }}
                <span v-if="selectedLot.resourceType && selectedLot.price > selectedLot.basePrice" class="resource-premium-badge" :title="t('cityMap.resourcePremiumTooltip')">
                  {{ t('cityMap.resourcePremium') }}
                </span>
              </span>
            </div>
            <div class="detail-item full-width population-index-item">
              <span class="detail-label">{{ t('cityMap.populationIndex') }}</span>
              <div class="population-index-display">
                <span class="population-index-value">{{ formatPopulationIndex(selectedLot.populationIndex) }}</span>
                <span class="population-index-tag" :class="populationIndexClass(selectedLot.populationIndex)">
                  {{ populationIndexLabel(selectedLot.populationIndex) }}
                </span>
              </div>
              <p class="population-index-hint">{{ t('cityMap.populationIndexHint') }}</p>
            </div>
            <div class="detail-item full-width">
              <span class="detail-label">{{ t('cityMap.suitableFor') }}</span>
              <div class="suitable-types">
                <span v-for="type in suitableTypesForLot" :key="type" class="type-tag">
                  {{ formatBuildingType(type) }}
                </span>
              </div>
            </div>
            <div class="detail-item full-width coordinates-item">
              <span class="detail-label">{{ t('cityMap.coordinates') }}</span>
              <span class="detail-value coordinates-value" data-testid="lot-coordinates">
                {{ Math.abs(selectedLot.latitude).toFixed(5) }}°{{ selectedLot.latitude >= 0 ? 'N' : 'S' }}, {{ Math.abs(selectedLot.longitude).toFixed(5) }}°{{
                  selectedLot.longitude >= 0 ? 'E' : 'W'
                }}
              </span>
              <p class="coordinates-hint">{{ t('cityMap.coordinatesHint') }}</p>
            </div>
          </div>

          <!-- Raw material deposit panel (shown for MINE-eligible lots with resource data) -->
          <div v-if="selectedLot.resourceType && selectedLot.materialQuality != null && selectedLot.materialQuantity != null" class="raw-material-panel" data-testid="raw-material-panel">
            <h3 class="raw-material-title">⛏ {{ t('cityMap.rawMaterialTitle') }}</h3>
            <div class="raw-material-grid">
              <div class="raw-material-item">
                <span class="detail-label">{{ t('cityMap.rawMaterialResource') }}</span>
                <span class="detail-value">{{ selectedLot.resourceType.name }}</span>
              </div>
              <div class="raw-material-item">
                <span class="detail-label">{{ t('cityMap.rawMaterialQuality') }}</span>
                <span class="quality-badge" :class="materialQualityClass(selectedLot.materialQuality)">
                  {{ materialQualityLabel(selectedLot.materialQuality) }}
                  ({{ Math.round(selectedLot.materialQuality * 100) }}%)
                </span>
              </div>
              <div class="raw-material-item full-width">
                <span class="detail-label">{{ t('cityMap.rawMaterialQuantity') }}</span>
                <span class="detail-value">
                  {{ selectedLot.materialQuantity.toLocaleString(locale) }}
                  {{ t('cityMap.rawMaterialQuantityUnit') }}
                </span>
              </div>
            </div>
            <p class="raw-material-hint">{{ t('cityMap.rawMaterialHint') }}</p>
          </div>

          <!-- Placement guidance panel -->
          <div class="placement-guidance-panel" data-testid="placement-guidance-panel">
            <h3 class="guidance-title">{{ t('cityMap.placementGuidanceTitle') }}</h3>
            <ul class="guidance-list">
              <li v-for="type in suitableTypesForLot" :key="type" class="guidance-item">
                <span class="guidance-building-type">{{ formatBuildingType(type) }}</span>
                <span class="guidance-text">{{ t(`cityMap.${placementGuidanceKey(type)}`) }}</span>
              </li>
            </ul>
            <p class="transport-cost-note">
              <span class="transport-icon">🚚</span>
              {{ t('cityMap.transportCostNote') }}
            </p>
          </div>

          <!-- Owner info for owned lots -->
          <div v-if="selectedLot.ownerCompany" class="owner-info">
            <span class="detail-label">{{ t('cityMap.owner') }}</span>
            <span class="detail-value">{{ selectedLot.ownerCompany.name }}</span>
          </div>
          <div v-if="selectedLot.building" class="building-info">
            <span class="detail-label">{{ t('cityMap.building') }}</span>
            <span class="detail-value">
              {{ selectedLot.building.name }}
              ({{ formatBuildingType(selectedLot.building.type) }})
            </span>
          </div>

          <!-- Purchase flow -->
          <div v-if="!auth.isAuthenticated" class="purchase-notice">
            {{ t('cityMap.loginRequired') }}
          </div>
          <div v-else-if="companies.length === 0" class="purchase-notice">
            {{ t('cityMap.noCompany') }}
          </div>
          <div v-else-if="!isCompanyAccountActive" class="purchase-notice">
            {{ t('cityMap.companyAccountRequired') }}
          </div>
          <template v-else>
            <!-- Stale-lot / general purchase error shown regardless of current lot availability -->
            <div v-if="purchaseError && !purchaseMode" class="error-message purchase-error-notice" role="alert" aria-live="polite">
              {{ purchaseError }}
            </div>

            <template v-if="canPurchase">
              <div v-if="!purchaseMode" class="purchase-actions">
                <button class="btn btn-primary" @click="startPurchase()">
                  {{ t('cityMap.purchase') }}
                </button>
              </div>

              <div v-else class="purchase-form">
                <div class="form-group">
                  <label>{{ t('cityMap.buildingType') }}</label>
                  <div class="building-type-cards" role="radiogroup" :aria-label="t('cityMap.buildingType')">
                    <button
                      v-for="type in suitableTypesForLot"
                      :key="type"
                      class="building-type-card"
                      :class="{ selected: selectedBuildingType === type }"
                      type="button"
                      role="radio"
                      :aria-checked="selectedBuildingType === type"
                      @click="selectedBuildingType = type"
                    >
                      <span class="card-type-icon">{{ t(`buildings.typeIcons.${type}`) }}</span>
                      <span class="card-type-name">{{ formatBuildingType(type) }}</span>
                      <span class="card-type-desc">{{ t(`buildings.typeDescriptions.${type}`) }}</span>
                    </button>
                  </div>
                  <p v-if="selectedBuildingType" class="selected-type-guidance">
                    {{ t(`cityMap.${placementGuidanceKey(selectedBuildingType)}`) }}
                  </p>
                </div>

                <div class="form-group">
                  <label
                    >{{ t('cityMap.buildingName') }} <span class="optional-hint">({{ t('common.optional') }})</span></label
                  >
                  <input v-model="buildingName" type="text" class="form-input" :placeholder="t('cityMap.buildingNamePlaceholder')" />
                </div>

                <div class="form-group">
                  <label>{{ t('cityMap.company') }}</label>
                  <div class="active-company-summary">
                    <strong>{{ selectedCompany?.name }}</strong>
                    <span>{{ selectedCompany ? formatCurrency(selectedCompany.cash) : '' }}</span>
                  </div>
                </div>

                <!-- Media house channel type (only for MEDIA_HOUSE) -->
                <div v-if="selectedBuildingType === 'MEDIA_HOUSE'" class="form-group">
                  <label>{{ t('cityMap.mediaHouseChannelType') }}</label>
                  <select v-model="selectedMediaType" class="form-select" required>
                    <option value="">{{ t('cityMap.selectMediaType') }}</option>
                    <option value="NEWSPAPER">📰 {{ t('cityMap.mediaTypeNewspaper') }} (×1.0)</option>
                    <option value="RADIO">📻 {{ t('cityMap.mediaTypeRadio') }} (×1.5)</option>
                    <option value="TV">📺 {{ t('cityMap.mediaTypeTv') }} (×2.0)</option>
                  </select>
                  <p class="form-hint">{{ t('cityMap.mediaTypeHint') }}</p>
                </div>

                <!-- Purchase cost summary -->
                <div class="purchase-cost-summary" aria-label="Purchase cost summary">
                  <div class="cost-row">
                    <span class="cost-label">{{ t('cityMap.costLotPrice') }}</span>
                    <span class="cost-value cost-debit">{{ selectedLot ? formatCurrency(selectedLot.price) : '—' }}</span>
                  </div>
                  <div v-if="selectedBuildingType" class="cost-row">
                    <span class="cost-label">{{ t('cityMap.costConstruction') }}</span>
                    <span class="cost-value cost-debit">{{ formatCurrency(constructionCostForType(selectedBuildingType)) }}</span>
                  </div>
                  <div v-if="selectedBuildingType" class="cost-row construction-time-row">
                    <span class="cost-label">{{ t('cityMap.constructionTime') }}</span>
                    <span class="cost-value construction-ticks">
                      {{ t('cityMap.constructionTicks', { ticks: constructionTicksForType(selectedBuildingType) }) }}
                    </span>
                  </div>
                  <div v-if="selectedCompany" class="cost-row">
                    <span class="cost-label">{{ t('cityMap.costCurrentCash') }}</span>
                    <span class="cost-value">{{ formatCurrency(selectedCompany.cash) }}</span>
                  </div>
                  <div v-if="cashAfterPurchase !== null" class="cost-row cost-row-result">
                    <span class="cost-label">{{ t('cityMap.costRemainingCash') }}</span>
                    <span class="cost-value" :class="cashAfterPurchase < 0 ? 'cost-negative' : 'cost-positive'">
                      {{ formatCurrency(cashAfterPurchase) }}
                    </span>
                  </div>
                </div>

                <div v-if="purchaseError" class="error-message" role="alert">
                  {{ purchaseError }}
                </div>

                <div class="purchase-actions">
                  <button class="btn btn-secondary" @click="purchaseMode = false">
                    {{ t('common.cancel') }}
                  </button>
                  <button class="btn btn-primary" :disabled="!canSubmitPurchase" @click="confirmPurchase()">
                    {{ purchasing ? t('cityMap.purchasing') : t('cityMap.confirmPurchase') }}
                  </button>
                </div>
              </div>
            </template>
          </template>

          <!-- Post-purchase banner: under-construction state -->
          <div
            v-if="justPurchasedBuildingId && isOwnedByActiveCompany && justPurchasedIsUnderConstruction"
            class="post-purchase-banner construction-banner"
            role="status"
            data-testid="construction-banner"
          >
            <div class="post-purchase-body">
              <strong class="post-purchase-title"> 🏗️ {{ t('cityMap.constructionStartedTitle') }} </strong>
              <p class="post-purchase-text">
                {{
                  t('cityMap.constructionStartedBody', {
                    type: formatBuildingType(justPurchasedBuildingType ?? 'FACTORY'),
                    ticks: justPurchasedConstructionCompletesAtTick
                      ? constructionTicksRemaining(justPurchasedConstructionCompletesAtTick)
                      : constructionTicksForType(justPurchasedBuildingType ?? 'FACTORY'),
                  })
                }}
              </p>
              <div class="construction-progress-bar" aria-label="Construction progress">
                <div class="construction-progress-fill" style="width: 0%"></div>
              </div>
              <p class="construction-hint">{{ t('cityMap.constructionHint') }}</p>
            </div>
          </div>

          <!-- Post-purchase setup guidance (shown immediately after a successful purchase, operational) -->
          <div v-else-if="justPurchasedBuildingId && isOwnedByActiveCompany" class="post-purchase-banner" role="status">
            <div class="post-purchase-body">
              <strong class="post-purchase-title">{{ t(`buildings.typeIcons.${justPurchasedBuildingType ?? 'FACTORY'}`) }} {{ t('cityMap.postPurchaseTitle') }}</strong>
              <p class="post-purchase-text">{{ t(`cityMap.${postPurchaseBodyKey(justPurchasedBuildingType ?? 'FACTORY')}`) }}</p>
            </div>
            <RouterLink :to="`/building/${justPurchasedBuildingId}`" class="btn btn-primary"> {{ t('cityMap.setupBuilding') }} → </RouterLink>
          </div>

          <div v-else-if="isOwnedByDifferentControlledCompany" class="purchase-notice">
            {{ t('cityMap.switchCompanyToManage', { company: selectedLot.ownerCompany?.name ?? t('cityMap.company') }) }}
          </div>

          <!-- Already owned by player: building under construction -->
          <div
            v-else-if="isOwnedByActiveCompany && selectedLot.building && selectedLot.building.isUnderConstruction"
            class="your-building-actions construction-state"
            data-testid="under-construction-panel"
          >
            <div class="construction-info">
              <span class="construction-badge">🏗️ {{ t('cityMap.underConstruction') }}</span>
              <p class="construction-detail">
                {{ selectedLot.building.name }}
                ({{ formatBuildingType(selectedLot.building.type) }})
              </p>
              <p class="construction-ticks-info" data-testid="construction-ticks-remaining">
                {{
                  t('cityMap.ticksRemaining', {
                    ticks: constructionTicksRemaining(selectedLot.building.constructionCompletesAtTick),
                  })
                }}
              </p>
            </div>
            <RouterLink :to="`/building/${selectedLot.buildingId}`" class="btn btn-ghost">
              {{ t('cityMap.viewConstruction') }}
            </RouterLink>
          </div>

          <!-- Already owned by player (standard manage link, building operational) -->
          <div v-else-if="isOwnedByActiveCompany && selectedLot.buildingId" class="your-building-actions">
            <RouterLink :to="`/building/${selectedLot.buildingId}`" class="btn btn-primary">
              {{ t('cityMap.manageBuilding') }}
            </RouterLink>
          </div>
        </aside>

        <!-- No selection prompt -->
        <aside v-else class="detail-panel empty-panel">
          <p class="select-prompt">{{ t('cityMap.selectLot') }}</p>
        </aside>
      </div>
    </template>

    <!-- City media houses section -->
    <section class="media-houses-section" aria-labelledby="media-houses-heading">
      <h2 id="media-houses-heading" class="section-heading">📺 {{ t('cityMap.mediaHouses.title') }}</h2>
      <p class="section-subtitle">{{ t('cityMap.mediaHouses.subtitle') }}</p>
      <div v-if="mediaHousesLoading" class="media-houses-loading">{{ t('common.loading') }}</div>
      <div v-else-if="cityMediaHouses.length === 0" class="media-houses-empty">
        <p>{{ t('cityMap.mediaHouses.empty') }}</p>
        <p class="hint">{{ t('cityMap.mediaHouses.emptyHint') }}</p>
      </div>
      <div v-else class="media-houses-grid">
        <div v-for="mh in cityMediaHouses" :key="mh.id" class="media-house-card" :class="{ 'mh-offline': mh.powerStatus === 'OFFLINE', 'mh-construction': mh.isUnderConstruction }">
          <div class="mh-channel-icon">
            <span v-if="mh.mediaType === 'TV'">📺</span>
            <span v-else-if="mh.mediaType === 'RADIO'">📻</span>
            <span v-else>📰</span>
          </div>
          <div class="mh-info">
            <strong class="mh-name">{{ mh.name }}</strong>
            <span class="mh-type-badge">{{ mh.mediaType ?? '?' }}</span>
            <span v-if="mh.isUnderConstruction" class="mh-status-badge construction">
              {{ t('cityMap.mediaHouses.underConstruction') }}
            </span>
            <span v-else-if="mh.powerStatus === 'OFFLINE'" class="mh-status-badge offline">
              {{ t('cityMap.mediaHouses.offline') }}
            </span>
            <div class="mh-owner">{{ t('cityMap.mediaHouses.owner') }}: {{ mh.ownerCompanyName }}</div>
            <div class="mh-effectiveness">
              {{ t('cityMap.mediaHouses.effectiveness') }}:
              <strong>×{{ mh.effectivenessMultiplier.toFixed(1) }}</strong>
              <span class="effectiveness-hint">
                {{ mh.mediaType === 'TV' ? t('cityMap.mediaHouses.tvHint') : mh.mediaType === 'RADIO' ? t('cityMap.mediaHouses.radioHint') : t('cityMap.mediaHouses.newspaperHint') }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.city-map-view {
  padding: 1.5rem 1rem;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  margin-bottom: 1.5rem;
  flex-wrap: wrap;
}

.page-header h1 {
  font-size: 1.5rem;
  margin: 0.5rem 0 0.25rem;
}

.subtitle {
  color: var(--color-text-secondary);
  font-size: 0.875rem;
  margin: 0;
}

.header-controls {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.view-toggle,
.filter-toggle {
  display: flex;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.toggle-btn {
  padding: 0.375rem 0.75rem;
  font-size: 0.8125rem;
  background: var(--color-bg);
  color: var(--color-text-secondary);
  border: none;
  cursor: pointer;
  transition: all 0.15s;
}

.toggle-btn.active {
  background: var(--color-primary);
  color: #fff;
}

.toggle-btn:not(:last-child) {
  border-right: 1px solid var(--color-border);
}

.lot-count {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

/* Map / List */
.city-content {
  display: grid;
  grid-template-columns: 1fr 380px;
  gap: 1.5rem;
  min-height: 500px;
}

.map-area {
  min-height: 500px;
  border-radius: var(--radius-lg);
  overflow: hidden;
  border: 1px solid var(--color-border);
}

.map-container {
  width: 100%;
  height: 100%;
  min-height: 500px;
}

/* Lot list */
.lot-list {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.5rem;
  max-height: 600px;
  overflow-y: auto;
}

.lot-list-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all 0.15s;
  text-align: left;
  width: 100%;
  color: var(--color-text);
}

.lot-list-item:hover {
  border-color: var(--color-primary);
}

.lot-list-item.selected {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.06);
}

.lot-status-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.lot-list-info {
  flex: 1;
  min-width: 0;
}

.lot-list-name {
  display: block;
  font-weight: 600;
  font-size: 0.875rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.lot-list-district {
  display: block;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.lot-list-meta {
  text-align: right;
  flex-shrink: 0;
}

.lot-list-price {
  display: block;
  font-weight: 600;
  font-size: 0.875rem;
  color: var(--color-secondary);
}

.lot-list-status {
  display: block;
  font-size: 0.6875rem;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.lot-list-status.available {
  color: var(--color-secondary);
}

.lot-list-status.owned {
  color: var(--color-text-secondary);
}

.lot-list-status.yours {
  color: var(--color-primary);
}

/* Detail panel */
.detail-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
  align-self: start;
  position: sticky;
  top: 80px;
}

.empty-panel {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 200px;
}

.select-prompt {
  color: var(--color-text-secondary);
  font-size: 0.875rem;
  text-align: center;
}

.detail-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.detail-header h2 {
  font-size: 1.125rem;
  margin: 0;
}

.status-badge {
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.status-badge.available {
  background: rgba(0, 200, 83, 0.12);
  color: var(--color-secondary);
}

.status-badge.owned {
  background: rgba(107, 114, 128, 0.12);
  color: var(--color-text-secondary);
}

.status-badge.yours {
  background: rgba(0, 71, 255, 0.12);
  color: var(--color-primary);
}

.lot-description {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  line-height: 1.5;
  margin: 0 0 0.625rem;
}

.strategic-recommendation {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.3rem 0.6rem;
  border-radius: var(--radius-md);
  font-size: 0.75rem;
  font-weight: 600;
  margin-bottom: 0.875rem;
  border: 1px solid currentColor;
}

.rec-icon {
  font-size: 0.875rem;
}

.rec-retail {
  color: #22c55e;
  background: rgba(34, 197, 94, 0.08);
}

.rec-resource {
  color: #f59e0b;
  background: rgba(245, 158, 11, 0.08);
}

.rec-industrial {
  color: var(--color-text-secondary);
  background: rgba(139, 148, 158, 0.08);
}

.rec-balanced {
  color: var(--color-primary);
  background: rgba(0, 71, 255, 0.06);
}

.detail-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.detail-item.full-width {
  grid-column: 1 / -1;
}

.detail-label {
  display: block;
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-text-secondary);
  margin-bottom: 0.25rem;
}

.detail-value {
  font-size: 0.875rem;
  font-weight: 500;
}

.detail-value.price {
  color: var(--color-secondary);
  font-size: 1rem;
  font-weight: 700;
}

.suitable-types {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
}

.type-tag {
  padding: 0.25rem 0.5rem;
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  border-radius: var(--radius-sm);
  font-size: 0.75rem;
  font-weight: 500;
}

.resource-premium-badge {
  display: inline-block;
  margin-left: 0.375rem;
  padding: 0.125rem 0.375rem;
  background: rgba(139, 92, 246, 0.12);
  color: #7c3aed;
  border-radius: var(--radius-sm);
  font-size: 0.7rem;
  font-weight: 600;
  vertical-align: middle;
  cursor: help;
}

.owner-info,
.building-info {
  margin-bottom: 0.75rem;
}

/* Raw material panel */
.raw-material-panel {
  background: rgba(139, 92, 246, 0.06);
  border: 1px solid rgba(139, 92, 246, 0.2);
  border-radius: var(--radius-md);
  padding: 1rem;
  margin-bottom: 1rem;
}

.raw-material-title {
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
  color: var(--color-text);
}

.raw-material-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.raw-material-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.raw-material-item.full-width {
  grid-column: span 2;
}

.raw-material-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  line-height: 1.5;
  margin: 0;
  border-top: 1px solid rgba(139, 92, 246, 0.15);
  padding-top: 0.5rem;
}

.quality-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  display: inline-block;
}

.quality-badge.quality-excellent {
  background: rgba(0, 200, 83, 0.12);
  color: var(--color-secondary);
}

.quality-badge.quality-good {
  background: rgba(0, 71, 255, 0.1);
  color: var(--color-primary);
}

.quality-badge.quality-fair {
  background: rgba(255, 180, 0, 0.12);
  color: #b45309;
}

.quality-badge.quality-poor {
  background: rgba(107, 114, 128, 0.1);
  color: var(--color-text-secondary);
}

/* Placement guidance panel */
.placement-guidance-panel {
  background: rgba(0, 71, 255, 0.04);
  border: 1px solid rgba(0, 71, 255, 0.12);
  border-radius: var(--radius-md);
  padding: 1rem;
  margin-bottom: 1rem;
}

.guidance-title {
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
  color: var(--color-text);
}

.guidance-list {
  list-style: none;
  padding: 0;
  margin: 0 0 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.guidance-item {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.guidance-building-type {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--color-primary);
}

.guidance-text {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  line-height: 1.5;
}

.transport-cost-note {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  line-height: 1.5;
  margin: 0;
  border-top: 1px solid rgba(0, 71, 255, 0.1);
  padding-top: 0.5rem;
  display: flex;
  gap: 0.375rem;
  align-items: flex-start;
}

.transport-icon {
  flex-shrink: 0;
  font-size: 0.875rem;
}

.purchase-notice {
  padding: 0.75rem;
  background: rgba(255, 109, 0, 0.08);
  border: 1px solid rgba(255, 109, 0, 0.2);
  border-radius: var(--radius-md);
  font-size: 0.8125rem;
  color: var(--color-tertiary);
  margin-top: 1rem;
}

.active-company-summary {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.75rem;
  padding: 0.8rem 0.95rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface-hover);
}

.purchase-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
}

.purchase-cost-summary {
  background: var(--color-bg-card);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.625rem 0.75rem;
  margin-top: 0.75rem;
  font-size: 0.8125rem;
}

.cost-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.125rem 0;
}

.cost-row-result {
  margin-top: 0.375rem;
  padding-top: 0.375rem;
  border-top: 1px solid var(--color-border);
  font-weight: 600;
}

.cost-label {
  color: var(--color-text-muted);
}

.cost-value {
  font-weight: 500;
}

.cost-debit {
  color: var(--color-text);
}

.cost-positive {
  color: var(--color-success, #22c55e);
}

.cost-negative {
  color: var(--color-danger, #ef4444);
}

.purchase-form {
  margin-top: 1rem;
}

.form-group {
  margin-bottom: 0.75rem;
}

.form-group label {
  display: block;
  font-size: 0.8125rem;
  font-weight: 600;
  margin-bottom: 0.375rem;
}

.form-select,
.form-input {
  width: 100%;
  padding: 0.5rem 0.75rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  color: var(--color-text);
  font-size: 0.875rem;
}

.form-select:focus,
.form-input:focus {
  outline: none;
  border-color: var(--color-primary);
}

/* Building type card picker */
.building-type-cards {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.building-type-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  padding: 0.75rem 0.5rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  cursor: pointer;
  text-align: center;
  transition:
    border-color 0.15s ease,
    background 0.15s ease;
  color: var(--color-text);
}

.building-type-card:hover {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.04);
}

.building-type-card.selected {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
  box-shadow: 0 0 0 1px var(--color-primary);
}

.card-type-icon {
  font-size: 1.5rem;
  line-height: 1;
}

.card-type-name {
  font-size: 0.75rem;
  font-weight: 700;
  line-height: 1.2;
}

.card-type-desc {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
  line-height: 1.3;
}

.selected-type-guidance {
  font-size: 0.75rem;
  color: var(--color-primary);
  line-height: 1.5;
  padding: 0.5rem 0.625rem;
  background: rgba(0, 71, 255, 0.05);
  border-left: 3px solid var(--color-primary);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  margin-top: 0.375rem;
  font-style: italic;
}

.success-message {
  padding: 0.75rem;
  background: rgba(0, 200, 83, 0.08);
  border: 1px solid rgba(0, 200, 83, 0.2);
  border-radius: var(--radius-md);
  font-size: 0.8125rem;
  color: var(--color-secondary);
  margin-bottom: 0.75rem;
}

.error-message {
  padding: 0.75rem;
  background: rgba(255, 59, 48, 0.08);
  border: 1px solid rgba(255, 59, 48, 0.2);
  border-radius: var(--radius-md);
  font-size: 0.8125rem;
  color: #ff3b30;
  margin-bottom: 0.75rem;
}

.your-building-actions {
  margin-top: 1rem;
}

.your-building-actions.construction-state {
  margin-top: 1rem;
  padding: 1rem;
  background: rgba(255, 149, 0, 0.07);
  border: 1px solid rgba(255, 149, 0, 0.3);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.construction-info {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.construction-badge {
  font-size: 0.875rem;
  font-weight: 600;
  color: #f59e0b;
}

.construction-detail {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin: 0;
}

.construction-ticks-info {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin: 0;
}

.construction-banner {
  background: rgba(255, 149, 0, 0.07) !important;
  border-color: rgba(255, 149, 0, 0.3) !important;
}

.construction-progress-bar {
  margin-top: 0.5rem;
  height: 6px;
  background: rgba(255, 149, 0, 0.2);
  border-radius: 3px;
  overflow: hidden;
}

.construction-progress-fill {
  height: 100%;
  background: #f59e0b;
  border-radius: 3px;
  transition: width 0.5s ease;
}

.construction-hint {
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin: 0.25rem 0 0;
  font-style: italic;
}

.construction-time-row .construction-ticks {
  color: #f59e0b;
  font-weight: 500;
}

.post-purchase-banner {
  margin-top: 1rem;
  padding: 1rem;
  background: rgba(0, 200, 83, 0.07);
  border: 1px solid rgba(0, 200, 83, 0.25);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.post-purchase-body {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.post-purchase-title {
  font-size: 0.9375rem;
  color: var(--color-secondary);
}

.post-purchase-text {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin: 0;
}

.population-index-item {
  margin-bottom: 0.25rem;
}

.population-index-display {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.25rem;
}

.population-index-value {
  font-size: 1.125rem;
  font-weight: 700;
}

.population-index-tag {
  padding: 0.2rem 0.5rem;
  border-radius: 999px;
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.pop-very-high {
  background: rgba(0, 200, 83, 0.15);
  color: #00b84a;
}

.pop-high {
  background: rgba(0, 150, 200, 0.12);
  color: #0096c8;
}

.pop-medium {
  background: rgba(255, 165, 0, 0.12);
  color: #cc8800;
}

.pop-low {
  background: rgba(107, 114, 128, 0.12);
  color: var(--color-text-secondary);
}

.population-index-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  line-height: 1.4;
  margin: 0;
  font-style: italic;
}

.coordinates-item {
  margin-top: 0.25rem;
}

.coordinates-value {
  font-family: monospace;
  font-size: 0.875rem;
  letter-spacing: 0.02em;
}

.coordinates-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  line-height: 1.4;
  margin: 0.25rem 0 0;
  font-style: italic;
}

.empty-state {
  padding: 2rem;
  text-align: center;
  color: var(--color-text-secondary);
}

@media (max-width: 768px) {
  .city-content {
    grid-template-columns: 1fr;
  }

  .detail-panel {
    position: static;
  }

  .page-header {
    flex-direction: column;
  }
}

/* Media houses section */
.form-hint {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin: 0.25rem 0 0;
  font-style: italic;
}

.media-houses-section {
  margin-top: 2.5rem;
  padding-top: 1.5rem;
  border-top: 1px solid var(--color-border);
}

.section-heading {
  font-size: 1.25rem;
  margin: 0 0 0.5rem;
}

.section-subtitle {
  color: var(--color-text-secondary);
  font-size: 0.875rem;
  margin: 0 0 1.25rem;
}

.media-houses-loading {
  color: var(--color-text-secondary);
  padding: 0.75rem 0;
}

.media-houses-empty {
  padding: 1.5rem;
  background: var(--color-surface-elevated);
  border-radius: 0.5rem;
  border: 1px dashed var(--color-border);
}

.media-houses-empty p {
  margin: 0 0 0.5rem;
  color: var(--color-text-secondary);
}

.media-houses-empty .hint {
  font-size: 0.875rem;
  font-style: italic;
}

.media-houses-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 1rem;
}

.media-house-card {
  display: flex;
  gap: 0.75rem;
  padding: 1rem;
  background: var(--color-surface-elevated);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  transition: border-color 0.15s;
}

.media-house-card:hover {
  border-color: var(--color-primary);
}

.media-house-card.mh-offline {
  opacity: 0.6;
}

.media-house-card.mh-construction {
  border-style: dashed;
}

.mh-channel-icon {
  font-size: 2rem;
  flex-shrink: 0;
  line-height: 1;
}

.mh-info {
  flex: 1;
  min-width: 0;
}

.mh-name {
  display: block;
  font-size: 0.9375rem;
  margin-bottom: 0.25rem;
}

.mh-type-badge {
  display: inline-block;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.125rem 0.5rem;
  border-radius: 9999px;
  background: var(--color-primary);
  color: #fff;
  margin-right: 0.5rem;
  margin-bottom: 0.375rem;
}

.mh-status-badge {
  display: inline-block;
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.125rem 0.5rem;
  border-radius: 9999px;
  margin-bottom: 0.375rem;
}

.mh-status-badge.construction {
  background: var(--color-warning, #f59e0b);
  color: #000;
}

.mh-status-badge.offline {
  background: var(--color-error, #ef4444);
  color: #fff;
}

.mh-owner {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.375rem;
}

.mh-effectiveness {
  font-size: 0.8125rem;
}

.effectiveness-hint {
  display: block;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-style: italic;
  margin-top: 0.125rem;
}
</style>
