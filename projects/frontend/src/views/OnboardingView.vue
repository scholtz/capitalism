<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { trackStartupPackEvent } from '@/lib/startupPackAnalytics'
import {
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedResourceName,
  getProductImageUrl,
} from '@/lib/catalogPresentation'
import OnboardingLotSelector from '@/components/onboarding/OnboardingLotSelector.vue'
import { useAuthStore } from '@/stores/auth'
import type {
  BuildingLot,
  City,
  OnboardingResult,
  OnboardingStartResult,
  ProductType,
  StartupPackClaimResult,
  StartupPackOffer,
} from '@/types'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const PROGRESS_KEY = 'onboarding_progress'
const STARTING_CASH = 500_000

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

const LOTS_QUERY = `
  query CityLots($cityId: UUID!) {
    cityLots(cityId: $cityId) {
      id
      cityId
      name
      description
      district
      latitude
      longitude
      price
      suitableTypes
      ownerCompanyId
      buildingId
      ownerCompany { id name }
      building { id name type }
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
      isProOnly
      isUnlockedForCurrentPlayer
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
const offerLoading = ref(false)
const offerError = ref<string | null>(null)
const offerMessage = ref<string | null>(null)
const onboardingCompanyCash = ref<number | null>(null)

const industries = ref<string[]>([])
const cities = ref<City[]>([])
const products = ref<ProductType[]>([])
const cityLots = ref<BuildingLot[]>([])

const selectedIndustry = ref('')
const selectedCityId = ref('')
const selectedProductId = ref('')
const selectedFactoryLotId = ref('')
const selectedShopLotId = ref('')
const companyName = ref('')

const completionResult = ref<OnboardingResult | null>(null)
const startupPackOffer = ref<StartupPackOffer | null>(null)

const selectedCity = computed(() => cities.value.find((city) => city.id === selectedCityId.value) ?? null)
const selectedProduct = computed(() => products.value.find((product) => product.id === selectedProductId.value) ?? null)
const selectedFactoryLot = computed(() => cityLots.value.find((lot) => lot.id === selectedFactoryLotId.value) ?? null)
const selectedShopLot = computed(() => cityLots.value.find((lot) => lot.id === selectedShopLotId.value) ?? null)
const starterCompany = computed(() => {
  const companyId = auth.player?.onboardingCompanyId
  if (!companyId) {
    return auth.player?.companies.find((company) => company.name === companyName.value) ?? null
  }

  return auth.player?.companies.find((company) => company.id === companyId) ?? null
})
const starterCash = computed(() => onboardingCompanyCash.value ?? starterCompany.value?.cash ?? STARTING_CASH)

const availableFactoryLots = computed(() =>
  cityLots.value.filter((lot) => lot.suitableTypes.split(',').map((type) => type.trim()).includes('FACTORY')),
)
const availableShopLots = computed(() =>
  cityLots.value.filter((lot) => lot.suitableTypes.split(',').map((type) => type.trim()).includes('SALES_SHOP')),
)
const recommendedFactoryLotIds = computed(() => {
  const industrialLots = availableFactoryLots.value
    .filter((lot) => !lot.ownerCompanyId && /industrial/i.test(lot.district))
    .sort((left, right) => left.price - right.price)

  if (industrialLots.length > 0) {
    return industrialLots.slice(0, 2).map((lot) => lot.id)
  }

  return availableFactoryLots.value
    .filter((lot) => !lot.ownerCompanyId)
    .sort((left, right) => left.price - right.price)
    .slice(0, 2)
    .map((lot) => lot.id)
})
const recommendedShopLotIds = computed(() => {
  const commercialLots = availableShopLots.value
    .filter((lot) => !lot.ownerCompanyId && /(commercial|business)/i.test(lot.district))
    .sort((left, right) => left.price - right.price)

  if (commercialLots.length > 0) {
    return commercialLots.slice(0, 2).map((lot) => lot.id)
  }

  return availableShopLots.value
    .filter((lot) => !lot.ownerCompanyId)
    .sort((left, right) => left.price - right.price)
    .slice(0, 2)
    .map((lot) => lot.id)
})

const canProceedStep1 = computed(() => !!selectedIndustry.value)
const canProceedStep2 = computed(() => !!selectedCityId.value)
const canProceedStep3 = computed(() =>
  !!companyName.value.trim()
  && !!selectedFactoryLot.value
  && !selectedFactoryLot.value.ownerCompanyId
  && STARTING_CASH >= selectedFactoryLot.value.price,
)
const canProceedStep4 = computed(() =>
  !!selectedProduct.value
  && !!selectedShopLot.value
  && !selectedShopLot.value.ownerCompanyId
  && starterCash.value >= selectedShopLot.value.price,
)
const canShowStep4Summary = computed(() => !!selectedProduct.value && !!selectedFactoryLot.value && !!selectedShopLot.value)
const activeStartupPackOffer = computed(() =>
  startupPackOffer.value
  && ['ELIGIBLE', 'SHOWN', 'DISMISSED'].includes(startupPackOffer.value.status)
    ? startupPackOffer.value
    : null,
)
const claimedStartupPackOffer = computed(() =>
  startupPackOffer.value?.status === 'CLAIMED' ? startupPackOffer.value : null,
)
const expiredStartupPackOffer = computed(() =>
  startupPackOffer.value?.status === 'EXPIRED' ? startupPackOffer.value : null,
)

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

function stepToKey(value: number): string {
  if (value === 2) return 'city'
  if (value === 3) return 'factory'
  if (value === 4) return 'shop'
  if (value === 5) return 'complete'
  return 'industry'
}

function keyToStep(value: unknown): number {
  if (value === 'city') return 2
  if (value === 'factory') return 3
  if (value === 'shop') return 4
  if (value === 'complete') return 5
  return 1
}

function getMaxReachableStep(): number {
  if (completionResult.value) return 5
  if (auth.player?.onboardingCurrentStep === 'SHOP_SELECTION') return 4
  if (selectedCityId.value) return 3
  if (selectedIndustry.value) return 2
  return 1
}

function clampStep(requestedStep: number): number {
  return Math.min(Math.max(requestedStep, 1), getMaxReachableStep())
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
        factoryLotId: selectedFactoryLotId.value,
        shopLotId: selectedShopLotId.value,
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
  if (auth.player?.onboardingCompletedAtUtc) {
    clearProgress()
    return
  }

  try {
    const raw = localStorage.getItem(PROGRESS_KEY)
    if (!raw) return

    const saved = JSON.parse(raw)
    if (typeof saved.industry === 'string') selectedIndustry.value = saved.industry
    if (typeof saved.cityId === 'string') selectedCityId.value = saved.cityId
    if (typeof saved.productId === 'string') selectedProductId.value = saved.productId
    if (typeof saved.companyName === 'string') companyName.value = saved.companyName
    if (typeof saved.factoryLotId === 'string') selectedFactoryLotId.value = saved.factoryLotId
    if (typeof saved.shopLotId === 'string') selectedShopLotId.value = saved.shopLotId
    if (typeof saved.step === 'number') step.value = clampStep(saved.step)
  } catch {
    // corrupted data — ignore
  }
}

watch(
  [step, selectedIndustry, selectedCityId, selectedProductId, companyName, selectedFactoryLotId, selectedShopLotId],
  saveProgress,
)

watch(step, async (currentStep) => {
  const currentKey = stepToKey(currentStep)
  if (route.query.step !== currentKey) {
    await router.replace({ query: { ...route.query, step: currentKey } })
  }
})

async function loadProducts() {
  if (!selectedIndustry.value) return

  loading.value = true
  try {
    const data = await gqlRequest<{ productTypes: ProductType[] }>(PRODUCTS_QUERY, {
      industry: selectedIndustry.value,
    })
    const allowedSlugs = starterProductSlugByIndustry[selectedIndustry.value] ?? []
    products.value = data.productTypes.filter((product) => allowedSlugs.includes(product.slug))
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load products'
  } finally {
    loading.value = false
  }
}

async function loadLots() {
  if (!selectedCityId.value) {
    cityLots.value = []
    return
  }

  loading.value = true
  try {
    const data = await gqlRequest<{ cityLots: BuildingLot[] }>(LOTS_QUERY, { cityId: selectedCityId.value })
    cityLots.value = data.cityLots
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load city lots'
  } finally {
    loading.value = false
  }
}

async function syncOngoingOnboardingState() {
  if (auth.player?.onboardingCurrentStep !== 'SHOP_SELECTION') {
    onboardingCompanyCash.value = null
    return
  }

  selectedIndustry.value = auth.player.onboardingIndustry ?? selectedIndustry.value
  selectedCityId.value = auth.player.onboardingCityId ?? selectedCityId.value
  selectedFactoryLotId.value = auth.player.onboardingFactoryLotId ?? selectedFactoryLotId.value

  const ongoingCompany = auth.player.companies.find(
    (company) => company.id === auth.player?.onboardingCompanyId,
  )
  if (ongoingCompany) {
    companyName.value = ongoingCompany.name
    onboardingCompanyCash.value = ongoingCompany.cash
  }

  step.value = 4
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  if (!auth.player) {
    await auth.fetchMe()
  }

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

    restoreProgress()
    await syncOngoingOnboardingState()

    if (selectedIndustry.value) {
      await loadProducts()
    }

    if (selectedCityId.value) {
      await loadLots()
    }

    step.value = clampStep(keyToStep(route.query.step))
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load data'
  } finally {
    loading.value = false
  }
})

async function nextStep() {
  error.value = null

  if (step.value === 1 && canProceedStep1.value) {
    await loadProducts()
    step.value = 2
    return
  }

  if (step.value === 2 && canProceedStep2.value) {
    await loadLots()
    step.value = 3
  }
}

function prevStep() {
  if (step.value > 1 && auth.player?.onboardingCurrentStep !== 'SHOP_SELECTION') {
    step.value--
  }
}

async function startOnboardingCompany() {
  if (!canProceedStep3.value || !selectedFactoryLot.value) return

  loading.value = true
  error.value = null

  try {
    const result = await gqlRequest<{ startOnboardingCompany: OnboardingStartResult }>(
      `mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
        startOnboardingCompany(input: $input) {
          nextStep
          company { id name cash }
          factory { id name type }
          factoryLot {
            id cityId name description district latitude longitude price suitableTypes
            ownerCompanyId buildingId
            ownerCompany { id name }
            building { id name type }
          }
        }
      }`,
      {
        input: {
          industry: selectedIndustry.value,
          cityId: selectedCityId.value,
          companyName: companyName.value.trim(),
          factoryLotId: selectedFactoryLotId.value,
        },
      },
    )

    onboardingCompanyCash.value = result.startOnboardingCompany.company.cash
    selectedFactoryLotId.value = result.startOnboardingCompany.factoryLot.id
    await auth.fetchMe()
    await loadLots()
    step.value = 4
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : t('onboarding.lotUnavailableBody')
    await loadLots()
    if (selectedFactoryLot.value?.ownerCompanyId) {
      selectedFactoryLotId.value = ''
    }
  } finally {
    loading.value = false
  }
}

async function completeOnboarding() {
  loading.value = true
  error.value = null

  try {
    const result = await gqlRequest<{ finishOnboarding: OnboardingResult }>(
      `mutation FinishOnboarding($input: FinishOnboardingInput!) {
        finishOnboarding(input: $input) {
          company { id name cash }
          factory { id name type }
          salesShop { id name type }
          selectedProduct { name industry }
          startupPackOffer {
            id
            offerKey
            status
            createdAtUtc
            expiresAtUtc
            shownAtUtc
            dismissedAtUtc
            claimedAtUtc
            companyCashGrant
            proDurationDays
            grantedCompanyId
          }
        }
      }`,
      {
        input: {
          productTypeId: selectedProductId.value,
          shopLotId: selectedShopLotId.value,
        },
      },
    )

    completionResult.value = result.finishOnboarding
    startupPackOffer.value = result.finishOnboarding.startupPackOffer
    auth.setStartupPackOffer(result.finishOnboarding.startupPackOffer)
    onboardingCompanyCash.value = result.finishOnboarding.company.cash
    clearProgress()
    await auth.fetchMe()
    startupPackOffer.value = auth.startupPackOffer ?? startupPackOffer.value
    if (startupPackOffer.value?.status === 'ELIGIBLE') {
      await markStartupPackOfferShown()
    } else if (startupPackOffer.value) {
      trackStartupPackEvent('view', { context: 'onboarding', status: startupPackOffer.value.status })
    }
    step.value = 5
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : t('onboarding.lotUnavailableBody')
    await auth.fetchMe()
    await loadLots()
    onboardingCompanyCash.value =
      auth.player?.companies.find((company) => company.id === auth.player?.onboardingCompanyId)?.cash
      ?? onboardingCompanyCash.value
    if (selectedShopLot.value?.ownerCompanyId) {
      selectedShopLotId.value = ''
    }
  } finally {
    loading.value = false
  }
}

async function markStartupPackOfferShown() {
  if (!startupPackOffer.value || startupPackOffer.value.status !== 'ELIGIBLE') {
    return
  }

  const data = await gqlRequest<{ markStartupPackOfferShown: StartupPackOffer | null }>(
    `mutation {
      markStartupPackOfferShown {
        id
        offerKey
        status
        createdAtUtc
        expiresAtUtc
        shownAtUtc
        dismissedAtUtc
        claimedAtUtc
        companyCashGrant
        proDurationDays
        grantedCompanyId
      }
    }`,
  )

  startupPackOffer.value = data.markStartupPackOfferShown
  auth.setStartupPackOffer(data.markStartupPackOfferShown)
  if (data.markStartupPackOfferShown) {
    trackStartupPackEvent('view', {
      context: 'onboarding',
      status: data.markStartupPackOfferShown.status,
    })
  }
}

async function dismissStartupPackOffer() {
  offerLoading.value = true
  offerError.value = null
  offerMessage.value = null

  try {
    const data = await gqlRequest<{ dismissStartupPackOffer: StartupPackOffer | null }>(
      `mutation {
        dismissStartupPackOffer {
          id
          offerKey
          status
          createdAtUtc
          expiresAtUtc
          shownAtUtc
          dismissedAtUtc
          claimedAtUtc
          companyCashGrant
          proDurationDays
          grantedCompanyId
        }
      }`,
    )

    startupPackOffer.value = data.dismissStartupPackOffer
    auth.setStartupPackOffer(data.dismissStartupPackOffer)
    offerMessage.value = t('startupPack.dismissedBody')
    trackStartupPackEvent('dismiss', { context: 'onboarding' })
  } catch (e: unknown) {
    offerError.value = e instanceof Error ? e.message : t('startupPack.claimFailed')
  } finally {
    offerLoading.value = false
  }
}

async function claimStartupPackOffer() {
  const targetCompanyId = completionResult.value?.company.id
  if (!targetCompanyId) {
    return
  }

  offerLoading.value = true
  offerError.value = null
  offerMessage.value = null
  trackStartupPackEvent('claim_click', { context: 'onboarding' })

  try {
    const data = await gqlRequest<{ claimStartupPack: StartupPackClaimResult }>(
      `mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
        claimStartupPack(input: $input) {
          offer {
            id
            offerKey
            status
            createdAtUtc
            expiresAtUtc
            shownAtUtc
            dismissedAtUtc
            claimedAtUtc
            companyCashGrant
            proDurationDays
            grantedCompanyId
          }
          company { id name cash }
          proSubscriptionEndsAtUtc
        }
      }`,
      { input: { companyId: targetCompanyId } },
    )

    startupPackOffer.value = data.claimStartupPack.offer
    auth.setStartupPackOffer(data.claimStartupPack.offer)
    if (completionResult.value) {
      completionResult.value = {
        ...completionResult.value,
        company: {
          ...completionResult.value.company,
          cash: data.claimStartupPack.company.cash,
        },
      }
    }
    await auth.fetchMe()
    offerMessage.value = t('startupPack.claimedBody', {
      date: formatDateTime(data.claimStartupPack.proSubscriptionEndsAtUtc),
    })
    trackStartupPackEvent('claim_success', { context: 'onboarding' })
  } catch (e: unknown) {
    offerError.value = e instanceof Error ? e.message : t('startupPack.claimFailed')
    trackStartupPackEvent('claim_error', { context: 'onboarding' })
  } finally {
    offerLoading.value = false
  }
}

function formatIndustry(industry: string): string {
  return industry.replace(/_/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase())
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

function formatCurrency(value: number): string {
  return value.toLocaleString(locale.value)
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function formatTimeRemaining(expiresAtUtc: string): string {
  const diffMs = new Date(expiresAtUtc).getTime() - Date.now()
  if (diffMs <= 0) {
    return t('startupPack.expiredNow')
  }

  const totalMinutes = Math.max(Math.ceil(diffMs / 60000), 1)
  const days = Math.floor(totalMinutes / (60 * 24))
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60)
  const minutes = totalMinutes % 60

  if (days > 0) {
    return t('startupPack.timeRemainingDaysHours', { days, hours })
  }

  if (hours > 0) {
    return t('startupPack.timeRemainingHoursMinutes', { hours, minutes })
  }

  return t('startupPack.timeRemainingMinutes', { minutes })
}
</script>

<template>
  <div class="onboarding-view">
    <div class="onboarding-container container">
      <div v-if="step < 5" class="onboarding-header">
        <h1>{{ t('onboarding.title') }}</h1>
        <p class="subtitle">{{ t('onboarding.subtitle') }}</p>
      </div>

      <div v-if="step < 5" class="progress-bar">
        <div class="progress-segment" :class="{ active: step >= 1, done: step > 1 }">
          <div class="progress-step"><span v-if="step > 1" class="check-icon">✓</span><span v-else>1</span></div>
          <span class="progress-label">{{ t('onboarding.step1Title') }}</span>
        </div>
        <div class="progress-line" :class="{ active: step > 1 }"></div>
        <div class="progress-segment" :class="{ active: step >= 2, done: step > 2 }">
          <div class="progress-step"><span v-if="step > 2" class="check-icon">✓</span><span v-else>2</span></div>
          <span class="progress-label">{{ t('onboarding.step2Title') }}</span>
        </div>
        <div class="progress-line" :class="{ active: step > 2 }"></div>
        <div class="progress-segment" :class="{ active: step >= 3, done: step > 3 }">
          <div class="progress-step"><span v-if="step > 3" class="check-icon">✓</span><span v-else>3</span></div>
          <span class="progress-label">{{ t('onboarding.step3Title') }}</span>
        </div>
        <div class="progress-line" :class="{ active: step > 3 }"></div>
        <div class="progress-segment" :class="{ active: step >= 4 }">
          <div class="progress-step">4</div>
          <span class="progress-label">{{ t('onboarding.step4Title') }}</span>
        </div>
      </div>

      <div v-if="error" class="error-message" role="alert">{{ error }}</div>

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
              <span v-for="(resource, index) in city.resources" :key="index" class="resource-badge">
                {{ getCityResourceName(city, index) }}
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

      <div v-if="step === 3" class="step-content step-content-wide">
        <div class="step-header">
          <h2>{{ t('onboarding.step3Title') }}</h2>
          <p class="step-desc">{{ t('onboarding.step3Desc') }}</p>
        </div>

        <div class="budget-grid">
          <article class="budget-card">
            <span class="budget-label">{{ t('onboarding.startingCash') }}</span>
            <strong>${{ formatCurrency(STARTING_CASH) }}</strong>
          </article>
          <article class="budget-card" :class="{ warning: !!selectedFactoryLot && STARTING_CASH < selectedFactoryLot.price }">
            <span class="budget-label">{{ t('onboarding.cashAfterPurchase') }}</span>
            <strong>${{ formatCurrency(Math.max(STARTING_CASH - (selectedFactoryLot?.price ?? 0), 0)) }}</strong>
          </article>
        </div>

        <div class="form-group compact">
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

        <div class="guidance-panel">
          <h3>{{ t('onboarding.factoryGuideTitle') }}</h3>
          <p>{{ t('onboarding.factoryGuideBody') }}</p>
          <ul>
            <li>{{ t('onboarding.factoryGuideHint1') }}</li>
            <li>{{ t('onboarding.factoryGuideHint2') }}</li>
            <li>{{ t('onboarding.factoryGuideHint3') }}</li>
          </ul>
        </div>

        <p v-if="availableFactoryLots.length === 0" class="empty-state-message">
          {{ t('onboarding.noFactoryLots') }}
        </p>
        <OnboardingLotSelector
          v-else
          v-model:selected-lot-id="selectedFactoryLotId"
          :lots="availableFactoryLots"
          required-building-type="FACTORY"
          :money-available="STARTING_CASH"
          :recommended-lot-ids="recommendedFactoryLotIds"
        />

        <div class="step-actions">
          <button class="btn btn-secondary" :disabled="auth.player?.onboardingCurrentStep === 'SHOP_SELECTION'" @click="prevStep">
            <span class="btn-arrow">←</span>
            {{ t('common.back') }}
          </button>
          <button class="btn btn-primary btn-lg" :disabled="!canProceedStep3 || loading" @click="startOnboardingCompany">
            {{ loading ? t('common.loading') : t('onboarding.purchaseFactory') }}
            <span v-if="!loading" class="btn-arrow">🏭</span>
          </button>
        </div>
      </div>

      <div v-if="step === 4" class="step-content step-content-wide">
        <div class="step-header">
          <h2>{{ t('onboarding.step4Title') }}</h2>
          <p class="step-desc">{{ t('onboarding.step4Desc') }}</p>
        </div>

        <div class="resume-banner" role="status">
          <strong>{{ t('onboarding.factoryPurchasedTitle') }}</strong>
          <span>
            {{
              t('onboarding.factoryPurchasedBody', {
                lot: selectedFactoryLot?.name ?? '',
                cash: '$' + formatCurrency(starterCash),
              })
            }}
          </span>
        </div>

        <div class="budget-grid">
          <article class="budget-card">
            <span class="budget-label">{{ t('onboarding.availableCash') }}</span>
            <strong>${{ formatCurrency(starterCash) }}</strong>
          </article>
          <article class="budget-card" :class="{ warning: !!selectedShopLot && starterCash < selectedShopLot.price }">
            <span class="budget-label">{{ t('onboarding.cashAfterPurchase') }}</span>
            <strong>${{ formatCurrency(Math.max(starterCash - (selectedShopLot?.price ?? 0), 0)) }}</strong>
          </article>
        </div>

        <div class="guidance-panel">
          <h3>{{ t('onboarding.shopGuideTitle') }}</h3>
          <p>{{ t('onboarding.shopGuideBody') }}</p>
        </div>

        <div class="form-group">
          <span class="form-section-title">{{ t('onboarding.selectProduct') }}</span>
          <p class="catalog-note">
            {{ auth.isProSubscriber ? t('onboarding.proCatalogUnlocked') : t('onboarding.proCatalogNote') }}
          </p>
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
                <span v-for="(recipe, index) in prod.recipes" :key="index" class="recipe-item">
                  {{ getRecipeIngredientLabel(prod, index) }}
                </span>
              </div>
            </button>
          </div>
        </div>

        <p v-if="availableShopLots.length === 0" class="empty-state-message">
          {{ t('onboarding.noShopLots') }}
        </p>
        <OnboardingLotSelector
          v-else
          v-model:selected-lot-id="selectedShopLotId"
          :lots="availableShopLots"
          required-building-type="SALES_SHOP"
          :money-available="starterCash"
          :recommended-lot-ids="recommendedShopLotIds"
        />

        <div v-if="canShowStep4Summary" class="summary">
          <div class="summary-header">
            <span class="summary-icon">📋</span>
            <h3>{{ t('onboarding.summary') }}</h3>
          </div>
          <p>
            {{
              t('onboarding.shopSummaryText', {
                company: companyName,
                city: selectedCity?.name ?? '',
                product: selectedProduct ? getProductName(selectedProduct) : '',
              })
            }}
          </p>
          <div class="summary-details">
            <div class="summary-item">
              <span class="summary-label">🏭</span>
              <span>{{ selectedFactoryLot?.name }} — {{ t(`cityMap.districts.${selectedFactoryLot?.district}`) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">🏪</span>
              <span>{{ selectedShopLot?.name }} — {{ t(`cityMap.districts.${selectedShopLot?.district}`) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">📦</span>
              <span>{{ selectedProduct ? getProductName(selectedProduct) : '' }}</span>
            </div>
          </div>
        </div>

        <div class="step-actions">
          <button class="btn btn-primary btn-lg" :disabled="!canProceedStep4 || loading" @click="completeOnboarding">
            {{ loading ? t('common.loading') : t('onboarding.purchaseShop') }}
            <span v-if="!loading" class="btn-arrow">🏪</span>
          </button>
        </div>
      </div>

      <div v-if="step === 5 && completionResult" class="step-content completion-step">
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

        <section v-if="startupPackOffer" class="startup-pack-panel" aria-labelledby="startup-pack-title">
          <div class="startup-pack-header">
            <div>
              <span class="startup-pack-eyebrow">{{ t('startupPack.eyebrow') }}</span>
              <h3 id="startup-pack-title">{{ t('startupPack.title') }}</h3>
            </div>
            <span class="startup-pack-status" :class="{ claimed: claimedStartupPackOffer, expired: expiredStartupPackOffer }">
              {{ t(`startupPack.status.${startupPackOffer.status.toLowerCase()}`) }}
            </span>
          </div>

          <p class="startup-pack-subtitle">
            {{ t('startupPack.subtitle', { amount: formatCurrency(startupPackOffer.companyCashGrant) }) }}
          </p>

          <div v-if="activeStartupPackOffer" class="startup-pack-active">
            <div class="startup-pack-benefits">
              <article class="startup-pack-benefit">
                <span class="benefit-icon">👑</span>
                <div>
                  <strong>{{ t('startupPack.proBenefitTitle') }}</strong>
                  <p>{{ t('startupPack.proBenefitBody', { days: activeStartupPackOffer.proDurationDays }) }}</p>
                </div>
              </article>
              <article class="startup-pack-benefit">
                <span class="benefit-icon">💵</span>
                <div>
                  <strong>{{ t('startupPack.cashBenefitTitle') }}</strong>
                  <p>
                    {{
                      t('startupPack.cashBenefitBody', {
                        amount: formatCurrency(activeStartupPackOffer.companyCashGrant),
                        company: completionResult.company.name,
                      })
                    }}
                  </p>
                </div>
              </article>
            </div>

            <div class="startup-pack-deadline">
              <strong>{{ t('startupPack.deadlineLabel') }}</strong>
              <span>{{ formatTimeRemaining(activeStartupPackOffer.expiresAtUtc) }}</span>
              <span>{{ t('startupPack.deadlineExact', { date: formatDateTime(activeStartupPackOffer.expiresAtUtc) }) }}</span>
            </div>

            <p v-if="offerMessage" class="startup-pack-message" role="status">{{ offerMessage }}</p>
            <p v-if="offerError" class="startup-pack-error" role="alert">{{ offerError }}</p>

            <div class="startup-pack-actions">
              <button class="btn btn-primary btn-lg" :disabled="offerLoading" @click="claimStartupPackOffer">
                {{ offerLoading ? t('common.loading') : t('startupPack.claim') }}
              </button>
              <button class="btn btn-secondary" :disabled="offerLoading" @click="dismissStartupPackOffer">
                {{ t('startupPack.maybeLater') }}
              </button>
            </div>
            <p class="startup-pack-free-path">{{ t('startupPack.freePath') }}</p>
          </div>

          <div v-else-if="claimedStartupPackOffer" class="startup-pack-state success">
            <strong>{{ t('startupPack.claimedTitle') }}</strong>
            <p v-if="auth.player?.proSubscriptionEndsAtUtc">
              {{ t('startupPack.claimedBody', { date: formatDateTime(auth.player.proSubscriptionEndsAtUtc) }) }}
            </p>
            <p v-else>{{ t('startupPack.claimedNoDate') }}</p>
            <p>
              {{
                t('startupPack.cashBenefitBody', {
                  amount: formatCurrency(claimedStartupPackOffer.companyCashGrant),
                  company: completionResult.company.name,
                })
              }}
            </p>
          </div>

          <div v-else class="startup-pack-state muted">
            <strong>{{ t('startupPack.expiredTitle') }}</strong>
            <p>{{ t('startupPack.expiredBody') }}</p>
          </div>
        </section>

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
  max-width: 1120px;
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

.step-content-wide {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.step-header {
  margin-bottom: 0.5rem;
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

.product-image {
  width: 100%;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border-radius: var(--radius-sm);
  background: var(--color-surface-raised);
}

.product-price {
  font-size: 1.125rem;
  font-weight: 700;
  color: var(--color-secondary);
}

.product-craft,
.catalog-note {
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
}

.form-group.compact {
  max-width: 420px;
}

.form-group label,
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
}

.form-group input:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(0, 71, 255, 0.15);
}

.guidance-panel,
.resume-banner,
.summary {
  background: var(--color-surface-raised);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1rem 1.25rem;
}

.guidance-panel h3,
.resume-banner strong,
.summary h3 {
  margin: 0 0 0.5rem;
}

.guidance-panel p,
.guidance-panel ul,
.resume-banner span,
.summary p,
.empty-state-message {
  margin: 0;
  color: var(--color-text-secondary);
}

.guidance-panel ul {
  padding-left: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  margin-top: 0.75rem;
}

.budget-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
}

.budget-card {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding: 1rem;
  border-radius: var(--radius-md);
  background: var(--color-bg);
  border: 1px solid var(--color-border);
}

.budget-card.warning strong {
  color: var(--color-tertiary);
}

.budget-label {
  color: var(--color-text-secondary);
  font-size: 0.8rem;
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

.summary-details {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  margin-top: 0.75rem;
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
  margin-top: 0.5rem;
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

.startup-pack-panel {
  margin-bottom: 1.5rem;
  padding: 1.5rem;
  border: 1px solid rgba(255, 109, 0, 0.35);
  border-radius: var(--radius-lg);
  background: linear-gradient(135deg, rgba(255, 109, 0, 0.12), rgba(0, 71, 255, 0.1));
  text-align: left;
}

.startup-pack-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
}

.startup-pack-eyebrow {
  display: inline-block;
  margin-bottom: 0.375rem;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--color-tertiary);
}

.startup-pack-header h3 {
  margin: 0;
  font-size: 1.35rem;
}

.startup-pack-status {
  padding: 0.375rem 0.75rem;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.startup-pack-status.claimed {
  background: rgba(0, 200, 83, 0.18);
  color: var(--color-secondary);
}

.startup-pack-status.expired {
  background: rgba(248, 113, 113, 0.12);
  color: var(--color-danger);
}

.startup-pack-subtitle {
  margin: 0.75rem 0 1.25rem;
  color: var(--color-text-secondary);
}

.startup-pack-benefits {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
}

.startup-pack-benefit {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 1rem;
  border-radius: var(--radius-md);
  background: rgba(13, 17, 23, 0.32);
  border: 1px solid rgba(48, 54, 61, 0.8);
}

.benefit-icon {
  font-size: 1.5rem;
}

.startup-pack-benefit p,
.startup-pack-state p {
  margin: 0.25rem 0 0;
  color: var(--color-text-secondary);
}

.startup-pack-deadline {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-top: 1.25rem;
  padding: 1rem;
  border-radius: var(--radius-md);
  background: rgba(13, 17, 23, 0.32);
  border: 1px solid rgba(48, 54, 61, 0.8);
}

.startup-pack-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 1rem;
}

.startup-pack-message,
.startup-pack-error {
  margin: 1rem 0 0;
  padding: 0.75rem 1rem;
  border-radius: var(--radius-md);
}

.startup-pack-message {
  background: rgba(0, 200, 83, 0.12);
  color: var(--color-secondary);
}

.startup-pack-error {
  background: rgba(248, 113, 113, 0.12);
  color: var(--color-danger);
}

.startup-pack-free-path {
  margin-top: 0.875rem;
  color: var(--color-text-secondary);
  font-size: 0.875rem;
}

.startup-pack-state {
  padding: 1rem;
  border-radius: var(--radius-md);
}

.startup-pack-state.success {
  background: rgba(0, 200, 83, 0.12);
}

.startup-pack-state.muted {
  background: rgba(255, 255, 255, 0.04);
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

@media (max-width: 640px) {
  .industry-grid,
  .city-grid,
  .product-grid,
  .budget-grid {
    grid-template-columns: 1fr;
  }

  .progress-label {
    display: none;
  }

  .step-content {
    padding: 1.25rem;
  }

  .step-actions,
  .startup-pack-actions {
    flex-direction: column;
  }

  .startup-pack-header {
    flex-direction: column;
  }
}
</style>
