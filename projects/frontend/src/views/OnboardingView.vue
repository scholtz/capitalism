<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import {
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedResourceName,
  getProductImageUrl,
} from '@/lib/catalogPresentation'
import { useAuthStore } from '@/stores/auth'
import type { City, ProductType, OnboardingResult } from '@/types'

const { t, locale } = useI18n()
const router = useRouter()
const auth = useAuthStore()

const PROGRESS_KEY = 'onboarding_progress'

const CITIES_QUERY = `
  {
    cities {
      id
      name
      countryCode
      latitude
      longitude
      population
      resources {
        resourceType {
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
        abundance
      }
    }
  }
`

const PRODUCTS_QUERY = `
  query Products($industry: String) {
    productTypes(industry: $industry) {
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
      description
      recipes {
        quantity
        resourceType {
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
        inputProductType {
          id
          name
          slug
          unitName
          unitSymbol
        }
      }
    }
  }
`

const starterProductSlugByIndustry: Record<string, string[]> = {
  FURNITURE: ['wooden-chair'],
  FOOD_PROCESSING: ['bread'],
  HEALTHCARE: ['basic-medicine'],
}

const step = ref(1)
const loading = ref(false)
const error = ref<string | null>(null)

// Data
const industries = ref<string[]>([])
const cities = ref<City[]>([])
const products = ref<ProductType[]>([])

// Selections
const selectedIndustry = ref('')
const selectedCityId = ref('')
const selectedProductId = ref('')
const companyName = ref('')

// Completion result (for step 4)
const completionResult = ref<OnboardingResult | null>(null)

const selectedCity = computed(() => cities.value.find((c) => c.id === selectedCityId.value))
const selectedProduct = computed(() => products.value.find((p) => p.id === selectedProductId.value))
const canShowSummary = computed(() => !!selectedCity.value && !!companyName.value && !!selectedProduct.value)

const canProceedStep1 = computed(() => !!selectedIndustry.value)
const canProceedStep2 = computed(() => !!selectedCityId.value)
const canProceedStep3 = computed(() => !!selectedProductId.value && !!companyName.value)

const industryIcons: Record<string, string> = {
  FURNITURE: '🪑',
  FOOD_PROCESSING: '🍞',
  HEALTHCARE: '💊',
}

const industryDescriptions: Record<string, string> = {
  FURNITURE: 'Craft wooden furniture from harvested timber.',
  FOOD_PROCESSING: 'Process grain and crops into food products.',
  HEALTHCARE: 'Manufacture medicines from chemical minerals.',
}

function saveProgress() {
  try {
    localStorage.setItem(
      PROGRESS_KEY,
      JSON.stringify({
        step: step.value,
        industry: selectedIndustry.value,
        cityId: selectedCityId.value,
        productId: selectedProductId.value,
        companyName: companyName.value,
      }),
    )
  } catch {
    // localStorage unavailable — ignore
  }
}

function clearProgress() {
  try {
    localStorage.removeItem(PROGRESS_KEY)
  } catch {
    // ignore
  }
}

function restoreProgress() {
  // Don't restore if onboarding was already completed
  if (auth.player?.onboardingCompletedAtUtc) {
    clearProgress()
    return
  }
  try {
    const raw = localStorage.getItem(PROGRESS_KEY)
    if (!raw) return
    const saved = JSON.parse(raw)
    if (saved.step && saved.step >= 1 && saved.step <= 3) {
      step.value = saved.step
    }
    if (saved.industry) selectedIndustry.value = saved.industry
    if (saved.cityId) selectedCityId.value = saved.cityId
    if (saved.productId) selectedProductId.value = saved.productId
    if (saved.companyName) companyName.value = saved.companyName
  } catch {
    // corrupted data — ignore
  }
}

// Persist progress whenever selections change
watch([step, selectedIndustry, selectedCityId, selectedProductId, companyName], saveProgress)

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  // Ensure player data is loaded (may be null if only token was restored from storage)
  if (!auth.player) {
    await auth.fetchMe()
  }

  // If the player already completed onboarding and has companies, redirect to dashboard
  if (auth.player?.onboardingCompletedAtUtc && auth.player.companies.length > 0) {
    router.push('/dashboard')
    return
  }

  try {
    loading.value = true
    const [industriesData, citiesData] = await Promise.all([
      gqlRequest<{ starterIndustries: { industries: string[] } }>(
        '{ starterIndustries { industries } }',
      ),
      gqlRequest<{ cities: City[] }>(CITIES_QUERY),
    ])
    industries.value = industriesData.starterIndustries.industries
    cities.value = citiesData.cities

    // Restore saved progress (step, selections) after data is loaded
    restoreProgress()

    // If we restored a step > 1 with an industry, reload products so the product list is ready
    if (step.value > 1 && selectedIndustry.value) {
      const data = await gqlRequest<{ productTypes: ProductType[] }>(
        PRODUCTS_QUERY,
        { industry: selectedIndustry.value },
      )
      const allowedSlugs = starterProductSlugByIndustry[selectedIndustry.value] ?? []
      products.value = data.productTypes.filter((product) => allowedSlugs.includes(product.slug))
    }
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load data'
  } finally {
    loading.value = false
  }
})

async function loadProducts() {
  loading.value = true
  try {
    const data = await gqlRequest<{ productTypes: ProductType[] }>(
      PRODUCTS_QUERY,
      { industry: selectedIndustry.value },
    )
    const allowedSlugs = starterProductSlugByIndustry[selectedIndustry.value] ?? []
    products.value = data.productTypes.filter((product) => allowedSlugs.includes(product.slug))
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load products'
  } finally {
    loading.value = false
  }
}

function nextStep() {
  if (step.value === 1 && canProceedStep1.value) {
    loadProducts()
    step.value = 2
  } else if (step.value === 2 && canProceedStep2.value) {
    step.value = 3
  }
}

function prevStep() {
  if (step.value > 1) step.value--
}

async function completeOnboarding() {
  loading.value = true
  error.value = null
  try {
    const result = await gqlRequest<{ completeOnboarding: OnboardingResult }>(
      `mutation CompleteOnboarding($input: OnboardingInput!) {
        completeOnboarding(input: $input) {
          company { id name cash }
          factory { id name type }
          salesShop { id name type }
          selectedProduct { name industry }
        }
      }`,
      {
        input: {
          industry: selectedIndustry.value,
          cityId: selectedCityId.value,
          productTypeId: selectedProductId.value,
          companyName: companyName.value,
        },
      },
    )
    completionResult.value = result.completeOnboarding
    clearProgress()
    await auth.fetchMe()
    step.value = 4
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Onboarding failed'
  } finally {
    loading.value = false
  }
}

function formatIndustry(industry: string): string {
  return industry.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}

function getProductName(product: ProductType): string {
  return getLocalizedProductName(product, locale.value)
}

function getProductDescription(product: ProductType): string {
  return getLocalizedProductDescription(product, locale.value)
}

function getProductImage(product: ProductType): string {
  return getProductImageUrl(product)
}

function getRecipeIngredientLabel(product: ProductType, index: number): string {
  const recipe = product.recipes[index]
  if (!recipe) return ''
  return `${recipe.quantity}× ${getLocalizedRecipeIngredientName(recipe, locale.value)}`
}

function getCityResourceName(city: City, index: number): string {
  const resource = city.resources[index]?.resourceType
  return resource ? getLocalizedResourceName(resource, locale.value) : ''
}
</script>

<template>
  <div class="onboarding-view">
    <div class="onboarding-container container">
      <div v-if="step < 4" class="onboarding-header">
        <h1>{{ t('onboarding.title') }}</h1>
        <p class="subtitle">{{ t('onboarding.subtitle') }}</p>
      </div>

      <div v-if="step < 4" class="progress-bar">
        <div class="progress-segment" :class="{ active: step >= 1, done: step > 1 }">
          <div class="progress-step">
            <span v-if="step > 1" class="check-icon">✓</span>
            <span v-else>1</span>
          </div>
          <span class="progress-label">{{ t('onboarding.step1Title') }}</span>
        </div>
        <div class="progress-line" :class="{ active: step > 1 }"></div>
        <div class="progress-segment" :class="{ active: step >= 2, done: step > 2 }">
          <div class="progress-step">
            <span v-if="step > 2" class="check-icon">✓</span>
            <span v-else>2</span>
          </div>
          <span class="progress-label">{{ t('onboarding.step2Title') }}</span>
        </div>
        <div class="progress-line" :class="{ active: step > 2 }"></div>
        <div class="progress-segment" :class="{ active: step >= 3 }">
          <div class="progress-step">3</div>
          <span class="progress-label">{{ t('onboarding.step3Title') }}</span>
        </div>
      </div>

      <div v-if="error" class="error-message" role="alert">{{ error }}</div>

      <!-- Step 1: Choose Industry -->
      <div v-if="step === 1" class="step-content">
        <div class="step-header">
          <h2>{{ t('onboarding.step1Title') }}</h2>
          <p class="step-desc">{{ t('onboarding.step1Desc') }}</p>
        </div>
        <div class="industry-grid">
          <button
            v-for="ind in industries"
            :key="ind"
            class="industry-card"
            :class="{ selected: selectedIndustry === ind }"
            @click="selectedIndustry = ind"
          >
            <span class="card-icon">{{ industryIcons[ind] || '🏭' }}</span>
            <span class="card-title">{{ formatIndustry(ind) }}</span>
            <span class="card-desc">{{ industryDescriptions[ind] || '' }}</span>
          </button>
        </div>
        <div class="step-actions">
          <button class="btn btn-primary btn-lg" :disabled="!canProceedStep1" @click="nextStep">
            {{ t('common.next') }}
            <span class="btn-arrow">→</span>
          </button>
        </div>
      </div>

      <!-- Step 2: Choose City -->
      <div v-if="step === 2" class="step-content">
        <div class="step-header">
          <h2>{{ t('onboarding.step2Title') }}</h2>
          <p class="step-desc">{{ t('onboarding.step2Desc') }}</p>
        </div>
        <div class="city-grid">
          <button
            v-for="city in cities"
            :key="city.id"
            class="city-card"
            :class="{ selected: selectedCityId === city.id }"
            @click="selectedCityId = city.id"
          >
            <div class="city-header">
              <span class="card-icon">🏙️</span>
              <span class="card-title">{{ city.name }}</span>
            </div>
            <div class="city-meta">
              <span class="city-country">{{ city.countryCode }}</span>
              <span class="city-pop">{{ t('onboarding.population') }} {{ city.population.toLocaleString() }}</span>
            </div>
            <div class="city-resources">
              <span class="resource-label">{{ t('onboarding.resources') }}:</span>
              <span v-for="(r, i) in city.resources" :key="i" class="resource-badge">
                {{ getCityResourceName(city, i) }}
              </span>
            </div>
          </button>
        </div>
        <div class="step-actions">
          <button class="btn btn-secondary" @click="prevStep">
            <span class="btn-arrow">←</span>
            {{ t('common.back') }}
          </button>
          <button class="btn btn-primary btn-lg" :disabled="!canProceedStep2" @click="nextStep">
            {{ t('common.next') }}
            <span class="btn-arrow">→</span>
          </button>
        </div>
      </div>

      <!-- Step 3: Choose Product & Name Company -->
      <div v-if="step === 3" class="step-content">
        <div class="step-header">
          <h2>{{ t('onboarding.step3Title') }}</h2>
          <p class="step-desc">{{ t('onboarding.step3Desc') }}</p>
        </div>

        <div class="form-group">
          <label for="companyName">{{ t('onboarding.companyName') }}</label>
          <input
            id="companyName"
            v-model="companyName"
            type="text"
            required
            maxlength="200"
            :placeholder="t('onboarding.companyNamePlaceholder')"
          />
        </div>

        <div class="form-group">
          <span class="form-section-title">{{ t('onboarding.selectProduct') }}</span>
          <div class="product-grid">
            <button
              v-for="prod in products"
              :key="prod.id"
              class="product-card"
              :class="{ selected: selectedProductId === prod.id }"
              @click="selectedProductId = prod.id"
            >
              <img :src="getProductImage(prod)" :alt="getProductName(prod)" class="product-image" />
              <span class="card-title">{{ getProductName(prod) }}</span>
              <span class="product-price">${{ prod.basePrice }}{{ t('onboarding.perUnit') }}</span>
              <span class="product-craft">{{ t('onboarding.craftTime', { ticks: prod.baseCraftTicks }) }}</span>
              <span class="card-desc">{{ getProductDescription(prod) }}</span>
              <div class="product-recipe">
                <span class="recipe-label">{{ t('onboarding.requires') }}:</span>
                <span v-for="(r, i) in prod.recipes" :key="i" class="recipe-item">
                  {{ getRecipeIngredientLabel(prod, i) }}
                </span>
              </div>
            </button>
          </div>
        </div>

        <div v-if="canShowSummary" class="summary">
          <div class="summary-header">
            <span class="summary-icon">📋</span>
            <h3>{{ t('onboarding.summary') }}</h3>
          </div>
          <p>{{ t('onboarding.summaryText', { company: companyName, city: selectedCity!.name, industry: formatIndustry(selectedIndustry) }) }}</p>
          <div class="summary-details">
            <div class="summary-item">
              <span class="summary-label">🏭</span>
              <span>{{ companyName }} Factory</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">🏪</span>
              <span>{{ companyName }} Shop</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">📦</span>
              <span>{{ getProductName(selectedProduct!) }} — ${{ selectedProduct!.basePrice }}</span>
            </div>
          </div>
        </div>

        <div class="step-actions">
          <button class="btn btn-secondary" @click="prevStep">
            <span class="btn-arrow">←</span>
            {{ t('common.back') }}
          </button>
          <button
            class="btn btn-primary btn-lg"
            :disabled="!canProceedStep3 || loading"
            @click="completeOnboarding"
          >
            {{ loading ? t('common.loading') : t('onboarding.startPlaying') }}
            <span v-if="!loading" class="btn-arrow">🚀</span>
          </button>
        </div>
      </div>

      <!-- Step 4: Completion -->
      <div v-if="step === 4 && completionResult" class="step-content completion-step">
        <div class="completion-hero">
          <h2 class="completion-title">{{ t('onboarding.completionTitle') }}</h2>
          <p class="completion-desc">{{ t('onboarding.completionDesc') }}</p>
        </div>

        <div class="completion-achievements">
          <div class="achievement-item">
            <span class="achievement-icon">🏭</span>
            <div class="achievement-text">
              <strong>{{ completionResult.factory.name }}</strong>
              <span>{{ t('onboarding.completionFactory') }}</span>
            </div>
          </div>
          <div class="achievement-item">
            <span class="achievement-icon">🏪</span>
            <div class="achievement-text">
              <strong>{{ completionResult.salesShop.name }}</strong>
              <span>{{ t('onboarding.completionShop') }}</span>
            </div>
          </div>
          <div class="achievement-item">
            <span class="achievement-icon">📦</span>
            <div class="achievement-text">
              <strong>{{ completionResult.selectedProduct.name }}</strong>
              <span>{{ t('onboarding.completionProduct', { product: completionResult.selectedProduct.name }) }}</span>
            </div>
          </div>
          <div class="achievement-item">
            <span class="achievement-icon">💰</span>
            <div class="achievement-text">
              <strong>${{ completionResult.company.cash.toLocaleString() }}</strong>
              <span>{{ t('onboarding.completionCapital', { amount: completionResult.company.cash.toLocaleString() }) }}</span>
            </div>
          </div>
        </div>

        <div class="completion-next">
          <h3>{{ t('onboarding.completionNextSteps') }}</h3>
          <div class="completion-actions">
            <RouterLink to="/dashboard" class="btn btn-primary btn-lg">
              {{ t('onboarding.completionGoDashboard') }}
              <span class="btn-arrow">→</span>
            </RouterLink>
            <RouterLink to="/leaderboard" class="btn btn-secondary">
              {{ t('onboarding.completionViewLeaderboard') }}
            </RouterLink>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.onboarding-view {
  min-height: calc(100vh - 112px);
  background: linear-gradient(180deg, var(--color-bg) 0%, rgba(0, 71, 255, 0.04) 100%);
  padding: 2rem 1rem;
}

.onboarding-container {
  max-width: 720px;
}

.onboarding-header {
  text-align: center;
  margin-bottom: 2rem;
}

.onboarding-header h1 {
  font-size: 2rem;
  background: linear-gradient(135deg, var(--color-primary), var(--color-secondary));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin-bottom: 0.5rem;
}

.subtitle {
  color: var(--color-text-secondary);
  font-size: 1rem;
}

.progress-bar {
  display: flex;
  align-items: flex-start;
  margin-bottom: 2.5rem;
  padding: 0 1rem;
}

.progress-segment {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  min-width: 80px;
}

.progress-step {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 0.9375rem;
  background: var(--color-surface);
  color: var(--color-text-secondary);
  border: 2px solid var(--color-border);
  transition: all 0.3s ease;
}

.progress-segment.active .progress-step {
  background: var(--color-primary);
  color: #fff;
  border-color: var(--color-primary);
  box-shadow: 0 0 16px rgba(0, 71, 255, 0.3);
}

.progress-segment.done .progress-step {
  background: var(--color-success);
  border-color: var(--color-success);
  color: #fff;
  box-shadow: 0 0 12px rgba(0, 200, 83, 0.3);
}

.check-icon {
  font-size: 1.125rem;
}

.progress-label {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
  text-align: center;
  max-width: 100px;
  line-height: 1.2;
}

.progress-segment.active .progress-label {
  color: var(--color-text);
  font-weight: 500;
}

.progress-line {
  flex: 1;
  height: 2px;
  background: var(--color-border);
  margin-top: 20px;
  transition: background 0.3s ease;
}

.progress-line.active {
  background: linear-gradient(90deg, var(--color-success), var(--color-primary));
}

.step-content {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 2rem;
}

.step-header {
  margin-bottom: 1.5rem;
}

.step-header h2 {
  font-size: 1.375rem;
  margin-bottom: 0.375rem;
}

.step-desc {
  color: var(--color-text-secondary);
  font-size: 0.875rem;
}

.industry-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.city-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.industry-card,
.city-card,
.product-card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1.5rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  cursor: pointer;
  transition: all 0.2s ease;
  color: var(--color-text);
  text-align: center;
}

.industry-card:hover,
.city-card:hover,
.product-card:hover {
  border-color: var(--color-primary);
  transform: translateY(-2px);
  box-shadow: 0 4px 16px rgba(0, 71, 255, 0.1);
}

.industry-card.selected,
.city-card.selected,
.product-card.selected {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
  box-shadow: 0 0 0 1px var(--color-primary), 0 4px 16px rgba(0, 71, 255, 0.15);
}

.card-icon {
  font-size: 2.5rem;
  line-height: 1;
}

.product-image {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border-radius: var(--radius-sm);
  background: var(--color-surface-raised);
}

.card-title {
  font-weight: 700;
  font-size: 1rem;
}

.card-desc {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  line-height: 1.4;
}

.city-card {
  text-align: left;
}

.city-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.city-meta {
  display: flex;
  gap: 0.75rem;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.city-country {
  background: var(--color-surface-raised);
  padding: 0.125rem 0.5rem;
  border-radius: var(--radius-sm);
  font-weight: 600;
  font-size: 0.75rem;
}

.city-resources {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
  align-items: center;
}

.resource-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.resource-badge {
  background: rgba(0, 200, 83, 0.1);
  color: var(--color-secondary);
  padding: 0.125rem 0.5rem;
  border-radius: 9999px;
  font-size: 0.6875rem;
  font-weight: 500;
}

.product-price {
  font-size: 1.125rem;
  font-weight: 700;
  color: var(--color-secondary);
}

.product-craft {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.product-recipe {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.recipe-label {
  font-weight: 500;
}

.recipe-item {
  color: var(--color-tertiary);
  font-weight: 500;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1.25rem;
}

.form-group label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text);
}

.form-section-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text);
}

.form-group input {
  padding: 0.75rem 1rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  color: var(--color-text);
  font-size: 1rem;
  transition: border-color 0.2s ease;
}

.form-group input:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(0, 71, 255, 0.15);
}

.form-group input::placeholder {
  color: var(--color-text-secondary);
}

.summary {
  background: var(--color-surface-raised);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1.25rem;
  margin-bottom: 1.5rem;
}

.summary-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
}

.summary-icon {
  font-size: 1.25rem;
}

.summary h3 {
  font-size: 1rem;
  font-weight: 600;
}

.summary p {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.75rem;
}

.summary-details {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.summary-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  padding: 0.375rem 0.5rem;
  background: var(--color-surface);
  border-radius: var(--radius-sm);
}

.summary-label {
  font-size: 1rem;
}

.step-actions {
  display: flex;
  gap: 0.75rem;
  justify-content: flex-end;
  margin-top: 1.5rem;
}

.btn-lg {
  padding: 0.75rem 1.75rem;
  font-size: 1rem;
  font-weight: 600;
}

.btn-arrow {
  margin-left: 0.25rem;
}

.error-message {
  background: rgba(248, 113, 113, 0.1);
  border: 1px solid rgba(248, 113, 113, 0.3);
  color: var(--color-danger);
  padding: 0.75rem 1rem;
  border-radius: var(--radius-md);
  font-size: 0.875rem;
  margin-bottom: 1rem;
}

@media (max-width: 640px) {
  .industry-grid {
    grid-template-columns: 1fr;
  }

  .city-grid {
    grid-template-columns: 1fr;
  }

  .product-grid {
    grid-template-columns: 1fr;
  }

  .progress-label {
    display: none;
  }

  .step-content {
    padding: 1.25rem;
  }
}

/* Completion Step */
.completion-step {
  text-align: center;
  padding: 2rem;
}

.completion-hero {
  margin-bottom: 2rem;
}

.completion-title {
  font-size: 1.75rem;
  background: linear-gradient(135deg, var(--color-secondary), var(--color-primary));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin-bottom: 0.75rem;
}

.completion-desc {
  color: var(--color-text-secondary);
  font-size: 1rem;
  max-width: 480px;
  margin: 0 auto;
  line-height: 1.6;
}

.completion-achievements {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 1rem;
  margin-bottom: 2rem;
  text-align: left;
}

.achievement-item {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  padding: 1rem;
}

.achievement-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.achievement-text {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.achievement-text strong {
  color: var(--color-text-primary);
  font-size: 0.9rem;
}

.achievement-text span {
  color: var(--color-text-secondary);
  font-size: 0.8rem;
}

.completion-next {
  margin-top: 1.5rem;
}

.completion-next h3 {
  font-size: 1.1rem;
  margin-bottom: 1rem;
  color: var(--color-text-secondary);
}

.completion-actions {
  display: flex;
  gap: 1rem;
  justify-content: center;
  flex-wrap: wrap;
}
</style>
