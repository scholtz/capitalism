<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useScrollPreservation } from '@/composables/useScrollPreservation'
import { gqlRequest } from '@/lib/graphql'
import { formatInGameTime } from '@/lib/gameTime'
import type { CompanyLedgerSummary, LedgerEntryResult } from '@/types'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()

const companyId = computed(() => route.params.companyId as string)
const ledger = ref<CompanyLedgerSummary | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const drillCategory = ref<string | null>(null)
const drillEntries = ref<LedgerEntryResult[]>([])
const drillLoading = ref(false)
const selectedGameYear = ref<number | null>(null)
const selectedResolvedGameYear = computed(() => selectedGameYear.value ?? ledger.value?.gameYear ?? null)

const LEDGER_QUERY = `
  query GetCompanyLedger($companyId: UUID!, $gameYear: Int) {
    companyLedger(companyId: $companyId, gameYear: $gameYear) {
      companyId companyName gameYear isCurrentGameYear currentCash
      totalRevenue totalPurchasingCosts totalShippingCosts totalLaborCosts totalEnergyCosts totalMarketingCosts totalTaxPaid totalOtherCosts taxableIncome estimatedIncomeTax netIncome
      propertyValue propertyAppreciation buildingValue inventoryValue totalAssets totalPropertyPurchases
      totalStockPurchaseCashOut totalStockSaleCashIn cashFromOperations cashFromInvestments firstRecordedTick lastRecordedTick
      incomeTaxDueAtTick incomeTaxDueGameTimeUtc incomeTaxDueGameYear isIncomeTaxSettled
      history {
        gameYear isCurrentGameYear totalRevenue totalLaborCosts totalEnergyCosts netIncome totalTaxPaid taxableIncome estimatedIncomeTax firstRecordedTick lastRecordedTick
      }
      buildingSummaries { buildingId buildingName buildingType revenue costs }
    }
  }
`

const DRILL_QUERY = `
  query GetLedgerDrillDown($companyId: UUID!, $category: String!, $gameYear: Int) {
    ledgerDrillDown(companyId: $companyId, category: $category, gameYear: $gameYear) {
      id category description amount recordedAtTick
      buildingId buildingName buildingUnitId
      productTypeId productName resourceTypeId resourceName
    }
  }
`

async function fetchLedger(isRefresh = false) {
  if (!isRefresh) loading.value = true
  error.value = null
  try {
    const data = await gqlRequest<{ companyLedger: CompanyLedgerSummary | null }>(LEDGER_QUERY, {
      companyId: companyId.value,
      gameYear: selectedGameYear.value,
    })
    if (!data.companyLedger) {
      error.value = t('ledger.notFound')
      return
    }
    ledger.value = data.companyLedger
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('ledger.loadFailed')
  } finally {
    if (!isRefresh) loading.value = false
  }
}

async function loadDrillEntries(category: string, isRefresh = false) {
  if (!isRefresh) drillLoading.value = true
  drillEntries.value = []
  try {
    const data = await gqlRequest<{ ledgerDrillDown: LedgerEntryResult[] }>(DRILL_QUERY, {
      companyId: companyId.value,
      category,
      gameYear: selectedResolvedGameYear.value,
    })
    drillEntries.value = data.ledgerDrillDown
  } catch {
    drillEntries.value = []
  } finally {
    if (!isRefresh) drillLoading.value = false
  }
}

async function toggleDrill(category: string) {
  if (drillCategory.value === category) {
    drillCategory.value = null
    drillEntries.value = []
    return
  }
  drillCategory.value = category
  await loadDrillEntries(category)
}

async function selectGameYear(gameYear: number | null) {
  selectedGameYear.value = gameYear
  drillCategory.value = null
  drillEntries.value = []
  await fetchLedger()
}

function formatAmount(amount: number): string {
  return `${amount < 0 ? '-' : ''}$${Math.abs(amount).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function amountClass(amount: number): string {
  return amount >= 0 ? 'amount-positive' : 'amount-negative'
}

function formatGameTime(value: string): string {
  return formatInGameTime(value, locale.value)
}

onMounted(fetchLedger)

useTickRefresh(async () => {
  if (selectedGameYear.value !== null && !ledger.value?.isCurrentGameYear) {
    return
  }

  const scrollPos = saveScrollPosition()
  await fetchLedger(true)
  if (drillCategory.value) {
    await loadDrillEntries(drillCategory.value, true)
  }
  await restoreScrollPosition(scrollPos)
})
</script>

<template>
  <div class="ledger-view">
    <div class="ledger-header">
      <button class="btn btn-ghost" @click="router.push('/dashboard')">← {{ t('common.back') }}</button>
      <div>
        <p class="ledger-eyebrow">{{ t('ledger.eyebrow') }}</p>
        <h1 class="ledger-title">{{ ledger?.companyName ?? t('ledger.title') }}</h1>
      </div>
    </div>

    <div v-if="loading" class="state-box">
      <span class="state-icon">⏳</span>
      <p>{{ t('common.loading') }}</p>
    </div>

    <div v-else-if="error" class="state-box state-error">
      <span class="state-icon">⚠️</span>
      <p>{{ error }}</p>
      <button class="btn btn-secondary" @click="() => fetchLedger()">{{ t('common.tryAgain') }}</button>
    </div>

    <div v-else-if="ledger" class="ledger-content">
      <div class="kpi-row">
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.gameYear') }}</span>
          <span class="kpi-value">{{ t('ledger.gameYearLabel', { year: ledger.gameYear }) }}</span>
        </div>
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.cash') }}</span>
          <span class="kpi-value" :class="amountClass(ledger.currentCash)">{{ formatAmount(ledger.currentCash) }}</span>
        </div>
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.netIncome') }}</span>
          <span class="kpi-value" :class="amountClass(ledger.netIncome)">{{ formatAmount(ledger.netIncome) }}</span>
        </div>
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.taxableIncome') }}</span>
          <span class="kpi-value" :class="amountClass(ledger.taxableIncome)">{{ formatAmount(ledger.taxableIncome) }}</span>
        </div>
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.estimatedIncomeTax') }}</span>
          <span class="kpi-value amount-negative">{{ formatAmount(-ledger.estimatedIncomeTax) }}</span>
        </div>
        <div class="kpi-card">
          <span class="kpi-label">{{ t('ledger.totalAssets') }}</span>
          <span class="kpi-value">{{ formatAmount(ledger.totalAssets) }}</span>
        </div>
      </div>

      <div v-if="ledger.lastRecordedTick === 0" class="info-banner">
        <span>📊</span>
        <span>{{ t('ledger.noHistoryYet') }}</span>
      </div>
      <p v-else class="tick-range-note">
        {{ t('ledger.dataRange', { from: ledger.firstRecordedTick, to: ledger.lastRecordedTick }) }}
      </p>

      <div class="year-meta-row">
        <div class="statement-card meta-card">
          <h2 class="statement-title">🧾 {{ t('ledger.incomeTaxSchedule') }}</h2>
          <p class="meta-copy">
            {{ ledger.isIncomeTaxSettled ? t('ledger.incomeTaxSettledAtTick', { tick: ledger.incomeTaxDueAtTick }) : t('ledger.incomeTaxDueAtTick', { tick: ledger.incomeTaxDueAtTick }) }}
          </p>
          <p class="meta-copy">{{ t('ledger.incomeTaxDueAtTime', { time: formatGameTime(ledger.incomeTaxDueGameTimeUtc) }) }}</p>
          <p class="meta-copy">{{ t('ledger.incomeTaxDueYear', { year: ledger.incomeTaxDueGameYear }) }}</p>
        </div>

        <div class="statement-card meta-card">
          <h2 class="statement-title">🗂️ {{ t('ledger.historyTitle') }}</h2>
          <div class="history-buttons">
            <button
              v-for="yearItem in ledger.history"
              :key="yearItem.gameYear"
              type="button"
              class="history-button"
              :class="{ active: yearItem.gameYear === selectedResolvedGameYear }"
              @click="selectGameYear(yearItem.isCurrentGameYear ? null : yearItem.gameYear)"
            >
              <span>{{ t('ledger.gameYearShort', { year: yearItem.gameYear }) }}</span>
              <span :class="amountClass(yearItem.netIncome)">{{ formatAmount(yearItem.netIncome) }}</span>
            </button>
          </div>
        </div>
      </div>

      <div v-if="!ledger.isCurrentGameYear" class="info-banner historical-note">
        <span>🕰️</span>
        <span>{{ t('ledger.historicalYearNote') }}</span>
      </div>

      <div class="statements-grid">
        <div class="statement-card">
          <h2 class="statement-title">📈 {{ t('ledger.incomeStatement') }}</h2>
          <div class="statement-rows">
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.revenue') }}</span>
              <span class="amount-positive">{{ formatAmount(ledger.totalRevenue) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'REVENUE' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.revenue')" @click="toggleDrill('REVENUE')">
                {{ drillCategory === 'REVENUE' ? '▲' : '▼' }}
              </button>
            </div>
            <div class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.purchasingCosts') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalPurchasingCosts) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'PURCHASING_COST' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.purchasingCosts')"
                @click="toggleDrill('PURCHASING_COST')"
              >
                {{ drillCategory === 'PURCHASING_COST' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalShippingCosts > 0" class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.shippingCosts') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalShippingCosts) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'SHIPPING_COST' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.shippingCosts')" @click="toggleDrill('SHIPPING_COST')">
                {{ drillCategory === 'SHIPPING_COST' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalLaborCosts > 0" class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.laborCosts') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalLaborCosts) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'LABOR_COST' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.laborCosts')" @click="toggleDrill('LABOR_COST')">
                {{ drillCategory === 'LABOR_COST' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalEnergyCosts > 0" class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.energyCosts') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalEnergyCosts) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'ENERGY_COST' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.energyCosts')" @click="toggleDrill('ENERGY_COST')">
                {{ drillCategory === 'ENERGY_COST' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalMarketingCosts > 0" class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.marketingCosts') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalMarketingCosts) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'MARKETING' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.marketingCosts')" @click="toggleDrill('MARKETING')">
                {{ drillCategory === 'MARKETING' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalTaxPaid > 0" class="statement-row cost-row">
              <span class="row-label">{{ t('ledger.taxPaid') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalTaxPaid) }}</span>
              <button class="drill-btn" :class="{ active: drillCategory === 'TAX' }" :aria-label="t('ledger.drillDown') + ': ' + t('ledger.taxPaid')" @click="toggleDrill('TAX')">
                {{ drillCategory === 'TAX' ? '▲' : '▼' }}
              </button>
            </div>
            <div class="statement-row total-row">
              <span class="row-label">{{ t('ledger.netIncome') }}</span>
              <span :class="amountClass(ledger.netIncome)">{{ formatAmount(ledger.netIncome) }}</span>
            </div>
          </div>
        </div>

        <div class="statement-card">
          <h2 class="statement-title">📊 {{ t('ledger.balanceSheet') }}</h2>
          <div class="statement-rows">
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.cash') }}</span>
              <span>{{ formatAmount(ledger.currentCash) }}</span>
            </div>
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.propertyValue') }}</span>
              <span>{{ formatAmount(ledger.propertyValue) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'PROPERTY_PURCHASE' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.propertyValue')"
                @click="toggleDrill('PROPERTY_PURCHASE')"
              >
                {{ drillCategory === 'PROPERTY_PURCHASE' ? '▲' : '▼' }}
              </button>
            </div>
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.propertyAppreciation') }}</span>
              <span :class="amountClass(ledger.propertyAppreciation)">{{ formatAmount(ledger.propertyAppreciation) }}</span>
            </div>
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.buildingValue') }}</span>
              <span>{{ formatAmount(ledger.buildingValue) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'BUILDING_VALUE' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.buildingValue')"
                @click="toggleDrill('BUILDING_VALUE')"
              >
                {{ drillCategory === 'BUILDING_VALUE' ? '▲' : '▼' }}
              </button>
            </div>
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.inventoryValue') }}</span>
              <span>{{ formatAmount(ledger.inventoryValue) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'INVENTORY_VALUE' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.inventoryValue')"
                @click="toggleDrill('INVENTORY_VALUE')"
              >
                {{ drillCategory === 'INVENTORY_VALUE' ? '▲' : '▼' }}
              </button>
            </div>
            <div class="statement-row total-row">
              <span class="row-label">{{ t('ledger.totalAssets') }}</span>
              <span>{{ formatAmount(ledger.totalAssets) }}</span>
            </div>
          </div>
        </div>

        <div class="statement-card">
          <h2 class="statement-title">💵 {{ t('ledger.cashFlow') }}</h2>
          <div class="statement-rows">
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.cashFromOperations') }}</span>
              <span :class="amountClass(ledger.cashFromOperations)">{{ formatAmount(ledger.cashFromOperations) }}</span>
            </div>
            <div class="statement-row">
              <span class="row-label">{{ t('ledger.cashFromInvestments') }}</span>
              <span :class="amountClass(ledger.cashFromInvestments)">{{ formatAmount(ledger.cashFromInvestments) }}</span>
            </div>
            <div v-if="ledger.totalStockPurchaseCashOut > 0" class="statement-row">
              <span class="row-label">{{ t('ledger.stockPurchases') }}</span>
              <span class="amount-negative">{{ formatAmount(-ledger.totalStockPurchaseCashOut) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'STOCK_PURCHASE' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.stockPurchases')"
                @click="toggleDrill('STOCK_PURCHASE')"
              >
                {{ drillCategory === 'STOCK_PURCHASE' ? '▲' : '▼' }}
              </button>
            </div>
            <div v-if="ledger.totalStockSaleCashIn > 0" class="statement-row">
              <span class="row-label">{{ t('ledger.stockSales') }}</span>
              <span class="amount-positive">{{ formatAmount(ledger.totalStockSaleCashIn) }}</span>
              <button
                class="drill-btn"
                :class="{ active: drillCategory === 'STOCK_SALE' }"
                :aria-label="t('ledger.drillDown') + ': ' + t('ledger.stockSales')"
                @click="toggleDrill('STOCK_SALE')"
              >
                {{ drillCategory === 'STOCK_SALE' ? '▲' : '▼' }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <div v-if="drillCategory" class="drill-panel">
        <div class="drill-header">
          <h3>{{ t('ledger.drillDown') }}: {{ t(`ledger.category.${drillCategory}`) }}</h3>
          <button
            class="btn btn-ghost btn-sm"
            @click="
              () => {
                drillCategory = null
                drillEntries = []
              }
            "
          >
            ✕ {{ t('common.close') }}
          </button>
        </div>
        <div v-if="drillLoading" class="state-box-sm">{{ t('common.loading') }}</div>
        <div v-else-if="drillEntries.length === 0" class="state-box-sm">
          {{ t('ledger.noEntries') }}
        </div>
        <div v-else class="drill-table-wrapper">
          <table class="drill-table">
            <thead>
              <tr>
                <th>{{ t('ledger.description') }}</th>
                <th>{{ t('ledger.amount') }}</th>
                <th>{{ t('ledger.tick') }}</th>
                <th>{{ t('ledger.building') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in drillEntries" :key="entry.id">
                <td>{{ entry.productName ?? entry.resourceName ?? entry.description }}</td>
                <td :class="amountClass(entry.amount)">{{ formatAmount(entry.amount) }}</td>
                <td>{{ entry.recordedAtTick }}</td>
                <td>
                  <RouterLink v-if="entry.buildingId" :to="`/building/${entry.buildingId}`" class="link-btn">
                    {{ entry.buildingName ?? t('ledger.viewBuilding') }}
                  </RouterLink>
                  <span v-else>—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-if="ledger.buildingSummaries.length > 0" class="buildings-card">
        <h2 class="statement-title">🏭 {{ t('ledger.buildingsPerformance') }}</h2>
        <div class="buildings-table-wrapper">
          <table class="buildings-table">
            <thead>
              <tr>
                <th>{{ t('ledger.buildingName') }}</th>
                <th>{{ t('ledger.buildingType') }}</th>
                <th>{{ t('ledger.revenue') }}</th>
                <th>{{ t('ledger.costs') }}</th>
                <th>{{ t('ledger.profit') }}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in ledger.buildingSummaries" :key="b.buildingId">
                <td>{{ b.buildingName }}</td>
                <td>{{ b.buildingType }}</td>
                <td class="amount-positive">{{ formatAmount(b.revenue) }}</td>
                <td class="amount-negative">{{ formatAmount(-b.costs) }}</td>
                <td :class="amountClass(b.revenue - b.costs)">
                  {{ formatAmount(b.revenue - b.costs) }}
                </td>
                <td>
                  <RouterLink :to="`/building/${b.buildingId}`" class="btn btn-ghost btn-sm">
                    {{ t('ledger.manage') }}
                  </RouterLink>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ledger-view {
  padding: 2rem 1rem;
  max-width: 1200px;
  margin: 0 auto;
}
.ledger-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 2rem;
}
.ledger-eyebrow {
  font-size: 0.8125rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--color-accent);
  margin: 0 0 0.25rem;
}
.ledger-title {
  font-size: 1.75rem;
  margin: 0;
}
.kpi-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}
.kpi-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.kpi-label {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  font-weight: 500;
}
.kpi-value {
  font-size: 1.375rem;
  font-weight: 700;
}
.info-banner {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 1.5rem;
  color: var(--color-text-secondary);
}
.tick-range-note {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-bottom: 1rem;
}
.year-meta-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}
.meta-card {
  margin-bottom: 0;
}
.meta-copy {
  margin: 0.35rem 0;
  color: var(--color-text-secondary);
}
.history-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}
.history-button {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 120px;
  padding: 0.75rem 0.9rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  background: transparent;
  color: var(--color-text);
  cursor: pointer;
}
.history-button.active {
  border-color: var(--color-accent);
  background: var(--color-surface-hover);
}
.historical-note {
  margin-top: -0.25rem;
}
.statements-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1.5rem;
  margin-bottom: 1.5rem;
}
.statement-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
  padding: 1.5rem;
}
.statement-title {
  font-size: 1.0625rem;
  font-weight: 600;
  margin: 0 0 1rem;
}
.statement-rows {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.statement-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0;
}
.cost-row {
  color: var(--color-text-secondary);
}
.total-row {
  border-top: 1px solid var(--color-border);
  padding-top: 0.5rem;
  margin-top: 0.25rem;
  font-weight: 600;
}
.row-label {
  flex: 1;
  font-size: 0.9375rem;
}
.drill-btn {
  background: none;
  border: 1px solid var(--color-border);
  border-radius: 0.25rem;
  padding: 0 0.375rem;
  cursor: pointer;
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  transition: all 0.15s;
  margin-left: auto;
}
.drill-btn:hover,
.drill-btn.active {
  background: var(--color-accent);
  color: #fff;
  border-color: var(--color-accent);
}
.amount-positive {
  color: var(--color-success, #22c55e);
}
.amount-negative {
  color: var(--color-danger, #ef4444);
}
.drill-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}
.drill-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}
.drill-header h3 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}
.state-box-sm {
  text-align: center;
  color: var(--color-text-secondary);
  padding: 1rem;
}
.drill-table-wrapper,
.buildings-table-wrapper {
  overflow-x: auto;
}
.drill-table,
.buildings-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.drill-table th,
.drill-table td,
.buildings-table th,
.buildings-table td {
  text-align: left;
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
}
.drill-table th,
.buildings-table th {
  color: var(--color-text-secondary);
  font-weight: 600;
  font-size: 0.8125rem;
}
.buildings-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.75rem;
  padding: 1.5rem;
  margin-bottom: 1.5rem;
}
.link-btn {
  color: var(--color-accent);
  text-decoration: none;
  font-size: 0.875rem;
}
.link-btn:hover {
  text-decoration: underline;
}
.btn-sm {
  padding: 0.25rem 0.75rem;
  font-size: 0.875rem;
}
.state-box {
  text-align: center;
  padding: 3rem 1rem;
  color: var(--color-text-secondary);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}
.state-error {
  color: var(--color-danger, #ef4444);
}
.state-icon {
  font-size: 2.5rem;
}
</style>
