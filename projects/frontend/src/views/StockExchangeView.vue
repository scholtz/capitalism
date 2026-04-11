<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useGameStateStore } from '@/stores/gameState'
import { useScrollPreservation } from '@/composables/useScrollPreservation'
import { deepEqual } from '@/lib/utils'
import type {
  PersonAccount,
  ShareTradeResult,
  StockExchangeListing,
  StockExchangePriceHistoryPoint,
} from '@/types'

type ControlledCompanyAccount = {
  id: string
  name: string
  cash: number | null
}

type SortField = 'name' | 'price' | 'marketValue' | 'ownership'
type SortDir = 'asc' | 'desc'

const { t, locale } = useI18n()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()
auth.initFromStorage()

const currentTick = computed(() => gameStateStore.gameState?.currentTick ?? null)

const loading = ref(true)
const error = ref<string | null>(null)
const actionLoadingKey = ref<string | null>(null)
const personAccount = ref<PersonAccount | null>(null)
const listings = ref<StockExchangeListing[]>([])
const quantityByCompany = ref<Record<string, number>>({})
const errorByCompany = ref<Record<string, string | null>>({})
const successByCompany = ref<Record<string, string | null>>({})
const expandedCompany = ref<string | null>(null)
const priceHistoryByCompany = ref<Record<string, StockExchangePriceHistoryPoint[]>>({})
const priceHistoryLoadingByCompany = ref<Record<string, boolean>>({})
const priceHistoryErrorByCompany = ref<Record<string, string | null>>({})

// Per-listing trade account selection: "PERSON" or "COMPANY:<id>"
const tradeAccountByCompany = ref<Record<string, string>>({})

// Sort and filter state
const filterText = ref('')
const sortField = ref<SortField>('marketValue')
const sortDir = ref<SortDir>('desc')
const currentPage = ref(1)
const pageSize = 10

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
      stockTrades {
        id
        companyId
        companyName
        direction
        shareCount
        pricePerShare
        totalValue
        recordedAtTick
        recordedAtUtc
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
      marketValue
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

const PRICE_HISTORY_QUERY = `
  query StockExchangePriceHistory($companyId: UUID!) {
    stockExchangePriceHistory(companyId: $companyId) {
      companyId
      tick
      price
      recordedAtUtc
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
  // Only include companies where the player has ALREADY switched to company account — not merely where
  // they COULD claim control. Including all `canClaimControl` companies causes isControlledCompany()
  // to return true, hiding the Claim Control button before the player has actually claimed anything.
  const activeCompanyId = personAccount.value?.activeCompanyId ?? null
  const derivedCompanies = listings.value
    .filter((listing) => listing.canClaimControl)
    .filter((listing) => !directCompanyIds.has(listing.companyId))
    .filter((listing) => activeCompanyId === listing.companyId)
    .map((listing) => ({
      id: listing.companyId,
      name: listing.companyName,
      cash: null,
    }))

  return [...directCompanies, ...derivedCompanies]
})

const portfolioValue = computed(() =>
  personAccount.value?.shareholdings.reduce((total, holding) => total + holding.marketValue, 0) ?? 0,
)

const recentDividendTotal = computed(() =>
  personAccount.value?.dividendPayments
    .slice(0, 5)
    .reduce((total, payment) => total + payment.totalAmount, 0) ?? 0,
)

const filteredAndSortedListings = computed(() => {
  const text = filterText.value.trim().toLowerCase()
  const filtered = text
    ? listings.value.filter((listing) => listing.companyName.toLowerCase().includes(text))
    : listings.value

  return [...filtered].sort((a, b) => {
    let cmp = 0
    if (sortField.value === 'name') {
      cmp = a.companyName.localeCompare(b.companyName)
    } else if (sortField.value === 'price') {
      cmp = a.sharePrice - b.sharePrice
    } else if (sortField.value === 'marketValue') {
      cmp = a.marketValue - b.marketValue
    } else if (sortField.value === 'ownership') {
      cmp = a.combinedControlledOwnershipRatio - b.combinedControlledOwnershipRatio
    }
    return sortDir.value === 'asc' ? cmp : -cmp
  })
})

const totalPages = computed(() => Math.max(1, Math.ceil(filteredAndSortedListings.value.length / pageSize)))

const paginatedListings = computed(() => {
  const start = (currentPage.value - 1) * pageSize
  return filteredAndSortedListings.value.slice(start, start + pageSize)
})

watch([filterText, sortField, sortDir], () => {
  currentPage.value = 1
})

watch(totalPages, (value) => {
  if (currentPage.value > value) {
    currentPage.value = value
  }
})

function toggleSort(field: SortField) {
  if (sortField.value === field) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortField.value = field
    sortDir.value = 'desc'
  }
}

function sortIcon(field: SortField): string {
  if (sortField.value !== field) return '\u2195'
  return sortDir.value === 'asc' ? '\u2191' : '\u2193'
}

function setDefaultQuantities() {
  for (const listing of listings.value) {
    quantityByCompany.value[listing.companyId] ??= 100
  }
}

function setDefaultTradeAccounts() {
  for (const listing of listings.value) {
    tradeAccountByCompany.value[listing.companyId] ??= 'PERSON'
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

function resolveTradeAccount(companyId: string): { tradeAccountType: string; tradeAccountCompanyId: string | null } {
  const selected = tradeAccountByCompany.value[companyId] ?? 'PERSON'
  if (selected === 'PERSON') {
    return { tradeAccountType: 'PERSON', tradeAccountCompanyId: null }
  }
  const colonIdx = selected.indexOf(':')
  const id = colonIdx >= 0 ? selected.slice(colonIdx + 1) : null
  return { tradeAccountType: 'COMPANY', tradeAccountCompanyId: id }
}

async function loadPriceHistory(companyId: string) {
  priceHistoryLoadingByCompany.value[companyId] = true
  priceHistoryErrorByCompany.value[companyId] = null

  try {
    const data = await gqlRequest<{ stockExchangePriceHistory: StockExchangePriceHistoryPoint[] }>(PRICE_HISTORY_QUERY, {
      companyId,
    })
    priceHistoryByCompany.value[companyId] = data.stockExchangePriceHistory
  } catch (reason: unknown) {
    priceHistoryErrorByCompany.value[companyId] = reason instanceof Error ? reason.message : t('stockExchange.historyLoadFailed')
  } finally {
    priceHistoryLoadingByCompany.value[companyId] = false
  }
}

async function toggleTradePanel(companyId: string) {
  expandedCompany.value = expandedCompany.value === companyId ? null : companyId
  errorByCompany.value[companyId] = null
  successByCompany.value[companyId] = null

  if (expandedCompany.value === companyId && !priceHistoryByCompany.value[companyId]) {
    await loadPriceHistory(companyId)
  }
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
    setDefaultTradeAccounts()
  } catch (reason: unknown) {
    if (!isRefresh) {
      error.value = reason instanceof Error ? reason.message : t('stockExchange.loadFailed')
    }
  } finally {
    loading.value = false
  }
}

async function switchToCompanyAccount(companyId: string) {
  actionLoadingKey.value = `switch-${companyId}`
  errorByCompany.value[companyId] = null
  successByCompany.value[companyId] = null

  const companyName =
    auth.player?.companies.find((company) => company.id === companyId)?.name ??
    listings.value.find((listing) => listing.companyId === companyId)?.companyName ??
    t('stockExchange.companyAccount')

  try {
    await auth.switchAccountContext('COMPANY', companyId)
    await loadData(true)
    successByCompany.value[companyId] = t('stockExchange.switchSuccess', { account: companyName })
    // Auto-open the trade panel so the success message is visible to the player
    expandedCompany.value = companyId
  } catch (reason: unknown) {
    errorByCompany.value[companyId] = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
  } finally {
    actionLoadingKey.value = null
  }
}

async function executeTrade(kind: 'buy' | 'sell', companyId: string) {
  const shareCount = getQuantity(companyId)
  if (shareCount <= 0) {
    errorByCompany.value[companyId] = t('stockExchange.invalidQuantity')
    successByCompany.value[companyId] = null
    return
  }

  actionLoadingKey.value = `${kind}-${companyId}`
  errorByCompany.value[companyId] = null
  successByCompany.value[companyId] = null

  const { tradeAccountType, tradeAccountCompanyId } = resolveTradeAccount(companyId)

  try {
    let result: ShareTradeResult
    if (kind === 'buy') {
      const data = await gqlRequest<{ buyShares: ShareTradeResult }>(BUY_MUTATION, {
        input: { companyId, shareCount, tradeAccountType, tradeAccountCompanyId },
      })
      result = data.buyShares
      successByCompany.value[companyId] = t('stockExchange.buySuccess', {
        company: result.companyName,
        shares: formatShares(result.shareCount),
      })
    } else {
      const data = await gqlRequest<{ sellShares: ShareTradeResult }>(SELL_MUTATION, {
        input: { companyId, shareCount, tradeAccountType, tradeAccountCompanyId },
      })
      result = data.sellShares
      successByCompany.value[companyId] = t('stockExchange.sellSuccess', {
        company: result.companyName,
        shares: formatShares(result.shareCount),
      })
    }

    await Promise.all([loadData(true), auth.fetchMe()])
  } catch (reason: unknown) {
    errorByCompany.value[companyId] = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
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

useTickRefresh(async () => {
  const scrollPos = saveScrollPosition()
  await loadData(true)
  await restoreScrollPosition(scrollPos)
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
            <span class="stocks-tick-value">{{ currentTick !== null ? currentTick : '\u2014' }}</span>
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
        <section v-if="personAccount" class="summary-grid">
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.personalCash') }}</span>
            <strong>{{ formatCurrency(personAccount.personalCash) }}</strong>
          </article>
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.portfolioValue') }}</span>
            <strong>{{ formatCurrency(portfolioValue) }}</strong>
          </article>
          <article class="summary-card">
            <span class="summary-label">{{ t('stockExchange.recentDividends') }}</span>
            <strong>{{ formatCurrency(recentDividendTotal) }}</strong>
          </article>
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

          <div class="market-controls">
            <input
              v-model="filterText"
              type="search"
              :placeholder="t('stockExchange.filterPlaceholder')"
              class="filter-input"
              :aria-label="t('stockExchange.filterLabel')"
            />
          </div>

          <div v-if="filteredAndSortedListings.length === 0" class="empty-state">
            {{ filterText ? t('stockExchange.noListingsFiltered') : t('stockExchange.noListings') }}
          </div>

          <div v-else class="table-wrapper">
            <table class="data-table market-table" :aria-label="t('stockExchange.marketTitle')">
              <thead>
                <tr>
                  <th>
                    <button class="sort-btn" @click="toggleSort('name')">
                      {{ t('stockExchange.company') }} <span aria-hidden="true">{{ sortIcon('name') }}</span>
                    </button>
                  </th>
                  <th>
                    <button class="sort-btn" @click="toggleSort('price')">
                      {{ t('stockExchange.sharePrice') }} <span aria-hidden="true">{{ sortIcon('price') }}</span>
                    </button>
                  </th>
                  <th>
                    <button class="sort-btn" @click="toggleSort('marketValue')">
                      {{ t('stockExchange.marketValue') }} <span aria-hidden="true">{{ sortIcon('marketValue') }}</span>
                    </button>
                  </th>
                  <th>{{ t('stockExchange.publicFloat') }}</th>
                  <th>
                    <button class="sort-btn" @click="toggleSort('ownership')">
                      {{ t('stockExchange.controlRatio') }} <span aria-hidden="true">{{ sortIcon('ownership') }}</span>
                    </button>
                  </th>
                  <th v-if="personAccount">{{ t('stockExchange.actions') }}</th>
                </tr>
              </thead>
              <tbody>
                <template v-for="listing in paginatedListings" :key="listing.companyId">
                  <tr
                    class="listing-row"
                    :class="{ 'listing-row--expanded': expandedCompany === listing.companyId }"
                  >
                    <td class="company-cell">
                      <span class="company-name">{{ listing.companyName }}</span>
                      <span
                        v-if="listing.canClaimControl && !isControlledCompany(listing.companyId)"
                        class="listing-chip listing-chip--control"
                      >{{ t('stockExchange.controlReady') }}</span>
                      <span
                        v-else-if="listing.playerOwnedShares + listing.controlledCompanyOwnedShares > 0"
                        class="listing-chip listing-chip--owned"
                      >{{ t('stockExchange.ownedBadge') }}</span>
                    </td>
                    <td class="price-cell">
                      <div class="price-stack">
                        <span class="price-main">{{ formatCurrency(listing.sharePrice) }}</span>
                        <span class="price-meta">
                          {{ t('stockExchange.bidAskHint', { bid: formatCurrency(listing.bidPrice), ask: formatCurrency(listing.askPrice) }) }}
                        </span>
                      </div>
                    </td>
                    <td>{{ formatCurrency(listing.marketValue) }}</td>
                    <td>{{ formatShares(listing.publicFloatShares) }}</td>
                    <td>
                      <div class="ownership-cell">
                        <span>{{ formatPercent(listing.combinedControlledOwnershipRatio) }}</span>
                        <span v-if="listing.playerOwnedShares > 0" class="owned-shares-hint">
                          {{ t('stockExchange.personalShares', { shares: formatShares(listing.playerOwnedShares) }) }}
                        </span>
                        <span v-if="listing.controlledCompanyOwnedShares > 0" class="owned-shares-hint">
                          {{ t('stockExchange.controlledCompanyShares', { shares: formatShares(listing.controlledCompanyOwnedShares) }) }}
                        </span>
                      </div>
                    </td>
                    <td v-if="personAccount" class="actions-cell">
                      <button
                        class="btn btn-primary btn-sm"
                        :class="{ 'btn-active': expandedCompany === listing.companyId }"
                        @click="toggleTradePanel(listing.companyId)"
                      >
                        {{ expandedCompany === listing.companyId ? t('stockExchange.closeTrade') : t('stockExchange.openTrade') }}
                      </button>
                      <button
                        v-if="listing.canClaimControl && !isControlledCompany(listing.companyId)"
                        class="btn btn-ghost btn-sm"
                        :disabled="actionLoadingKey === `switch-${listing.companyId}`"
                        @click="switchToCompanyAccount(listing.companyId)"
                      >
                        {{ t('stockExchange.claimControl') }}
                      </button>
                    </td>
                  </tr>

                  <tr v-if="expandedCompany === listing.companyId" class="trade-panel-row">
                    <td :colspan="personAccount ? 6 : 5">
                      <div class="trade-panel">
                        <div class="trade-price-context">
                          <div class="trade-price-item">
                            <span class="trade-price-label">{{ t('stockExchange.askPriceLabel') }}</span>
                            <strong class="trade-price-value trade-price-ask">{{ formatCurrency(listing.askPrice) }}</strong>
                            <span class="trade-price-hint">{{ t('stockExchange.askPriceHint') }}</span>
                          </div>
                          <div class="trade-price-item">
                            <span class="trade-price-label">{{ t('stockExchange.bidPriceLabel') }}</span>
                            <strong class="trade-price-value trade-price-bid">{{ formatCurrency(listing.bidPrice) }}</strong>
                            <span class="trade-price-hint">{{ t('stockExchange.bidPriceHint') }}</span>
                          </div>
                        </div>

                        <div class="trade-form">
                          <label class="trade-field">
                            <span>{{ t('stockExchange.tradeWith') }}</span>
                            <select
                              v-model="tradeAccountByCompany[listing.companyId]"
                              class="trade-select"
                              :aria-label="`${t('stockExchange.tradeWith')} ${listing.companyName}`"
                            >
                              <option value="PERSON">
                                {{ personAccount?.displayName }} ({{ formatCurrency(personAccount?.personalCash ?? 0) }})
                              </option>
                              <option
                                v-for="company in controlledCompanies"
                                :key="company.id"
                                :value="`COMPANY:${company.id}`"
                              >
                                {{ company.name }}{{ company.cash != null ? ` (${formatCurrency(company.cash)})` : '' }}
                              </option>
                            </select>
                          </label>

                          <div class="trade-order-row">
                            <label class="trade-field trade-field--quantity">
                              <span>{{ t('stockExchange.quantity') }}</span>
                              <input
                                :value="quantityByCompany[listing.companyId] ?? 100"
                                type="number"
                                min="1"
                                step="1"
                                class="trade-input"
                                :aria-label="`${t('stockExchange.quantity')} ${listing.companyName}`"
                                @input="updateQuantity(listing.companyId, Number(($event.target as HTMLInputElement).value))"
                              />
                            </label>

                            <div class="trade-actions">
                              <button
                                class="btn btn-primary"
                                :disabled="actionLoadingKey === `buy-${listing.companyId}`"
                                @click="executeTrade('buy', listing.companyId)"
                              >
                                {{ t('stockExchange.buyAt', { price: formatCurrency(listing.askPrice) }) }}
                              </button>
                              <button
                                class="btn btn-secondary"
                                :disabled="actionLoadingKey === `sell-${listing.companyId}`"
                                @click="executeTrade('sell', listing.companyId)"
                              >
                                {{ t('stockExchange.sellAt', { price: formatCurrency(listing.bidPrice) }) }}
                              </button>
                            </div>
                          </div>
                        </div>

                        <p
                          v-if="successByCompany[listing.companyId]"
                          class="trade-feedback trade-feedback--success"
                          role="status"
                        >{{ successByCompany[listing.companyId] }}</p>
                        <p
                          v-if="errorByCompany[listing.companyId]"
                          class="trade-feedback trade-feedback--error"
                          role="alert"
                        >{{ errorByCompany[listing.companyId] }}</p>

                        <div class="history-panel">
                          <div class="history-panel__header">
                            <h3>{{ t('stockExchange.priceHistoryTitle') }}</h3>
                            <span class="history-panel__hint">{{ t('stockExchange.priceHistoryHint') }}</span>
                          </div>

                          <p v-if="priceHistoryLoadingByCompany[listing.companyId]" class="history-panel__state">
                            {{ t('common.loading') }}
                          </p>
                          <p
                            v-else-if="priceHistoryErrorByCompany[listing.companyId]"
                            class="history-panel__state history-panel__state--error"
                          >
                            {{ priceHistoryErrorByCompany[listing.companyId] }}
                          </p>
                          <p v-else-if="!priceHistoryByCompany[listing.companyId]?.length" class="history-panel__state">
                            {{ t('stockExchange.priceHistoryEmpty') }}
                          </p>
                          <div v-else class="history-table-wrapper">
                            <table class="history-table" :aria-label="`${listing.companyName} ${t('stockExchange.priceHistoryTitle')}`">
                              <thead>
                                <tr>
                                  <th>{{ t('stockExchange.tick') }}</th>
                                  <th>{{ t('stockExchange.sharePrice') }}</th>
                                </tr>
                              </thead>
                              <tbody>
                                <tr
                                  v-for="point in priceHistoryByCompany[listing.companyId]"
                                  :key="`${listing.companyId}-${point.tick}-${point.recordedAtUtc}`"
                                >
                                  <td>{{ point.tick }}</td>
                                  <td>{{ formatCurrency(point.price) }}</td>
                                </tr>
                              </tbody>
                            </table>
                          </div>
                        </div>
                      </div>
                    </td>
                  </tr>
                </template>
              </tbody>
            </table>
            <div v-if="totalPages > 1" class="pagination-bar">
              <button class="btn btn-secondary btn-sm" :disabled="currentPage === 1" @click="currentPage -= 1">
                {{ t('stockExchange.prevPage') }}
              </button>
              <span class="pagination-bar__status">
                {{ t('stockExchange.pageStatus', { page: currentPage, total: totalPages }) }}
              </span>
              <button
                class="btn btn-secondary btn-sm"
                :disabled="currentPage === totalPages"
                @click="currentPage += 1"
              >
                {{ t('stockExchange.nextPage') }}
              </button>
            </div>
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

        <section v-if="personAccount" class="panel">
          <div class="section-header">
            <div>
              <h2>{{ t('stockExchange.tradeHistoryTitle') }}</h2>
              <p>{{ t('stockExchange.tradeHistoryDesc') }}</p>
            </div>
          </div>

          <p v-if="personAccount.stockTrades.length === 0" class="empty-state">
            {{ t('stockExchange.tradeHistoryEmpty') }}
          </p>

          <div v-else class="table-wrapper">
            <table class="data-table" :aria-label="t('stockExchange.tradeHistoryTitle')">
              <thead>
                <tr>
                  <th>{{ t('stockExchange.company') }}</th>
                  <th>{{ t('stockExchange.tradeDirection') }}</th>
                  <th>{{ t('stockExchange.tradeQuantity') }}</th>
                  <th>{{ t('stockExchange.tradePrice') }}</th>
                  <th>{{ t('stockExchange.tradeTotal') }}</th>
                  <th>{{ t('stockExchange.recordedAt') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="trade in personAccount.stockTrades" :key="trade.id" class="trade-history-row">
                  <td>{{ trade.companyName }}</td>
                  <td>
                    <span
                      :class="trade.direction === 'BUY' ? 'direction-badge direction-badge--buy' : 'direction-badge direction-badge--sell'"
                    >{{ trade.direction === 'BUY' ? t('stockExchange.tradeBuy') : t('stockExchange.tradeSell') }}</span>
                  </td>
                  <td>{{ formatShares(trade.shareCount) }}</td>
                  <td>{{ formatCurrency(trade.pricePerShare) }}</td>
                  <td>{{ formatCurrency(trade.totalValue) }}</td>
                  <td>{{ formatDateTime(trade.recordedAtUtc) }}</td>
                </tr>
              </tbody>
            </table>
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
.trade-price-label,
.trade-price-hint {
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

.market-controls {
  display: flex;
  gap: 0.75rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.filter-input {
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-background);
  color: var(--color-text);
  padding: 0.55rem 0.85rem;
  font-size: 0.9rem;
  min-width: 200px;
  max-width: 360px;
  width: 100%;
}

.filter-input:focus {
  outline: 2px solid var(--color-primary);
  outline-offset: 1px;
}

.table-wrapper {
  overflow-x: auto;
}

.pagination-bar {
  margin-top: 1rem;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.pagination-bar__status {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
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

.market-table th {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--color-text-secondary);
}

.sort-btn {
  background: none;
  border: none;
  color: inherit;
  font: inherit;
  font-weight: 700;
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  cursor: pointer;
  padding: 0;
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  white-space: nowrap;
}

.sort-btn:hover {
  color: var(--color-primary);
}

.listing-row {
  transition: background-color 0.15s ease;
}

.listing-row:hover {
  background: color-mix(in srgb, var(--color-primary) 4%, transparent);
}

.listing-row--expanded {
  background: color-mix(in srgb, var(--color-primary) 6%, var(--color-surface));
}

.listing-row--expanded td {
  border-bottom-color: transparent;
}

.company-cell {
  display: flex !important;
  flex-direction: column;
  gap: 0.3rem;
  white-space: normal !important;
}

.company-name {
  font-weight: 600;
}

.price-stack {
  display: grid;
  gap: 0.15rem;
}

.price-main {
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.price-meta {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.ownership-cell {
  display: grid;
  gap: 0.15rem;
}

.owned-shares-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  white-space: normal;
}

.listing-chip {
  display: inline-flex;
  align-items: center;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 700;
  white-space: nowrap;
}

.listing-chip--control {
  background: color-mix(in srgb, #f59e0b 18%, transparent);
  color: #b45309;
}

.listing-chip--owned {
  background: color-mix(in srgb, var(--color-primary) 16%, transparent);
  color: var(--color-primary);
}

.direction-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.15rem 0.5rem;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 700;
  white-space: nowrap;
}

.direction-badge--buy {
  background: color-mix(in srgb, #22c55e 18%, transparent);
  color: #16a34a;
}

.direction-badge--sell {
  background: color-mix(in srgb, #ef4444 18%, transparent);
  color: #dc2626;
}

.actions-cell {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  white-space: nowrap;
}

.btn-sm {
  padding: 0.4rem 0.85rem;
  font-size: 0.82rem;
}

.btn-active {
  background: color-mix(in srgb, var(--color-primary) 20%, var(--color-surface));
}

.trade-panel-row td {
  padding: 0 !important;
  border-bottom: 1px solid var(--color-border);
}

.trade-panel {
  padding: 1.1rem 1rem;
  background: color-mix(in srgb, var(--color-primary) 4%, var(--color-surface));
  display: grid;
  gap: 1rem;
}

.trade-price-context {
  display: flex;
  gap: 2rem;
  flex-wrap: wrap;
}

.trade-price-item {
  display: grid;
  gap: 0.15rem;
}

.trade-price-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.trade-price-value {
  font-size: 1.15rem;
  font-variant-numeric: tabular-nums;
}

.trade-price-ask {
  color: var(--color-danger, #ef4444);
}

.trade-price-bid {
  color: var(--color-success, #22c55e);
}

.trade-price-hint {
  font-size: 0.75rem;
}

.trade-form {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
  align-items: flex-end;
}

.trade-field {
  display: grid;
  gap: 0.35rem;
}

.trade-field--quantity {
  min-width: 130px;
}

.trade-select,
.trade-input {
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-background);
  color: var(--color-text);
  padding: 0.6rem 0.8rem;
  font-size: 0.9rem;
}

.trade-select {
  min-width: 200px;
}

.trade-input {
  width: 130px;
}

.trade-order-row {
  display: flex;
  gap: 0.75rem;
  align-items: flex-end;
  flex-wrap: wrap;
}

.trade-actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  align-items: center;
}

.history-panel {
  display: grid;
  gap: 0.6rem;
  padding-top: 0.25rem;
  border-top: 1px solid var(--color-border);
}

.history-panel__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.history-panel__header h3 {
  margin: 0;
  font-size: 0.95rem;
}

.history-panel__hint,
.history-panel__state {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.history-panel__state--error {
  color: var(--color-danger, #ef4444);
}

.history-table-wrapper {
  overflow-x: auto;
}

.history-table {
  width: 100%;
  border-collapse: collapse;
}

.history-table th,
.history-table td {
  padding: 0.5rem 0.35rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
  white-space: nowrap;
}

.trade-feedback {
  margin: 0;
  padding: 0.65rem 0.9rem;
  border-radius: 10px;
  font-size: 0.88rem;
}

.trade-feedback--success {
  background: color-mix(in srgb, var(--color-success, #22c55e) 14%, var(--color-surface));
  color: var(--color-success, #22c55e);
}

.trade-feedback--error {
  background: color-mix(in srgb, var(--color-danger, #ef4444) 14%, var(--color-surface));
  color: var(--color-danger, #ef4444);
}

.state-box {
  padding: 1.2rem;
}

.state-error {
  background: color-mix(in srgb, var(--color-danger, #ef4444) 12%, var(--color-surface));
}

.market-note a {
  margin-left: 0.35rem;
}

@media (max-width: 720px) {
  .stocks-hero {
    padding-top: 2.5rem;
  }

  .panel {
    padding: 1rem;
  }

  .trade-price-context {
    gap: 1rem;
  }

  .pagination-bar {
    justify-content: flex-start;
  }
}
</style>
