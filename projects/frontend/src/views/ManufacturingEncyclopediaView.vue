<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { isProductLocked } from '@/lib/productAccess'
import {
  getLocalizedCategory,
  getLocalizedIndustry,
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedRecipeSummary,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
  getLocalizedUnitName,
  getProductImageUrl,
  getResourceImageUrl,
} from '@/lib/catalogPresentation'
import { useAuthStore } from '@/stores/auth'
import type { ProductType, ResourceType } from '@/types'

const { t, locale } = useI18n()
const auth = useAuthStore()

const loading = ref(true)
const error = ref<string | null>(null)
const search = ref('')
const industry = ref('ALL')
const selectedProductId = ref<string | null>(null)
const resources = ref<ResourceType[]>([])
const products = ref<ProductType[]>([])

const industries = computed(() => ['ALL', ...new Set(products.value.map((product) => product.industry))])

const filteredProducts = computed(() => {
  const query = search.value.trim().toLowerCase()
  return products.value.filter((product) => {
    const localizedName = getLocalizedProductName(product, locale.value).toLowerCase()
    const localizedDescription = getLocalizedProductDescription(product, locale.value).toLowerCase()
    const matchesIndustry = industry.value === 'ALL' || product.industry === industry.value
    const matchesSearch = query.length === 0
      || localizedName.includes(query)
      || localizedDescription.includes(query)
      || product.recipes.some((recipe) => getLocalizedRecipeIngredientName(recipe, locale.value).toLowerCase().includes(query))

    return matchesIndustry && matchesSearch
  })
})

const selectedProduct = computed(() => filteredProducts.value.find((product) => product.id === selectedProductId.value) ?? filteredProducts.value[0] ?? null)
const lockedProductCount = computed(() => products.value.filter((product) => isProductLocked(product)).length)

watch(filteredProducts, (nextProducts) => {
  if (nextProducts.length === 0) {
    selectedProductId.value = null
    return
  }

  if (!nextProducts.some((product) => product.id === selectedProductId.value)) {
    selectedProductId.value = nextProducts[0]?.id ?? null
  }
}, { immediate: true })

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

function getResourceCardName(resource: ResourceType) {
  return getLocalizedResourceName(resource, locale.value)
}

function getResourceCardDescription(resource: ResourceType) {
  return getLocalizedResourceDescription(resource, locale.value)
}

function getProductCardName(product: ProductType) {
  return getLocalizedProductName(product, locale.value)
}

function getProductCardDescription(product: ProductType) {
  return getLocalizedProductDescription(product, locale.value)
}

function getIndustryLabel(value: string) {
  return getLocalizedIndustry(value, locale.value)
}

function getCategoryLabel(value: string) {
  return getLocalizedCategory(value, locale.value)
}

function getResourceCardImage(resource: ResourceType) {
  return getResourceImageUrl(resource)
}

function getProductCardImage(product: ProductType) {
  return getProductImageUrl(product)
}

function getRecipeText(product: ProductType) {
  return getLocalizedRecipeSummary(product, locale.value)
}

function getProductAccessText(product: ProductType) {
  if (!product.isProOnly) {
    return t('catalog.free')
  }

  return isProductLocked(product) ? t('catalog.proRequired') : t('catalog.proUnlocked')
}

function getProductAccessDetail(product: ProductType) {
  return isProductLocked(product) ? t('catalog.proDetail') : t('catalog.proUnlockedDetail')
}

function getIngredientImage(recipe: ProductType['recipes'][number]) {
  if (recipe.resourceType) {
    return getResourceImageUrl(recipe.resourceType)
  }

  if (recipe.inputProductType) {
    const product = products.value.find((candidate) => candidate.id === recipe.inputProductType?.id)
    return getProductImageUrl({
      slug: recipe.inputProductType.slug,
      name: recipe.inputProductType.name,
      industry: product?.industry ?? 'ELECTRONICS',
    })
  }

  return null
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
          <strong>{{ resources.length }}</strong>
          <span>{{ t('encyclopedia.resourcesCount') }}</span>
        </div>
        <div class="stat-card">
          <strong>{{ products.length }}</strong>
          <span>{{ t('encyclopedia.productsCount') }}</span>
        </div>
      </div>
    </header>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="error-message" role="alert">{{ error }}</div>
    <template v-else>
      <section class="resources-section">
        <div class="section-header">
          <h2>{{ t('encyclopedia.rawMaterials') }}</h2>
          <p>{{ t('encyclopedia.rawMaterialsHelp') }}</p>
        </div>
        <div class="resource-grid">
          <article v-for="resource in resources" :key="resource.id" class="resource-card">
            <img v-if="getResourceCardImage(resource)" :src="getResourceCardImage(resource) ?? undefined" :alt="getResourceCardName(resource)" class="resource-image" />
            <div class="resource-body">
              <div class="resource-heading">
                <h3>{{ getResourceCardName(resource) }}</h3>
                <span class="resource-unit">{{ resource.unitSymbol }}</span>
              </div>
              <p class="resource-description">{{ getResourceCardDescription(resource) }}</p>
              <div class="resource-meta">
                <span>{{ t('encyclopedia.basePrice') }}: ${{ resource.basePrice }}</span>
                <span>{{ t('encyclopedia.weight') }}: {{ resource.weightPerUnit }} kg/{{ resource.unitSymbol }}</span>
                <span>{{ getCategoryLabel(resource.category) }}</span>
              </div>
            </div>
          </article>
        </div>
      </section>

      <section class="products-section">
        <div class="section-header">
          <h2>{{ t('encyclopedia.manufacturedProducts') }}</h2>
          <p>{{ t('encyclopedia.manufacturedProductsHelp') }}</p>
          <p class="catalog-note">
            {{
              auth.isProSubscriber
                ? t('catalog.discoveryUnlocked')
                : t('catalog.discoveryLocked', { count: lockedProductCount })
            }}
          </p>
        </div>

        <div class="filters">
          <input v-model="search" type="search" class="filter-input" :placeholder="t('encyclopedia.searchPlaceholder')" />
          <select v-model="industry" class="filter-select">
            <option v-for="option in industries" :key="option" :value="option">
              {{ option === 'ALL' ? t('encyclopedia.allIndustries') : getIndustryLabel(option) }}
            </option>
          </select>
        </div>

        <section v-if="selectedProduct" class="selected-product">
          <div class="selected-product-main">
            <img :src="getProductCardImage(selectedProduct)" :alt="getProductCardName(selectedProduct)" class="selected-product-image" />
            <div class="selected-product-copy">
              <p class="product-industry">{{ getIndustryLabel(selectedProduct.industry) }}</p>
              <div class="product-access-row">
                <span
                  v-if="selectedProduct.isProOnly"
                  class="product-access-badge"
                  :class="{ locked: isProductLocked(selectedProduct), unlocked: !isProductLocked(selectedProduct) }"
                >
                  {{ getProductAccessText(selectedProduct) }}
                </span>
              </div>
              <h3>{{ getProductCardName(selectedProduct) }}</h3>
              <p class="product-description">{{ getProductCardDescription(selectedProduct) }}</p>
              <p v-if="selectedProduct.isProOnly" class="product-access-detail">
                {{ getProductAccessDetail(selectedProduct) }}
              </p>
              <div class="product-meta">
                <span>{{ t('encyclopedia.basePrice') }}: ${{ selectedProduct.basePrice }}</span>
                <span>{{ t('encyclopedia.craftTime') }}: {{ selectedProduct.baseCraftTicks }}</span>
                <span>{{ t('encyclopedia.energy') }}: {{ selectedProduct.energyConsumptionMwh }} MW</span>
                <span>{{ t('encyclopedia.output') }}: {{ selectedProduct.outputQuantity }} {{ getLocalizedUnitName(selectedProduct.unitName, locale) }}</span>
              </div>
            </div>
          </div>

          <div class="composition-panel">
            <div class="composition-header">
              <h4>{{ t('encyclopedia.compositionTitle') }}</h4>
              <p>{{ t('encyclopedia.compositionHelp') }}</p>
            </div>
            <div class="composition-flow">
              <div v-for="(recipe, index) in selectedProduct.recipes" :key="`${selectedProduct.id}-${index}`" class="composition-node ingredient">
                <img v-if="getIngredientImage(recipe)" :src="getIngredientImage(recipe) ?? undefined" :alt="getLocalizedRecipeIngredientName(recipe, locale)" class="composition-image" />
                <strong>{{ recipe.quantity }} {{ recipe.resourceType?.unitSymbol ?? recipe.inputProductType?.unitSymbol }}</strong>
                <span>{{ getLocalizedRecipeIngredientName(recipe, locale) }}</span>
              </div>
              <span class="composition-arrow" aria-hidden="true">→</span>
              <div class="composition-node output">
                <img :src="getProductCardImage(selectedProduct)" :alt="getProductCardName(selectedProduct)" class="composition-image" />
                <strong>{{ selectedProduct.outputQuantity }} {{ selectedProduct.unitSymbol }}</strong>
                <span>{{ getProductCardName(selectedProduct) }}</span>
              </div>
            </div>
            <p class="recipe-line">
              <strong>{{ t('encyclopedia.recipe') }}:</strong>
              {{ getRecipeText(selectedProduct) }}
            </p>
          </div>
        </section>

        <div class="product-grid">
          <button
            v-for="product in filteredProducts"
            :key="product.id"
            type="button"
            class="product-card"
            :class="{ active: selectedProductId === product.id }"
            @click="selectedProductId = product.id"
          >
            <img :src="getProductCardImage(product)" :alt="getProductCardName(product)" class="product-image" />
            <div class="product-heading">
              <div>
                <h3>{{ getProductCardName(product) }}</h3>
                <p class="product-industry">{{ getIndustryLabel(product.industry) }}</p>
              </div>
              <span
                v-if="product.isProOnly"
                class="product-access-badge"
                :class="{ locked: isProductLocked(product), unlocked: !isProductLocked(product) }"
              >
                {{ getProductAccessText(product) }}
              </span>
              <span class="product-batch">{{ product.outputQuantity }} {{ product.unitSymbol }}</span>
            </div>
            <p class="product-description">{{ getProductCardDescription(product) }}</p>
            <div class="product-meta">
              <span>{{ t('encyclopedia.basePrice') }}: ${{ product.basePrice }}</span>
              <span>{{ t('encyclopedia.energy') }}: {{ product.energyConsumptionMwh }} MW</span>
            </div>
          </button>
        </div>
      </section>
    </template>
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
.resource-meta,
.product-meta,
.selected-product-main,
.composition-header {
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
.product-description,
.product-industry,
.recipe-line,
.resource-meta,
.product-meta,
.composition-header p,
.product-access-detail {
  color: var(--color-text-secondary);
}

.hero h1,
.section-header h2,
.resource-heading h3,
.product-heading h3,
.selected-product-copy h3,
.composition-header h4 {
  margin: 0;
}

.hero-stats {
  justify-content: flex-end;
}

.stat-card,
.resource-card,
.product-card,
.selected-product,
.composition-node {
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

.resource-grid,
.product-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 1rem;
}

.resource-card,
.product-card,
.selected-product {
  overflow: hidden;
}

.resource-image,
.product-image,
.selected-product-image,
.composition-image {
  width: 100%;
  background: var(--color-bg);
  object-fit: cover;
}

.resource-image,
.product-image {
  aspect-ratio: 16 / 9;
}

.selected-product-image {
  width: min(280px, 100%);
  border-radius: 16px;
  aspect-ratio: 1;
}

.resource-body,
.product-card {
  padding: 1rem;
}

.product-card {
  display: grid;
  gap: 0.75rem;
  text-align: left;
  cursor: pointer;
}

.product-access-row {
  display: flex;
  gap: 0.5rem;
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

.product-card.active {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 1px var(--color-primary);
}

.resource-heading,
.product-heading {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.resource-unit,
.product-batch {
  padding: 0.25rem 0.6rem;
  border-radius: 999px;
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  font-weight: 600;
  font-size: 0.75rem;
}

.filters {
  align-items: center;
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

.selected-product {
  display: grid;
  gap: 1.25rem;
  padding: 1rem;
}

.selected-product-copy {
  display: grid;
  gap: 0.75rem;
  flex: 1;
  min-width: 240px;
}

.composition-panel {
  display: grid;
  gap: 1rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--color-border);
}

.composition-flow {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.composition-node {
  width: 150px;
  padding: 0.75rem;
  display: grid;
  gap: 0.5rem;
  justify-items: center;
  text-align: center;
}

.composition-node.output {
  border-color: var(--color-primary);
}

.composition-image {
  border-radius: 12px;
  aspect-ratio: 1;
}

.composition-arrow {
  font-size: 1.5rem;
  color: var(--color-primary);
  font-weight: 700;
}

.recipe-line {
  margin: 0;
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

  .composition-flow {
    flex-direction: column;
    align-items: stretch;
  }

  .composition-node {
    width: 100%;
  }

  .composition-arrow {
    transform: rotate(90deg);
    align-self: center;
  }
}
</style>
