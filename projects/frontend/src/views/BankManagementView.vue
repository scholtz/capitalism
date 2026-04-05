<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { gqlRequest } from '@/lib/graphql'
import type { LoanOfferSummary, LoanSummary } from '@/types'
import {
  formatLoanDuration,
  formatCurrency,
  formatPercent,
  loanStatusClass,
  computeCapacityUsedPercent,
} from '@/lib/loanHelpers'

const { t } = useI18n()
const route = useRoute()

const bankBuildingId = computed(() => route.params.buildingId as string)

const loading = ref(true)
const error = ref<string | null>(null)
const myOffers = ref<LoanOfferSummary[]>([])
const issuedLoans = ref<LoanSummary[]>([])

// Publish offer form
const showPublishForm = ref(false)
const publishLoading = ref(false)
const publishError = ref<string | null>(null)
const offerForm = ref({
  annualInterestRatePercent: 10,
  maxPrincipalPerLoan: 50000,
  totalCapacity: 200000,
  durationTicks: 1440,
})

const MY_LOAN_OFFERS_QUERY = `
  {
    myLoanOffers {
      id
      bankBuildingId
      bankBuildingName
      cityId
      cityName
      lenderCompanyId
      lenderCompanyName
      annualInterestRatePercent
      maxPrincipalPerLoan
      totalCapacity
      usedCapacity
      remainingCapacity
      durationTicks
      isActive
      createdAtTick
      createdAtUtc
    }
  }
`

const BANK_LOANS_QUERY = `
  query BankLoans($bankBuildingId: UUID!) {
    bankLoans(bankBuildingId: $bankBuildingId) {
      id
      loanOfferId
      borrowerCompanyId
      borrowerCompanyName
      lenderCompanyId
      lenderCompanyName
      bankBuildingId
      bankBuildingName
      originalPrincipal
      remainingPrincipal
      annualInterestRatePercent
      durationTicks
      startTick
      dueTick
      nextPaymentTick
      paymentAmount
      paymentsMade
      totalPayments
      status
      missedPayments
      accumulatedPenalty
      acceptedAtUtc
      closedAtUtc
    }
  }
`

const PUBLISH_OFFER_MUTATION = `
  mutation PublishLoanOffer($input: PublishLoanOfferInput!) {
    publishLoanOffer(input: $input) {
      id
      isActive
      annualInterestRatePercent
    }
  }
`

const UPDATE_OFFER_MUTATION = `
  mutation UpdateLoanOffer($input: UpdateLoanOfferInput!) {
    updateLoanOffer(input: $input) {
      id
      isActive
      annualInterestRatePercent
    }
  }
`

const DEACTIVATE_OFFER_MUTATION = `
  mutation DeactivateLoanOffer($id: UUID!) {
    deactivateLoanOffer(loanOfferId: $id) {
      id
      isActive
    }
  }
`

async function loadData() {
  loading.value = true
  error.value = null
  try {
    const offersResult = await gqlRequest<{ myLoanOffers: LoanOfferSummary[] }>(MY_LOAN_OFFERS_QUERY)
    myOffers.value = (offersResult.myLoanOffers ?? []).filter(
      (o) => !bankBuildingId.value || o.bankBuildingId === bankBuildingId.value,
    )

    if (bankBuildingId.value) {
      const loansResult = await gqlRequest<{ bankLoans: LoanSummary[] }>(BANK_LOANS_QUERY, {
        bankBuildingId: bankBuildingId.value,
      })
      issuedLoans.value = loansResult.bankLoans ?? []
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

onMounted(loadData)

const overdueLoans = computed(() => issuedLoans.value.filter((l) => l.status !== 'ACTIVE' && l.status !== 'REPAID'))

const totalIssuedCapacity = computed(() =>
  issuedLoans.value
    .filter((l) => l.status === 'ACTIVE' || l.status === 'OVERDUE')
    .reduce((sum, l) => sum + l.remainingPrincipal, 0),
)

const expectedMonthlyIncome = computed(() =>
  issuedLoans.value
    .filter((l) => l.status === 'ACTIVE' || l.status === 'OVERDUE')
    .reduce((sum, l) => sum + l.paymentAmount, 0),
)

async function publishOffer() {
  if (!bankBuildingId.value) return
  publishLoading.value = true
  publishError.value = null
  try {
    await gqlRequest(PUBLISH_OFFER_MUTATION, {
      input: {
        bankBuildingId: bankBuildingId.value,
        ...offerForm.value,
      },
    })
    showPublishForm.value = false
    offerForm.value = { annualInterestRatePercent: 10, maxPrincipalPerLoan: 50000, totalCapacity: 200000, durationTicks: 1440 }
    await loadData()
  } catch (err) {
    publishError.value = err instanceof Error ? err.message : String(err)
  } finally {
    publishLoading.value = false
  }
}

async function toggleOfferActive(offer: LoanOfferSummary) {
  try {
    if (offer.isActive) {
      await gqlRequest(DEACTIVATE_OFFER_MUTATION, { id: offer.id })
    } else {
      await gqlRequest(UPDATE_OFFER_MUTATION, {
        input: { loanOfferId: offer.id, isActive: true },
      })
    }
    await loadData()
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  }
}
</script>

<template>
  <div class="bank-management-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('bank.configureBank') }}</h1>
      <p class="page-subtitle">{{ t('bank.lending') }}</p>
    </div>

    <div v-if="loading" class="loading-state">
      <div class="spinner" />
      <span>{{ t('common.loading') }}</span>
    </div>

    <div v-else-if="error" class="error-state">
      <p class="error-message">{{ error }}</p>
      <button class="btn btn-secondary" @click="loadData">{{ t('common.retry') }}</button>
    </div>

    <template v-else>
      <!-- Overview stats -->
      <div class="stats-row">
        <div class="stat-card">
          <span class="stat-label">Active Loans</span>
          <span class="stat-value">{{ issuedLoans.filter((l) => l.status === 'ACTIVE').length }}</span>
        </div>
        <div class="stat-card">
          <span class="stat-label">Capital Outstanding</span>
          <span class="stat-value">{{ formatCurrency(totalIssuedCapacity) }}</span>
        </div>
        <div class="stat-card" :class="{ 'stat-card-warning': overdueLoans.length > 0 }">
          <span class="stat-label">Overdue/Defaulted</span>
          <span class="stat-value">{{ overdueLoans.length }}</span>
        </div>
        <div class="stat-card">
          <span class="stat-label">Expected Income/Payment</span>
          <span class="stat-value">{{ formatCurrency(expectedMonthlyIncome) }}</span>
        </div>
      </div>

      <!-- Loan Offers Management -->
      <section class="offers-section">
        <div class="section-header">
          <h2 class="section-title">{{ t('bank.loanOffers') }}</h2>
          <button class="btn btn-primary btn-sm" @click="showPublishForm = !showPublishForm">
            {{ showPublishForm ? t('common.cancel') : t('bank.publishOffer') }}
          </button>
        </div>

        <!-- Publish Form -->
        <div v-if="showPublishForm" class="publish-form">
          <h3>{{ t('bank.publishOffer') }}</h3>
          <div class="form-grid">
            <div class="form-group">
              <label for="offer-interest-rate">{{ t('bank.interestRate') }} (%)</label>
              <input id="offer-interest-rate" v-model.number="offerForm.annualInterestRatePercent" type="number" min="0.1" max="200" step="0.1" class="form-input" />
            </div>
            <div class="form-group">
              <label for="offer-max-principal">{{ t('bank.maxPrincipal') }} ($)</label>
              <input id="offer-max-principal" v-model.number="offerForm.maxPrincipalPerLoan" type="number" min="1000" step="1000" class="form-input" />
            </div>
            <div class="form-group">
              <label for="offer-total-capacity">{{ t('bank.totalCapacity') }} ($)</label>
              <input id="offer-total-capacity" v-model.number="offerForm.totalCapacity" type="number" min="1000" step="1000" class="form-input" />
            </div>
            <div class="form-group">
              <label for="offer-duration">{{ t('bank.durationTicks') }}</label>
              <input id="offer-duration" v-model.number="offerForm.durationTicks" type="number" min="24" max="87600" step="24" class="form-input" />
              <span class="form-hint">{{ formatLoanDuration(offerForm.durationTicks) }}</span>
            </div>
          </div>
          <div v-if="publishError" class="error-message">{{ publishError }}</div>
          <button class="btn btn-primary" :disabled="publishLoading" @click="publishOffer">
            {{ publishLoading ? t('common.loading') : t('bank.publishOffer') }}
          </button>
        </div>

        <!-- Offers list -->
        <div v-if="myOffers.length === 0" class="empty-state">
          <p>No loan offers published for this bank yet.</p>
        </div>
        <div v-else class="offers-table">
          <table>
            <thead>
              <tr>
                <th>{{ t('bank.interestRate') }}</th>
                <th>{{ t('bank.maxPrincipal') }}</th>
                <th>{{ t('bank.remainingCapacity') }}</th>
                <th>{{ t('bank.duration') }}</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="offer in myOffers" :key="offer.id">
                <td>{{ formatPercent(offer.annualInterestRatePercent) }}</td>
                <td>{{ formatCurrency(offer.maxPrincipalPerLoan) }}</td>
                <td>
                  {{ formatCurrency(offer.remainingCapacity) }}
                  <div class="capacity-bar">
                    <div class="capacity-fill" :style="{ width: `${computeCapacityUsedPercent(offer)}%` }" />
                  </div>
                </td>
                <td>{{ formatLoanDuration(offer.durationTicks) }}</td>
                <td>
                  <span class="status-pill" :class="offer.isActive ? 'status-active' : 'status-inactive'">
                    {{ offer.isActive ? 'Active' : 'Inactive' }}
                  </span>
                </td>
                <td>
                  <button class="btn btn-sm btn-secondary" @click="toggleOfferActive(offer)">
                    {{ offer.isActive ? t('bank.deactivateOffer') : t('bank.activateOffer') }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Issued Loans -->
      <section class="loans-section">
        <h2 class="section-title">{{ t('bank.issuedLoans') }}</h2>
        <div v-if="issuedLoans.length === 0" class="empty-state">
          <p>{{ t('bank.noIssuedLoans') }}</p>
        </div>
        <div v-else class="loans-table">
          <table>
            <thead>
              <tr>
                <th>{{ t('bank.borrower') }}</th>
                <th>{{ t('bank.originalPrincipal') }}</th>
                <th>{{ t('bank.remainingPrincipal') }}</th>
                <th>{{ t('bank.paymentAmount') }}</th>
                <th>Payments</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="loan in issuedLoans" :key="loan.id" :class="loanStatusClass(loan.status)">
                <td>{{ loan.borrowerCompanyName }}</td>
                <td>{{ formatCurrency(loan.originalPrincipal) }}</td>
                <td>{{ formatCurrency(loan.remainingPrincipal) }}</td>
                <td>{{ formatCurrency(loan.paymentAmount) }}</td>
                <td>{{ loan.paymentsMade }} / {{ loan.totalPayments }}</td>
                <td>
                  <span class="loan-status-badge" :class="loanStatusClass(loan.status)">
                    {{ t(`bank.statusBadge.${loan.status}`) }}
                  </span>
                  <div v-if="loan.missedPayments > 0" class="missed-hint">
                    {{ loan.missedPayments }} missed
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.bank-management-view {
  max-width: 1100px;
  margin: 0 auto;
  padding: var(--spacing-lg);
}

.page-header {
  margin-bottom: var(--spacing-xl);
}

.page-title {
  font-size: 1.8rem;
  font-weight: 700;
}

.page-subtitle {
  color: var(--color-text-secondary);
}

.loading-state,
.empty-state,
.error-state {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-xl);
  color: var(--color-text-secondary);
  justify-content: center;
}

.stats-row {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-xl);
}

.stat-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.stat-card-warning {
  border-color: var(--color-warning, #f59e0b);
}

.stat-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  color: var(--color-text-secondary);
  letter-spacing: 0.05em;
}

.stat-value {
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--color-text-primary);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: var(--spacing-md);
}

.section-title {
  font-size: 1.2rem;
  font-weight: 600;
}

.offers-section,
.loans-section {
  margin-bottom: var(--spacing-xl);
}

.publish-form {
  background: var(--color-background, #f9fafb);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-md);
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 500;
}

.form-input {
  padding: 6px var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-text-primary);
}

.form-hint {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.offers-table,
.loans-table {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

th, td {
  padding: var(--spacing-sm);
  text-align: left;
  border-bottom: 1px solid var(--color-border);
}

th {
  font-weight: 600;
  color: var(--color-text-secondary);
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.capacity-bar {
  height: 4px;
  background: var(--color-border);
  border-radius: 2px;
  margin-top: 4px;
  overflow: hidden;
}

.capacity-fill {
  height: 100%;
  background: var(--color-primary, #3b82f6);
  border-radius: 2px;
  transition: width 0.3s;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
}

.status-pill.status-active {
  background: #d1fae5;
  color: #065f46;
}

.status-pill.status-inactive {
  background: #f3f4f6;
  color: #6b7280;
}

.loan-status-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
}

.loan-status-badge.status-active {
  background: #d1fae5;
  color: #065f46;
}

.loan-status-badge.status-overdue {
  background: #fef3c7;
  color: #92400e;
}

.loan-status-badge.status-defaulted {
  background: #fee2e2;
  color: #991b1b;
}

.loan-status-badge.status-repaid {
  background: #e0e7ff;
  color: #3730a3;
}

.missed-hint {
  font-size: 0.7rem;
  color: #92400e;
  margin-top: 2px;
}

.error-message {
  background: #fee2e2;
  color: #991b1b;
  padding: var(--spacing-sm);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
  margin-bottom: var(--spacing-sm);
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-xs) var(--spacing-md);
  border-radius: var(--radius-sm);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  border: none;
  transition: background-color 0.2s;
}

.btn-sm {
  padding: 4px var(--spacing-sm);
  font-size: 0.8rem;
}

.btn-primary {
  background: var(--color-primary, #3b82f6);
  color: white;
}

.btn-primary:hover:not(:disabled) {
  background: var(--color-primary-hover, #2563eb);
}

.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-secondary {
  background: var(--color-surface);
  color: var(--color-text-primary);
  border: 1px solid var(--color-border);
}

.spinner {
  width: 20px;
  height: 20px;
  border: 2px solid var(--color-border);
  border-top-color: var(--color-primary, #3b82f6);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
