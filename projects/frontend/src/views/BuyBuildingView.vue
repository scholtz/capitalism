<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import type { City, BuildingLot, Company, PurchaseLotResult } from '@/types'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const companyId = computed(() => route.params.companyId as string)

const loading = ref(false)
const lotsLoading = ref(false)
const error = ref<string | null>(null)
const cities = ref<City[]>([])
const availableLots = ref<BuildingLot[]>([])
const selectedLotId = ref('')
const selectedType = ref('')
const selectedCityId = ref('')
const buildingName = ref('')
const submitting = ref(false)

const buildingTypes = [
  'MINE',
  'FACTORY',
  'SALES_SHOP',
  'RESEARCH_DEVELOPMENT',
  'APARTMENT',
  'COMMERCIAL',
  'MEDIA_HOUSE',
  'BANK',
  'EXCHANGE',
  'POWER_PLANT',
]

const selectedCompany = computed<Company | null>(() => {
  return auth.player?.companies.find((company) => company.id === companyId.value) ?? null
})

const selectedLot = computed<BuildingLot | null>(() => {
  return availableLots.value.find((lot) => lot.id === selectedLotId.value) ?? null
})

const canSubmit = computed(
  () => !!selectedType.value && !!selectedCityId.value && !!selectedLot.value,
)

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  if (!auth.player) {
    await auth.fetchMe()
  }

  loading.value = true
  try {
    const data = await gqlRequest<{ cities: City[] }>(
      '{ cities { id name countryCode population } }',
    )
    cities.value = data.cities

    if (!selectedCompany.value) {
      error.value = t('cityMap.noCompany')
    }
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load cities'
  } finally {
    loading.value = false
  }
})

watch([selectedCityId, selectedType], async ([cityId, buildingType]) => {
  selectedLotId.value = ''
  availableLots.value = []

  if (!cityId || !buildingType) {
    return
  }

  lotsLoading.value = true
  error.value = null

  try {
    const data = await gqlRequest<{ cityLots: BuildingLot[] }>(
      `query BuyBuildingLots($cityId: UUID!) {
        cityLots(cityId: $cityId) {
          id cityId name description district latitude longitude populationIndex basePrice price suitableTypes
          ownerCompanyId buildingId
        }
      }`,
      { cityId },
    )

    availableLots.value = data.cityLots.filter((lot) => {
      const supportedTypes = lot.suitableTypes.split(',').map((item) => item.trim())
      return !lot.ownerCompanyId && supportedTypes.includes(buildingType)
    })
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : t('cityMap.purchaseError')
  } finally {
    lotsLoading.value = false
  }
})

function formatCurrency(value: number) {
  return `$${value.toLocaleString(undefined, { maximumFractionDigits: 0 })}`
}

function formatPopulationIndex(value: number) {
  return value.toFixed(2)
}

function districtLabel(district: string) {
  const key = `cityMap.districts.${district}`
  const translated = t(key)
  return translated === key ? district : translated
}

async function buyBuilding() {
  if (!canSubmit.value || !selectedCompany.value || !selectedLot.value) return

  submitting.value = true
  error.value = null

  try {
    const data = await gqlRequest<{ purchaseLot: PurchaseLotResult }>(
      `mutation PurchaseLot($input: PurchaseLotInput!) {
        purchaseLot(input: $input) {
          building { id name type level }
        }
      }`,
      {
        input: {
          companyId: selectedCompany.value.id,
          lotId: selectedLot.value.id,
          buildingType: selectedType.value,
          buildingName: buildingName.value.trim() || null,
        },
      },
    )

    router.push(`/building/${data.purchaseLot.building.id}`)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : t('cityMap.purchaseError')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="buy-building-view container">
    <div class="page-nav">
      <RouterLink to="/dashboard" class="back-link">
        <span>←</span> {{ t('buildingDetail.backToDashboard') }}
      </RouterLink>
    </div>

    <div class="buy-card">
      <h1>{{ t('buildings.title') }}</h1>

      <div v-if="error" class="error-message" role="alert">{{ error }}</div>

      <div v-if="loading" class="loading">{{ t('common.loading') }}</div>

      <template v-else>
        <div class="form-section">
          <h2>{{ t('buildings.selectType') }}</h2>
          <div class="type-grid">
            <button
              v-for="bType in buildingTypes"
              :key="bType"
              class="type-card"
              :class="{ selected: selectedType === bType }"
              @click="selectedType = bType"
            >
              <span class="type-icon">{{ t(`buildings.typeIcons.${bType}`) }}</span>
              <span class="type-name">{{ t(`buildings.types.${bType}`) }}</span>
              <span class="type-desc">{{ t(`buildings.typeDescriptions.${bType}`) }}</span>
            </button>
          </div>
        </div>

        <div v-if="selectedType" class="form-section">
          <div class="form-row">
            <div class="form-group">
              <label for="buildingName">{{ t('buildings.buildingName') }} <span class="optional-hint">({{ t('common.optional') }})</span></label>
              <input
                id="buildingName"
                v-model="buildingName"
                type="text"
                maxlength="200"
                :placeholder="t('buildings.buildingNamePlaceholder')"
              />
            </div>

            <div class="form-group">
              <label for="city">{{ t('buildings.selectCity') }}</label>
              <div class="city-select-grid">
                <button
                  v-for="city in cities"
                  :key="city.id"
                  class="city-option"
                  :class="{ selected: selectedCityId === city.id }"
                  @click="selectedCityId = city.id"
                >
                  <span class="city-name">{{ city.name }}</span>
                  <span class="city-country">{{ city.countryCode }}</span>
                </button>
              </div>
            </div>
          </div>

          <div v-if="selectedCompany" class="company-banner">
            <span>{{ selectedCompany.name }}</span>
            <strong>{{ formatCurrency(selectedCompany.cash) }}</strong>
          </div>
        </div>

        <div v-if="selectedType && selectedCityId" class="form-section">
          <div class="section-header">
            <h2>{{ t('buildings.selectLand') }}</h2>
            <span v-if="lotsLoading" class="loading-inline">{{ t('common.loading') }}</span>
          </div>

          <div v-if="!lotsLoading && availableLots.length === 0" class="empty-state">
            {{ t('buildings.noAvailableLand') }}
          </div>

          <div v-else class="lot-grid">
            <button
              v-for="lot in availableLots"
              :key="lot.id"
              class="lot-card"
              :class="{ selected: selectedLotId === lot.id }"
              @click="selectedLotId = lot.id"
            >
              <div class="lot-card-header">
                <span class="lot-name">{{ lot.name }}</span>
                <span class="lot-price">{{ formatCurrency(lot.price) }}</span>
              </div>
              <span class="lot-district">{{ districtLabel(lot.district) }}</span>
              <span class="lot-meta">
                {{ t('buildings.populationIndex') }}: {{ formatPopulationIndex(lot.populationIndex) }}
              </span>
              <span class="lot-meta">
                {{ t('buildings.appraisedValue') }}: {{ formatCurrency(lot.basePrice) }}
              </span>
            </button>
          </div>

          <div v-if="selectedLot" class="selected-lot-panel">
            <div>
              <span class="selected-lot-label">{{ t('buildings.selectedLand') }}</span>
              <strong>{{ selectedLot.name }}</strong>
            </div>
            <div class="selected-lot-stats">
              <span>{{ districtLabel(selectedLot.district) }}</span>
              <span>{{ t('buildings.askingPrice') }}: {{ formatCurrency(selectedLot.price) }}</span>
              <span>
                {{ t('buildings.populationIndex') }}: {{ formatPopulationIndex(selectedLot.populationIndex) }}
              </span>
            </div>
          </div>

          <div class="action-bar">
            <button
              class="btn btn-primary btn-lg"
              :disabled="!canSubmit || submitting"
              @click="buyBuilding"
            >
              {{ submitting ? t('common.loading') : t('buildings.buyNow') }}
            </button>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.buy-building-view {
  padding: 2rem 1rem;
  max-width: 900px;
}

.page-nav {
  margin-bottom: 1.5rem;
}

.back-link {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  text-decoration: none;
  transition: color 0.15s;
}

.back-link:hover {
  color: var(--color-primary);
  text-decoration: none;
}

.buy-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 2rem;
}

.buy-card h1 {
  font-size: 1.5rem;
  margin-bottom: 1.5rem;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.form-section {
  margin-bottom: 2rem;
}

.form-section h2 {
  font-size: 1.125rem;
  margin-bottom: 1rem;
}

.type-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 0.75rem;
}

.type-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.375rem;
  padding: 1.25rem 0.75rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  cursor: pointer;
  transition: all 0.2s ease;
  color: var(--color-text);
  text-align: center;
}

.type-card:hover {
  border-color: var(--color-primary);
  transform: translateY(-2px);
}

.type-card.selected {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
  box-shadow: 0 0 0 1px var(--color-primary);
}

.type-icon {
  font-size: 2rem;
}

.type-name {
  font-weight: 700;
  font-size: 0.875rem;
}

.type-desc {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
  line-height: 1.3;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.form-group label {
  font-size: 0.875rem;
  font-weight: 600;
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

.form-group input::placeholder {
  color: var(--color-text-secondary);
}

.city-select-grid {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.company-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1rem;
  padding: 0.875rem 1rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
}

.city-option {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.5rem 1rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  color: var(--color-text);
  cursor: pointer;
  transition: all 0.15s ease;
}

.city-option:hover {
  border-color: var(--color-primary);
}

.city-option.selected {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
}

.city-name {
  font-weight: 600;
  font-size: 0.875rem;
}

.city-country {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.lot-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 0.75rem;
}

.lot-card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  border: 2px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  color: var(--color-text);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.2s ease, transform 0.2s ease;
}

.lot-card:hover {
  border-color: var(--color-primary);
  transform: translateY(-1px);
}

.lot-card.selected {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 1px var(--color-primary);
  background: rgba(0, 71, 255, 0.08);
}

.lot-card-header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: baseline;
}

.lot-name {
  font-weight: 700;
}

.lot-price {
  font-size: 0.875rem;
  font-weight: 700;
}

.lot-district,
.lot-meta,
.loading-inline,
.selected-lot-label {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.selected-lot-panel,
.empty-state {
  margin-top: 1rem;
  padding: 1rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
}

.selected-lot-panel {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.selected-lot-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  font-size: 0.875rem;
}

.action-bar {
  display: flex;
  justify-content: flex-end;
  margin-top: 1.5rem;
}

.btn-lg {
  padding: 0.75rem 2rem;
  font-size: 1rem;
  font-weight: 600;
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

.loading {
  text-align: center;
  padding: 2rem;
  color: var(--color-text-secondary);
}

@media (max-width: 640px) {
  .type-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
</style>
