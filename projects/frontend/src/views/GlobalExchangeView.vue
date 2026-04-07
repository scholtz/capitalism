<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest } from '@/lib/graphql'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useGameStateStore } from '@/stores/gameState'
import { deepEqual } from '@/lib/utils'
import type { GlobalExchangeOffer, GlobalExchangeProductListing, ResourceType, ProductType } from '@/types'

interface City {
  id: string
  name: string
  countryCode: string
  latitude: number
  longitude: number
}

interface ExchangeRow {
  resourceId: string
  resourceName: string
  resourceSlug: string
  unitSymbol: string
  category: string
  offers: GlobalExchangeOffer[]
  bestDeliveredPrice: number
  bestCityId: string
}

interface ProductRow {
  productId: string
  productName: string
  productSlug: string
  productIndustry: string
  unitSymbol: string
  basePrice: number
  listings: GlobalExchangeProductListing[]
  bestPrice: number
}

type MarketMode = 'resources' | 'products'

const { t } = useI18n()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const route = useRoute()
const router = useRouter()

const loading = ref(true)
const productListingsLoading = ref(false)
const error = ref<string | null>(null)
const productListingsError = ref<string | null>(null)
const cities = ref<City[]>([])
const resources = ref<ResourceType[]>([])
const products = ref<ProductType[]>([])
const allOffers = ref<GlobalExchangeOffer[]>([])
const allProductListings = ref<GlobalExchangeProductListing[]>([])

const selectedCityId = ref<string | null>(null)
const search = ref('')
const selectedCategory = ref('ALL')
const marketMode = ref<MarketMode>('resources')
const productSearch = ref('')
const selectedIndustry = ref('ALL')

const CITIES_QUERY = `
  {
    cities {
      id
      name
      countryCode
      latitude
      longitude
    }
  }
`

const RESOURCES_QUERY = `
  {
    resourceTypes {
      id
      name
      slug
      category
      basePrice
      weightPerUnit
      unitName
      unitSymbol
    }
  }
`

const PRODUCTS_QUERY = `
  {
    productTypes {
      id
      name
      slug
      industry
      basePrice
      unitName
      unitSymbol
      isProOnly
    }
  }
`

const EXCHANGE_QUERY = `
  query GlobalExchangeOffers($destinationCityId: UUID!) {
    globalExchangeOffers(destinationCityId: $destinationCityId) {
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
  }
`

const PRODUCT_LISTINGS_QUERY = `
  query GlobalExchangeProductListings($productTypeId: UUID) {
    globalExchangeProductListings(productTypeId: $productTypeId) {
      orderId
      productTypeId
      productName
      productSlug
      productIndustry
      unitSymbol
      unitName
      basePrice
      pricePerUnit
      remainingQuantity
      sellerCityId
      sellerCityName
      sellerCompanyId
      sellerCompanyName
      createdAtUtc
    }
  }
`

async function loadCitiesAndResources() {
  const [citiesData, resourcesData, productsData] = await Promise.all([
    gqlRequest<{ cities: City[] }>(CITIES_QUERY),
    gqlRequest<{ resourceTypes: ResourceType[] }>(RESOURCES_QUERY),
    gqlRequest<{ productTypes: ProductType[] }>(PRODUCTS_QUERY),
  ])
  if (!deepEqual(cities.value, citiesData.cities)) {
    cities.value = citiesData.cities
  }
  if (!deepEqual(resources.value, resourcesData.resourceTypes)) {
    resources.value = resourcesData.resourceTypes
  }
  if (!deepEqual(products.value, productsData.productTypes)) {
    products.value = productsData.productTypes
  }
  if (citiesData.cities.length > 0 && !selectedCityId.value) {
    // If a city was pre-selected via query param (?city=<id>), use it; otherwise default to first city
    const queryCityId = typeof route.query.city === 'string' ? route.query.city : null
    const matchedCity = queryCityId ? citiesData.cities.find((c) => c.id === queryCityId) : null
    const firstCity = citiesData.cities[0]
    selectedCityId.value = (matchedCity ?? firstCity)?.id ?? null
  }
  // Pre-fill search from ?resource=<slug> query param
  if (!search.value) {
    const queryResource = typeof route.query.resource === 'string' ? route.query.resource : null
    if (queryResource) {
      search.value = queryResource
    }
  }
  // Restore market mode from query param
  const queryMode = route.query.mode
  if (queryMode === 'products') {
    marketMode.value = 'products'
  }
}

async function loadOffers(isRefresh = false) {
  if (!selectedCityId.value) return
  if (!isRefresh) {
    loading.value = true
  }
  error.value = null
  try {
    const data = await gqlRequest<{ globalExchangeOffers: GlobalExchangeOffer[] }>(
      EXCHANGE_QUERY,
      { destinationCityId: selectedCityId.value },
    )
    if (!deepEqual(allOffers.value, data.globalExchangeOffers)) {
      allOffers.value = data.globalExchangeOffers
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('globalExchange.loadFailed')
  } finally {
    loading.value = false
  }
}

async function loadProductListings(isRefresh = false) {
  if (!isRefresh) {
    productListingsLoading.value = true
  }
  productListingsError.value = null
  try {
    const data = await gqlRequest<{ globalExchangeProductListings: GlobalExchangeProductListing[] }>(
      PRODUCT_LISTINGS_QUERY,
    )
    if (!deepEqual(allProductListings.value, data.globalExchangeProductListings)) {
      allProductListings.value = data.globalExchangeProductListings
    }
  } catch (e) {
    productListingsError.value = e instanceof Error ? e.message : t('globalExchange.loadFailed')
  } finally {
    productListingsLoading.value = false
  }
}

async function refreshAll() {
  // Only refresh the data for the currently visible tab to avoid unnecessary requests
  if (marketMode.value === 'resources') {
    await loadOffers(true)
  } else {
    await loadProductListings(true)
  }
}

onMounted(async () => {
  auth.initFromStorage()
  if (auth.isAuthenticated) {
    void auth.fetchMe()
  }
  gameStateStore.start()
  try {
    await loadCitiesAndResources()
    await Promise.all([loadOffers(), loadProductListings()])
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('globalExchange.loadFailed')
    loading.value = false
  }
})

watch(selectedCityId, async (cityId) => {
  // Persist selected city in the URL so page reload restores the same city
  void router.replace({ query: { ...route.query, city: cityId ?? undefined } })
  await loadOffers()
})

watch(marketMode, (mode) => {
  void router.replace({ query: { ...route.query, mode: mode === 'resources' ? undefined : mode } })
})

useTickRefresh(refreshAll)

const currentTick = computed(() => gameStateStore.gameState?.currentTick ?? null)

const categories = computed(() => {
  const cats = [...new Set(resources.value.map((r) => r.category))]
  return ['ALL', ...cats]
})

const industries = computed(() => {
  const inds = [...new Set(products.value.map((p) => p.industry))]
  return ['ALL', ...inds]
})

const exchangeRows = computed<ExchangeRow[]>(() => {
  const q = search.value.trim().toLowerCase()
  const filtered = resources.value.filter((r) => {
    const matchesCat = selectedCategory.value === 'ALL' || r.category === selectedCategory.value
    const matchesSearch =
      !q || r.name.toLowerCase().includes(q) || r.slug.toLowerCase().includes(q)
    return matchesCat && matchesSearch
  })

  return filtered.map((resource) => {
    const offers = allOffers.value.filter((o) => o.resourceTypeId === resource.id)
    const bestOffer = offers.reduce<GlobalExchangeOffer | null>((best, offer) => {
      if (!best) return offer
      return offer.deliveredPricePerUnit < best.deliveredPricePerUnit ? offer : best
    }, null)
    return {
      resourceId: resource.id,
      resourceName: resource.name,
      resourceSlug: resource.slug,
      unitSymbol: resource.unitSymbol,
      category: resource.category,
      offers,
      bestDeliveredPrice: bestOffer?.deliveredPricePerUnit ?? 0,
      bestCityId: bestOffer?.cityId ?? '',
    }
  })
})

const productRows = computed<ProductRow[]>(() => {
  const q = productSearch.value.trim().toLowerCase()
  const filtered = products.value.filter((p) => {
    const matchesInd = selectedIndustry.value === 'ALL' || p.industry === selectedIndustry.value
    const matchesSearch =
      !q || p.name.toLowerCase().includes(q) || p.slug.toLowerCase().includes(q)
    return matchesInd && matchesSearch
  })

  return filtered.map((product) => {
    const listings = allProductListings.value.filter((l) => l.productTypeId === product.id)
    return {
      productId: product.id,
      productName: product.name,
      productSlug: product.slug,
      productIndustry: product.industry,
      unitSymbol: product.unitSymbol,
      basePrice: product.basePrice,
      listings,
      bestPrice: listings.length > 0 ? Math.min(...listings.map((l) => l.pricePerUnit)) : 0,
    }
  })
})

// Only show rows that have listings when in products mode, plus a filtered empty indicator
const productRowsWithListings = computed(() => productRows.value.filter((r) => r.listings.length > 0))
const productRowsEmpty = computed(() => productRows.value.length === 0)
const noProductListingsAtAll = computed(
  () => !productListingsLoading.value && productRows.value.length > 0 && productRowsWithListings.value.length === 0,
)

function formatPrice(value: number): string {
  return `$${value.toFixed(2)}`
}

function formatPercent(value: number): string {
  return `${Math.round(value * 100)}%`
}

function localizedCategory(cat: string): string {
  const map: Record<string, string> = {
    RAW_MATERIAL: t('globalExchange.categoryRaw'),
    MINERAL: t('globalExchange.categoryMineral'),
    ORGANIC: t('globalExchange.categoryOrganic'),
  }
  return map[cat] ?? cat
}

function localizedIndustry(ind: string): string {
  const map: Record<string, string> = {
    FURNITURE: t('globalExchange.industryFurniture'),
    FOOD_PROCESSING: t('globalExchange.industryFoodProcessing'),
    HEALTHCARE: t('globalExchange.industryHealthcare'),
    ELECTRONICS: t('globalExchange.industryElectronics'),
    CONSTRUCTION: t('globalExchange.industryConstruction'),
  }
  return map[ind] ?? ind
}

function priceVsBase(pricePerUnit: number, basePrice: number): string {
  if (basePrice <= 0) return ''
  const diff = pricePerUnit - basePrice
  const pct = Math.round((diff / basePrice) * 100)
  return pct >= 0 ? `+${pct}%` : `${pct}%`
}

function priceVsBaseClass(pricePerUnit: number, basePrice: number): string {
  if (basePrice <= 0) return ''
  const diff = pricePerUnit - basePrice
  return diff > 0 ? 'price-above-base' : diff < 0 ? 'price-below-base' : 'price-at-base'
}
</script>

<template>
  <div class="exchange-view">
    <div class="exchange-hero">
      <div class="container">
        <p class="exchange-eyebrow">{{ t('globalExchange.eyebrow') }}</p>
        <h1 class="exchange-title">{{ t('globalExchange.title') }}</h1>
        <p class="exchange-subtitle">{{ t('globalExchange.subtitle') }}</p>
        <div class="exchange-hero-meta">
          <span class="exchange-tick-chip" :title="t('globalExchange.tickHint')">
            <span class="exchange-tick-label">{{ t('globalExchange.tick') }}</span>
            <span class="exchange-tick-value">{{ currentTick !== null ? currentTick : '—' }}</span>
          </span>
          <span v-if="marketMode === 'resources'" class="exchange-supply-chip">{{ t('globalExchange.endlessSupply') }}</span>
        </div>
      </div>
    </div>

    <div class="container exchange-body">
      <!-- Market mode toggle: Resources / Products -->
      <div class="market-mode-tabs" role="tablist" :aria-label="'Market type'">
        <button
          role="tab"
          :aria-selected="marketMode === 'resources'"
          :class="['mode-tab', { active: marketMode === 'resources' }]"
          @click="marketMode = 'resources'"
        >
          {{ t('globalExchange.modeResources') }}
        </button>
        <button
          role="tab"
          :aria-selected="marketMode === 'products'"
          :class="['mode-tab', 'mode-tab-products', { active: marketMode === 'products' }]"
          @click="marketMode = 'products'"
        >
          {{ t('globalExchange.modeProducts') }}
        </button>
      </div>

      <!-- ── Resources mode ── -->
      <template v-if="marketMode === 'resources'">
        <!-- City selector -->
        <div class="city-tabs" role="tablist" :aria-label="t('globalExchange.cityTabsLabel')">
          <button
            v-for="city in cities"
            :key="city.id"
            role="tab"
            :aria-selected="selectedCityId === city.id"
            :class="['city-tab', { active: selectedCityId === city.id }]"
            @click="selectedCityId = city.id"
          >
            {{ city.name }}
            <span class="city-tab-code">{{ city.countryCode }}</span>
          </button>
        </div>

        <!-- Search and filter row -->
        <div class="exchange-filters">
          <div class="search-wrapper">
            <input
              v-model="search"
              type="search"
              class="search-input"
              :placeholder="t('globalExchange.searchPlaceholder')"
              :aria-label="t('globalExchange.searchPlaceholder')"
            />
          </div>
          <div class="category-filter">
            <label for="category-select" class="filter-label">{{ t('globalExchange.filterCategory') }}</label>
            <select id="category-select" v-model="selectedCategory" class="filter-select">
              <option v-for="cat in categories" :key="cat" :value="cat">
                {{ cat === 'ALL' ? t('globalExchange.allCategories') : localizedCategory(cat) }}
              </option>
            </select>
          </div>
        </div>

        <!-- Loading / error -->
        <p v-if="loading" class="exchange-loading">{{ t('common.loading') }}</p>
        <p v-else-if="error" class="exchange-error">{{ error }}</p>
        <p v-else-if="exchangeRows.length === 0" class="exchange-empty">
          {{ t('globalExchange.noResults') }}
        </p>

        <!-- Resource rows -->
        <template v-else>
          <div
            v-for="row in exchangeRows"
            :key="row.resourceId"
            class="resource-row"
            :data-slug="row.resourceSlug"
          >
            <div class="resource-row-header">
              <span class="resource-name">{{ row.resourceName }}</span>
              <span class="resource-category-badge">{{ localizedCategory(row.category) }}</span>
              <RouterLink
                :to="`/encyclopedia/resources/${row.resourceSlug}`"
                class="production-chain-link"
                :aria-label="`${t('globalExchange.viewProductionChain')}: ${row.resourceName}`"
              >{{ t('globalExchange.viewProductionChain') }}</RouterLink>
            </div>

            <div class="city-offers-grid">
              <div
                v-for="offer in row.offers"
                :key="offer.cityId"
                :class="['city-offer-card', { 'best-offer': offer.cityId === row.bestCityId }]"
              >
                <div class="offer-card-header">
                  <strong class="offer-city-name">{{ offer.cityName }}</strong>
                  <span
                    v-if="offer.cityId === row.bestCityId"
                    class="best-badge"
                    :title="t('globalExchange.bestDeliveredHint')"
                  >{{ t('globalExchange.bestDelivered') }}</span>
                </div>

                <div class="offer-metrics">
                  <div class="offer-metric">
                    <span class="metric-label">{{ t('globalExchange.exchangePrice') }}</span>
                    <span class="metric-value exchange-price">
                      {{ formatPrice(offer.exchangePricePerUnit) }}/{{ offer.unitSymbol }}
                    </span>
                  </div>
                  <div class="offer-metric">
                    <span class="metric-label">{{ t('globalExchange.transitCost') }}</span>
                    <span
                      :class="['metric-value transit-cost', { 'transit-free': offer.transitCostPerUnit === 0 }]"
                    >
                      {{
                        offer.transitCostPerUnit === 0
                          ? t('globalExchange.transitFree')
                          : `+${formatPrice(offer.transitCostPerUnit)} · ${offer.distanceKm} km`
                      }}
                    </span>
                  </div>
                  <div class="offer-metric delivered-metric">
                    <span class="metric-label">{{ t('globalExchange.deliveredPrice') }}</span>
                    <span class="metric-value delivered-price">
                      {{ formatPrice(offer.deliveredPricePerUnit) }}/{{ offer.unitSymbol }}
                    </span>
                  </div>
                  <div class="offer-metric">
                    <span class="metric-label">{{ t('globalExchange.quality') }}</span>
                    <span class="metric-value quality-value">
                      {{ formatPercent(offer.estimatedQuality) }}
                    </span>
                  </div>
                  <div class="offer-metric">
                    <span class="metric-label">{{ t('globalExchange.abundance') }}</span>
                    <span class="metric-value">{{ formatPercent(offer.localAbundance) }}</span>
                  </div>
                </div>
              </div>

              <p v-if="row.offers.length === 0" class="no-offers-hint">
                {{ t('globalExchange.noOffersForResource') }}
              </p>
            </div>
          </div>
        </template>
      </template>

      <!-- ── Products mode ── -->
      <template v-else>
        <!-- Product search and filter -->
        <div class="exchange-filters">
          <div class="search-wrapper">
            <input
              v-model="productSearch"
              type="search"
              class="search-input"
              :placeholder="t('globalExchange.productSearchPlaceholder')"
              :aria-label="t('globalExchange.productSearchPlaceholder')"
            />
          </div>
          <div class="category-filter">
            <label for="industry-select" class="filter-label">{{ t('globalExchange.filterIndustry') }}</label>
            <select id="industry-select" v-model="selectedIndustry" class="filter-select">
              <option v-for="ind in industries" :key="ind" :value="ind">
                {{ ind === 'ALL' ? t('globalExchange.allIndustries') : localizedIndustry(ind) }}
              </option>
            </select>
          </div>
        </div>

        <!-- Products mode hint -->
        <p class="products-mode-hint">{{ t('globalExchange.modeProductsHint') }}</p>

        <!-- Loading / error -->
        <p v-if="productListingsLoading" class="exchange-loading">{{ t('common.loading') }}</p>
        <p v-else-if="productListingsError" class="exchange-error">{{ productListingsError }}</p>
        <p v-else-if="productRowsEmpty" class="exchange-empty">
          {{ t('globalExchange.noProductResults') }}
        </p>
        <p v-else-if="noProductListingsAtAll" class="exchange-empty products-empty-state">
          {{ t('globalExchange.noProductListings') }}
          <br />
          <span class="empty-hint">{{ t('globalExchange.noProductListingsHint') }}</span>
        </p>

        <!-- Product rows — only show products with active listings -->
        <template v-else>
          <div
            v-for="row in productRowsWithListings"
            :key="row.productId"
            class="product-row"
            :data-slug="row.productSlug"
          >
            <div class="product-row-header">
              <span class="product-name">{{ row.productName }}</span>
              <span class="product-industry-badge">{{ localizedIndustry(row.productIndustry) }}</span>
              <span class="product-listing-count">{{ row.listings.length }} listing{{ row.listings.length !== 1 ? 's' : '' }}</span>
              <RouterLink
                :to="`/encyclopedia/products/${row.productSlug}`"
                class="production-chain-link"
                :aria-label="`${t('globalExchange.viewProductDetail')}: ${row.productName}`"
              >{{ t('globalExchange.viewProductDetail') }}</RouterLink>
            </div>

            <!-- Listings table -->
            <div class="product-listings-table">
              <div class="listings-header">
                <span>{{ t('globalExchange.productAskPrice') }}</span>
                <span>{{ t('globalExchange.productPriceVsBase') }}</span>
                <span>{{ t('globalExchange.productAvailable') }}</span>
                <span>{{ t('globalExchange.productSeller') }}</span>
                <span>{{ t('globalExchange.productCity') }}</span>
              </div>
              <div
                v-for="listing in row.listings"
                :key="listing.orderId"
                class="listing-row"
              >
                <span class="listing-price">
                  {{ formatPrice(listing.pricePerUnit) }}/{{ listing.unitSymbol }}
                </span>
                <span :class="['listing-vs-base', priceVsBaseClass(listing.pricePerUnit, row.basePrice)]">
                  {{ priceVsBase(listing.pricePerUnit, row.basePrice) }}
                </span>
                <span class="listing-quantity">
                  {{ listing.remainingQuantity.toFixed(0) }} {{ listing.unitSymbol }}
                </span>
                <span class="listing-seller">{{ listing.sellerCompanyName }}</span>
                <span class="listing-city">{{ listing.sellerCityName }}</span>
              </div>
            </div>
          </div>

          <!-- Show all matching products (including those without listings) if no listings found -->
          <template v-if="productRowsWithListings.length === 0 && productRows.length > 0">
            <p class="exchange-empty products-empty-state">
              {{ t('globalExchange.noProductListings') }}
              <br />
              <span class="empty-hint">{{ t('globalExchange.noProductListingsHint') }}</span>
            </p>
          </template>
        </template>
      </template>
    </div>
  </div>
</template>

<style scoped>
.exchange-view {
  min-height: 100vh;
  background: var(--color-background);
  color: var(--color-text);
}

.exchange-hero {
  background: linear-gradient(135deg, var(--color-surface) 0%, var(--color-surface-raised, var(--color-surface)) 100%);
  border-bottom: 1px solid var(--color-border);
  padding: 3rem 1rem 2rem;
}

.container {
  max-width: 1100px;
  margin: 0 auto;
  padding: 0 1rem;
}

.exchange-eyebrow {
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--color-primary);
  margin: 0 0 0.5rem;
}

.exchange-title {
  font-size: 2rem;
  font-weight: 700;
  margin: 0 0 0.5rem;
  color: var(--color-text);
}

.exchange-subtitle {
  font-size: 0.9375rem;
  color: var(--color-text-secondary);
  margin: 0 0 1rem;
  max-width: 640px;
}

.exchange-hero-meta {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: 0.5rem;
}

.exchange-tick-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  font-size: 0.75rem;
  cursor: default;
}

.exchange-tick-label {
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  font-weight: 600;
  font-size: 0.7rem;
}

.exchange-tick-value {
  font-weight: 700;
  color: var(--color-primary);
  font-variant-numeric: tabular-nums;
}

.exchange-supply-chip {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.625rem;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, var(--color-success, #22c55e) 30%, transparent);
  background: color-mix(in srgb, var(--color-success, #22c55e) 8%, transparent);
  color: var(--color-success, #22c55e);
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.exchange-body {
  padding-top: 2rem;
  padding-bottom: 3rem;
}

/* Market mode tabs */
.market-mode-tabs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
  border-bottom: 2px solid var(--color-border);
  padding-bottom: 0;
}

.mode-tab {
  padding: 0.5rem 1.25rem;
  border: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  background: transparent;
  color: var(--color-text-secondary);
  font-size: 0.9375rem;
  font-weight: 600;
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s;
  border-radius: var(--radius-sm, 4px) var(--radius-sm, 4px) 0 0;
}

.mode-tab:hover {
  color: var(--color-text);
}

.mode-tab.active {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}

.mode-tab-products.active {
  color: var(--color-accent, #a855f7);
  border-bottom-color: var(--color-accent, #a855f7);
}

/* City tabs */
.city-tabs {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 1.5rem;
}

.city-tab {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.5rem 1rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  background: var(--color-surface);
  color: var(--color-text-secondary);
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s, color 0.15s, border-color 0.15s;
}

.city-tab:hover {
  background: color-mix(in srgb, var(--color-surface) 85%, white 15%);
  color: var(--color-text);
}

.city-tab.active {
  background: var(--color-primary);
  color: #fff;
  border-color: var(--color-primary);
}

.city-tab-code {
  font-size: 0.7rem;
  opacity: 0.75;
  font-weight: 400;
}

/* Filters */
.exchange-filters {
  display: flex;
  gap: 1rem;
  align-items: flex-end;
  flex-wrap: wrap;
  margin-bottom: 1.5rem;
}

.search-wrapper {
  flex: 1;
  min-width: 180px;
}

.search-input {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  background: var(--color-surface);
  color: var(--color-text);
  font-size: 0.875rem;
}

.search-input:focus {
  outline: 2px solid var(--color-primary);
  outline-offset: 1px;
}

.category-filter {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.filter-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.filter-select {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  background: var(--color-surface);
  color: var(--color-text);
  font-size: 0.875rem;
  min-width: 140px;
}

/* Status states */
.exchange-loading,
.exchange-error,
.exchange-empty {
  padding: 2rem;
  text-align: center;
  color: var(--color-text-secondary);
  font-size: 0.9375rem;
}

.exchange-error {
  color: var(--color-error, #ef4444);
}

.products-empty-state {
  background: var(--color-surface);
  border: 1px dashed var(--color-border);
  border-radius: var(--radius-lg, 12px);
}

.empty-hint {
  font-size: 0.8125rem;
  opacity: 0.75;
}

.products-mode-hint {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-bottom: 1rem;
  padding: 0.5rem 0.75rem;
  background: color-mix(in srgb, var(--color-accent, #a855f7) 6%, transparent);
  border-left: 3px solid var(--color-accent, #a855f7);
  border-radius: 0 var(--radius-sm, 4px) var(--radius-sm, 4px) 0;
}

/* Resource rows */
.resource-row {
  margin-bottom: 2rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg, 12px);
  overflow: hidden;
}

.resource-row-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.875rem 1rem;
  background: color-mix(in srgb, var(--color-surface) 92%, var(--color-primary) 8%);
  border-bottom: 1px solid var(--color-border);
}

.resource-name {
  font-size: 1rem;
  font-weight: 700;
  color: var(--color-text);
}

.resource-category-badge {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.2rem 0.5rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-primary) 15%, transparent);
  color: var(--color-primary);
}

.production-chain-link {
  margin-left: auto;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-primary);
  text-decoration: none;
  white-space: nowrap;
  padding: 0.2rem 0.5rem;
  border-radius: var(--radius-sm, 4px);
  border: 1px solid color-mix(in srgb, var(--color-primary) 40%, transparent);
  transition: background 0.15s;
}

.production-chain-link:hover {
  background: color-mix(in srgb, var(--color-primary) 10%, transparent);
  text-decoration: none;
}

/* City offer cards grid */
.city-offers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 1rem;
  padding: 1rem;
}

.city-offer-card {
  padding: 0.875rem;
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--color-border);
  background: var(--color-background);
  transition: border-color 0.15s;
}

.city-offer-card.best-offer {
  border-color: var(--color-primary);
  background: color-mix(in srgb, var(--color-background) 95%, var(--color-primary) 5%);
}

.offer-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.625rem;
}

.offer-city-name {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--color-text);
}

.best-badge {
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.15rem 0.4rem;
  border-radius: 999px;
  background: var(--color-primary);
  color: #fff;
  white-space: nowrap;
}

.offer-metrics {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.offer-metric {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.5rem;
  font-size: 0.78rem;
}

.metric-label {
  color: var(--color-text-secondary);
  white-space: nowrap;
}

.metric-value {
  font-weight: 500;
  color: var(--color-text);
  text-align: right;
}

.delivered-metric {
  padding-top: 0.3rem;
  margin-top: 0.2rem;
  border-top: 1px solid var(--color-border);
}

.delivered-price {
  font-size: 0.875rem;
  font-weight: 700;
  color: var(--color-primary);
}

.transit-free {
  color: var(--color-success, #22c55e);
}

.no-offers-hint {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  padding: 0.5rem 0;
}

/* Product rows */
.product-row {
  margin-bottom: 2rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg, 12px);
  overflow: hidden;
}

.product-row-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.875rem 1rem;
  background: color-mix(in srgb, var(--color-surface) 92%, var(--color-accent, #a855f7) 8%);
  border-bottom: 1px solid var(--color-border);
}

.product-name {
  font-size: 1rem;
  font-weight: 700;
  color: var(--color-text);
}

.product-industry-badge {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.2rem 0.5rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-accent, #a855f7) 15%, transparent);
  color: var(--color-accent, #a855f7);
}

.product-listing-count {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-weight: 500;
}

/* Product listings table */
.product-listings-table {
  padding: 0.75rem 1rem;
}

.listings-header {
  display: grid;
  grid-template-columns: 1.5fr 0.75fr 1fr 1.5fr 1fr;
  gap: 0.5rem;
  padding: 0.375rem 0.5rem;
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-text-secondary);
  border-bottom: 1px solid var(--color-border);
  margin-bottom: 0.25rem;
}

.listing-row {
  display: grid;
  grid-template-columns: 1.5fr 0.75fr 1fr 1.5fr 1fr;
  gap: 0.5rem;
  align-items: center;
  padding: 0.5rem;
  border-radius: var(--radius-sm, 4px);
  font-size: 0.8125rem;
  transition: background 0.1s;
}

.listing-row:hover {
  background: color-mix(in srgb, var(--color-surface) 80%, var(--color-accent, #a855f7) 20%);
}

.listing-price {
  font-weight: 700;
  color: var(--color-text);
  font-variant-numeric: tabular-nums;
}

.listing-vs-base {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 0.1rem 0.3rem;
  border-radius: 4px;
}

.price-above-base {
  color: var(--color-error, #ef4444);
  background: color-mix(in srgb, var(--color-error, #ef4444) 10%, transparent);
}

.price-below-base {
  color: var(--color-success, #22c55e);
  background: color-mix(in srgb, var(--color-success, #22c55e) 10%, transparent);
}

.price-at-base {
  color: var(--color-text-secondary);
}

.listing-quantity {
  color: var(--color-text);
  font-variant-numeric: tabular-nums;
}

.listing-seller {
  color: var(--color-text);
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.listing-city {
  color: var(--color-text-secondary);
}

/* Narrow / mobile */
@media (max-width: 480px) {
  .exchange-title {
    font-size: 1.5rem;
  }

  .city-offers-grid {
    grid-template-columns: 1fr;
  }

  .exchange-filters {
    flex-direction: column;
  }

  .search-wrapper {
    width: 100%;
  }

  .listings-header,
  .listing-row {
    grid-template-columns: 1.2fr 0.6fr 0.8fr 1fr 0.8fr;
    font-size: 0.7rem;
  }
}
</style>
