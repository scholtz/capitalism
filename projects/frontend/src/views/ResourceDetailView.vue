<script setup lang="ts">
import { computed, ref, watch } from 'vue'
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
  getLocalizedRecipeSummary,
  getLocalizedResourceDescription,
  getLocalizedResourceName,
  getLocalizedUnitName,
  getProductImageUrl,
  getResourceImageUrl,
} from '@/lib/catalogPresentation'
import type { ProductType, ResourceType } from '@/types'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()

const loading = ref(true)
const error = ref<string | null>(null)
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

const selectedSlug = computed(() => String(route.params.slug ?? ''))

const visibleProducts = computed(() => (showProProducts.value ? products.value : products.value.filter((product) => !product.isProOnly)))

const selectedResource = computed(() => resources.value.find((resource) => resource.slug === selectedSlug.value) ?? null)

const hiddenProduct = computed(() => {
  const product = products.value.find((candidate) => candidate.slug === selectedSlug.value) ?? null
  return product?.isProOnly && !showProProducts.value ? product : null
})

const selectedProduct = computed(() => visibleProducts.value.find((product) => product.slug === selectedSlug.value) ?? null)

const relatedProducts = computed(() => {
  if (selectedResource.value) {
    return visibleProducts.value.filter((product) => product.recipes.some((recipe) => recipe.resourceType?.slug === selectedResource.value?.slug))
  }

  if (selectedProduct.value) {
    const selectedProductValue = selectedProduct.value

    return visibleProducts.value.filter((product) => product.id !== selectedProductValue.id && product.recipes.some((recipe) => recipe.inputProductType?.slug === selectedProductValue.slug))
  }

  return []
})

watch(
  () => route.params.slug as string,
  async (slug) => {
    if (!slug) {
      return
    }

    try {
      loading.value = true
      error.value = null

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
      error.value = reason instanceof Error ? reason.message : t('resourceDetail.loadFailed')
    } finally {
      loading.value = false
    }
  },
  { immediate: true },
)

function getSelectedImageUrl() {
  if (selectedResource.value) {
    return getResourceImageUrl(selectedResource.value)
  }

  if (selectedProduct.value) {
    return getProductImageUrl(selectedProduct.value)
  }

  return null
}

function getSelectedTitle() {
  if (selectedResource.value) {
    return getLocalizedResourceName(selectedResource.value, locale.value)
  }

  if (selectedProduct.value) {
    return getLocalizedProductName(selectedProduct.value, locale.value)
  }

  return ''
}

function getSelectedDescription() {
  if (selectedResource.value) {
    return getLocalizedResourceDescription(selectedResource.value, locale.value)
  }

  if (selectedProduct.value) {
    return getLocalizedProductDescription(selectedProduct.value, locale.value)
  }

  return ''
}

function getProductImage(product: ProductType) {
  return getProductImageUrl(product)
}

function getIngredientImage(recipe: ProductType['recipes'][number]) {
  if (recipe.resourceType) {
    return getResourceImageUrl(recipe.resourceType)
  }

  if (recipe.inputProductType) {
    const inputProductType = recipe.inputProductType
    const product = products.value.find((candidate) => candidate.id === inputProductType.id)
    return getProductImageUrl({
      slug: inputProductType.slug,
      name: inputProductType.name,
      industry: product?.industry ?? 'ELECTRONICS',
    })
  }

  return null
}

function getIngredientTargetSlug(recipe: ProductType['recipes'][number]) {
  return recipe.resourceType?.slug ?? recipe.inputProductType?.slug ?? null
}

function getIngredientQuantityForSelectedEntry(product: ProductType): number {
  if (selectedResource.value) {
    const recipe = product.recipes.find((candidate) => candidate.resourceType?.id === selectedResource.value?.id)
    return recipe?.quantity ?? 0
  }

  if (selectedProduct.value) {
    const recipe = product.recipes.find((candidate) => candidate.inputProductType?.id === selectedProduct.value?.id)
    return recipe?.quantity ?? 0
  }

  return 0
}

function getIngredientUnitForSelectedEntry(product: ProductType): string {
  if (selectedResource.value) {
    return selectedResource.value.unitSymbol
  }

  if (selectedProduct.value) {
    const recipe = product.recipes.find((candidate) => candidate.inputProductType?.id === selectedProduct.value?.id)
    return recipe?.inputProductType?.unitSymbol ?? selectedProduct.value.unitSymbol
  }

  return ''
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

function getNotFoundHint() {
  return hiddenProduct.value ? t('resourceDetail.hiddenByFilterHint') : t('resourceDetail.notFoundHint')
}

function navigateToEntry(slug: string) {
  router.push({
    name: 'encyclopedia-detail',
    params: { slug },
    query: showProProducts.value ? { showPro: '1' } : {},
  })
}

function goBack() {
  router.push({ name: 'encyclopedia', query: showProProducts.value ? { showPro: '1' } : {} })
}
</script>

<template>
  <div class="resource-detail-view container">
    <nav class="breadcrumb">
      <button type="button" class="back-link" @click="goBack">← {{ t('resourceDetail.backToEncyclopedia') }}</button>
      <label class="filter-toggle">
        <input v-model="showProProducts" type="checkbox" />
        <span>{{ t('encyclopedia.showProProducts') }}</span>
      </label>
    </nav>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="error-message" role="alert">{{ error }}</div>
    <div v-else-if="!selectedResource && !selectedProduct" class="not-found">
      <h2>{{ t('resourceDetail.notFound') }}</h2>
      <p>{{ getNotFoundHint() }}</p>
      <button type="button" class="btn-primary" @click="goBack">
        {{ t('resourceDetail.backToEncyclopedia') }}
      </button>
    </div>

    <template v-else>
      <header class="resource-hero">
        <img v-if="getSelectedImageUrl()" :src="getSelectedImageUrl() ?? undefined" :alt="getSelectedTitle()" class="resource-hero-image" />
        <div class="resource-hero-body">
          <div class="resource-badges">
            <span v-if="selectedResource" class="badge badge--category">{{ getLocalizedCategory(selectedResource.category, locale) }}</span>
            <span v-if="selectedResource" class="badge badge--unit">{{ getLocalizedUnitName(selectedResource.unitName, locale) }} ({{ selectedResource.unitSymbol }})</span>
            <span v-if="selectedProduct" class="badge badge--category">{{ getLocalizedIndustry(selectedProduct.industry, locale) }}</span>
            <span v-if="selectedProduct" class="badge badge--unit">{{ getLocalizedUnitName(selectedProduct.unitName, locale) }} ({{ selectedProduct.unitSymbol }})</span>
            <span v-if="selectedProduct?.isProOnly" class="product-access-badge" :class="{ locked: isProductLocked(selectedProduct), unlocked: !isProductLocked(selectedProduct) }">
              {{ getProductAccessText(selectedProduct) }}
            </span>
          </div>
          <h1>{{ getSelectedTitle() }}</h1>
          <p class="resource-description">{{ getSelectedDescription() }}</p>
          <p v-if="selectedProduct?.isProOnly" class="resource-description">
            {{ getProductAccessDetail(selectedProduct) }}
          </p>
          <div class="resource-meta">
            <div class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.basePrice') }}</span>
              <strong class="meta-value">${{ selectedResource?.basePrice ?? selectedProduct?.basePrice }}</strong>
            </div>
            <div v-if="selectedResource" class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.weight') }}</span>
              <strong class="meta-value">{{ selectedResource.weightPerUnit }} kg/{{ selectedResource.unitSymbol }}</strong>
            </div>
            <div v-if="selectedProduct" class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.craftTicks') }}</span>
              <strong class="meta-value">{{ selectedProduct.baseCraftTicks }}</strong>
            </div>
            <div v-if="selectedProduct" class="meta-item">
              <span class="meta-label">{{ t('encyclopedia.energy') }}</span>
              <strong class="meta-value">{{ selectedProduct.energyConsumptionMwh }} MWh</strong>
            </div>
            <div v-if="selectedProduct" class="meta-item">
              <span class="meta-label">{{ t('encyclopedia.basicLaborHours') }}</span>
              <strong class="meta-value">{{ selectedProduct.basicLaborHours }} h</strong>
            </div>
            <div v-if="selectedProduct" class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.batchOutput') }}</span>
              <strong class="meta-value">{{ selectedProduct.outputQuantity }} {{ selectedProduct.unitSymbol }}</strong>
            </div>
          </div>
        </div>
      </header>

      <section v-if="selectedProduct" class="composition-panel">
        <div class="section-header">
          <h2>{{ t('encyclopedia.compositionTitle') }}</h2>
          <p>{{ t('encyclopedia.compositionHelp') }}</p>
        </div>

        <div class="composition-flow">
          <div
            v-for="(recipe, index) in selectedProduct.recipes"
            :key="`${selectedProduct.id}-${index}`"
            class="composition-node ingredient clickable"
            role="link"
            tabindex="0"
            :aria-label="t('encyclopedia.viewDetail') + ': ' + getLocalizedRecipeIngredientName(recipe, locale)"
            @click="getIngredientTargetSlug(recipe) && navigateToEntry(getIngredientTargetSlug(recipe)!)"
            @keydown.enter="getIngredientTargetSlug(recipe) && navigateToEntry(getIngredientTargetSlug(recipe)!)"
            @keydown.space.prevent="getIngredientTargetSlug(recipe) && navigateToEntry(getIngredientTargetSlug(recipe)!)"
          >
            <img v-if="getIngredientImage(recipe)" :src="getIngredientImage(recipe) ?? undefined" :alt="getLocalizedRecipeIngredientName(recipe, locale)" class="composition-image" />
            <strong>{{ recipe.quantity }} {{ recipe.resourceType?.unitSymbol ?? recipe.inputProductType?.unitSymbol }}</strong>
            <span>{{ getLocalizedRecipeIngredientName(recipe, locale) }}</span>
          </div>
          <span class="composition-arrow" aria-hidden="true">→</span>
          <div
            class="composition-node output clickable"
            role="link"
            tabindex="0"
            :aria-label="t('encyclopedia.viewDetail') + ': ' + getLocalizedProductName(selectedProduct, locale)"
            @click="navigateToEntry(selectedProduct.slug)"
            @keydown.enter="navigateToEntry(selectedProduct.slug)"
            @keydown.space.prevent="navigateToEntry(selectedProduct.slug)"
          >
            <img :src="getProductImage(selectedProduct)" :alt="getLocalizedProductName(selectedProduct, locale)" class="composition-image" />
            <strong>{{ selectedProduct.outputQuantity }} {{ selectedProduct.unitSymbol }}</strong>
            <span>{{ getLocalizedProductName(selectedProduct, locale) }}</span>
          </div>
        </div>

        <p class="recipe-summary">
          <span class="meta-label">{{ t('resourceDetail.recipeLabel') }}:</span>
          <span>{{ getLocalizedRecipeSummary(selectedProduct, locale) }}</span>
        </p>
      </section>

      <section class="products-section">
        <div class="section-header">
          <h2>{{ t('resourceDetail.usedInProducts') }}</h2>
          <p>{{ t('resourceDetail.usedInProductsHelp') }}</p>
        </div>

        <p v-if="relatedProducts.length === 0" class="empty-state">
          {{ t('resourceDetail.noProductsUsingResource') }}
        </p>

        <div v-else class="product-grid">
          <article
            v-for="product in relatedProducts"
            :key="product.id"
            class="product-card clickable"
            role="link"
            tabindex="0"
            :aria-label="t('encyclopedia.viewDetail') + ': ' + getLocalizedProductName(product, locale)"
            @click="navigateToEntry(product.slug)"
            @keydown.enter="navigateToEntry(product.slug)"
            @keydown.space.prevent="navigateToEntry(product.slug)"
          >
            <img :src="getProductImage(product)" :alt="getLocalizedProductName(product, locale)" class="product-image" />
            <div class="product-body">
              <div class="product-heading">
                <div>
                  <h3>{{ getLocalizedProductName(product, locale) }}</h3>
                  <p class="product-industry">{{ getLocalizedIndustry(product.industry, locale) }}</p>
                </div>
                <span class="product-batch">{{ product.outputQuantity }} {{ product.unitSymbol }}</span>
              </div>

              <p class="product-description">{{ getLocalizedProductDescription(product, locale) }}</p>

              <div class="product-meta">
                <span>{{ t('resourceDetail.craftTicks') }}: {{ product.baseCraftTicks }}</span>
                <span>{{ t('resourceDetail.batchOutput') }}: {{ product.outputQuantity }} {{ getLocalizedUnitName(product.unitName, locale) }}</span>
              </div>

              <div class="ingredient-highlight">
                <span class="ingredient-label">{{ t('resourceDetail.ingredientQuantity') }}:</span>
                <strong>{{ getIngredientQuantityForSelectedEntry(product) }} {{ getIngredientUnitForSelectedEntry(product) }}</strong>
              </div>
            </div>
          </article>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.resource-detail-view {
  padding: 2rem 1rem 3rem;
  display: grid;
  gap: 2rem;
}

.breadcrumb {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.back-link {
  background: none;
  border: none;
  color: var(--color-primary);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  padding: 0.4rem 0;
  text-decoration: none;
}

.back-link:hover {
  text-decoration: underline;
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

.not-found {
  text-align: center;
  padding: 3rem 1rem;
  display: grid;
  gap: 1rem;
  justify-items: center;
}

.not-found h2 {
  margin: 0;
}

.resource-hero {
  display: flex;
  gap: 2rem;
  align-items: flex-start;
  flex-wrap: wrap;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  overflow: hidden;
  padding: 1.5rem;
}

.resource-hero-image {
  width: min(280px, 100%);
  border-radius: 12px;
  aspect-ratio: 1;
  object-fit: cover;
  background: var(--color-bg);
  flex-shrink: 0;
}

.resource-hero-body {
  flex: 1;
  min-width: 240px;
  display: grid;
  gap: 0.75rem;
}

.resource-badges {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.badge,
.product-access-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.6rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  border: 1px solid var(--color-border);
}

.badge--category {
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  border-color: rgba(0, 71, 255, 0.25);
}

.badge--unit {
  background: var(--color-bg);
  color: var(--color-text-secondary);
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

.resource-hero-body h1 {
  margin: 0;
  font-size: 1.75rem;
}

.resource-description {
  color: var(--color-text-secondary);
  margin: 0;
}

.resource-meta {
  display: flex;
  gap: 1.5rem;
  flex-wrap: wrap;
}

.meta-item {
  display: grid;
  gap: 0.15rem;
}

.meta-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.meta-value {
  font-size: 1rem;
}

.section-header {
  display: grid;
  gap: 0.25rem;
}

.section-header h2 {
  margin: 0;
}

.section-header p {
  color: var(--color-text-secondary);
  margin: 0;
}

.empty-state {
  color: var(--color-text-secondary);
  padding: 2rem;
  text-align: center;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 12px;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 1rem;
}

.composition-panel {
  display: grid;
  gap: 1rem;
}

.composition-flow {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.composition-flow--card {
  align-items: stretch;
}

.composition-node {
  width: 150px;
  padding: 0.75rem;
  display: grid;
  gap: 0.5rem;
  justify-items: center;
  text-align: center;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
}

.composition-node.clickable {
  cursor: pointer;
  transition:
    transform 0.2s ease,
    box-shadow 0.2s ease;
}

.composition-node.clickable:hover,
.composition-node.clickable:focus-visible {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  outline: none;
}

.composition-node.output {
  border-color: var(--color-primary);
}

.composition-image {
  width: 100%;
  border-radius: 12px;
  aspect-ratio: 1;
  object-fit: cover;
  background: var(--color-bg);
}

.composition-arrow {
  font-size: 1.5rem;
  color: var(--color-primary);
  font-weight: 700;
}

.product-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  overflow: hidden;
  display: grid;
}

.product-card.clickable {
  cursor: pointer;
  transition: all 0.2s ease;
}

.product-card.clickable:hover {
  border-color: var(--color-primary);
  box-shadow: 0 4px 12px rgba(0, 71, 255, 0.15);
  transform: translateY(-2px);
}

.product-card.clickable:focus {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.product-image {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  background: var(--color-bg);
}

.product-body {
  padding: 1rem;
  display: grid;
  gap: 0.75rem;
}

.product-heading {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.product-heading h3 {
  margin: 0;
}

.product-industry {
  color: var(--color-text-secondary);
  font-size: 0.85rem;
  margin: 0;
}

.product-description {
  color: var(--color-text-secondary);
  font-size: 0.9rem;
  margin: 0;
}

.product-batch {
  padding: 0.25rem 0.6rem;
  border-radius: 999px;
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-primary);
  font-weight: 600;
  font-size: 0.75rem;
  white-space: nowrap;
}

.product-meta {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
  font-size: 0.85rem;
  color: var(--color-text-secondary);
}

.ingredient-highlight {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  padding: 0.5rem 0.75rem;
  background: rgba(0, 71, 255, 0.06);
  border: 1px solid rgba(0, 71, 255, 0.2);
  border-radius: 8px;
  font-size: 0.875rem;
}

.ingredient-label {
  color: var(--color-text-secondary);
}

.recipe-summary {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  display: flex;
  gap: 0.4rem;
  flex-wrap: wrap;
  margin: 0;
}

.other-ingredients {
  display: grid;
  gap: 0.4rem;
}

.ingredient-chips {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.ingredient-chip {
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  font-size: 0.78rem;
  background: var(--color-bg);
  color: var(--color-text-secondary);
}

.btn-primary {
  padding: 0.75rem 1.5rem;
  background: var(--color-primary);
  color: #fff;
  border: none;
  border-radius: 10px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-primary:hover {
  opacity: 0.9;
}

@media (max-width: 720px) {
  .resource-hero {
    flex-direction: column;
  }

  .resource-hero-image {
    width: 100%;
  }

  .product-grid {
    grid-template-columns: 1fr;
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
