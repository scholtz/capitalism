<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CompanyLedgerSummary } from '@/types'

interface Props {
  ledger: CompanyLedgerSummary | null
  loading: boolean
}

const props = defineProps<Props>()
const { t, locale } = useI18n()

const totalCosts = computed(() => {
  if (!props.ledger) return 0
  const l = props.ledger
  return (
    l.totalPurchasingCosts +
    l.totalLaborCosts +
    l.totalEnergyCosts +
    l.totalMarketingCosts +
    l.totalOtherCosts +
    l.totalTaxPaid
  )
})

/** Use the backend-authoritative netIncome (includes taxes) as the single source of truth. */
const netProfit = computed(() => {
  if (!props.ledger) return 0
  return props.ledger.netIncome
})

function formatAmount(value: number): string {
  const abs = Math.abs(value)
  const sign = value < 0 ? '-' : value > 0 ? '+' : ''
  return `${sign}$${abs.toLocaleString(locale.value, { maximumFractionDigits: 0 })}`
}

function formatAmountPlain(value: number): string {
  return `$${Math.abs(value).toLocaleString(locale.value, { maximumFractionDigits: 0 })}`
}

function profitClass(value: number): string {
  if (value > 0) return 'amount-positive'
  if (value < 0) return 'amount-negative'
  return 'amount-neutral'
}
</script>

<template>
  <div class="financial-summary-card" aria-labelledby="financial-summary-title">
    <h3 id="financial-summary-title" class="financial-summary-title">
      {{ t('financialSummary.title') }}
    </h3>
    <div v-if="loading" class="financial-summary-loading">{{ t('common.loading') }}</div>
    <div v-else-if="!ledger" class="financial-summary-empty">
      {{ t('financialSummary.noData') }}
    </div>
    <div v-else class="financial-summary-metrics">
      <div class="metric">
        <span class="metric-label">{{ t('financialSummary.revenue') }}</span>
        <span class="metric-value amount-positive">{{ formatAmountPlain(ledger.totalRevenue) }}</span>
      </div>
      <div class="metric">
        <span class="metric-label">{{ t('financialSummary.costs') }}</span>
        <span class="metric-value amount-negative">{{ formatAmountPlain(totalCosts) }}</span>
      </div>
      <div class="metric metric--profit">
        <span class="metric-label">{{ t('financialSummary.netProfit') }}</span>
        <span class="metric-value" :class="profitClass(netProfit)">{{ formatAmount(netProfit) }}</span>
      </div>
    </div>
    <RouterLink v-if="ledger" :to="`/ledger/${ledger.companyId}`" class="financial-summary-link">
      {{ t('financialSummary.viewFullLedger') }}
    </RouterLink>
  </div>
</template>

<style scoped>
.financial-summary-card {
  padding: 1rem 1.25rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}

.financial-summary-title {
  margin: 0 0 0.75rem;
  font-size: 0.8125rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.financial-summary-loading,
.financial-summary-empty {
  font-size: 0.875rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.financial-summary-metrics {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.metric {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 6rem;
}

.metric--profit {
  border-left: 1px solid var(--color-border);
  padding-left: 1rem;
  margin-left: 0.25rem;
}

.metric-label {
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.metric-value {
  font-size: 1.0625rem;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.amount-positive {
  color: var(--color-secondary);
}

.amount-negative {
  color: var(--color-danger);
}

.amount-neutral {
  color: var(--color-text-secondary);
}

.financial-summary-link {
  display: inline-block;
  margin-top: 0.625rem;
  font-size: 0.8125rem;
  color: var(--color-primary);
  text-decoration: none;
}

.financial-summary-link:hover {
  text-decoration: underline;
}
</style>
