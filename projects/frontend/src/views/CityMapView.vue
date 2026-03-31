<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest, GraphQLError } from '@/lib/graphql'
import {
  getLotStatus as lotStatusFromOwnership,
  getLotMarkerColor as markerColorFromStatus,
  formatPopulationIndex,
  populationIndexClass,
  canPurchaseLot as isPurchasable,
  canSubmitPurchaseForm as isFormSubmittable,
} from '@/lib/cityMapHelpers'
import type { City, BuildingLot, Company, PurchaseLotResult } from '@/types'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const cityId = computed(() => route.params.id as string)

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
const selectedCompanyId = ref('')
const purchasing = ref(false)
const purchaseError = ref<string | null>(null)
const purchaseSuccess = ref<string | null>(null)
const justPurchasedBuildingId = ref<string | null>(null)

// Map reference
const mapContainer = ref<HTMLDivElement | null>(null)
let map: L.Map | null = null
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

const canPurchase = computed(() =>
  selectedLot.value
    ? isPurchasable(
        auth.isAuthenticated,
        companies.value.length,
        selectedLot.value.ownerCompanyId,
      )
    : false,
)

const canSubmitPurchase = computed(() =>
  isFormSubmittable(
    selectedBuildingType.value,
    buildingName.value,
    selectedCompanyId.value,
    purchasing.value,
  ),
)

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

async function fetchData() {
  loading.value = true
  error.value = null
  try {
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
          building { id name type }
          resourceType { id name slug }
          materialQuality materialQuantity
        }
      }`,
      { cityId: cityId.value },
    )
    const companiesData: { myCompanies: Company[] } = auth.isAuthenticated
      ? await gqlRequest<{ myCompanies: Company[] }>(
          `{ myCompanies { id name cash foundedAtUtc buildings { id } } }`,
        )
      : { myCompanies: [] }

    city.value = cityData.city
    lots.value = lotsData.cityLots
    companies.value = companiesData.myCompanies

    if (companies.value.length > 0) {
      selectedCompanyId.value = companies.value[0]!.id
    }
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load city data'
  } finally {
    loading.value = false
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
    const bounds = L.latLngBounds(
      filteredLots.value.map((lot) => [lot.latitude, lot.longitude] as [number, number]),
    )
    map.fitBounds(bounds.pad(0.15))
  }
}

function selectLot(lot: BuildingLot) {
  selectedLot.value = lot
  purchaseMode.value = false
  purchaseError.value = null
  purchaseSuccess.value = null
  justPurchasedBuildingId.value = null
  selectedBuildingType.value = ''
  buildingName.value = ''

  // Update markers to show selection
  updateMarkers()

  // Pan map to selected lot
  if (map) {
    map.panTo([lot.latitude, lot.longitude])
  }
}

function startPurchase() {
  purchaseMode.value = true
  purchaseError.value = null
  purchaseSuccess.value = null
}

async function confirmPurchase() {
  if (!selectedLot.value || !canSubmitPurchase.value) return

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
            building { id name type }
          }
          building { id name type }
          company { id name cash }
        }
      }`,
      {
        input: {
          companyId: selectedCompanyId.value,
          lotId: selectedLot.value.id,
          buildingType: selectedBuildingType.value,
          buildingName: buildingName.value,
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
                building { id name type }
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

onMounted(async () => {
  await fetchData()
  await nextTick()
  if (viewMode.value === 'map') {
    initMap()
  }
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
        <button class="btn btn-secondary btn-sm" @click="router.push('/dashboard')">
          ← {{ t('cityMap.backToDashboard') }}
        </button>
        <h1 v-if="city">🗺️ {{ city.name }} — {{ t('cityMap.title') }}</h1>
        <p class="subtitle">{{ t('cityMap.subtitle') }}</p>
      </div>
      <div class="header-controls">
        <div class="view-toggle">
          <button
            class="toggle-btn"
            :class="{ active: viewMode === 'map' }"
            @click="viewMode = 'map'"
          >
            🗺️ {{ t('cityMap.mapView') }}
          </button>
          <button
            class="toggle-btn"
            :class="{ active: viewMode === 'list' }"
            @click="viewMode = 'list'"
          >
            📋 {{ t('cityMap.listView') }}
          </button>
        </div>
        <div class="filter-toggle">
          <button
            class="toggle-btn"
            :class="{ active: !showAvailableOnly }"
            @click="showAvailableOnly = false"
          >
            {{ t('cityMap.filterAll') }}
          </button>
          <button
            class="toggle-btn"
            :class="{ active: showAvailableOnly }"
            @click="showAvailableOnly = true"
          >
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
                <span
                  class="lot-list-status"
                  :class="getLotStatus(lot)"
                >
                  {{
                    getLotStatus(lot) === 'available'
                      ? t('cityMap.available')
                      : getLotStatus(lot) === 'yours'
                        ? t('cityMap.yourProperty')
                        : t('cityMap.owned')
                  }}
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
            <span
              class="status-badge"
              :class="getLotStatus(selectedLot)"
            >
              {{
                getLotStatus(selectedLot) === 'available'
                  ? t('cityMap.available')
                  : getLotStatus(selectedLot) === 'yours'
                    ? t('cityMap.yourProperty')
                    : t('cityMap.owned')
              }}
            </span>
          </div>

          <p class="lot-description">{{ selectedLot.description }}</p>

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
                <span
                  v-if="selectedLot.resourceType && selectedLot.price > selectedLot.basePrice"
                  class="resource-premium-badge"
                  :title="t('cityMap.resourcePremiumTooltip')"
                >
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
                <span
                  v-for="type in suitableTypesForLot"
                  :key="type"
                  class="type-tag"
                >
                  {{ formatBuildingType(type) }}
                </span>
              </div>
            </div>
            <div class="detail-item full-width coordinates-item">
              <span class="detail-label">{{ t('cityMap.coordinates') }}</span>
              <span class="detail-value coordinates-value" data-testid="lot-coordinates">
                {{ Math.abs(selectedLot.latitude).toFixed(5) }}°{{ selectedLot.latitude >= 0 ? 'N' : 'S' }},
                {{ Math.abs(selectedLot.longitude).toFixed(5) }}°{{ selectedLot.longitude >= 0 ? 'E' : 'W' }}
              </span>
              <p class="coordinates-hint">{{ t('cityMap.coordinatesHint') }}</p>
            </div>
          </div>

          <!-- Raw material deposit panel (shown for MINE-eligible lots with resource data) -->
          <div
            v-if="selectedLot.resourceType && selectedLot.materialQuality != null && selectedLot.materialQuantity != null"
            class="raw-material-panel"
            data-testid="raw-material-panel"
          >
            <h3 class="raw-material-title">⛏ {{ t('cityMap.rawMaterialTitle') }}</h3>
            <div class="raw-material-grid">
              <div class="raw-material-item">
                <span class="detail-label">{{ t('cityMap.rawMaterialResource') }}</span>
                <span class="detail-value">{{ selectedLot.resourceType.name }}</span>
              </div>
              <div class="raw-material-item">
                <span class="detail-label">{{ t('cityMap.rawMaterialQuality') }}</span>
                <span
                  class="quality-badge"
                  :class="materialQualityClass(selectedLot.materialQuality)"
                >
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
              <li
                v-for="type in suitableTypesForLot"
                :key="type"
                class="guidance-item"
              >
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
                  <select v-model="selectedBuildingType" class="form-select">
                    <option value="">{{ t('cityMap.selectBuildingType') }}</option>
                    <option v-for="type in suitableTypesForLot" :key="type" :value="type">
                      {{ formatBuildingType(type) }}
                    </option>
                  </select>
                </div>

                <div class="form-group">
                  <label>{{ t('cityMap.buildingName') }}</label>
                  <input
                    v-model="buildingName"
                    type="text"
                    class="form-input"
                    :placeholder="t('cityMap.buildingNamePlaceholder')"
                  />
                </div>

                <div v-if="companies.length > 1" class="form-group">
                  <label>{{ t('cityMap.company') }}</label>
                  <select v-model="selectedCompanyId" class="form-select">
                    <option v-for="c in companies" :key="c.id" :value="c.id">
                      {{ c.name }} ({{ formatCurrency(c.cash) }})
                    </option>
                  </select>
                </div>

                <div v-if="purchaseError" class="error-message" role="alert">
                  {{ purchaseError }}
                </div>

                <div class="purchase-actions">
                  <button class="btn btn-secondary" @click="purchaseMode = false">
                    {{ t('common.cancel') }}
                  </button>
                  <button
                    class="btn btn-primary"
                    :disabled="!canSubmitPurchase"
                    @click="confirmPurchase()"
                  >
                    {{ purchasing ? t('cityMap.purchasing') : t('cityMap.confirmPurchase') }}
                  </button>
                </div>
              </div>
            </template>
          </template>

          <!-- Post-purchase setup guidance (shown immediately after a successful purchase) -->
          <div v-if="justPurchasedBuildingId && isOwnedByPlayer" class="post-purchase-banner" role="status">
            <div class="post-purchase-body">
              <strong class="post-purchase-title">🏭 {{ t('cityMap.postPurchaseTitle') }}</strong>
              <p class="post-purchase-text">{{ t('cityMap.postPurchaseBody') }}</p>
            </div>
            <RouterLink
              :to="`/building/${justPurchasedBuildingId}`"
              class="btn btn-primary"
            >
              {{ t('cityMap.setupBuilding') }} →
            </RouterLink>
          </div>

          <!-- Already owned by player (standard manage link) -->
          <div v-else-if="isOwnedByPlayer && selectedLot.buildingId" class="your-building-actions">
            <RouterLink
              :to="`/building/${selectedLot.buildingId}`"
              class="btn btn-primary"
            >
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
  margin: 0 0 1rem;
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

.purchase-actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
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
</style>
