<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import {
  getLocalizedCategory,
  getLocalizedIndustry,
  getLocalizedProductDescription,
  getLocalizedProductName,
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
const resource = ref<ResourceType | null>(null)
const productsUsingResource = ref<ProductType[]>([])

const ENCYCLOPEDIA_RESOURCE_QUERY = `
  query EncyclopediaResource($slug: String!) {
    encyclopediaResource(slug: $slug) {
      resource {
        id name slug category basePrice weightPerUnit unitName unitSymbol imageUrl description
      }
      productsUsingResource {
        id name slug industry basePrice baseCraftTicks outputQuantity energyConsumptionMwh
        unitName unitSymbol isProOnly isUnlockedForCurrentPlayer description
        recipes {
          quantity
          resourceType { id name slug category basePrice weightPerUnit unitName unitSymbol imageUrl description }
          inputProductType { id name slug unitName unitSymbol }
        }
      }
    }
  }
`

async function loadResource(slug: string) {
  try {
    loading.value = true
    error.value = null

    const data = await gqlRequest<{
      encyclopediaResource: {
        resource: ResourceType
        productsUsingResource: ProductType[]
      } | null
    }>(ENCYCLOPEDIA_RESOURCE_QUERY, { slug })

    if (data.encyclopediaResource === null) {
      resource.value = null
      productsUsingResource.value = []
    } else {
      resource.value = data.encyclopediaResource.resource
      productsUsingResource.value = data.encyclopediaResource.productsUsingResource
    }
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('resourceDetail.loadFailed')
  } finally {
    loading.value = false
  }
}

watch(
  () => route.params.slug as string,
  (slug) => {
    if (slug) loadResource(slug)
  },
  { immediate: true },
)


function getResourceImage(res: ResourceType) {
  return getResourceImageUrl(res)
}

function getProductImage(product: ProductType) {
  return getProductImageUrl(product)
}

function getIngredientQuantityForResource(product: ProductType): number {
  if (!resource.value) return 0
  const recipe = product.recipes.find((r) => r.resourceType?.id === resource.value!.id)
  return recipe?.quantity ?? 0
}

function getOtherIngredients(product: ProductType) {
  if (!resource.value) return []
  return product.recipes.filter((r) => r.resourceType?.id !== resource.value!.id)
}

function goBack() {
  router.push({ name: 'encyclopedia' })
}
</script>

<template>
  <div class="resource-detail-view container">
    <nav class="breadcrumb">
      <button type="button" class="back-link" @click="goBack">
        ← {{ t('resourceDetail.backToEncyclopedia') }}
      </button>
    </nav>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="error-message" role="alert">{{ error }}</div>
    <div v-else-if="!resource" class="not-found">
      <h2>{{ t('resourceDetail.notFound') }}</h2>
      <p>{{ t('resourceDetail.notFoundHint') }}</p>
      <button type="button" class="btn-primary" @click="goBack">
        {{ t('resourceDetail.backToEncyclopedia') }}
      </button>
    </div>

    <template v-else>
      <header class="resource-hero">
        <img
          v-if="getResourceImage(resource)"
          :src="getResourceImage(resource) ?? undefined"
          :alt="getLocalizedResourceName(resource, locale)"
          class="resource-hero-image"
        />
        <div class="resource-hero-body">
          <div class="resource-badges">
            <span class="badge badge--category">{{ getLocalizedCategory(resource.category, locale) }}</span>
            <span class="badge badge--unit">{{ getLocalizedUnitName(resource.unitName, locale) }} ({{ resource.unitSymbol }})</span>
          </div>
          <h1>{{ getLocalizedResourceName(resource, locale) }}</h1>
          <p class="resource-description">{{ getLocalizedResourceDescription(resource, locale) }}</p>
          <div class="resource-meta">
            <div class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.basePrice') }}</span>
              <strong class="meta-value">${{ resource.basePrice }}</strong>
            </div>
            <div class="meta-item">
              <span class="meta-label">{{ t('resourceDetail.weight') }}</span>
              <strong class="meta-value">{{ resource.weightPerUnit }} kg/{{ resource.unitSymbol }}</strong>
            </div>
          </div>
        </div>
      </header>

      <section class="products-section">
        <div class="section-header">
          <h2>{{ t('resourceDetail.usedInProducts') }}</h2>
          <p>{{ t('resourceDetail.usedInProductsHelp') }}</p>
        </div>

        <p v-if="productsUsingResource.length === 0" class="empty-state">
          {{ t('resourceDetail.noProductsUsingResource') }}
        </p>

        <div v-else class="product-grid">
          <article v-for="product in productsUsingResource" :key="product.id" class="product-card">
            <img
              :src="getProductImage(product)"
              :alt="getLocalizedProductName(product, locale)"
              class="product-image"
            />
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
                <strong>{{ getIngredientQuantityForResource(product) }} {{ resource.unitSymbol }}</strong>
              </div>

              <div class="recipe-summary">
                <span class="meta-label">{{ t('resourceDetail.recipeLabel') }}:</span>
                <span>{{ getLocalizedRecipeSummary(product, locale) }}</span>
              </div>

              <div v-if="getOtherIngredients(product).length > 0" class="other-ingredients">
                <span class="meta-label">{{ t('resourceDetail.manufacturingDetails') }}:</span>
                <div class="ingredient-chips">
                  <span
                    v-for="(recipe, idx) in getOtherIngredients(product)"
                    :key="idx"
                    class="ingredient-chip"
                  >
                    {{ recipe.quantity }}
                    {{ recipe.resourceType?.unitSymbol ?? recipe.inputProductType?.unitSymbol }}
                    {{ recipe.resourceType?.name ?? recipe.inputProductType?.name }}
                  </span>
                </div>
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

.badge {
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
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1rem;
}

.product-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  overflow: hidden;
  display: grid;
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
}
</style>
