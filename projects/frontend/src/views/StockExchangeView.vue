<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useGameStateStore } from '@/stores/gameState'
import { useScrollPreservation } from '@/composables/useScrollPreservation'
import { getActiveAccountOption } from '@/lib/accountContext'
import { deepEqual } from '@/lib/utils'
import type {
  CompanyOwnership,
  MergeCompanyResult,
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

type SortField = 'name' | 'price' | 'marketValue' | 'ownership' | 'dividend'
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

// Shareholders state per company
const shareholdersByCompany = ref<Record<string, CompanyOwnership>>({})
const shareholdersLoadingByCompany = ref<Record<string, boolean>>({})
const shareholdersErrorByCompany = ref<Record<string, string | null>>({})

// Merge dialog state
const mergeDialogCompanyId = ref<string | null>(null)
const mergeDestinationCompanyId = ref<string>('')
const mergeLoading = ref(false)
const mergeError = ref<string | null>(null)
const mergeSuccess = ref<MergeCompanyResult | null>(null)

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
      canMerge
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

const MERGE_MUTATION = `
  mutation MergeCompany($input: MergeCompanyInput!) {
    mergeCompany(input: $input) {
      destinationCompanyId
      destinationCompanyName
      absorbedCompanyName
      cashTransferred
      buildingsTransferred
    }
  }
`

const COMPANY_SHAREHOLDERS_QUERY = `
  query CompanyShareholders($companyId: UUID!) {
    companyShareholders(companyId: $companyId) {
      companyId
      companyName
      totalSharesIssued
      publicFloatShares
      shareholderCount
      shareholders {
        holderName
        holderType
        holderPlayerId
        holderCompanyId
        shareCount
        ownershipRatio
      }
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

const activeTradeAccount = computed(() => getActiveAccountOption(auth.player, auth.player?.companies ?? []))

const activeTradeAccountName = computed(() =>
  activeTradeAccount.value?.name ?? personAccount.value?.displayName ?? t('stockExchange.personAccount'),
)

const activeTradeAccountType = computed(() => activeTradeAccount.value?.accountType ?? 'PERSON')

const activeTradeAccountCash = computed(() => {
  if (activeTradeAccount.value?.accountType === 'COMPANY') {
    return activeTradeAccount.value.cash
  }

  return auth.player?.personalCash ?? personAccount.value?.personalCash ?? null
})

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
    } else if (sortField.value === 'dividend') {
      cmp = a.dividendPayoutRatio - b.dividendPayoutRatio
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

function estimatedBuyCost(listing: StockExchangeListing): number {
  return getQuantity(listing.companyId) * listing.askPrice
}

function estimatedSellProceeds(listing: StockExchangeListing): number {
  return getQuantity(listing.companyId) * listing.bidPrice
}

function resolveTradeAccount(): { tradeAccountType: string; tradeAccountCompanyId: string | null } {
  if (activeTradeAccountType.value === 'COMPANY') {
    return {
      tradeAccountType: 'COMPANY',
      tradeAccountCompanyId: activeTradeAccount.value?.companyId ?? auth.player?.activeCompanyId ?? null,
    }
  }

  return { tradeAccountType: 'PERSON', tradeAccountCompanyId: null }
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

async function loadShareholders(companyId: string) {
  shareholdersLoadingByCompany.value[companyId] = true
  shareholdersErrorByCompany.value[companyId] = null

  try {
    const data = await gqlRequest<{ companyShareholders: CompanyOwnership | null }>(
      COMPANY_SHAREHOLDERS_QUERY,
      { companyId },
    )
    if (data.companyShareholders) {
      shareholdersByCompany.value[companyId] = data.companyShareholders
    }
  } catch (reason: unknown) {
    shareholdersErrorByCompany.value[companyId] =
      reason instanceof Error ? reason.message : t('stockExchange.shareholdersLoadFailed')
  } finally {
    shareholdersLoadingByCompany.value[companyId] = false
  }
}

async function toggleTradePanel(companyId: string) {
  expandedCompany.value = expandedCompany.value === companyId ? null : companyId
  errorByCompany.value[companyId] = null
  successByCompany.value[companyId] = null

  if (expandedCompany.value === companyId) {
    const loadTasks: Promise<void>[] = []
    if (!priceHistoryByCompany.value[companyId]) {
      loadTasks.push(loadPriceHistory(companyId))
    }
    if (!shareholdersByCompany.value[companyId]) {
      loadTasks.push(loadShareholders(companyId))
    }
    await Promise.all(loadTasks)
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

  const { tradeAccountType, tradeAccountCompanyId } = resolveTradeAccount()

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

function openMergeDialog(companyId: string) {
  mergeDialogCompanyId.value = companyId
  mergeDestinationCompanyId.value = controlledCompanies.value[0]?.id ?? ''
  mergeError.value = null
  mergeSuccess.value = null
}

function closeMergeDialog() {
  mergeDialogCompanyId.value = null
  mergeError.value = null
  mergeSuccess.value = null
}

async function executeMerge() {
  const targetCompanyId = mergeDialogCompanyId.value
  if (!targetCompanyId || !mergeDestinationCompanyId.value) return

  mergeLoading.value = true
  mergeError.value = null
  mergeSuccess.value = null

  try {
    const data = await gqlRequest<{ mergeCompany: MergeCompanyResult }>(MERGE_MUTATION, {
      input: {
        targetCompanyId,
        destinationCompanyId: mergeDestinationCompanyId.value,
      },
    })
    mergeSuccess.value = data.mergeCompany
    await Promise.all([loadData(true), auth.fetchMe()])
  } catch (reason: unknown) {
    mergeError.value = reason instanceof Error ? reason.message : t('stockExchange.actionFailed')
  } finally {
    mergeLoading.value = false
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

// --- Pie chart helpers ---

const PIE_COLORS = [
  '#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f',
  '#edc948', '#b07aa1', '#ff9da7', '#9c755f', '#bab0ac',
]

const PUBLIC_FLOAT_COLOR = '#c8c8c8'
const PIE_OTHER_THRESHOLD = 0.02 // holders < 2% are grouped as "Other"
const PIE_MAX_NAMED_SLICES = 8   // at most 8 named slices before grouping

type PieSlice = {
  label: string
  ratio: number
  color: string
  isPublicFloat?: boolean
  isOther?: boolean
}

function buildPieSlices(ownership: CompanyOwnership): PieSlice[] {
  if (ownership.totalSharesIssued <= 0) return []

  const slices: PieSlice[] = []
  const holders = ownership.shareholders
  let namedSliceCount = 0

  // Separate prominent holders from minor ones
  const prominentHolders = holders.filter((h) => h.ownershipRatio >= PIE_OTHER_THRESHOLD)
  const minorHolders = holders.filter((h) => h.ownershipRatio < PIE_OTHER_THRESHOLD)

  // If there are too many, cap and push rest to "other"
  const displayHolders =
    prominentHolders.length > PIE_MAX_NAMED_SLICES
      ? prominentHolders.slice(0, PIE_MAX_NAMED_SLICES)
      : prominentHolders
  const overflowHolders =
    prominentHolders.length > PIE_MAX_NAMED_SLICES
      ? [...prominentHolders.slice(PIE_MAX_NAMED_SLICES), ...minorHolders]
      : minorHolders

  for (const holder of displayHolders) {
    slices.push({
      label: holder.holderName,
      ratio: holder.ownershipRatio,
      // ?? fallback needed for TypeScript strict noUncheckedIndexedAccess, never reached at runtime
      color: PIE_COLORS[namedSliceCount % PIE_COLORS.length] ?? '#808080',
    })
    namedSliceCount++
  }

  // Group "other" named shareholders
  const otherNamedRatio = overflowHolders.reduce((sum, h) => sum + h.ownershipRatio, 0)
  if (otherNamedRatio > 0.0001) {
    slices.push({
      label: t('stockExchange.shareholdersOther'),
      ratio: otherNamedRatio,
      // ?? fallback needed for TypeScript strict noUncheckedIndexedAccess, never reached at runtime
      color: PIE_COLORS[namedSliceCount % PIE_COLORS.length] ?? '#808080',
      isOther: true,
    })
  }

  // Public float slice
  const floatRatio =
    ownership.totalSharesIssued > 0
      ? ownership.publicFloatShares / ownership.totalSharesIssued
      : 0
  if (floatRatio > 0.0001) {
    slices.push({
      label: t('stockExchange.shareholdersPublicFloatLabel'),
      ratio: floatRatio,
      color: PUBLIC_FLOAT_COLOR,
      isPublicFloat: true,
    })
  }

  return slices
}

/** Converts a list of pie slices into SVG path data for a donut chart. */
function buildDonutPaths(slices: PieSlice[], cx: number, cy: number, r: number, innerR: number) {
  const paths: { d: string; color: string; label: string; ratio: number; isPublicFloat?: boolean; isOther?: boolean }[] = []
  if (slices.length === 0) return paths

  let startAngle = -Math.PI / 2 // Start at 12 o'clock

  for (const slice of slices) {
    const sweep = slice.ratio * 2 * Math.PI
    const endAngle = startAngle + sweep

    // Outer arc
    const x1 = cx + r * Math.cos(startAngle)
    const y1 = cy + r * Math.sin(startAngle)
    const x2 = cx + r * Math.cos(endAngle)
    const y2 = cy + r * Math.sin(endAngle)
    // Inner arc (for donut)
    const ix1 = cx + innerR * Math.cos(endAngle)
    const iy1 = cy + innerR * Math.sin(endAngle)
    const ix2 = cx + innerR * Math.cos(startAngle)
    const iy2 = cy + innerR * Math.sin(startAngle)

    const largeArc = sweep > Math.PI ? 1 : 0

    const d =
      `M ${x1} ${y1}` +
      ` A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2}` +
      ` L ${ix1} ${iy1}` +
      ` A ${innerR} ${innerR} 0 ${largeArc} 0 ${ix2} ${iy2}` +
      ` Z`

    paths.push({ d, color: slice.color, label: slice.label, ratio: slice.ratio, isPublicFloat: slice.isPublicFloat, isOther: slice.isOther })
    startAngle = endAngle
  }

  return paths
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
                  <th>
                    <button class="sort-btn" @click="toggleSort('dividend')">
                      {{ t('stockExchange.dividendPayout') }} <span aria-hidden="true">{{ sortIcon('dividend') }}</span>
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
                        v-if="listing.canMerge"
                        class="listing-chip listing-chip--merge"
                      >{{ t('stockExchange.mergeReady') }}</span>
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
                    <td class="dividend-cell">
                      <span class="dividend-badge">{{ formatPercent(listing.dividendPayoutRatio) }}</span>
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
                      <button
                        v-if="listing.canMerge"
                        class="btn btn-warning btn-sm"
                        @click="openMergeDialog(listing.companyId)"
                      >
                        {{ t('stockExchange.mergeCompany') }}
                      </button>
                    </td>
                  </tr>

                  <tr v-if="expandedCompany === listing.companyId" class="trade-panel-row">
                    <td :colspan="personAccount ? 7 : 6">
                      <div class="trade-panel">
                        <div class="company-snapshot">
                          <dl class="snapshot-grid">
                            <div class="snapshot-item">
                              <dt>{{ t('stockExchange.totalSharesLabel') }}</dt>
                              <dd>{{ formatShares(listing.totalSharesIssued) }}</dd>
                            </div>
                            <div class="snapshot-item">
                              <dt>{{ t('stockExchange.publicFloatLabel') }}</dt>
                              <dd>
                                {{ formatShares(listing.publicFloatShares) }}
                                <span class="snapshot-pct">({{ formatPercent(listing.publicFloatShares / listing.totalSharesIssued) }})</span>
                              </dd>
                            </div>
                            <div class="snapshot-item">
                              <dt>{{ t('stockExchange.dividendPayoutLabel') }}</dt>
                              <dd>{{ formatPercent(listing.dividendPayoutRatio) }}</dd>
                            </div>
                          </dl>
                        </div>

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
                          <div class="trade-order-panel">
                            <div class="trade-order-header">
                              <div class="trade-order-context">
                                <span class="trade-order-caption">{{ t('stockExchange.tradeAccountLabel') }}</span>
                                <strong class="trade-order-name">{{ activeTradeAccountName }}</strong>
                                <p class="trade-order-hint">
                                  {{
                                    activeTradeAccountType === 'COMPANY'
                                      ? t('stockExchange.tradeAccountHintCompany')
                                      : t('stockExchange.tradeAccountHintPerson')
                                  }}
                                </p>
                              </div>
                              <span v-if="activeTradeAccountCash !== null" class="trade-account-cash">
                                {{ formatCurrency(activeTradeAccountCash) }}
                              </span>
                            </div>

                            <div class="trade-order-controls">
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

                              <div class="trade-actions-card">
                                <div class="trade-actions-header">
                                  <span class="trade-actions-caption">{{ t('stockExchange.tradeActionsLabel') }}</span>
                                  <span class="trade-actions-hint">{{ t('stockExchange.tradeActionsHint') }}</span>
                                </div>

                                <div class="trade-actions">
                                  <div class="trade-action-group">
                                    <button
                                      class="btn btn-primary"
                                      :disabled="actionLoadingKey === `buy-${listing.companyId}`"
                                      @click="executeTrade('buy', listing.companyId)"
                                    >
                                      {{ t('stockExchange.buyAt', { price: formatCurrency(listing.askPrice) }) }}
                                    </button>
                                    <span class="trade-est" aria-live="polite">
                                      {{ t('stockExchange.estimatedCost', { total: formatCurrency(estimatedBuyCost(listing)) }) }}
                                    </span>
                                  </div>
                                  <div class="trade-action-group">
                                    <button
                                      class="btn btn-secondary"
                                      :disabled="actionLoadingKey === `sell-${listing.companyId}`"
                                      @click="executeTrade('sell', listing.companyId)"
                                    >
                                      {{ t('stockExchange.sellAt', { price: formatCurrency(listing.bidPrice) }) }}
                                    </button>
                                    <span class="trade-est" aria-live="polite">
                                      {{ t('stockExchange.estimatedProceeds', { total: formatCurrency(estimatedSellProceeds(listing)) }) }}
                                    </span>
                                  </div>
                                </div>
                              </div>
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

                        <!-- Shareholders ownership panel -->
                        <div class="shareholders-panel">
                          <div class="history-panel__header">
                            <h3>{{ t('stockExchange.shareholdersTitle') }}</h3>
                            <span class="history-panel__hint">{{ t('stockExchange.shareholdersDesc') }}</span>
                          </div>

                          <p v-if="shareholdersLoadingByCompany[listing.companyId]" class="history-panel__state">
                            {{ t('stockExchange.shareholdersLoading') }}
                          </p>
                          <p
                            v-else-if="shareholdersErrorByCompany[listing.companyId]"
                            class="history-panel__state history-panel__state--error"
                          >
                            {{ shareholdersErrorByCompany[listing.companyId] }}
                          </p>
                          <template v-else-if="shareholdersByCompany[listing.companyId]">
                            <div v-for="ow in [shareholdersByCompany[listing.companyId]]" :key="ow ? ow.companyId : listing.companyId">
                            <template v-if="ow">
                            <div class="shareholders-summary">
                              <span class="shareholders-summary__item">
                                {{ t('stockExchange.shareholdersTotalLabel') }}:
                                <strong>{{ formatShares(ow.totalSharesIssued) }}</strong>
                              </span>
                              <span class="shareholders-summary__item">
                                {{ t('stockExchange.shareholdersCountLabel', { count: ow.shareholderCount }) }}
                              </span>
                              <span v-if="ow.shareholders.length > 0" class="shareholders-summary__item">
                                {{ t('stockExchange.shareholdersLargestHolder') }}:
                                <strong>{{ ow.shareholders[0]!.holderName }}</strong>
                                ({{ formatPercent(ow.shareholders[0]!.ownershipRatio) }})
                              </span>
                            </div>

                            <p v-if="ow.shareholders.length === 0" class="history-panel__state">
                              {{ t('stockExchange.shareholdersEmpty') }}
                            </p>
                            <p v-else-if="ow.shareholders.length === 1 && ow.publicFloatShares === 0" class="history-panel__state shareholders-single-owner">
                              {{ t('stockExchange.shareholdersSingleOwner') }}
                            </p>

                            <div v-if="ow.shareholders.length > 0" class="shareholders-layout">
                              <!-- Pie chart -->
                              <div class="ownership-chart">
                                <svg
                                  viewBox="0 0 160 160"
                                  width="160"
                                  height="160"
                                  :aria-label="t('stockExchange.shareholdersPieChartLabel')"
                                  role="img"
                                  class="ownership-donut"
                                >
                                  <template v-if="buildPieSlices(ow).length > 0">
                                    <path
                                      v-for="(seg, idx) in buildDonutPaths(buildPieSlices(ow), 80, 80, 72, 44)"
                                      :key="idx"
                                      :d="seg.d"
                                      :fill="seg.color"
                                      class="donut-segment"
                                    />
                                  </template>
                                  <circle v-else cx="80" cy="80" r="72" fill="#e0e0e0" />
                                </svg>

                                <!-- Legend -->
                                <ul class="ownership-legend" :aria-label="t('stockExchange.shareholdersPieChartLabel')">
                                  <li
                                    v-for="(seg, idx) in buildPieSlices(ow)"
                                    :key="idx"
                                    class="ownership-legend__item"
                                  >
                                    <span class="ownership-legend__swatch" :style="{ background: seg.color }" />
                                    <span class="ownership-legend__label">{{ seg.label }}</span>
                                    <span class="ownership-legend__pct">{{ formatPercent(seg.ratio) }}</span>
                                  </li>
                                </ul>
                              </div>

                              <!-- Shareholders table -->
                              <div class="shareholders-table-wrapper">
                                <table class="shareholders-table" :aria-label="`${listing.companyName} ${t('stockExchange.shareholdersTitle')}`">
                                  <thead>
                                    <tr>
                                      <th>{{ t('stockExchange.shareholdersHolder') }}</th>
                                      <th>{{ t('stockExchange.shareholdersShares') }}</th>
                                      <th>{{ t('stockExchange.shareholdersOwnership') }}</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    <tr
                                      v-for="holder in ow.shareholders"
                                      :key="`${holder.holderPlayerId ?? holder.holderCompanyId}`"
                                      class="shareholder-row"
                                    >
                                      <td>
                                        <span class="holder-name">{{ holder.holderName }}</span>
                                        <span class="holder-type-badge" :class="holder.holderType === 'PERSON' ? 'holder-type-badge--person' : 'holder-type-badge--company'">
                                          {{ holder.holderType === 'PERSON' ? t('stockExchange.shareholdersTypePerson') : t('stockExchange.shareholdersTypeCompany') }}
                                        </span>
                                      </td>
                                      <td>{{ formatShares(holder.shareCount) }}</td>
                                      <td>
                                        <div class="ownership-bar-cell">
                                          <div class="ownership-bar">
                                            <div class="ownership-bar__fill" :style="{ width: `${Math.min(holder.ownershipRatio * 100, 100)}%` }" />
                                          </div>
                                          <span class="ownership-bar__pct">{{ formatPercent(holder.ownershipRatio) }}</span>
                                        </div>
                                      </td>
                                    </tr>
                                    <!-- Public float row -->
                                    <tr
                                      v-if="ow.publicFloatShares > 0"
                                      class="shareholder-row shareholder-row--float"
                                    >
                                      <td>
                                        <span class="holder-name">{{ t('stockExchange.shareholdersPublicFloat') }}</span>
                                        <span class="holder-type-badge holder-type-badge--float">Float</span>
                                      </td>
                                      <td>{{ formatShares(ow.publicFloatShares) }}</td>
                                      <td>
                                        <div class="ownership-bar-cell">
                                          <div class="ownership-bar ownership-bar--float">
                                            <div
                                              class="ownership-bar__fill"
                                              :style="{ width: `${Math.min((ow.publicFloatShares / ow.totalSharesIssued) * 100, 100)}%` }"
                                            />
                                          </div>
                                          <span class="ownership-bar__pct">{{ formatPercent(ow.publicFloatShares / ow.totalSharesIssued) }}</span>
                                        </div>
                                      </td>
                                    </tr>
                                  </tbody>
                                </table>
                              </div>
                            </div>
                            </template>
                            </div>
                          </template>
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

  <!-- Merge company dialog -->
  <Teleport to="body">
    <div
      v-if="mergeDialogCompanyId"
      class="merge-overlay"
      role="dialog"
      aria-modal="true"
      :aria-label="t('stockExchange.mergeDialogTitle')"
    >
      <div class="merge-dialog">
        <h2 class="merge-dialog__title">{{ t('stockExchange.mergeDialogTitle') }}</h2>
        <template v-if="mergeSuccess">
          <p class="merge-dialog__success" role="status">
            {{ t('stockExchange.mergeSuccessMsg', {
              absorbed: mergeSuccess.absorbedCompanyName,
              destination: mergeSuccess.destinationCompanyName,
              buildings: mergeSuccess.buildingsTransferred,
              cash: formatCurrency(mergeSuccess.cashTransferred),
            }) }}
          </p>
          <div class="merge-dialog__actions">
            <button class="btn btn-primary" @click="closeMergeDialog">{{ t('common.close') }}</button>
          </div>
        </template>
        <template v-else>
          <p class="merge-dialog__desc">{{ t('stockExchange.mergeDialogDesc') }}</p>
          <p class="merge-dialog__eligibility">{{ t('stockExchange.mergeEligibilityHint') }}</p>
          <label class="trade-field">
            <span>{{ t('stockExchange.mergeDestinationLabel') }}</span>
            <select v-model="mergeDestinationCompanyId" class="trade-select" :aria-label="t('stockExchange.mergeDestinationLabel')">
              <option v-for="company in controlledCompanies" :key="company.id" :value="company.id">
                {{ company.name }}
              </option>
            </select>
          </label>
          <p v-if="mergeError" class="trade-feedback trade-feedback--error" role="alert">{{ mergeError }}</p>
          <div class="merge-dialog__actions">
            <button
              class="btn btn-warning"
              :disabled="mergeLoading || !mergeDestinationCompanyId"
              @click="executeMerge"
            >
              {{ mergeLoading ? t('common.loading') : t('stockExchange.mergeConfirm') }}
            </button>
            <button class="btn btn-ghost" :disabled="mergeLoading" @click="closeMergeDialog">
              {{ t('common.cancel') }}
            </button>
          </div>
        </template>
      </div>
    </div>
  </Teleport>
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
  display: grid;
  gap: 1rem;
}

.trade-order-panel {
  display: grid;
  gap: 1rem;
  padding: 1rem;
  border: 1px solid var(--color-border);
  border-radius: 14px;
  background: color-mix(in srgb, var(--color-surface) 88%, var(--color-primary) 12%);
}

.trade-order-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.trade-order-context,
.trade-actions-header {
  display: grid;
  gap: 0.25rem;
}

.trade-order-caption,
.trade-actions-caption {
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.trade-order-name {
  font-size: 1rem;
  color: var(--color-text-primary);
}

.trade-order-hint,
.trade-actions-hint {
  margin: 0;
  font-size: 0.82rem;
  color: var(--color-text-secondary);
}

.trade-account-cash {
  display: inline-flex;
  align-items: center;
  padding: 0.35rem 0.7rem;
  border-radius: 999px;
  background: color-mix(in srgb, var(--color-primary) 12%, var(--color-surface));
  color: var(--color-text-primary);
  font-size: 0.82rem;
  font-variant-numeric: tabular-nums;
  font-weight: 700;
}

.trade-order-controls {
  display: grid;
  grid-template-columns: minmax(140px, 180px) minmax(0, 1fr);
  gap: 1rem;
  align-items: end;
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
  width: 100%;
}

.trade-actions-card {
  display: grid;
  gap: 0.75rem;
}

.trade-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
  align-items: start;
}

.trade-action-group {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  align-items: stretch;
}

.trade-action-group .btn {
  width: 100%;
  justify-content: center;
}

.trade-est {
  font-size: 0.78rem;
  color: var(--color-text-muted, var(--color-text-secondary));
  font-variant-numeric: tabular-nums;
}

.company-snapshot {
  background: var(--color-surface-secondary, var(--color-surface));
  border: 1px solid var(--color-border);
  border-radius: 10px;
  padding: 0.75rem 1rem;
  margin-bottom: 0.75rem;
}

.snapshot-grid {
  display: flex;
  gap: 2rem;
  flex-wrap: wrap;
  margin: 0;
}

.snapshot-item {
  display: grid;
  gap: 0.15rem;
}

.snapshot-item dt {
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--color-text-muted, var(--color-text-secondary));
}

.snapshot-item dd {
  margin: 0;
  font-size: 0.95rem;
  font-variant-numeric: tabular-nums;
}

.snapshot-pct {
  font-size: 0.78rem;
  color: var(--color-text-muted, var(--color-text-secondary));
  margin-left: 0.25rem;
}

.dividend-cell {
  white-space: nowrap;
}

.dividend-badge {
  display: inline-block;
  background: color-mix(in srgb, var(--color-success, #22c55e) 14%, transparent);
  color: var(--color-success, #22c55e);
  padding: 0.2rem 0.55rem;
  border-radius: 6px;
  font-size: 0.82rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
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

.listing-chip--merge {
  background: color-mix(in srgb, var(--color-warning, #f59e0b) 18%, var(--color-surface));
  color: var(--color-warning, #f59e0b);
  border: 1px solid color-mix(in srgb, var(--color-warning, #f59e0b) 40%, transparent);
}

.merge-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.merge-dialog {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  padding: 2rem;
  max-width: 480px;
  width: 90%;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  box-shadow: var(--shadow-lg, 0 20px 40px rgba(0, 0, 0, 0.4));
}

.merge-dialog__title {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 0;
}

.merge-dialog__desc {
  color: var(--color-text-secondary);
  font-size: 0.95rem;
  margin: 0;
}

.merge-dialog__eligibility {
  background: color-mix(in srgb, var(--color-warning, #f59e0b) 10%, var(--color-surface));
  border: 1px solid color-mix(in srgb, var(--color-warning, #f59e0b) 30%, transparent);
  border-radius: 8px;
  padding: 0.6rem 0.9rem;
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  margin: 0;
}

.merge-dialog__success {
  color: var(--color-success, #22c55e);
  font-size: 0.95rem;
  margin: 0;
}

.merge-dialog__actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-top: 0.25rem;
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

  .trade-order-controls {
    grid-template-columns: 1fr;
  }

  .trade-actions {
    grid-template-columns: 1fr;
  }

  .pagination-bar {
    justify-content: flex-start;
  }
}

/* ── Shareholders panel ───────────────────────────────────────────────── */

.shareholders-panel {
  display: grid;
  gap: 0.6rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--color-border);
  margin-top: 0.5rem;
}

.shareholders-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1.25rem;
  font-size: 0.82rem;
  color: var(--color-text-secondary);
}

.shareholders-summary__item strong {
  color: var(--color-text-primary);
}

.shareholders-single-owner {
  color: var(--color-text-secondary);
  font-size: 0.85rem;
  font-style: italic;
}

.shareholders-layout {
  display: flex;
  gap: 1.5rem;
  flex-wrap: wrap;
  align-items: flex-start;
}

/* Donut chart */
.ownership-chart {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  min-width: 160px;
}

.ownership-donut {
  flex-shrink: 0;
}

.donut-segment {
  transition: opacity 0.15s;
}

.donut-segment:hover {
  opacity: 0.82;
}

/* Legend */
.ownership-legend {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  font-size: 0.78rem;
  min-width: 140px;
}

.ownership-legend__item {
  display: flex;
  align-items: center;
  gap: 0.45rem;
}

.ownership-legend__swatch {
  display: inline-block;
  width: 10px;
  height: 10px;
  border-radius: 2px;
  flex-shrink: 0;
}

.ownership-legend__label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text-primary);
}

.ownership-legend__pct {
  color: var(--color-text-secondary);
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}

/* Shareholders table */
.shareholders-table-wrapper {
  overflow-x: auto;
  flex: 1;
  min-width: 0;
}

.shareholders-table {
  border-collapse: collapse;
  width: 100%;
  font-size: 0.82rem;
}

.shareholders-table th {
  text-align: left;
  padding: 0.3rem 0.5rem;
  border-bottom: 1px solid var(--color-border);
  color: var(--color-text-secondary);
  font-weight: 600;
  white-space: nowrap;
}

.shareholders-table td {
  padding: 0.35rem 0.5rem;
  border-bottom: 1px solid color-mix(in srgb, var(--color-border) 60%, transparent);
  vertical-align: middle;
}

.shareholder-row--float td {
  color: var(--color-text-secondary);
  font-style: italic;
}

.holder-name {
  margin-right: 0.35rem;
  color: var(--color-text-primary);
}

.holder-type-badge {
  display: inline-block;
  font-size: 0.68rem;
  font-weight: 600;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
  vertical-align: middle;
}

.holder-type-badge--person {
  background: color-mix(in srgb, var(--color-accent, #6366f1) 16%, var(--color-surface));
  color: var(--color-accent, #6366f1);
}

.holder-type-badge--company {
  background: color-mix(in srgb, var(--color-warning, #f59e0b) 16%, var(--color-surface));
  color: color-mix(in srgb, var(--color-warning, #f59e0b) 80%, var(--color-text-primary));
}

.holder-type-badge--float {
  background: color-mix(in srgb, var(--color-text-secondary) 12%, var(--color-surface));
  color: var(--color-text-secondary);
}

/* Ownership bar */
.ownership-bar-cell {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.ownership-bar {
  height: 8px;
  background: color-mix(in srgb, var(--color-accent, #6366f1) 18%, var(--color-surface));
  border-radius: 4px;
  flex: 1;
  min-width: 60px;
  overflow: hidden;
}

.ownership-bar--float {
  background: color-mix(in srgb, var(--color-text-secondary) 14%, var(--color-surface));
}

.ownership-bar__fill {
  height: 100%;
  background: var(--color-accent, #6366f1);
  border-radius: 4px;
  transition: width 0.3s;
}

.ownership-bar--float .ownership-bar__fill {
  background: var(--color-text-secondary);
}

.ownership-bar__pct {
  font-size: 0.78rem;
  font-variant-numeric: tabular-nums;
  color: var(--color-text-secondary);
  white-space: nowrap;
  min-width: 3.5rem;
  text-align: right;
}
</style>
