<script setup lang="ts">
import { ref, onMounted, computed, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useGameStateStore } from '@/stores/gameState'
import { gqlRequest } from '@/lib/graphql'
import { trackStartupPackEvent } from '@/lib/startupPackAnalytics'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useTickCountdown } from '@/composables/useTickCountdown'
import { deepEqual } from '@/lib/utils'
import PendingActionsTimeline from '@/components/dashboard/PendingActionsTimeline.vue'
import type { Company, GameState, ScheduledActionSummary, StartupPackOffer, CityPowerBalance } from '@/types'

const { t, locale } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const { gameState } = storeToRefs(gameStateStore)

const companies = ref<Company[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const offerLoading = ref(false)
const offerError = ref<string | null>(null)
const offerMessage = ref<string | null>(null)
const pendingActions = ref<ScheduledActionSummary[]>([])
const pendingActionsLoading = ref(false)
const cityPowerBalances = ref<Record<string, CityPowerBalance>>({})

const { tickCountdown, startTickCountdown, stopTickCountdown } = useTickCountdown(gameState)

const activeStartupPackOffer = computed(() => (auth.startupPackOffer && ['ELIGIBLE', 'SHOWN', 'DISMISSED'].includes(auth.startupPackOffer.status) ? auth.startupPackOffer : null))
const claimedStartupPackOffer = computed(() => (auth.startupPackOffer?.status === 'CLAIMED' ? auth.startupPackOffer : null))
const expiredStartupPackOffer = computed(() => (auth.startupPackOffer?.status === 'EXPIRED' ? auth.startupPackOffer : null))
const targetCompany = computed(() => companies.value[0] ?? null)

const buildingTypeIcons: Record<string, string> = {
  MINE: '⛏️',
  FACTORY: '🏭',
  SALES_SHOP: '🏪',
  RESEARCH_DEVELOPMENT: '🔬',
  APARTMENT: '🏢',
  COMMERCIAL: '🏛️',
  MEDIA_HOUSE: '📺',
  BANK: '🏦',
  EXCHANGE: '📊',
  POWER_PLANT: '⚡',
}

function getBuildingIcon(type: string): string {
  return buildingTypeIcons[type] || '🏗️'
}

function formatBuildingType(type: string): string {
  return type.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}

/** Returns the power status label for a building's powerStatus field. */
function getBuildingPowerLabel(powerStatus: string): string {
  const key = `powerGrid.buildingStatus.${powerStatus}` as Parameters<typeof t>[0]
  return t(key)
}

/** Returns the CSS class for a power status badge. */
function powerStatusClass(status: string): string {
  if (status === 'CONSTRAINED') return 'power-badge power-badge--constrained'
  if (status === 'OFFLINE') return 'power-badge power-badge--offline'
  return 'power-badge power-badge--powered'
}

/** Returns the CSS class for the city power balance status. */
function powerBalanceClass(status: string): string {
  if (status === 'CRITICAL') return 'power-balance power-balance--critical'
  if (status === 'CONSTRAINED') return 'power-balance power-balance--constrained'
  return 'power-balance power-balance--balanced'
}

async function loadDashboardData() {
  const companiesData = await gqlRequest<{ myCompanies: Company[] }>(
    `{ myCompanies {
      id name cash foundedAtUtc
      buildings { id name type level cityId powerStatus units { id unitType gridX gridY level } }
    } }`,
  )

  if (!deepEqual(companies.value, companiesData.myCompanies)) {
    companies.value = companiesData.myCompanies
  }
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    router.push('/login')
    return
  }

  try {
    await auth.fetchMe()
    if (auth.player && !auth.player.onboardingCompletedAtUtc) {
      router.push('/onboarding')
      return
    }
    const [companiesData, gameStateData] = await Promise.all([
      gqlRequest<{ myCompanies: Company[] }>(
        `{ myCompanies {
          id name cash foundedAtUtc
          buildings { id name type level cityId powerStatus units { id unitType gridX gridY level } }
        } }`,
      ),
      gqlRequest<{ gameState: GameState }>(
        '{ gameState { currentTick lastTickAtUtc tickIntervalSeconds taxCycleTicks taxRate currentGameYear currentGameTimeUtc ticksPerDay ticksPerYear nextTaxTick nextTaxGameTimeUtc nextTaxGameYear } }',
      ),
    ])
    companies.value = companiesData.myCompanies
    gameState.value = gameStateData.gameState
    startTickCountdown()

    if (auth.startupPackOffer?.status === 'ELIGIBLE') {
      await markStartupPackOfferShown()
    } else if (auth.startupPackOffer) {
      trackStartupPackEvent('view', { context: 'dashboard', status: auth.startupPackOffer.status })
    }

    // Load city power balances for each unique city that has buildings.
    const cityIds = [...new Set(companiesData.myCompanies.flatMap((c) => c.buildings.map((b) => b.cityId)))]
    await loadCityPowerBalances(cityIds)

    await loadPendingActions()
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load dashboard'
  } finally {
    loading.value = false
  }
})

useTickRefresh(async () => {
  if (!auth.isAuthenticated) {
    return
  }

  await Promise.all([loadDashboardData(), loadPendingActions()])
  startTickCountdown()
})

onUnmounted(stopTickCountdown)

async function loadCityPowerBalances(cityIds: string[]) {
  if (cityIds.length === 0) return
  // Load balances best-effort in parallel; failures are non-critical.
  const results = await Promise.allSettled(
    cityIds.map((cityId) =>
      gqlRequest<{ cityPowerBalance: CityPowerBalance }>(
        `query CityPower($cityId: UUID!) {
          cityPowerBalance(cityId: $cityId) {
            cityId totalSupplyMw totalDemandMw reserveMw reservePercent status
            powerPlantCount consumerBuildingCount
            powerPlants { buildingId buildingName plantType outputMw powerStatus }
          }
        }`,
        { cityId },
      ),
    ),
  )
  for (const result of results) {
    if (result.status === 'fulfilled') {
      const balance = result.value.cityPowerBalance
      cityPowerBalances.value[balance.cityId] = balance
    }
  }
}

async function loadPendingActions() {
  pendingActionsLoading.value = true
  try {
    const data = await gqlRequest<{ myPendingActions: ScheduledActionSummary[] }>(
      `{ myPendingActions {
        id actionType buildingId buildingName buildingType
        submittedAtUtc submittedAtTick appliesAtTick ticksRemaining totalTicksRequired
      } }`,
    )
    if (!deepEqual(pendingActions.value, data.myPendingActions)) {
      pendingActions.value = data.myPendingActions
    }
  } catch {
    // best-effort — pending actions list is non-critical
  } finally {
    pendingActionsLoading.value = false
  }
}

async function markStartupPackOfferShown() {
  if (!auth.startupPackOffer || auth.startupPackOffer.status !== 'ELIGIBLE') {
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

  auth.setStartupPackOffer(data.markStartupPackOfferShown)
  if (data.markStartupPackOfferShown) {
    trackStartupPackEvent('view', { context: 'dashboard', status: data.markStartupPackOfferShown.status })
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
    auth.setStartupPackOffer(data.dismissStartupPackOffer)
    offerMessage.value = t('startupPack.dismissedBody')
    trackStartupPackEvent('dismiss', { context: 'dashboard' })
  } catch (e: unknown) {
    offerError.value = e instanceof Error ? e.message : t('startupPack.claimFailed')
  } finally {
    offerLoading.value = false
  }
}

async function claimStartupPackOffer() {
  if (!targetCompany.value) {
    return
  }

  offerLoading.value = true
  offerError.value = null
  offerMessage.value = null
  trackStartupPackEvent('claim_click', { context: 'dashboard' })

  try {
    const data = await gqlRequest<{
      claimStartupPack: {
        offer: StartupPackOffer
        company: Company
        proSubscriptionEndsAtUtc: string
      }
    }>(
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
          company {
            id
            name
            cash
            foundedAtUtc
            buildings { id name type level units { id unitType gridX gridY level } }
          }
          proSubscriptionEndsAtUtc
        }
      }`,
      { input: { companyId: targetCompany.value.id } },
    )

    auth.setStartupPackOffer(data.claimStartupPack.offer)
    companies.value = companies.value.map((company) => (company.id === data.claimStartupPack.company.id ? data.claimStartupPack.company : company))
    await auth.fetchMe()
    offerMessage.value = t('startupPack.claimedBody', {
      date: formatDateTime(data.claimStartupPack.proSubscriptionEndsAtUtc),
    })
    trackStartupPackEvent('claim_success', { context: 'dashboard' })
  } catch (e: unknown) {
    offerError.value = e instanceof Error ? e.message : t('startupPack.claimFailed')
    trackStartupPackEvent('claim_error', { context: 'dashboard' })
  } finally {
    offerLoading.value = false
  }
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
  <div class="dashboard-view container">
    <div class="dashboard-header">
      <div>
        <h1>{{ t('dashboard.title') }}</h1>
        <div v-if="auth.player" class="player-info">
          <span class="player-name">{{ auth.player.displayName }}</span>
          <span class="player-email">{{ auth.player.email }}</span>
        </div>
      </div>
      <div v-if="gameState" class="tick-clock-widget" :aria-label="t('tickClock.sectionTitle')">
        <span class="tick-clock-label">{{ t('tickClock.currentTick', { tick: gameState.currentTick }) }}</span>
        <span v-if="tickCountdown" class="tick-clock-countdown" role="timer">{{ tickCountdown }}</span>
      </div>
    </div>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>

    <div v-else-if="error" class="error-message" role="alert">
      {{ error }}
      <button class="btn btn-secondary" @click="router.go(0)">{{ t('common.tryAgain') }}</button>
    </div>

    <template v-else>
      <section v-if="auth.startupPackOffer" class="startup-pack-panel" aria-labelledby="dashboard-startup-pack-title">
        <div class="startup-pack-header">
          <div>
            <span class="startup-pack-eyebrow">{{ t('startupPack.eyebrow') }}</span>
            <h2 id="dashboard-startup-pack-title">{{ t('startupPack.revisitTitle') }}</h2>
          </div>
          <span
            class="startup-pack-status"
            :class="{
              claimed: claimedStartupPackOffer,
              expired: expiredStartupPackOffer,
            }"
          >
            {{ t(`startupPack.status.${auth.startupPackOffer.status.toLowerCase()}`) }}
          </span>
        </div>

        <p class="startup-pack-subtitle">
          {{
            t('startupPack.subtitle', {
              amount: '$' + formatCurrency(auth.startupPackOffer.companyCashGrant),
            })
          }}
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
                      amount: '$' + formatCurrency(activeStartupPackOffer.companyCashGrant),
                      company: targetCompany?.name ?? t('dashboard.title'),
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
            <button class="btn btn-primary" :disabled="offerLoading || !targetCompany" @click="claimStartupPackOffer">
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
            {{
              t('startupPack.claimedBody', {
                date: formatDateTime(auth.player.proSubscriptionEndsAtUtc),
              })
            }}
          </p>
          <p v-else>{{ t('startupPack.claimedNoDate') }}</p>
        </div>

        <div v-else class="startup-pack-state muted">
          <strong>{{ t('startupPack.expiredTitle') }}</strong>
          <p>{{ t('startupPack.expiredBody') }}</p>
        </div>
      </section>

      <PendingActionsTimeline :actions="pendingActions" :loading="pendingActionsLoading" :current-tick="gameState?.currentTick ?? null" />

      <div v-if="companies.length === 0" class="empty-state">
        <div class="empty-icon">🏗️</div>
        <p>{{ t('dashboard.noCompanies') }}</p>
        <RouterLink to="/onboarding" class="btn btn-primary btn-lg">{{ t('dashboard.startOnboarding') }}</RouterLink>
      </div>

      <div v-else class="companies-section">
        <div v-for="company in companies" :key="company.id" class="company-card">
          <div class="company-header">
            <div>
              <h2>{{ company.name }}</h2>
              <div class="company-meta">
                <span class="meta-item">
                  <span class="meta-label">{{ t('dashboard.cash') }}</span>
                  <span class="cash">${{ company.cash.toLocaleString() }}</span>
                </span>
                <span class="meta-item">
                  <span class="meta-label">{{ t('dashboard.buildings') }}</span>
                  <span>{{ company.buildings.length }}</span>
                </span>
              </div>
            </div>
            <RouterLink :to="`/buy-building/${company.id}`" class="btn btn-primary">
              {{ t('dashboard.buyBuilding') }}
            </RouterLink>
            <RouterLink v-if="company.buildings.length > 0 && company.buildings[0]" :to="`/city/${company.buildings[0].cityId}`" class="btn btn-secondary"> 🗺️ {{ t('nav.cityMap') }} </RouterLink>
            <RouterLink :to="`/ledger/${company.id}`" class="btn btn-ghost"> 📒 {{ t('dashboard.viewLedger') }} </RouterLink>
            <RouterLink :to="`/company/${company.id}/settings`" class="btn btn-ghost"> ⚙️ {{ t('dashboard.companySettings') }} </RouterLink>
          </div>

          <div v-if="company.buildings.length === 0" class="no-buildings">
            <p>{{ t('dashboard.noBuildings') }}</p>
          </div>

          <div v-else class="buildings-grid">
            <RouterLink v-for="building in company.buildings" :key="building.id" :to="`/building/${building.id}`" class="building-card">
              <div class="building-icon">{{ getBuildingIcon(building.type) }}</div>
              <div class="building-info">
                <span class="building-name">{{ building.name }}</span>
                <span class="building-type-label">{{ formatBuildingType(building.type) }}</span>
              </div>
              <div class="building-stats">
                <span class="building-level">Lv.{{ building.level }}</span>
                <span class="building-units">{{ building.units.length }} units</span>
                <span v-if="building.powerStatus && building.powerStatus !== 'POWERED'" :class="powerStatusClass(building.powerStatus)" :aria-label="getBuildingPowerLabel(building.powerStatus)">
                  {{ building.powerStatus === 'OFFLINE' ? '🔴' : '🟡' }}
                  {{ getBuildingPowerLabel(building.powerStatus) }}
                </span>
              </div>
            </RouterLink>
          </div>

          <!-- City power summary for this company's city -->
          <div v-if="company.buildings.length > 0 && company.buildings[0]" class="city-power-row">
            <template v-for="cityId in [...new Set(company.buildings.map((b) => b.cityId))]" :key="cityId">
              <div v-if="cityPowerBalances[cityId] && cityPowerBalances[cityId].powerPlantCount > 0" :class="powerBalanceClass(cityPowerBalances[cityId].status)" :aria-label="t('powerGrid.title')">
                <span class="power-balance-icon">⚡</span>
                <span class="power-balance-label">{{ t('powerGrid.powerCardTitle') }}</span>
                <span v-if="cityPowerBalances[cityId].status === 'BALANCED'" class="power-balance-value">
                  {{ cityPowerBalances[cityId].totalSupplyMw }} / {{ cityPowerBalances[cityId].totalDemandMw }} {{ t('powerGrid.unit') }}
                </span>
                <span v-else class="power-balance-warning">
                  {{ cityPowerBalances[cityId].status === 'CRITICAL' ? t('powerGrid.criticalWarning') : t('powerGrid.shortageWarning') }}
                </span>
                <RouterLink :to="`/city/${cityId}`" class="power-balance-link">{{ t('powerGrid.viewDetails') }}</RouterLink>
              </div>
              <div v-else class="power-balance power-balance--legacy">
                <span class="power-balance-icon">⚡</span>
                <span class="power-balance-label">{{ t('powerGrid.powerCardTitle') }}</span>
                <span class="power-balance-value">{{ t('powerGrid.powerCardNoPower') }}</span>
              </div>
            </template>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.dashboard-view {
  padding: 2rem 1rem;
}

.dashboard-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 2rem;
}

.dashboard-header h1 {
  font-size: 1.75rem;
  margin-bottom: 0.25rem;
}

.player-info {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.player-name {
  font-weight: 600;
  font-size: 0.9375rem;
}

.player-email {
  color: var(--color-text-secondary);
  font-size: 0.8125rem;
}

.tick-clock-widget {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.25rem;
  padding: 0.625rem 1rem;
  border-radius: var(--radius-md);
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid var(--color-border);
  min-width: 9rem;
  text-align: right;
}

.tick-clock-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  font-weight: 500;
  letter-spacing: 0.03em;
}

.tick-clock-countdown {
  font-size: 0.9375rem;
  font-weight: 700;
  color: var(--color-primary);
  font-variant-numeric: tabular-nums;
}

.empty-state {
  text-align: center;
  padding: 4rem 1rem;
}

.empty-icon {
  font-size: 3rem;
  margin-bottom: 1rem;
}

.empty-state p {
  margin-bottom: 1.5rem;
  color: var(--color-text-secondary);
  font-size: 1.125rem;
}

.startup-pack-panel {
  margin-bottom: 1.5rem;
  padding: 1.5rem;
  border: 1px solid rgba(255, 109, 0, 0.35);
  border-radius: var(--radius-lg);
  background: linear-gradient(135deg, rgba(255, 109, 0, 0.12), rgba(0, 71, 255, 0.1));
}

.startup-pack-header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
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

.startup-pack-header h2 {
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
  margin-top: 1rem;
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

.btn-lg {
  padding: 0.75rem 1.75rem;
  font-size: 1rem;
}

.companies-section {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.company-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.5rem;
}

.company-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 1.25rem;
}

.company-header h2 {
  font-size: 1.25rem;
  margin-bottom: 0.5rem;
}

.company-meta {
  display: flex;
  gap: 1.5rem;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.meta-label {
  font-size: 0.6875rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-text-secondary);
  font-weight: 600;
}

.cash {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--color-success);
}

.no-buildings {
  text-align: center;
  padding: 1.5rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.buildings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 0.75rem;
}

.building-card {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  text-decoration: none;
  color: var(--color-text);
  transition: all 0.2s ease;
}

.building-card:hover {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.04);
  transform: translateY(-1px);
  text-decoration: none;
}

.building-icon {
  font-size: 1.75rem;
  flex-shrink: 0;
}

.building-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.building-name {
  font-weight: 600;
  font-size: 0.9375rem;
}

.building-type-label {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.building-stats {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.125rem;
}

.building-level {
  background: var(--color-primary);
  color: #fff;
  padding: 0.125rem 0.5rem;
  border-radius: var(--radius-sm);
  font-size: 0.6875rem;
  font-weight: 700;
}

.building-units {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
}

/* Power status badges on building cards */
.power-badge {
  font-size: 0.625rem;
  font-weight: 700;
  padding: 0.125rem 0.375rem;
  border-radius: var(--radius-sm);
  white-space: nowrap;
}

.power-badge--powered {
  background: rgba(34, 197, 94, 0.15);
  color: #15803d;
}

.power-badge--constrained {
  background: rgba(251, 191, 36, 0.2);
  color: #b45309;
}

.power-badge--offline {
  background: rgba(248, 113, 113, 0.15);
  color: var(--color-danger);
}

/* City power balance bar under buildings grid */
.city-power-row {
  margin-top: 0.75rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.power-balance {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-sm);
  font-size: 0.8125rem;
  flex-wrap: wrap;
}

.power-balance--balanced {
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.25);
  color: #15803d;
}

.power-balance--constrained {
  background: rgba(251, 191, 36, 0.15);
  border: 1px solid rgba(251, 191, 36, 0.3);
  color: #b45309;
}

.power-balance--critical {
  background: rgba(248, 113, 113, 0.1);
  border: 1px solid rgba(248, 113, 113, 0.25);
  color: var(--color-danger);
}

.power-balance--legacy {
  background: var(--color-bg-secondary, rgba(0, 0, 0, 0.04));
  border: 1px solid var(--color-border);
  color: var(--color-text-secondary);
}

.power-balance-icon {
  flex-shrink: 0;
}

.power-balance-label {
  font-weight: 600;
}

.power-balance-link {
  color: inherit;
  text-decoration: underline;
  font-size: 0.75rem;
  margin-left: auto;
}

.error-message {
  background: rgba(248, 113, 113, 0.1);
  color: var(--color-danger);
  padding: 1rem;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  gap: 1rem;
}

.loading {
  text-align: center;
  padding: 3rem;
  color: var(--color-text-secondary);
}

@media (max-width: 640px) {
  .startup-pack-header {
    flex-direction: column;
  }

  .startup-pack-actions {
    flex-direction: column;
  }

  .company-header {
    flex-direction: column;
    gap: 1rem;
  }

  .buildings-grid {
    grid-template-columns: 1fr;
  }
}
</style>
