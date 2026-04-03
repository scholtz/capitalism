<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest, GraphQLError } from '@/lib/graphql'
import { trackStartupPackEvent } from '@/lib/startupPackAnalytics'
import { computeSimulatedProfit, trackOnboardingEvent } from '@/lib/onboardingAnalytics'
import {
  getLocalizedProductDescription,
  getLocalizedProductName,
  getLocalizedRecipeIngredientName,
  getLocalizedResourceName,
  getProductImageUrl,
} from '@/lib/catalogPresentation'
import { useTickRefresh } from '@/composables/useTickRefresh'
import {
  canProceedStep3 as checkCanProceedStep3,
  canProceedStep4 as checkCanProceedStep4,
  clampStep,
  getAvailableLots,
  getMaxReachableStep,
  getRecommendedFactoryLotIds,
  getRecommendedShopLotIds,
  keyToStep,
  stepToKey,
} from '@/lib/onboardingHelpers'
import OnboardingLotSelector from '@/components/onboarding/OnboardingLotSelector.vue'
import { useAuthStore } from '@/stores/auth'
import { useTickCountdown } from '@/composables/useTickCountdown'
import type {
  BuildingLot,
  City,
  FirstSaleMission,
  GameState,
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
const milestoneLoading = ref(false)
const milestoneError = ref<string | null>(null)
const milestoneCompleted = ref(false)

// Guest mode state
const isGuestMode = computed(() => !auth.isAuthenticated)
const guestSaveError = ref<string | null>(null)
const guestSaveLoading = ref(false)
const guestAuthMode = ref<'register' | 'login'>('register')
const guestEmail = ref('')
const guestPassword = ref('')
const guestDisplayName = ref('')

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
const gameState = ref<GameState | null>(null)
const firstSaleMission = ref<FirstSaleMission | null>(null)
const firstSaleMissionLoading = ref(false)

const { tickCountdown, startTickCountdown, stopTickCountdown } = useTickCountdown(gameState)

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

const availableFactoryLots = computed(() => getAvailableLots(cityLots.value, 'FACTORY'))
const availableShopLots = computed(() => getAvailableLots(cityLots.value, 'SALES_SHOP'))
const recommendedFactoryLotIds = computed(() => getRecommendedFactoryLotIds(availableFactoryLots.value))
const recommendedShopLotIds = computed(() => getRecommendedShopLotIds(availableShopLots.value))

const canProceedStep1 = computed(() => !!selectedIndustry.value)
const canProceedStep2 = computed(() => !!selectedCityId.value)
const canProceedStep3 = computed(() =>
  checkCanProceedStep3(companyName.value, selectedFactoryLot.value, STARTING_CASH),
)
const canProceedStep4 = computed(() =>
  checkCanProceedStep4(selectedProductId.value, selectedShopLot.value, starterCash.value),
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

/**
 * True when the player has completed the lot flow but has not yet completed
 * the first-sale milestone. In this state the configure-guide step (step 5)
 * should be shown even after a page refresh.
 */
const isResumingConfigureStep = computed(() =>
  !!auth.player?.onboardingCompletedAtUtc
  && !auth.player.onboardingFirstSaleCompletedAtUtc
  && !!auth.player.onboardingShopBuildingId,
)

/** Building ID for the "Configure My Sales Shop" CTA. Works both in-session and after resume. */
const shopBuildingId = computed(() =>
  completionResult.value?.salesShop.id ?? auth.player?.onboardingShopBuildingId ?? null,
)

/** Cash balance to show in the configure-guide panel. Works in-session and after resume. */
const configureGuideCash = computed(() => {
  if (completionResult.value) return completionResult.value.company.cash
  return auth.player?.companies[0]?.cash ?? 0
})

/**
 * The auto-configured public sale price for the guest's shop.
 * The backend sets MinPrice = basePrice × 1.5 for the PUBLIC_SALES unit during FinishOnboarding.
 */
const guestConfiguredShopPrice = computed(() => {
  const base = selectedProduct.value?.basePrice
  if (!base) return null
  return Math.round(base * 1.5 * 100) / 100
})

/** Simulated first-tick profit for the guest completion preview. */
const simulatedProfit = computed(() => {
  if (!selectedProduct.value) return null
  const recipeCost = selectedProduct.value.recipes.reduce((sum, r) => {
    const unitCost = r.resourceType?.basePrice ?? 0
    return sum + unitCost * r.quantity
  }, 0)
  return computeSimulatedProfit(
    selectedProduct.value.basePrice,
    selectedProduct.value.outputQuantity,
    recipeCost > 0 ? recipeCost : undefined,
  )
})

function findResumedShopBasePrice(): number | null {
  const resumedShop = auth.player?.companies
    .flatMap((company) => company.buildings)
    .find((building) => building.id === shopBuildingId.value)
  const publicSalesUnit = resumedShop?.units.find((unit) => unit.unitType === 'PUBLIC_SALES')
  return publicSalesUnit?.minPrice ?? null
}

const configureGuideBasePrice = computed(() => {
  if (completionResult.value?.selectedProduct.basePrice) {
    return completionResult.value.selectedProduct.basePrice
  }

  if (selectedProduct.value?.basePrice) {
    return selectedProduct.value.basePrice
  }

  return findResumedShopBasePrice()
})

/** Unit type icon mapping for the factory layout display. */
const unitTypeIcons: Record<string, string> = {
  PURCHASE: '🛒',
  MANUFACTURING: '⚙️',
  STORAGE: '📦',
  B2B_SALES: '🔗',
  PUBLIC_SALES: '🏷️',
  MINING: '⛏️',
  BRANDING: '🎨',
  MARKETING: '📣',
}

/**
 * Returns the factory units from completionResult sorted by gridX position,
 * ready for display as a production chain.
 */
const completionFactoryUnits = computed(() => {
  const units = completionResult.value?.factory?.units
  if (!units || units.length === 0) return null
  return [...units].sort((a, b) => a.gridX - b.gridX || a.gridY - b.gridY)
})

/**
 * Returns the sales shop units from completionResult sorted by gridX position.
 */
const completionShopUnits = computed(() => {
  const units = completionResult.value?.salesShop?.units
  if (!units || units.length === 0) return null
  return [...units].sort((a, b) => a.gridX - b.gridX || a.gridY - b.gridY)
})

/**
 * Returns a static guest factory layout showing what will be configured on save.
 * Matches the ConfigureStarterFactory backend output: PURCHASE → MANUFACTURING → STORAGE → B2B_SALES.
 */
const guestFactoryLayout = computed(() => {
  if (!isGuestMode.value || step.value !== 5) return null
  return [
    { unitType: 'PURCHASE', gridX: 0 },
    { unitType: 'MANUFACTURING', gridX: 1 },
    { unitType: 'STORAGE', gridX: 2 },
    { unitType: 'B2B_SALES', gridX: 3 },
  ]
})

/**
 * Returns a static guest shop layout showing what will be configured on save.
 * Matches the AddStarterShop backend output: PURCHASE → PUBLIC_SALES.
 */
const guestShopLayout = computed(() => {
  if (!isGuestMode.value || step.value !== 5) return null
  return [
    { unitType: 'PURCHASE', gridX: 0 },
    { unitType: 'PUBLIC_SALES', gridX: 1 },
  ]
})

const industryIcons: Record<string, string> = {
  FURNITURE: '🪑',
  FOOD_PROCESSING: '🍞',
  HEALTHCARE: '💊',
}

/** Maps each starter industry to its i18n description key. */
const industryDescKeys: Record<string, string> = {
  FURNITURE: 'onboarding.industryDescFurniture',
  FOOD_PROCESSING: 'onboarding.industryDescFoodProcessing',
  HEALTHCARE: 'onboarding.industryDescHealthcare',
}

/** Maps each starter industry to its i18n first-product hint key. */
const industryFirstProductKeys: Record<string, string> = {
  FURNITURE: 'onboarding.industryFirstProductFurniture',
  FOOD_PROCESSING: 'onboarding.industryFirstProductFoodProcessing',
  HEALTHCARE: 'onboarding.industryFirstProductHealthcare',
}

/** Maps each starter industry to its i18n "why choose" tag key. */
const industryWhyKeys: Record<string, string> = {
  FURNITURE: 'onboarding.industryWhyFurniture',
  FOOD_PROCESSING: 'onboarding.industryWhyFoodProcessing',
  HEALTHCARE: 'onboarding.industryWhyHealthcare',
}

function resolveMaxReachableStep(): number {
  // In guest mode, if factory lot is selected (simulated purchase complete), step 4 is reachable.
  // If shop lot AND product are also selected (guest completed step 4), step 5 is reachable.
  if (isGuestMode.value) {
    const guestCompletedAllSteps =
      !!selectedFactoryLotId.value
      && onboardingCompanyCash.value !== null
      && !!selectedShopLotId.value
      && !!selectedProductId.value
    if (guestCompletedAllSteps) return 5
    if (selectedFactoryLotId.value && onboardingCompanyCash.value !== null) return 4
    return getMaxReachableStep({
      hasCompletionResult: false,
      isResumingConfigureStep: false,
      onboardingCurrentStep: null,
      selectedCityId: selectedCityId.value,
      selectedIndustry: selectedIndustry.value,
    })
  }
  return getMaxReachableStep({
    hasCompletionResult: !!completionResult.value,
    isResumingConfigureStep: isResumingConfigureStep.value,
    onboardingCurrentStep: auth.player?.onboardingCurrentStep,
    selectedCityId: selectedCityId.value,
    selectedIndustry: selectedIndustry.value,
  })
}

function resolveClampStep(requestedStep: number): number {
  return clampStep(requestedStep, resolveMaxReachableStep())
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
        guestCash: isGuestMode.value ? (onboardingCompanyCash.value ?? undefined) : undefined,
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
    if (typeof saved.guestCash === 'number') onboardingCompanyCash.value = saved.guestCash
    if (typeof saved.step === 'number') step.value = resolveClampStep(saved.step)
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
  // Track onboarding start event
  trackOnboardingEvent('onboarding_start', { authenticated: auth.isAuthenticated })

  // Authenticated users: check onboarding completion state first
  if (auth.isAuthenticated) {
    if (!auth.player) {
      await auth.fetchMe()
    }

    if (auth.player?.onboardingFirstSaleCompletedAtUtc) {
      // Fully done with onboarding — go straight to dashboard
      router.push('/dashboard')
      return
    }

    if (auth.player?.onboardingCompletedAtUtc && !auth.player.onboardingShopBuildingId) {
      // Legacy players who completed before shop-building tracking was added,
      // or players whose milestone shop is gone
      router.push('/dashboard')
      return
    }

    if (isResumingConfigureStep.value) {
      // Player completed the lot flow but hasn't finished the configure-guide step.
      // Resume at step 5 with the guidance panel visible.
      await Promise.all([loadGameState(), loadFirstSaleMission()])
      step.value = 5
      return
    }
  }

  // Load public data (works for both guests and authenticated users)
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

    if (auth.isAuthenticated) {
      await syncOngoingOnboardingState()
    }

    if (selectedIndustry.value) {
      await loadProducts()
    }

    if (selectedCityId.value) {
      await loadLots()
    }

    step.value = resolveClampStep(keyToStep(route.query.step))
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load data'
  } finally {
    loading.value = false
  }
})

async function nextStep() {
  error.value = null

  if (step.value === 1 && canProceedStep1.value) {
    trackOnboardingEvent('industry_selected', { industry: selectedIndustry.value })
    await loadProducts()
    step.value = 2
    return
  }

  if (step.value === 2 && canProceedStep2.value) {
    trackOnboardingEvent('city_selected', { cityId: selectedCityId.value })
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

  // Guest mode: simulate factory purchase locally without backend call
  if (isGuestMode.value) {
    onboardingCompanyCash.value = STARTING_CASH - selectedFactoryLot.value.price
    trackOnboardingEvent('factory_configured', {
      guest: true,
      lotId: selectedFactoryLotId.value,
      industry: selectedIndustry.value,
      cityId: selectedCityId.value,
    })
    await loadLots()
    step.value = 4
    return
  }

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
    trackOnboardingEvent('factory_configured', {
      guest: false,
      lotId: selectedFactoryLotId.value,
      industry: selectedIndustry.value,
      cityId: selectedCityId.value,
    })
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
  // Guest mode: simulate shop purchase locally, then show save-progress prompt
  if (isGuestMode.value) {
    clearProgress()
    trackOnboardingEvent('shop_configured', {
      guest: true,
      lotId: selectedShopLotId.value,
      productId: selectedProductId.value,
      industry: selectedIndustry.value,
    })
    trackOnboardingEvent('save_prompt_shown', { guest: true })
    if (simulatedProfit.value) {
      trackOnboardingEvent('first_profit_shown', {
        guest: true,
        revenue: simulatedProfit.value.revenue,
        cost: simulatedProfit.value.cost,
        profit: simulatedProfit.value.profit,
        productId: selectedProductId.value,
      })
    }
    step.value = 5
    await loadGameState()
    return
  }

  loading.value = true
  error.value = null

  try {
    const result = await gqlRequest<{ finishOnboarding: OnboardingResult }>(
      `mutation FinishOnboarding($input: FinishOnboardingInput!) {
        finishOnboarding(input: $input) {
          company { id name cash }
          factory { id name type units { id unitType gridX gridY level linkRight } }
          salesShop { id name type units { id unitType gridX gridY level linkRight } }
          selectedProduct { name industry basePrice }
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
    trackOnboardingEvent('shop_configured', {
      guest: false,
      productId: selectedProductId.value,
      industry: selectedIndustry.value,
    })
    trackOnboardingEvent('completed', { guest: false })
    await auth.fetchMe()
    startupPackOffer.value = auth.startupPackOffer ?? startupPackOffer.value
    if (startupPackOffer.value?.status === 'ELIGIBLE') {
      await markStartupPackOfferShown()
    } else if (startupPackOffer.value) {
      trackStartupPackEvent('view', { context: 'onboarding', status: startupPackOffer.value.status })
    }
    step.value = 5
    await Promise.all([loadGameState(), loadFirstSaleMission()])
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

/**
 * Guest mode: register or login, then migrate saved choices to the real backend.
 * If the lot was taken in the meantime, restart wizard from step 1 with auth.
 */
async function saveGuestProgress() {
  guestSaveError.value = null

  // Client-side validation: password must be at least 8 characters for registration
  if (guestAuthMode.value === 'register' && guestPassword.value.length < 8) {
    guestSaveError.value = t('auth.passwordTooShort')
    return
  }

  guestSaveLoading.value = true

  try {
    if (guestAuthMode.value === 'register') {
      await auth.register(guestEmail.value, guestDisplayName.value || companyName.value, guestPassword.value)
    } else {
      await auth.login(guestEmail.value, guestPassword.value)
    }

    // Now authenticated — check if this player already completed onboarding
    if (!auth.player) {
      await auth.fetchMe()
    }

    if (auth.player?.onboardingFirstSaleCompletedAtUtc) {
      router.push('/dashboard')
      return
    }

    if (auth.player?.onboardingCompletedAtUtc) {
      router.push('/dashboard')
      return
    }

    // Try to run the real mutations with the saved guest choices
    if (
      selectedIndustry.value
      && selectedCityId.value
      && companyName.value
      && selectedFactoryLotId.value
      && selectedProductId.value
      && selectedShopLotId.value
    ) {
      try {
        loading.value = true
        error.value = null

        const startResult = await gqlRequest<{ startOnboardingCompany: OnboardingStartResult }>(
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

        onboardingCompanyCash.value = startResult.startOnboardingCompany.company.cash
        selectedFactoryLotId.value = startResult.startOnboardingCompany.factoryLot.id
        await auth.fetchMe()

        const finishResult = await gqlRequest<{ finishOnboarding: OnboardingResult }>(
          `mutation FinishOnboarding($input: FinishOnboardingInput!) {
            finishOnboarding(input: $input) {
              company { id name cash }
              factory { id name type units { id unitType gridX gridY level linkRight } }
              salesShop { id name type units { id unitType gridX gridY level linkRight } }
              selectedProduct { name industry basePrice }
              startupPackOffer {
                id offerKey status createdAtUtc expiresAtUtc shownAtUtc
                dismissedAtUtc claimedAtUtc companyCashGrant proDurationDays grantedCompanyId
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

        completionResult.value = finishResult.finishOnboarding
        startupPackOffer.value = finishResult.finishOnboarding.startupPackOffer
        auth.setStartupPackOffer(finishResult.finishOnboarding.startupPackOffer)
        onboardingCompanyCash.value = finishResult.finishOnboarding.company.cash
        await auth.fetchMe()
        startupPackOffer.value = auth.startupPackOffer ?? startupPackOffer.value
        if (startupPackOffer.value?.status === 'ELIGIBLE') {
          await markStartupPackOfferShown()
        }
        // Guest progress has been successfully persisted to the backend — clear local state
        // so stale sandbox choices don't persist in localStorage or confuse future sessions.
        clearProgress()
        trackOnboardingEvent('onboarding_converted', {
          industry: selectedIndustry.value,
          cityId: selectedCityId.value,
          authMode: guestAuthMode.value,
        })
      } catch (migrationErr: unknown) {
        const code = migrationErr instanceof GraphQLError ? migrationErr.code : undefined
        if (code === 'LOT_ALREADY_OWNED') {
          // A lot was taken between the guest simulation and the real purchase — restart
          // wizard from step 1 so the player can pick fresh lots.
          clearProgress()
          onboardingCompanyCash.value = null
          completionResult.value = null
          selectedFactoryLotId.value = ''
          selectedShopLotId.value = ''
          await loadLots()
          step.value = 1
          error.value = t('onboarding.guestMigrationRetry')
        } else {
          // Any other backend failure (network outage, validation error, auth mismatch,
          // duplicate submit, etc.) must be shown explicitly — NOT masked as a lot-conflict.
          error.value =
            migrationErr instanceof Error
              ? migrationErr.message
              : t('onboarding.guestMigrationGenericError')
        }
      } finally {
        loading.value = false
      }
    } else {
      // Not enough guest data — restart from step 1 with auth
      clearProgress()
      step.value = 1
    }
  } catch (e: unknown) {
    guestSaveError.value = e instanceof Error ? e.message : t('auth.loginFailed')
  } finally {
    guestSaveLoading.value = false
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

function formatNumber(value: number): string {
  return value.toLocaleString(locale.value)
}

/** Returns the translated label for a building unit type, falling back to the raw string. */
function getUnitTypeLabel(unitType: string): string {
  const key = `buildingDetail.unitTypes.${unitType}`
  const translated = t(key)
  return translated === key ? unitType : translated
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

async function markMilestoneComplete() {
  milestoneError.value = null
  milestoneLoading.value = true
  try {
    await gqlRequest<{ completeFirstSaleMilestone: { onboardingFirstSaleCompletedAtUtc: string } }>(
      `mutation {
        completeFirstSaleMilestone {
          onboardingFirstSaleCompletedAtUtc
        }
      }`,
    )
    await auth.fetchMe()
    milestoneCompleted.value = true
  } catch (e: unknown) {
    if (e instanceof GraphQLError && e.code === 'FIRST_SALE_NOT_RECORDED') {
      milestoneError.value = t('onboarding.milestoneErrorFirstSaleNotRecorded')
    } else {
      milestoneError.value = (e instanceof Error ? e.message : '') || t('onboarding.milestoneError')
    }
  } finally {
    milestoneLoading.value = false
  }
}

const FIRST_SALE_MISSION_QUERY = `
  {
    firstSaleMission {
      phase
      shopBuildingId
      shopName
      blockers
      firstSaleRevenue
      firstSaleProductName
      firstSaleTick
      firstSaleQuantity
      firstSalePricePerUnit
    }
  }
`

async function loadFirstSaleMission() {
  if (!auth.isAuthenticated || milestoneCompleted.value) return
  firstSaleMissionLoading.value = true
  try {
    const data = await gqlRequest<{ firstSaleMission: FirstSaleMission }>(FIRST_SALE_MISSION_QUERY)
    firstSaleMission.value = data.firstSaleMission

    // Auto-complete the milestone when the simulation has recorded a real first sale
    if (
      data.firstSaleMission.phase === 'FIRST_SALE_RECORDED'
      && !milestoneCompleted.value
      && !milestoneLoading.value
    ) {
      await markMilestoneComplete()
    }
  } catch (err) {
    // Best-effort polling — log for debugging but don't surface to user
    console.error('[firstSaleMission] Failed to load mission status:', err)
  } finally {
    firstSaleMissionLoading.value = false
  }
}

/** Translates a blocker code into a human-readable explanation. */
function blockerMessage(code: string): string {
  const map: Record<string, string> = {
    BUILDING_UNDER_CONSTRUCTION: t('onboarding.missionBlockerUnderConstruction'),
    PUBLIC_SALES_UNIT_MISSING: t('onboarding.missionBlockerNoPublicSalesUnit'),
    PRICE_NOT_SET: t('onboarding.missionBlockerPriceNotSet'),
    NO_INVENTORY: t('onboarding.missionBlockerNoInventory'),
  }
  return map[code] ?? t('onboarding.missionBlockerUnknown', { code })
}

function navigateToDashboard() {
  stopTickCountdown()
  router.push('/dashboard')
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

async function loadGameState() {
  try {
    const data = await gqlRequest<{ gameState: GameState }>(
      '{ gameState { currentTick lastTickAtUtc tickIntervalSeconds taxRate } }',
    )
    gameState.value = data.gameState
    startTickCountdown()
  } catch {
    // ignore — tick countdown is best-effort
  }
}

onUnmounted(() => {
  stopTickCountdown()
})

useTickRefresh(async () => {
  if (step.value !== 5) {
    return
  }

  await loadGameState()

  // Poll first-sale mission for authenticated players who are still in the configure step
  if (auth.isAuthenticated && !milestoneCompleted.value) {
    await loadFirstSaleMission()
  }
})
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
            <span class="card-first-product">{{ t(industryFirstProductKeys[ind] || '') }}</span>
            <span class="card-desc">{{ t(industryDescKeys[ind] || '') }}</span>
            <span class="card-why">{{ t(industryWhyKeys[ind] || '') }}</span>
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

      <div v-if="step === 5 && (completionResult || isResumingConfigureStep || isGuestMode || milestoneCompleted)" class="step-content completion-step">
        <div class="completion-hero">
          <h2 class="completion-title">{{ t(isGuestMode ? 'onboarding.guestCompletionTitle' : 'onboarding.completionTitle') }}</h2>
          <p class="completion-desc">{{ t(isGuestMode ? 'onboarding.guestCompletionDesc' : 'onboarding.completionDesc') }}</p>
        </div>

        <!-- Guest mode: show simulated achievements and save-progress form -->
        <div v-if="isGuestMode" class="completion-achievements">
          <div class="achievement-item">
            <span class="achievement-icon">🏭</span>
            <div class="achievement-text">
              <strong>{{ companyName || t('onboarding.guestCompanyPlaceholder') }}</strong>
              <span>{{ t('onboarding.completionFactory') }}</span>
            </div>
          </div>
          <div class="achievement-item">
            <span class="achievement-icon">🏪</span>
            <div class="achievement-text">
              <strong>{{ selectedShopLot?.name || t('onboarding.guestShopPlaceholder') }}</strong>
              <span>{{ t('onboarding.completionShop') }}</span>
            </div>
          </div>
          <div v-if="selectedProduct" class="achievement-item">
            <span class="achievement-icon">📦</span>
            <div class="achievement-text">
              <strong>{{ getProductName(selectedProduct) }}</strong>
              <span>{{ t('onboarding.completionProduct', { product: getProductName(selectedProduct) }) }}</span>
            </div>
          </div>
          <div class="achievement-item">
            <span class="achievement-icon">💰</span>
            <div class="achievement-text">
              <strong>${{ formatCurrency(onboardingCompanyCash ?? STARTING_CASH) }}</strong>
              <span>{{ t('onboarding.completionCapital', { amount: '$' + formatCurrency(onboardingCompanyCash ?? STARTING_CASH) }) }}</span>
            </div>
          </div>
        </div>

        <!-- Guest factory layout preview (ROADMAP: "Wizard will show them the factory layout") -->
        <div v-if="isGuestMode && guestFactoryLayout" class="factory-layout-panel" aria-label="Factory layout">
          <h3>{{ t('onboarding.factoryLayoutTitle') }}</h3>
          <p>{{ t('onboarding.factoryLayoutGuestDesc') }}</p>
          <div class="unit-chain">
            <template v-for="(unit, index) in guestFactoryLayout" :key="unit.unitType">
              <div class="unit-chain-step">
                <span class="unit-chain-icon">{{ unitTypeIcons[unit.unitType] ?? '▪️' }}</span>
                <span class="unit-chain-label">{{ getUnitTypeLabel(unit.unitType) }}</span>
              </div>
              <span v-if="index < guestFactoryLayout.length - 1" class="unit-chain-arrow" aria-hidden="true">→</span>
            </template>
          </div>
        </div>

        <!-- Guest shop layout preview -->
        <div v-if="isGuestMode && guestShopLayout" class="factory-layout-panel factory-layout-shop" aria-label="Sales shop layout">
          <h3>{{ t('onboarding.shopLayoutTitle') }}</h3>
          <p>{{ t('onboarding.shopLayoutGuestDesc') }}</p>
          <div class="unit-chain">
            <template v-for="(unit, index) in guestShopLayout" :key="unit.unitType">
              <div class="unit-chain-step">
                <span class="unit-chain-icon">{{ unitTypeIcons[unit.unitType] ?? '▪️' }}</span>
                <span class="unit-chain-label">{{ getUnitTypeLabel(unit.unitType) }}</span>
              </div>
              <span v-if="index < guestShopLayout.length - 1" class="unit-chain-arrow" aria-hidden="true">→</span>
            </template>
          </div>
        </div>

        <!-- Guest mode tick countdown -->
        <div v-if="isGuestMode && gameState" class="guest-tick-panel">
          <span class="configure-step-icon">⏱</span>
          <div>
            <strong>{{ t('onboarding.configureStepTick') }}</strong>
            <p>{{ t('onboarding.configureStepTickDesc') }}</p>
            <p class="tick-status">{{ t('onboarding.configureStepTickStatus', { tick: formatNumber(gameState.currentTick) }) }}</p>
            <p v-if="tickCountdown" class="tick-countdown" role="timer">{{ tickCountdown }}</p>
          </div>
        </div>

        <!-- Guest simulated profit preview -->
        <div v-if="isGuestMode && simulatedProfit" class="guest-profit-preview" role="region" aria-label="Simulated first profit">
          <div class="profit-preview-header">
            <span class="profit-preview-icon">📈</span>
            <div>
              <strong>{{ t('onboarding.guestProfitPreviewTitle') }}</strong>
              <p>{{ t('onboarding.guestProfitPreviewDesc') }}</p>
            </div>
          </div>
          <div class="profit-preview-stats">
            <div class="profit-stat">
              <span class="profit-stat-label">{{ t('onboarding.guestProfitRevenue') }}</span>
              <span class="profit-stat-value profit-stat-revenue">${{ formatCurrency(simulatedProfit.revenue) }}</span>
            </div>
            <div class="profit-stat">
              <span class="profit-stat-label">{{ t('onboarding.guestProfitCost') }}</span>
              <span class="profit-stat-value profit-stat-cost">-${{ formatCurrency(simulatedProfit.cost) }}</span>
            </div>
            <div class="profit-stat profit-stat-net">
              <span class="profit-stat-label">{{ t('onboarding.guestProfitNet') }}</span>
              <span class="profit-stat-value" :class="simulatedProfit.profit >= 0 ? 'profit-stat-positive' : 'profit-stat-negative'">
                {{ simulatedProfit.profit >= 0 ? '+' : '' }}${{ formatCurrency(simulatedProfit.profit) }}
              </span>
            </div>
          </div>
        </div>

        <!-- Guest pricing explanation -->
        <div v-if="isGuestMode && selectedProduct && guestConfiguredShopPrice" class="guest-price-panel" role="region" aria-label="Configured sale price">
          <div class="price-panel-header">
            <span class="price-panel-icon">💲</span>
            <div>
              <strong>{{ t('onboarding.guestPriceTitle') }}</strong>
              <p>{{ t('onboarding.guestPriceDesc', { product: getProductName(selectedProduct), price: '$' + formatCurrency(guestConfiguredShopPrice), basePrice: '$' + formatCurrency(selectedProduct.basePrice) }) }}</p>
            </div>
          </div>
          <p class="price-panel-tip">{{ t('onboarding.guestPriceTip') }}</p>
        </div>

        <!-- Guest save-progress section -->
        <section v-if="isGuestMode" class="guest-save-progress" aria-labelledby="guest-save-title">
          <div class="guest-save-header">
            <span class="guest-save-icon">🔐</span>
            <div>
              <h3 id="guest-save-title">{{ t('onboarding.guestSaveTitle') }}</h3>
              <p class="guest-save-subtitle">{{ t('onboarding.guestSaveSubtitle') }}</p>
            </div>
          </div>

          <div class="guest-auth-toggle">
            <button
              class="btn-tab"
              :class="{ active: guestAuthMode === 'register' }"
              @click="guestAuthMode = 'register'"
            >{{ t('onboarding.guestRegister') }}</button>
            <button
              class="btn-tab"
              :class="{ active: guestAuthMode === 'login' }"
              @click="guestAuthMode = 'login'"
            >{{ t('onboarding.guestLogin') }}</button>
          </div>

          <div class="guest-auth-form">
            <div class="form-group compact">
              <label for="guestEmail">{{ t('auth.email') }}</label>
              <input id="guestEmail" v-model="guestEmail" type="email" autocomplete="email" :placeholder="t('auth.emailPlaceholder')" />
            </div>
            <div v-if="guestAuthMode === 'register'" class="form-group compact">
              <label for="guestDisplayName">{{ t('auth.displayName') }}</label>
              <input id="guestDisplayName" v-model="guestDisplayName" type="text" autocomplete="name" :placeholder="companyName || t('auth.displayNamePlaceholder')" />
            </div>
            <div class="form-group compact">
              <label for="guestPassword">{{ t('auth.password') }}</label>
              <input id="guestPassword" v-model="guestPassword" type="password" autocomplete="current-password" :placeholder="t('auth.passwordPlaceholder')" />
            </div>

            <p v-if="guestSaveError" class="error-inline" role="alert">{{ guestSaveError }}</p>

            <button class="btn btn-primary btn-lg btn-full" :disabled="guestSaveLoading" @click="saveGuestProgress">
              {{ guestSaveLoading ? t('common.loading') : t('onboarding.guestSaveCta') }}
              <span v-if="!guestSaveLoading" class="btn-arrow">💾</span>
            </button>
          </div>
        </section>

        <div v-if="completionResult" class="completion-achievements">
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
              <span>{{ t('onboarding.completionCapital', { amount: '$' + completionResult.company.cash.toLocaleString() }) }}</span>
            </div>
          </div>
        </div>

        <!-- Authenticated factory layout display (ROADMAP: "Wizard will show them the factory layout") -->
        <div v-if="completionFactoryUnits" class="factory-layout-panel" aria-label="Factory layout">
          <h3>{{ t('onboarding.factoryLayoutTitle') }}</h3>
          <p>{{ t('onboarding.factoryLayoutDesc') }}</p>
          <div class="unit-chain">
            <template v-for="(unit, index) in completionFactoryUnits" :key="unit.id">
              <div class="unit-chain-step">
                <span class="unit-chain-icon">{{ unitTypeIcons[unit.unitType] ?? '▪️' }}</span>
                <span class="unit-chain-label">{{ getUnitTypeLabel(unit.unitType) }}</span>
              </div>
              <span v-if="index < completionFactoryUnits.length - 1" class="unit-chain-arrow" aria-hidden="true">→</span>
            </template>
          </div>
        </div>

        <!-- Authenticated shop layout display -->
        <div v-if="completionShopUnits" class="factory-layout-panel factory-layout-shop" aria-label="Sales shop layout">
          <h3>{{ t('onboarding.shopLayoutTitle') }}</h3>
          <p>{{ t('onboarding.shopLayoutDesc') }}</p>
          <div class="unit-chain">
            <template v-for="(unit, index) in completionShopUnits" :key="unit.id">
              <div class="unit-chain-step">
                <span class="unit-chain-icon">{{ unitTypeIcons[unit.unitType] ?? '▪️' }}</span>
                <span class="unit-chain-label">{{ getUnitTypeLabel(unit.unitType) }}</span>
              </div>
              <span v-if="index < completionShopUnits.length - 1" class="unit-chain-arrow" aria-hidden="true">→</span>
            </template>
          </div>
        </div>

        <section v-if="startupPackOffer" class="startup-pack-panel" aria-labelledby="startup-pack-title">
          <div class="startup-pack-header">
            <div>
              <span class="startup-pack-eyebrow">{{ t('startupPack.eyebrow') }}</span>
              <h3 id="startup-pack-title">{{ t('startupPack.title') }}</h3>
            </div>
            <div class="startup-pack-header-right">
              <span class="startup-pack-price">{{ t('startupPack.price') }}</span>
              <span class="startup-pack-pro-monthly">{{ t('startupPack.proMonthlyPrice') }}</span>
              <span class="startup-pack-status" :class="{ claimed: claimedStartupPackOffer, expired: expiredStartupPackOffer }">
                {{ t(`startupPack.status.${startupPackOffer.status.toLowerCase()}`) }}
              </span>
            </div>
          </div>

          <p class="startup-pack-subtitle">
            {{ t('startupPack.subtitle', { amount: '$' + formatCurrency(startupPackOffer.companyCashGrant) }) }}
          </p>
          <p v-if="activeStartupPackOffer" class="startup-pack-savings">{{ t('startupPack.proSavings') }}</p>

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
                        amount: '$' + formatCurrency(activeStartupPackOffer.companyCashGrant),
                        company: completionResult?.company.name ?? '',
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
                  amount: '$' + formatCurrency(claimedStartupPackOffer.companyCashGrant),
                  company: completionResult?.company.name ?? '',
                })
              }}
            </p>
          </div>

          <div v-else class="startup-pack-state muted">
            <strong>{{ t('startupPack.expiredTitle') }}</strong>
            <p>{{ t('startupPack.expiredBody') }}</p>
          </div>
        </section>

        <section v-if="!isGuestMode && !milestoneCompleted" class="configure-guide" aria-labelledby="configure-guide-title">
          <h3 id="configure-guide-title">{{ t('onboarding.configureShopTitle') }}</h3>
          <p class="configure-guide-desc">{{ t('onboarding.configureShopDesc') }}</p>

          <div class="configure-steps">
            <article class="configure-step">
              <span class="configure-step-icon">💰</span>
              <div class="configure-step-body">
                <strong>{{ t('onboarding.configureStepCash') }}</strong>
                <p>{{ t('onboarding.configureStepCashDesc', { amount: '$' + formatCurrency(configureGuideCash) }) }}</p>
              </div>
            </article>

            <article class="configure-step">
              <span class="configure-step-icon">💲</span>
              <div class="configure-step-body">
                <strong>{{ t('onboarding.configureStepPrice') }}</strong>
                <p>
                  {{
                    configureGuideBasePrice === null
                      ? t('onboarding.configureStepPriceDesc')
                      : t('onboarding.configureStepPriceDescWithPrice', {
                          price: '$' + formatCurrency(configureGuideBasePrice),
                        })
                  }}
                </p>
              </div>
            </article>

            <article class="configure-step">
              <span class="configure-step-icon">🌐</span>
              <div class="configure-step-body">
                <strong>{{ t('onboarding.configureStepPublicSales') }}</strong>
                <p>{{ t('onboarding.configureStepPublicSalesDesc') }}</p>
              </div>
            </article>

            <article class="configure-step">
              <span class="configure-step-icon">⏱</span>
              <div class="configure-step-body">
                <strong>{{ t('onboarding.configureStepTick') }}</strong>
                <p>{{ t('onboarding.configureStepTickDesc') }}</p>
                <ol class="next-tick-process-list">
                  <li>{{ t('onboarding.nextTickProcessStep1') }}</li>
                  <li>{{ t('onboarding.nextTickProcessStep2') }}</li>
                  <li>{{ t('onboarding.nextTickProcessStep3') }}</li>
                </ol>
                <p v-if="gameState" class="tick-status">
                  {{ t('onboarding.configureStepTickStatus', { tick: formatNumber(gameState.currentTick) }) }}
                </p>
                <p v-if="tickCountdown" class="tick-countdown" role="timer">{{ tickCountdown }}</p>
              </div>
            </article>
          </div>

          <div class="configure-cta">
            <RouterLink v-if="shopBuildingId" :to="'/building/' + shopBuildingId" class="btn btn-primary btn-lg">
              {{ t('onboarding.configureShopCta') }}
              <span class="btn-arrow">🏪</span>
            </RouterLink>
          </div>

          <!-- Mission readiness status: shows blockers or "awaiting first sale" state -->
          <div
            v-if="firstSaleMission"
            class="mission-status"
            :class="{
              'mission-status--blocking': firstSaleMission.phase === 'CONFIGURE_SHOP',
              'mission-status--awaiting': firstSaleMission.phase === 'AWAITING_FIRST_SALE',
            }"
            role="status"
            aria-label="Business readiness"
          >
            <div class="mission-status-header">
              <span class="mission-status-icon">
                {{ firstSaleMission.phase === 'AWAITING_FIRST_SALE' ? '⏳' : '⚠️' }}
              </span>
              <strong>{{ t('onboarding.missionStatusTitle') }}</strong>
              <span
                class="mission-phase-badge"
                :class="{
                  'badge-configure': firstSaleMission.phase === 'CONFIGURE_SHOP',
                  'badge-awaiting': firstSaleMission.phase === 'AWAITING_FIRST_SALE',
                }"
              >
                {{
                  firstSaleMission.phase === 'CONFIGURE_SHOP'
                    ? t('onboarding.missionPhaseConfigureShop')
                    : t('onboarding.missionPhaseAwaiting')
                }}
              </span>
            </div>
            <ul v-if="firstSaleMission.blockers.length > 0" class="mission-blockers">
              <li v-for="blocker in firstSaleMission.blockers" :key="blocker" class="mission-blocker">
                {{ blockerMessage(blocker) }}
              </li>
            </ul>
            <p v-else class="mission-awaiting-desc">
              {{ t('onboarding.configureStepTickDesc') }}
            </p>
          </div>

          <div class="milestone-complete">
            <p class="milestone-complete-hint">{{ t('onboarding.milestoneCompleteHint') }}</p>
            <button
              class="btn btn-secondary"
              :disabled="milestoneLoading"
              @click="markMilestoneComplete"
            >
              {{ milestoneLoading ? t('common.loading') : t('onboarding.milestoneCompleteCta') }}
              <span v-if="!milestoneLoading" class="btn-arrow">✓</span>
            </button>
            <p v-if="milestoneError" class="milestone-error" role="alert">{{ milestoneError }}</p>
          </div>
        </section>

        <!-- Business live panel: shown after marking milestone complete -->
        <section v-if="!isGuestMode && milestoneCompleted" class="business-live-panel" aria-labelledby="business-live-title">
          <div class="business-live-header">
            <span class="business-live-icon">🎉</span>
            <div>
              <h3 id="business-live-title">{{ t('onboarding.businessLiveTitle') }}</h3>
              <p>{{ t('onboarding.businessLiveDesc') }}</p>
            </div>
          </div>

          <!-- First-sale celebration: concrete business feedback -->
          <div
            v-if="firstSaleMission && firstSaleMission.firstSaleRevenue !== null"
            class="first-sale-celebration"
            role="region"
            aria-label="First sale details"
          >
            <h4>{{ t('onboarding.firstSaleCelebrationTitle') }}</h4>
            <p>{{ t('onboarding.firstSaleCelebrationDesc') }}</p>
            <dl class="first-sale-stats">
              <div v-if="firstSaleMission.firstSaleProductName" class="first-sale-stat">
                <dt>{{ t('onboarding.firstSaleCelebrationProduct') }}</dt>
                <dd>{{ firstSaleMission.firstSaleProductName }}</dd>
              </div>
              <div v-if="firstSaleMission.firstSaleRevenue !== null" class="first-sale-stat">
                <dt>{{ t('onboarding.firstSaleCelebrationRevenue') }}</dt>
                <dd class="first-sale-revenue">${{ formatCurrency(firstSaleMission.firstSaleRevenue) }}</dd>
              </div>
              <div v-if="firstSaleMission.firstSaleQuantity !== null" class="first-sale-stat">
                <dt>{{ t('onboarding.firstSaleCelebrationQuantity') }}</dt>
                <dd>{{ firstSaleMission.firstSaleQuantity }}</dd>
              </div>
              <div v-if="firstSaleMission.firstSalePricePerUnit !== null" class="first-sale-stat">
                <dt>{{ t('onboarding.firstSaleCelebrationPrice') }}</dt>
                <dd>${{ formatCurrency(firstSaleMission.firstSalePricePerUnit) }}</dd>
              </div>
              <div v-if="firstSaleMission.firstSaleTick !== null" class="first-sale-stat">
                <dt>{{ t('onboarding.firstSaleCelebrationTick') }}</dt>
                <dd>{{ formatNumber(firstSaleMission.firstSaleTick) }}</dd>
              </div>
            </dl>
          </div>

          <div v-if="gameState" class="business-live-tick">
            <p class="tick-status">{{ t('onboarding.businessLiveTickInfo', { tick: formatNumber(gameState.currentTick) }) }}</p>
            <p v-if="tickCountdown" class="tick-countdown" role="timer">{{ tickCountdown }}</p>
          </div>

          <div class="business-live-process">
            <h4>{{ t('onboarding.nextTickProcessTitle') }}</h4>
            <ol class="next-tick-process-list">
              <li>{{ t('onboarding.nextTickProcessStep1') }}</li>
              <li>{{ t('onboarding.nextTickProcessStep2') }}</li>
              <li>{{ t('onboarding.nextTickProcessStep3') }}</li>
            </ol>
          </div>

          <div class="business-live-actions">
            <button class="btn btn-primary btn-lg" @click="navigateToDashboard">
              {{
                firstSaleMission?.firstSaleRevenue !== null
                  ? t('onboarding.firstSaleCelebrationCta')
                  : t('onboarding.businessLiveDashboardCta')
              }}
              <span class="btn-arrow">→</span>
            </button>
            <RouterLink to="/leaderboard" class="btn btn-secondary">
              {{ t('onboarding.completionViewLeaderboard') }}
            </RouterLink>
          </div>
        </section>

        <div v-if="!isGuestMode && !milestoneCompleted" class="completion-next">
          <h3>{{ t('onboarding.completionNextSteps') }}</h3>
          <div class="completion-actions">
            <RouterLink to="/dashboard" class="btn btn-secondary">
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

.card-first-product {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
  border-radius: var(--radius-sm, 4px);
  padding: 0.2rem 0.5rem;
  display: inline-block;
}

.card-why {
  font-size: 0.6875rem;
  color: var(--color-text-muted, var(--color-text-secondary));
  font-style: italic;
  line-height: 1.3;
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

/* Factory layout panel — ROADMAP: "Wizard will show them the factory layout" */
.factory-layout-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
  padding: 1.25rem 1.5rem;
  margin-bottom: 1.5rem;
  text-align: left;
}

.factory-layout-panel h3 {
  font-size: 1rem;
  font-weight: 600;
  color: var(--color-text-primary);
  margin: 0 0 0.4rem;
}

.factory-layout-panel p {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  margin: 0 0 0.9rem;
}

.factory-layout-shop {
  border-color: rgba(var(--color-success-rgb, 72, 199, 142), 0.4);
}

.unit-chain {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.unit-chain-step {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.2rem;
  background: var(--color-background);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  padding: 0.5rem 0.75rem;
  min-width: 80px;
}

.unit-chain-icon {
  font-size: 1.25rem;
}

.unit-chain-label {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--color-text-primary);
  text-align: center;
}

.unit-chain-arrow {
  font-size: 1rem;
  color: var(--color-text-secondary);
  padding: 0 0.1rem;
  align-self: center;
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

.startup-pack-header-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.375rem;
}

.startup-pack-price {
  font-size: 1.5rem;
  font-weight: 800;
  color: var(--color-primary);
  letter-spacing: -0.02em;
}

.startup-pack-pro-monthly {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.startup-pack-savings {
  margin: 0 0 1rem;
  font-size: 0.8rem;
  color: var(--color-secondary);
  font-weight: 600;
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

.configure-guide {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 12px;
  padding: 1.5rem;
  margin-top: 1.5rem;
}

.configure-guide h3 {
  font-size: 1.2rem;
  margin-bottom: 0.5rem;
  color: var(--color-primary);
}

.configure-guide-desc {
  color: var(--color-text-secondary);
  font-size: 0.95rem;
  margin-bottom: 1.25rem;
}

.configure-steps {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.configure-step {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1rem;
}

.configure-step-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.configure-step-body strong {
  display: block;
  margin-bottom: 0.25rem;
  font-size: 0.95rem;
}

.configure-step-body p {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.configure-step-body p + p {
  margin-top: 0.5rem;
}

.tick-status {
  font-size: 0.85rem !important;
}

.tick-countdown {
  color: var(--color-secondary) !important;
  font-weight: 600;
  font-size: 0.9rem !important;
}

.configure-cta {
  display: flex;
  justify-content: center;
  margin-bottom: 1.5rem;
}

.milestone-complete {
  border-top: 1px solid var(--color-border);
  padding-top: 1.25rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  text-align: center;
}

.milestone-complete-hint {
  font-size: 0.9rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.milestone-error {
  font-size: 0.9rem;
  color: var(--color-error, #ff4757);
  margin: 0;
}

/* Mission readiness status panel */
.mission-status {
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.mission-status--blocking {
  border-color: rgba(255, 149, 0, 0.5);
  background: rgba(255, 149, 0, 0.05);
}

.mission-status--awaiting {
  border-color: rgba(0, 122, 255, 0.4);
  background: rgba(0, 122, 255, 0.04);
}

.mission-status-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.mission-status-icon {
  font-size: 1.1rem;
}

.mission-phase-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.2rem 0.5rem;
  border-radius: 999px;
  margin-left: auto;
}

.badge-configure {
  background: rgba(255, 149, 0, 0.15);
  color: var(--color-warning, #ff9500);
}

.badge-awaiting {
  background: rgba(0, 122, 255, 0.12);
  color: var(--color-primary, #007aff);
}

.mission-blockers {
  margin: 0;
  padding: 0 0 0 1.25rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.mission-blocker {
  color: var(--color-warning, #ff9500);
}

.mission-awaiting-desc {
  margin: 0;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

/* First-sale celebration stats inside business-live-panel */
.first-sale-celebration {
  background: rgba(0, 200, 83, 0.08);
  border: 1px solid rgba(0, 200, 83, 0.3);
  border-radius: 10px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.first-sale-celebration h4 {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--color-text);
}

.first-sale-celebration > p {
  margin: 0;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.first-sale-stats {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem 1rem;
  margin: 0;
}

@media (max-width: 480px) {
  .first-sale-stats {
    grid-template-columns: 1fr;
  }
}

.first-sale-stat {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.first-sale-stat dt {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-weight: 500;
}

.first-sale-stat dd {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--color-text);
}

.first-sale-revenue {
  color: var(--color-success, #00c853);
  font-size: 1.1rem;
}

/* Next-tick process list inside configure-step */
.next-tick-process-list {
  margin: 0.5rem 0 0 1.25rem;
  padding: 0;
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

/* Business live panel: shown after marking milestone complete */
.business-live-panel {
  background: linear-gradient(135deg, rgba(0, 200, 83, 0.08) 0%, rgba(0, 71, 255, 0.04) 100%);
  border: 1px solid rgba(0, 200, 83, 0.4);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.business-live-header {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.business-live-icon {
  font-size: 2rem;
  flex-shrink: 0;
}

.business-live-header h3 {
  margin: 0 0 0.4rem;
  color: var(--color-text);
  font-size: 1.1rem;
}

.business-live-header p {
  margin: 0;
  font-size: 0.9rem;
  color: var(--color-text-secondary);
}

.business-live-tick {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.business-live-process h4 {
  margin: 0 0 0.5rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--color-text);
}

.business-live-actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  flex-wrap: wrap;
}

/* Guest mode styles */
.guest-tick-panel {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1rem;
  font-size: 0.9rem;
  color: var(--color-text-secondary);
}

.guest-tick-panel .configure-step-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.guest-tick-panel strong {
  display: block;
  margin-bottom: 0.25rem;
  color: var(--color-text);
}

/* Guest simulated profit preview */
.guest-profit-preview {
  background: linear-gradient(135deg, rgba(0, 200, 83, 0.08) 0%, rgba(0, 71, 255, 0.04) 100%);
  border: 1px solid rgba(0, 200, 83, 0.4);
  border-radius: 10px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.profit-preview-header {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.profit-preview-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.profit-preview-header strong {
  display: block;
  margin-bottom: 0.25rem;
  color: var(--color-text);
}

.profit-preview-header p {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.profit-preview-stats {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  border-top: 1px solid rgba(0, 200, 83, 0.25);
  padding-top: 0.75rem;
}

.profit-stat {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.9rem;
}

.profit-stat-net {
  border-top: 1px solid rgba(0, 200, 83, 0.25);
  padding-top: 0.5rem;
  font-weight: 600;
}

.profit-stat-label {
  color: var(--color-text-secondary);
}

.profit-stat-value {
  font-weight: 500;
}

.profit-stat-revenue {
  color: var(--color-text);
}

.profit-stat-cost {
  color: var(--color-text-secondary);
}

.profit-stat-positive {
  color: #22c55e;
}

.profit-stat-negative {
  color: var(--color-error, #ff4757);
}

.guest-price-panel {
  background: linear-gradient(135deg, rgba(0, 71, 255, 0.06) 0%, rgba(0, 200, 83, 0.04) 100%);
  border: 1px solid rgba(0, 71, 255, 0.3);
  border-radius: 10px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.price-panel-header {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.price-panel-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.price-panel-header strong {
  display: block;
  margin-bottom: 0.25rem;
  color: var(--color-text);
}

.price-panel-header p {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.price-panel-tip {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  border-top: 1px solid rgba(0, 71, 255, 0.15);
  padding-top: 0.5rem;
  margin: 0;
  font-style: italic;
}

.guest-save-progress {
  background: linear-gradient(135deg, rgba(0, 71, 255, 0.06) 0%, rgba(0, 200, 83, 0.04) 100%);
  border: 2px solid var(--color-primary);
  border-radius: 12px;
  padding: 1.75rem;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.guest-save-header {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.guest-save-icon {
  font-size: 2rem;
  flex-shrink: 0;
}

.guest-save-header h3 {
  margin: 0 0 0.375rem;
  font-size: 1.25rem;
  color: var(--color-primary);
}

.guest-save-subtitle {
  color: var(--color-text-secondary);
  font-size: 0.9rem;
  margin: 0;
}

.guest-auth-toggle {
  display: flex;
  gap: 0;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  overflow: hidden;
  width: fit-content;
}

.btn-tab {
  padding: 0.5rem 1.25rem;
  background: none;
  border: none;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--color-text-secondary);
  transition: all 0.2s ease;
}

.btn-tab.active {
  background: var(--color-primary);
  color: #fff;
}

.guest-auth-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 420px;
}

.btn-full {
  width: 100%;
  justify-content: center;
}

.error-inline {
  color: var(--color-error, #ff4757);
  font-size: 0.875rem;
  margin: 0;
}

@media (max-width: 640px) {
  .industry-grid,
  .city-grid,
  .product-grid,
  .budget-grid,
  .configure-steps {
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
