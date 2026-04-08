<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useGameStateStore } from '@/stores/gameState'
import { deepEqual } from '@/lib/utils'
import type { PersonAccount, ShareTradeResult, StockExchangeListing } from '@/types'

type ControlledCompanyAccount = {
  id: string
  name: string
  cash: number | null
}

const { t, locale } = useI18n()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
auth.initFromStorage()

const currentTick = computed(() => gameStateStore.gameState?.currentTick ?? null)

const loading = ref(true)
const error = ref<string | null>(null)
const actionError = ref<string | null>(null)
const actionMessage = ref<string | null>(null)
const actionLoadingKey = ref<string | null>(null)
const personAccount = ref<PersonAccount | null>(null)
const listings = ref<StockExchangeListing[]>([])
const quantityByCompany = ref<Record<string, number>>({})

const PERSON_ACCOUNT_QUERY = `
  query PersonAccount {
    personAccount {
      playerId
      displayName
      personalCash
      activeAccountType
      activeCompanyId
      shareholdings {
        companyId
        companyName
        shareCount
        ownershipRatio
        sharePrice
        marketValue
      }
      dividendPayments {
        id
        companyId
        companyName
        shareCount
        amountPerShare
        totalAmount
        gameYear
        recordedAtTick
        recordedAtUtc
        description
      }
    }
  }
`

const LISTINGS_QUERY = `
  query StockExchangeListings {
    stockExchangeListings {
      companyId
      companyName
      totalSharesIssued
      publicFloatShares
      sharePrice
      bidPrice
      askPrice
      dividendPayoutRatio
      playerOwnedShares
      controlledCompanyOwnedShares
      combinedControlledOwnershipRatio
      canClaimControl
    }
  }
`

const BUY_MUTATION = `
  mutation BuyShares($input: BuySharesInput!) {
    buyShares(input: $input) {
      companyId
      companyName
      accountType
      accountCompanyId
      accountName
      shareCount
      pricePerShare
      totalValue
      ownedShareCount
      publicFloatShares
      personalCash
      companyCash
    }
  }
`

const SELL_MUTATION = `
  mutation SellShares($input: SellSharesInput!) {
    sellShares(input: $input) {
      companyId
      companyName
      accountType
      accountCompanyId
      accountName
      shareCount
      pricePerShare
      totalValue
      ownedShareCount
      publicFloatShares
      personalCash
      companyCash
    }
  }
`

const controlledCompanies = computed<ControlledCompanyAccount[]>(() => {
  const directCompanies = (auth.player?.companies ?? []).map((company) => ({
    id: company.id,
    name: company.name,
    cash: company.cash,
  }))
  const directCompanyIds = new Set(directCompanies.map((company) => company.id))
  const derivedCompanies = listings.value
    .filter((listing) => listing.canClaimControl)
    .filter((listing) => !directCompanyIds.has(listing.companyId))
    .map((listing) => ({
      id: listing.companyId,
      name: listing.companyName,
      cash: null,
    }))

  return [...directCompanies, ...derivedCompanies]
})

const activeCompany = computed(() =>
  controlledCompanies.value.find((company) => company.id === personAccount.value?.activeCompanyId) ?? null,
)

const activeAccountName = computed(() => {
  if (!personAccount.value) {
    return null
  }

  if (personAccount.value.activeAccountType === 'COMPANY') {
    return activeCompany.value?.name ?? personAccount.value.displayName
  }

  return personAccount.value.displayName
})

const portfolioValue = computed(() =>
  personAccount.value?.shareholdings.reduce((total, holding) => total + holding.marketValue, 0) ?? 0,
)

const recentDividendTotal = computed(() =>
  personAccount.value?.dividendPayments
    .slice(0, 5)
    .reduce((total, payment) => total + payment.totalAmount, 0) ?? 0,
)

const sortedListings = computed(() =>
  [...listings.value].sort((left, right) => {
    if (left.canClaimControl !== right.canClaimControl) {
      return left.canClaimControl ? -1 : 1
    }

    if (left.playerOwnedShares !== right.playerOwnedShares) {
      return right.playerOwnedShares - left.playerOwnedShares
    }

    return left.companyName.localeCompare(right.companyName)
  }),
)

function setDefaultQuantities() {
  for (const listing of listings.value) {
    quantityByCompany.value[listing.companyId] ??= 100
  }
}

function isControlledCompany(companyId: string): boolean {
  return controlledCompanies.value.some((company) => company.id === companyId)
}

function getQuantity(companyId: string): number {
  const value = Number(quantityByCompany.value[companyId] ?? 100)
  if (!Number.isFinite(value)) {
    return 0
  }

  return Math.max(Math.floor(value), 0)
}

function updateQuantity(companyId: string, value: number) {
  quantityByCompany.value[companyId] = Math.max(Math.floor(Number.isFinite(value) ? value : 0), 1)
}

async function loadData(isRefresh = false) {
  if (!isRefresh) {
    loading.value = true
  }
  error.value = null

  try {
    if (auth.isAuthenticated && !auth.player) {
      await auth.fetchMe()
    }

    const listingDataPromise = gqlRequest<{ stockExchangeListings: StockExchangeListing[] }>(LISTINGS_QUERY)
    let resolvedPersonAccount: PersonAccount | null = null

    try {
      const personData = await gqlRequest<{ personAccount: PersonAccount | null }>(PERSON_ACCOUNT_QUERY)
      resolvedPersonAccount = personData.personAccount
    } catch {
      resolvedPersonAccount = null
    }

    const listingData = await listingDataPromise

    if (!deepEqual(personAccount.value, resolvedPersonAccount)) {
      personAccount.value = resolvedPersonAccount
    }
    if (!deepEqual(listings.value, listingData.stockExchangeListings)) {
      listings.value = listingData.stockExchangeListings
    }

    setDefaultQuantities()
  } catch (reason: unknown) {
    if (!isRefresh) {
      error.value = reason instanceof Error ? reason.message : t('stockExchange.loadFailed')
    }
  } finally {
    loading.value = false
  }
}

async function switchToPersonAccount() {
  actionLoadingKey.value = 'switch-person'
  actionError.value = null
  actionMessage.value = null

  try {
    await auth.switchAccountContext('PERSON')
    await loadData(true)
    actionMessage.value = t('stockExchange.switchSuccess', {
      account: auth.player?.displayName ?? t('stockExchange.personAccount'),
    })
  } catch (reason: unknown) {
    actionError.value = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
  } finally {
    actionLoadingKey.value = null
  }
}

async function switchToCompanyAccount(companyId: string) {
  actionLoadingKey.value = `switch-${companyId}`
  actionError.value = null
  actionMessage.value = null

  const companyName =
    auth.player?.companies.find((company) => company.id === companyId)?.name
    ?? listings.value.find((listing) => listing.companyId === companyId)?.companyName
    ?? t('stockExchange.companyAccount')

  try {
    await auth.switchAccountContext('COMPANY', companyId)
    await loadData(true)
    actionMessage.value = t('stockExchange.switchSuccess', { account: companyName })
  } catch (reason: unknown) {
    actionError.value = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
  } finally {
    actionLoadingKey.value = null
  }
}

async function executeTrade(kind: 'buy' | 'sell', companyId: string) {
  const shareCount = getQuantity(companyId)
  if (shareCount <= 0) {
    actionError.value = t('stockExchange.invalidQuantity')
    actionMessage.value = null
    return
  }

  actionLoadingKey.value = `${kind}-${companyId}`
  actionError.value = null
  actionMessage.value = null

  try {
    let result: ShareTradeResult
    if (kind === 'buy') {
      const data = await gqlRequest<{ buyShares: ShareTradeResult }>(BUY_MUTATION, {
        input: { companyId, shareCount },
      })
      result = data.buyShares
      actionMessage.value = t('stockExchange.buySuccess', {
        company: result.companyName,
        shares: formatShares(result.shareCount),
      })
    } else {
      const data = await gqlRequest<{ sellShares: ShareTradeResult }>(SELL_MUTATION, {
        input: { companyId, shareCount },
      })
      result = data.sellShares
      actionMessage.value = t('stockExchange.sellSuccess', {
        company: result.companyName,
        shares: formatShares(result.shareCount),
      })
    }

    await Promise.all([loadData(true), auth.fetchMe()])
  } catch (reason: unknown) {
    actionError.value = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
  } finally {
    actionLoadingKey.value = null
  }
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2,
  }).format(value)
}

function formatPercent(value: number): string {
  return `${(value * 100).toFixed(1)}%`
}

function formatShares(value: number): string {
  return new Intl.NumberFormat(locale.value, {
    minimumFractionDigits: Number.isInteger(value) ? 0 : 2,
    maximumFractionDigits: Number.isInteger(value) ? 0 : 4,
  }).format(value)
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

onMounted(() => {
  void loadData()
})

useTickRefresh(() => {
  void loadData(true)
})
</script>

<template>
  <div class="stocks-view">
    <section class="stocks-hero">
      <div class="container">
        <p class="stocks-eyebrow">{{ t('stockExchange.eyebrow') }}</p>
        <h1 class="stocks-title">{{ t('stockExchange.title') }}</h1>
        <p class="stocks-subtitle">{{ t('stockExchange.subtitle') }}</p>
        <div class="stocks-hero-meta">
          <span class="stocks-tick-chip" :title="t('stockExchange.tickHint')">
            <span class="stocks-tick-label">{{ t('stockExchange.tick') }}</span>
            <span class="stocks-tick-value">{{ currentTick !== null ? currentTick : '—' }}</span>
          </span>
        </div>
      </div>
    </section>

    <div class="container stocks-body">
      <div v-if="loading" class="state-box">
        <p>{{ t('common.loading') }}</p>
      </div>

      <div v-else-if="error" class="state-box state-error" role="alert">
        <p>{{ error }}</p>
        <button class="btn btn-secondary" @click="() => void loadData()">{{ t('common.tryAgain') }}</button>
      </div>

      <template v-else>
        <p v-if="actionMessage" class="action-message action-message--success" role="status">{{ actionMessage }}</p>
        <p v-if="actionError" class="action-message action-message--error" role="alert">{{ actionError }}</p>

        <section v-if="personAccount" class="summary-grid">
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.personalCash') }}</span>
            <strong>{{ formatCurrency(personAccount.personalCash) }}</strong>
          </article>
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.activeAccount') }}</span>
            <strong>{{ activeAccountName }}</strong>
          </article>
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.portfolioValue') }}</span>
            <strong>{{ formatCurrency(portfolioValue) }}</strong>
          </article>
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.recentDividends') }}</span>
            <strong>{{ formatCurrency(recentDividendTotal) }}</strong>
          </article>
          <article v-if="activeCompany?.cash != null" class="summary-card">
            <span class="summary-label">{{ t('stockExchange.activeCompanyCash') }}</span>
            <strong>{{ formatCurrency(activeCompany.cash) }}</strong>
          </article>
        </section>

        <section v-if="personAccount" class="panel">
          <div class="section-header">
            <div>
              <h2>{{ t('stockExchange.accountSwitchTitle') }}</h2>
              <p>{{ t('stockExchange.accountSwitchDesc') }}</p>
            </div>
          </div>

          <div class="account-switch-grid">
            <button
              class="account-button"
              :class="{ active: personAccount.activeAccountType === 'PERSON' }"
              :disabled="actionLoadingKey === 'switch-person'"
              @click="switchToPersonAccount"
            >
              <span class="account-button-title">{{ t('stockExchange.personAccount') }}</span>
              <span class="account-button-meta">{{ formatCurrency(personAccount.personalCash) }}</span>
            </button>
            <button
              v-for="company in controlledCompanies"
              :key="company.id"
              class="account-button"
              :class="{ active: personAccount.activeAccountType === 'COMPANY' && personAccount.activeCompanyId === company.id }"
              :disabled="actionLoadingKey === `switch-${company.id}`"
              @click="switchToCompanyAccount(company.id)"
            >
              <span class="account-button-title">{{ company.name }}</span>
              <span class="account-button-meta">{{ company.cash != null ? formatCurrency(company.cash) : '' }}</span>
            </button>
          </div>
        </section>

        <section v-if="personAccount" class="panel">
          <div class="section-header">
            <div>
              <h2>{{ t('stockExchange.portfolioTitle') }}</h2>
              <p>{{ t('stockExchange.portfolioDesc') }}</p>
            </div>
          </div>

          <p v-if="personAccount.shareholdings.length === 0" class="empty-state">
            {{ t('stockExchange.portfolioEmpty') }}
          </p>

          <div v-else class="table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th>{{ t('stockExchange.company') }}</th>
                  <th>{{ t('stockExchange.ownedShares') }}</th>
                  <th>{{ t('stockExchange.holdingOwnership') }}</th>
                  <th>{{ t('stockExchange.sharePrice') }}</th>
                  <th>{{ t('stockExchange.holdingMarketValue') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="holding in personAccount.shareholdings" :key="holding.companyId">
                  <td>{{ holding.companyName }}</td>
                  <td>{{ formatShares(holding.shareCount) }}</td>
                  <td>{{ formatPercent(holding.ownershipRatio) }}</td>
                  <td>{{ formatCurrency(holding.sharePrice) }}</td>
                  <td>{{ formatCurrency(holding.marketValue) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <section v-if="personAccount" class="panel">
          <div class="section-header">
            <div>
              <h2>{{ t('stockExchange.dividendHistoryTitle') }}</h2>
              <p>{{ t('stockExchange.dividendHistoryDesc') }}</p>
            </div>
          </div>

          <p v-if="personAccount.dividendPayments.length === 0" class="empty-state">
            {{ t('stockExchange.dividendEmpty') }}
          </p>

          <div v-else class="table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th>{{ t('stockExchange.company') }}</th>
                  <th>{{ t('stockExchange.dividendYear') }}</th>
                  <th>{{ t('stockExchange.dividendPerShare') }}</th>
                  <th>{{ t('stockExchange.dividendAmount') }}</th>
                  <th>{{ t('stockExchange.recordedAt') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="payment in personAccount.dividendPayments" :key="payment.id">
                  <td>{{ payment.companyName }}</td>
                  <td>{{ payment.gameYear }}</td>
                  <td>{{ formatCurrency(payment.amountPerShare) }}</td>
                  <td>{{ formatCurrency(payment.totalAmount) }}</td>
                  <td>{{ formatDateTime(payment.recordedAtUtc) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <section class="panel">
          <div class="section-header">
            <div>
              <h2>{{ t('stockExchange.marketTitle') }}</h2>
              <p>{{ t('stockExchange.marketDesc') }}</p>
            </div>
          </div>

          <p v-if="!personAccount" class="market-note">
            {{ t('stockExchange.signInBody') }}
            <RouterLink to="/login">{{ t('common.login') }}</RouterLink>
          </p>

          <div class="listing-grid">
            <article v-for="listing in sortedListings" :key="listing.companyId" class="listing-card">
              <div class="listing-header">
                <div>
                  <h3>{{ listing.companyName }}</h3>
                  <p>{{ t('stockExchange.totalShares') }}: {{ formatShares(listing.totalSharesIssued) }}</p>
                </div>
                <span v-if="listing.canClaimControl && !isControlledCompany(listing.companyId)" class="listing-chip listing-chip--control">
                  {{ t('stockExchange.controlReady') }}
                </span>
                <span v-else-if="listing.playerOwnedShares > 0" class="listing-chip listing-chip--owned">
                  {{ t('stockExchange.ownedBadge') }}
                </span>
              </div>

              <div class="listing-metrics">
                <div class="metric">
                  <span>{{ t('stockExchange.sharePrice') }}</span>
                  <strong>{{ formatCurrency(listing.sharePrice) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.askPrice') }}</span>
                  <strong>{{ formatCurrency(listing.askPrice) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.bidPrice') }}</span>
                  <strong>{{ formatCurrency(listing.bidPrice) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.publicFloat') }}</span>
                  <strong>{{ formatShares(listing.publicFloatShares) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.dividendPayout') }}</span>
                  <strong>{{ formatPercent(listing.dividendPayoutRatio) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.controlRatio') }}</span>
                  <strong>{{ formatPercent(listing.combinedControlledOwnershipRatio) }}</strong>
                </div>
                <div class="metric">
                  <span>{{ t('stockExchange.ownedShares') }}</span>
                  <strong>{{ formatShares(listing.playerOwnedShares + listing.controlledCompanyOwnedShares) }}</strong>
                </div>
              </div>

              <div v-if="personAccount" class="listing-actions">
                <label class="quantity-field">
                  <span>{{ t('stockExchange.quantity') }}</span>
                  <input
                    :value="quantityByCompany[listing.companyId] ?? 100"
                    type="number"
                    min="1"
                    step="1"
                    :aria-label="`${t('stockExchange.quantity')} ${listing.companyName}`"
                    @input="updateQuantity(listing.companyId, Number(($event.target as HTMLInputElement).value))"
                  />
                </label>

                <div class="action-row">
                  <button
                    class="btn btn-primary"
                    :disabled="actionLoadingKey === `buy-${listing.companyId}`"
                    @click="executeTrade('buy', listing.companyId)"
                  >
                    {{ t('stockExchange.buy') }}
                  </button>
                  <button
                    class="btn btn-secondary"
                    :disabled="actionLoadingKey === `sell-${listing.companyId}`"
                    @click="executeTrade('sell', listing.companyId)"
                  >
                    {{ t('stockExchange.sell') }}
                  </button>
                  <button
                    v-if="isControlledCompany(listing.companyId) || listing.canClaimControl"
                    class="btn btn-ghost"
                    :disabled="actionLoadingKey === `switch-${listing.companyId}`"
                    @click="switchToCompanyAccount(listing.companyId)"
                  >
                    {{ isControlledCompany(listing.companyId) ? t('stockExchange.switchToCompany') : t('stockExchange.claimControl') }}
                  </button>
                </div>
              </div>
            </article>
          </div>
        </section>
      </template>
    </div>
  </div>
</template>

<style scoped>
.stocks-view {
  min-height: 100vh;
  background:
    radial-gradient(circle at top, color-mix(in srgb, var(--color-primary) 10%, transparent), transparent 35%),
    var(--color-background);
}

.stocks-hero {
  padding: 3.5rem 0 2rem;
}

.stocks-eyebrow {
  margin: 0 0 0.5rem;
  color: var(--color-primary);
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.stocks-title {
  margin: 0;
}

.stocks-subtitle {
  margin: 0.75rem 0 0;
  max-width: 60rem;
  color: var(--color-text-secondary);
}

.stocks-hero-meta {
  display: flex;
  margin-top: 1rem;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.stocks-tick-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  background: rgba(255, 255, 255, 0.07);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 9999px;
  padding: 0.25rem 0.75rem;
  font-size: 0.78rem;
  color: var(--color-text-secondary);
  cursor: default;
  user-select: none;
}

.stocks-tick-label {
  font-weight: 600;
  color: var(--color-primary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  font-size: 0.72rem;
}

.stocks-tick-value {
  font-variant-numeric: tabular-nums;
  font-weight: 700;
  color: var(--color-text-primary);
}

.stocks-body {
  display: grid;
  gap: 1.5rem;
  padding-bottom: 3rem;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 1rem;
}

.summary-card,
.panel,
.listing-card,
.state-box {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 18px;
  box-shadow: var(--shadow-sm);
}

.summary-card {
  padding: 1rem 1.1rem;
  display: grid;
  gap: 0.3rem;
}

.summary-label,
.section-header p,
.market-note,
.empty-state,
.metric span,
.listing-header p,
.account-button-meta {
  color: var(--color-text-secondary);
}

.panel {
  padding: 1.4rem;
}

.section-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.section-header h2 {
  margin: 0;
}

.section-header p {
  margin: 0.35rem 0 0;
}

.account-switch-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 0.75rem;
}

.account-button {
  border: 1px solid var(--color-border);
  background: var(--color-background);
  color: var(--color-text);
  border-radius: 14px;
  padding: 0.9rem 1rem;
  display: grid;
  gap: 0.25rem;
  text-align: left;
  cursor: pointer;
  transition:
    border-color 0.18s ease,
    transform 0.18s ease,
    background-color 0.18s ease;
}

.account-button:hover {
  border-color: var(--color-primary);
  transform: translateY(-1px);
}

.account-button.active {
  border-color: var(--color-primary);
  background: color-mix(in srgb, var(--color-primary) 10%, var(--color-surface));
}

.account-button-title {
  font-weight: 700;
}

.table-wrapper {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
}

.data-table th,
.data-table td {
  padding: 0.85rem 0.7rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
  white-space: nowrap;
}

.data-table tbody tr:last-child td {
  border-bottom: none;
}

.listing-grid {
  display: grid;
  gap: 1rem;
}

.listing-card {
  padding: 1.2rem;
  display: grid;
  gap: 1rem;
}

.listing-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.listing-header h3 {
  margin: 0;
}

.listing-header p {
  margin: 0.35rem 0 0;
}

.listing-chip {
  display: inline-flex;
  align-items: center;
  padding: 0.3rem 0.65rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 700;
}

.listing-chip--control {
  background: color-mix(in srgb, #f59e0b 18%, transparent);
  color: #b45309;
}

.listing-chip--owned {
  background: color-mix(in srgb, var(--color-primary) 16%, transparent);
  color: var(--color-primary);
}

.listing-metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(135px, 1fr));
  gap: 0.75rem;
}

.metric {
  display: grid;
  gap: 0.2rem;
}

.metric strong {
  font-size: 1rem;
}

.listing-actions {
  display: grid;
  gap: 0.9rem;
}

.quantity-field {
  display: grid;
  gap: 0.35rem;
  max-width: 160px;
}

.quantity-field input {
  width: 100%;
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-background);
  color: var(--color-text);
  padding: 0.65rem 0.8rem;
}

.action-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.action-message {
  margin: 0;
  padding: 0.8rem 1rem;
  border-radius: 12px;
  border: 1px solid var(--color-border);
}

.action-message--success {
  background: color-mix(in srgb, var(--color-success, #22c55e) 12%, var(--color-surface));
}

.action-message--error,
.state-error {
  background: color-mix(in srgb, var(--color-danger, #ef4444) 12%, var(--color-surface));
}

.state-box {
  padding: 1.2rem;
}

.market-note a {
  margin-left: 0.35rem;
}

@media (max-width: 720px) {
  .stocks-hero {
    padding-top: 2.5rem;
  }

  .panel,
  .listing-card {
    padding: 1rem;
  }

  .listing-header,
  .section-header {
    flex-direction: column;
  }
}
</style>
