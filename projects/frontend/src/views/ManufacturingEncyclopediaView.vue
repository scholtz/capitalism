<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { gqlRequest } from '@/lib/graphql'
import { isProductLocked } from '@/lib/productAccess'
import {
  getLocalizedCategory,
  getLocalizedIndustry,
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
  getProductImageUrl,
  getResourceImageUrl,
} from '@/lib/catalogPresentation'
import type { ProductType, ResourceType } from '@/types'

type CatalogEntry = {
  id: string
  slug: string
  kind: 'resource' | 'product'
  title: string
  description: string
  imageUrl: string | null
  pill: string
  badge: string
  meta: string[]
  industry: string | null
  accessText: string | null
  accessClass: 'locked' | 'unlocked' | null
  searchText: string
}

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()

const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const industry = ref('ALL')
const resources = ref<ResourceType[]>([])
const products = ref<ProductType[]>([])

const showProProducts = computed({
  get: () => route.query.showPro === '1',
  set: (value: boolean) => {
    const nextQuery = { ...route.query }

    if (value) {
      nextQuery.showPro = '1'
    } else {
      delete nextQuery.showPro
    }

    router.replace({ path: route.path, query: nextQuery })
  },
})

const visibleProducts = computed(() => (showProProducts.value ? products.value : products.value.filter((product) => !product.isProOnly)))

const industries = computed(() => ['ALL', ...new Set(visibleProducts.value.map((product) => product.industry))])

const hiddenProProductCount = computed(() => (showProProducts.value ? 0 : products.value.filter((product) => product.isProOnly).length))

const catalogEntries = computed<CatalogEntry[]>(() => {
  const query = search.value.trim().toLowerCase()
  const entries: CatalogEntry[] = [
    ...resources.value.map((resource) => {
      const title = getLocalizedResourceName(resource, locale.value)
      const description = getLocalizedResourceDescription(resource, locale.value)

      return {
        id: resource.id,
        slug: resource.slug,
        kind: 'resource' as const,
        title,
        description,
        imageUrl: getResourceImageUrl(resource),
        pill: resource.unitSymbol,
        badge: t('encyclopedia.resourceTypeRaw'),
        meta: [
          `${t('encyclopedia.basePrice')}: $${resource.basePrice}`,
          `${t('encyclopedia.weight')}: ${resource.weightPerUnit} kg/${resource.unitSymbol}`,
          getLocalizedCategory(resource.category, locale.value),
        ],
        industry: null,
        accessText: null,
        accessClass: null,
        searchText: [title, description, resource.category].join(' ').toLowerCase(),
      }
    }),
    ...visibleProducts.value.map((product) => {
      const title = getLocalizedProductName(product, locale.value)
      const description = getLocalizedProductDescription(product, locale.value)

      return {
        id: product.id,
        slug: product.slug,
        kind: 'product' as const,
        title,
        description,
        imageUrl: getProductImageUrl(product),
        pill: product.unitSymbol,
        badge: getLocalizedIndustry(product.industry, locale.value),
        meta: [
          `${t('encyclopedia.basePrice')}: $${product.basePrice}`,
          `${t('encyclopedia.energy')}: ${product.energyConsumptionMwh} MWh`,
          `${t('encyclopedia.basicLaborHours')}: ${product.basicLaborHours} h`,
          `${t('encyclopedia.output')}: ${product.outputQuantity} ${product.unitSymbol}`,
        ],
        industry: product.industry,
        accessText: product.isProOnly ? getProductAccessText(product) : null,
        accessClass: product.isProOnly ? (isProductLocked(product) ? ('locked' as const) : ('unlocked' as const)) : null,
        searchText: [title, description, product.industry, ...product.recipes.map((recipe) => getLocalizedRecipeIngredientName(recipe, locale.value))].join(' ').toLowerCase(),
      }
    }),
  ]

  return entries.filter((entry) => {
    const matchesIndustry = industry.value === 'ALL' || entry.industry === industry.value
    const matchesSearch = query.length === 0 || entry.searchText.includes(query)

    return matchesIndustry && matchesSearch
  })
})

watch(
  industries,
  (nextIndustries) => {
    if (!nextIndustries.includes(industry.value)) {
      industry.value = 'ALL'
    }
  },
  { immediate: true },
)

onMounted(async () => {
  try {
    loading.value = true
    const [resourceData, productData] = await Promise.all([
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
          basicLaborHours
          unitName
          unitSymbol
          isProOnly
          isUnlockedForCurrentPlayer
          description
          recipes {
            quantity
            resourceType { id name slug category basePrice weightPerUnit unitName unitSymbol imageUrl description }
            inputProductType { id name slug unitName unitSymbol }
          }
        }
      }`),
    ])

    resources.value = resourceData.resourceTypes
    products.value = productData.productTypes
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('encyclopedia.loadFailed')
  } finally {
    loading.value = false
  }
})

function getIndustryLabel(value: string) {
  return getLocalizedIndustry(value, locale.value)
}

function getProductAccessText(product: ProductType) {
  if (!product.isProOnly) {
    return t('catalog.free')
  }

  return isProductLocked(product) ? t('catalog.proRequired') : t('catalog.proUnlocked')
}

function navigateToEntry(slug: string) {
  router.push({
    name: 'encyclopedia-detail',
    params: { slug },
    query: showProProducts.value ? { showPro: '1' } : {},
  })
}
</script>

<template>
  <div class="encyclopedia-view container">
    <header class="hero">
      <div>
        <p class="eyebrow">{{ t('encyclopedia.eyebrow') }}</p>
        <h1>{{ t('encyclopedia.title') }}</h1>
        <p class="subtitle">{{ t('encyclopedia.subtitle') }}</p>
      </div>
      <div class="hero-stats">
        <div class="stat-card">
          <strong>{{ catalogEntries.length }}</strong>
          <span>{{ t('encyclopedia.resourcesCount') }}</span>
        </div>
        <div class="stat-card">
          <strong>{{ visibleProducts.length }}</strong>
          <span>{{ t('encyclopedia.productsCount') }}</span>
        </div>
      </div>
    </header>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="error-message" role="alert">{{ error }}</div>
    <section v-else class="resources-section">
      <div class="section-header">
        <h2>{{ t('encyclopedia.resourcesTitle') }}</h2>
        <p>{{ t('encyclopedia.resourcesHelp') }}</p>
        <p v-if="hiddenProProductCount > 0 && !showProProducts" class="catalog-note">
          {{ t('encyclopedia.proHiddenNotice', { count: hiddenProProductCount }) }}
        </p>
      </div>

      <div class="filters">
        <input v-model="search" type="search" class="filter-input" :placeholder="t('encyclopedia.searchPlaceholder')" />
        <select v-model="industry" class="filter-select" :aria-label="t('encyclopedia.filterByIndustry')">
          <option v-for="option in industries" :key="option" :value="option">
            {{ option === 'ALL' ? t('encyclopedia.allIndustries') : getIndustryLabel(option) }}
          </option>
        </select>
        <label class="filter-toggle">
          <input v-model="showProProducts" type="checkbox" />
          <span>{{ t('encyclopedia.showProProducts') }}</span>
        </label>
      </div>

      <div class="resource-grid">
        <p v-if="catalogEntries.length === 0" class="search-empty-state">
          {{ t('encyclopedia.searchNoResults') }}
        </p>
        <article
          v-for="entry in catalogEntries"
          :key="entry.id"
          class="resource-card resource-card--link"
          :class="`resource-card--${entry.kind}`"
          role="button"
          tabindex="0"
          :aria-label="t('encyclopedia.viewDetail') + ': ' + entry.title"
          @click="navigateToEntry(entry.slug)"
          @keydown.enter="navigateToEntry(entry.slug)"
          @keydown.space.prevent="navigateToEntry(entry.slug)"
        >
          <img v-if="entry.imageUrl" :src="entry.imageUrl ?? undefined" :alt="entry.title" class="resource-image" />
          <div class="resource-body">
            <div class="resource-heading">
              <div>
                <p class="resource-kind">{{ entry.badge }}</p>
                <h3>{{ entry.title }}</h3>
              </div>
              <span class="resource-unit">{{ entry.pill }}</span>
            </div>
            <span v-if="entry.accessText" class="product-access-badge" :class="entry.accessClass ?? undefined">
              {{ entry.accessText }}
            </span>
            <p class="resource-description">{{ entry.description }}</p>
            <div class="resource-meta">
              <span v-for="metaEntry in entry.meta" :key="metaEntry">{{ metaEntry }}</span>
            </div>
            <span class="resource-view-detail">{{ t('encyclopedia.viewDetail') }} →</span>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.encyclopedia-view {
  padding: 2rem 1rem 3rem;
  display: grid;
  gap: 2rem;
}

.hero,
.section-header,
.filters,
.hero-stats,
.resource-meta {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.hero {
  justify-content: space-between;
  align-items: flex-end;
}

.eyebrow,
.subtitle,
.section-header p,
.catalog-note,
.resource-description,
.resource-meta {
  color: var(--color-text-secondary);
}

.hero h1,
.section-header h2,
.resource-heading h3 {
  margin: 0;
}

.hero-stats {
  justify-content: flex-end;
}

.stat-card,
.resource-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
}

.stat-card {
  padding: 1rem 1.25rem;
  min-width: 120px;
  display: grid;
  gap: 0.25rem;
}

.resource-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 1rem;
  margin-top: 1.5rem;
}

.resource-card {
  overflow: hidden;
}

.resource-image {
  width: 100%;
  aspect-ratio: 16 / 9;
  background: var(--color-bg);
  object-fit: cover;
}

.resource-body {
  padding: 1rem;
  display: grid;
  gap: 0.75rem;
}

.product-access-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: fit-content;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  font-size: 0.72rem;
  font-weight: 700;
}

.product-access-badge.locked {
  color: var(--color-tertiary);
  border-color: rgba(255, 109, 0, 0.45);
  background: rgba(255, 109, 0, 0.12);
}

.product-access-badge.unlocked {
  color: var(--color-secondary);
  border-color: rgba(0, 200, 83, 0.45);
  background: rgba(0, 200, 83, 0.12);
}

.resource-heading {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.resource-kind {
  margin: 0 0 0.3rem;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.resource-unit {
  padding: 0.25rem 0.6rem;
  border-radius: 999px;
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  font-weight: 600;
  font-size: 0.75rem;
}

.filters {
  align-items: center;
  margin-top: 1.5rem;
}

.filter-input,
.filter-select {
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-bg);
  color: var(--color-text);
  padding: 0.75rem 0.9rem;
}

.filter-input {
  flex: 1;
  min-width: 240px;
}

.filter-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--color-text-secondary);
  font-weight: 600;
}

.filter-toggle input {
  accent-color: var(--color-primary);
}

.resource-card--link {
  cursor: pointer;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}

.resource-card--link:hover,
.resource-card--link:focus-visible {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 1px var(--color-primary);
  outline: none;
}

.resource-view-detail {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-primary);
}

.search-empty-state {
  grid-column: 1 / -1;
  text-align: center;
  padding: 3rem 1rem;
  color: var(--color-text-secondary);
  font-size: 0.95rem;
}

@media (max-width: 720px) {
  .hero {
    align-items: stretch;
  }

  .hero-stats {
    justify-content: stretch;
  }

  .stat-card {
    flex: 1;
  }
}
</style>
