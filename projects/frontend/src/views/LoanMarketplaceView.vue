<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest } from '@/lib/graphql'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useScrollPreservation } from '@/composables/useScrollPreservation'
import { deepEqual } from '@/lib/utils'
import { getActiveCompany } from '@/lib/accountContext'
import type { LoanOfferSummary, LoanSummary, Company } from '@/types'
import { formatLoanDuration, computeTotalRepayment, computePaymentAmount, computeTotalPayments, loanStatusClass, formatCurrency, formatPercent } from '@/lib/loanHelpers'

const { t } = useI18n()
const auth = useAuthStore()
const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()
const loading = ref(true)
const error = ref<string | null>(null)
const offers = ref<LoanOfferSummary[]>([])
const myLoans = ref<LoanSummary[]>([])
const myCompanies = ref<Company[]>([])

// Accept modal state
const showAcceptModal = ref(false)
const selectedOffer = ref<LoanOfferSummary | null>(null)
const selectedCompanyId = ref('')
const principalAmount = ref(0)
const acceptLoading = ref(false)
const acceptError = ref<string | null>(null)

const LOAN_OFFERS_QUERY = `
  {
    loanOffers {
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

const MY_LOANS_QUERY = `
  {
    myLoans {
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

const MY_COMPANIES_QUERY = `
  {
    me {
      companies {
        id
        name
        cash
      }
    }
  }
`

const ACCEPT_LOAN_MUTATION = `
  mutation AcceptLoan($input: AcceptLoanInput!) {
    acceptLoan(input: $input) {
      id
      status
      originalPrincipal
      remainingPrincipal
      paymentAmount
      totalPayments
    }
  }
`

async function loadData(isRefresh = false) {
  if (!isRefresh) {
    loading.value = true
  }
  error.value = null
  try {
    if (auth.isAuthenticated && !auth.player) {
      await auth.fetchMe()
    }

    const offersResult = await gqlRequest<{ loanOffers: LoanOfferSummary[] }>(LOAN_OFFERS_QUERY)
    const newOffers = offersResult.loanOffers ?? []
    if (!deepEqual(offers.value, newOffers)) {
      offers.value = newOffers
    }

    if (auth.isAuthenticated) {
      const [loansResult, companiesResult] = await Promise.all([gqlRequest<{ myLoans: LoanSummary[] }>(MY_LOANS_QUERY), gqlRequest<{ me: { companies: Company[] } }>(MY_COMPANIES_QUERY)])
      const newLoans = loansResult.myLoans ?? []
      if (!deepEqual(myLoans.value, newLoans)) {
        myLoans.value = newLoans
      }
      myCompanies.value = companiesResult.me?.companies ?? []
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
}

onMounted(loadData)

useTickRefresh(async () => {
  const scrollPos = saveScrollPosition()
  await loadData(true)
  await restoreScrollPosition(scrollPos)
})

const activeLoans = computed(() => myLoans.value.filter((l) => l.status === 'ACTIVE' || l.status === 'OVERDUE'))
const activeCompany = computed(() => getActiveCompany(auth.player, myCompanies.value))
const isCompanyAccountActive = computed(() => auth.player?.activeAccountType === 'COMPANY' && !!activeCompany.value)

function openAcceptModal(offer: LoanOfferSummary) {
  if (!isCompanyAccountActive.value || !activeCompany.value) {
    acceptError.value = t('bank.companyAccountRequired')
    return
  }

  selectedOffer.value = offer
  principalAmount.value = Math.min(offer.maxPrincipalPerLoan, offer.remainingCapacity)
  selectedCompanyId.value = activeCompany.value.id
  acceptError.value = null
  showAcceptModal.value = true
}

function closeAcceptModal() {
  showAcceptModal.value = false
  selectedOffer.value = null
  acceptError.value = null
}

const estimatedTotalRepayment = computed(() => {
  if (!selectedOffer.value || principalAmount.value <= 0) return 0
  return computeTotalRepayment(principalAmount.value, selectedOffer.value.annualInterestRatePercent, selectedOffer.value.durationTicks)
})

const estimatedPaymentAmount = computed(() => {
  if (!selectedOffer.value || principalAmount.value <= 0) return 0
  return computePaymentAmount(principalAmount.value, selectedOffer.value.annualInterestRatePercent, selectedOffer.value.durationTicks)
})

const estimatedTotalPayments = computed(() => {
  if (!selectedOffer.value) return 0
  return computeTotalPayments(selectedOffer.value.durationTicks)
})

const selectedCompanyCash = computed(() => {
  const company = myCompanies.value.find((c) => c.id === selectedCompanyId.value)
  return company?.cash ?? 0
})

async function confirmAcceptLoan() {
  if (!selectedOffer.value || !selectedCompanyId.value || principalAmount.value <= 0) return
  acceptLoading.value = true
  acceptError.value = null
  try {
    await gqlRequest(ACCEPT_LOAN_MUTATION, {
      input: {
        loanOfferId: selectedOffer.value.id,
        borrowerCompanyId: selectedCompanyId.value,
        principalAmount: principalAmount.value,
      },
    })
    closeAcceptModal()
    await loadData()
  } catch (err) {
    acceptError.value = err instanceof Error ? err.message : String(err)
  } finally {
    acceptLoading.value = false
  }
}
</script>

<template>
  <div class="loan-marketplace-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('bank.loanOffers') }}</h1>
      <p class="page-subtitle">
        {{ t('bank.browseMarketplace') }}
      </p>
    </div>

    <div v-if="loading" class="loading-state">
      <div class="spinner" />
      <span>{{ t('common.loading') }}</span>
    </div>

    <div v-else-if="error" class="error-state">
      <p class="error-message">{{ error }}</p>
      <button class="btn btn-secondary" @click="() => loadData()">{{ t('common.retry') }}</button>
    </div>

    <template v-else>
      <!-- Active loans section (authenticated borrowers) -->
      <section v-if="auth.isAuthenticated && activeLoans.length > 0" class="my-loans-section">
        <h2 class="section-title">{{ t('bank.myLoans') }}</h2>
        <div class="loans-grid">
          <div v-for="loan in activeLoans" :key="loan.id" class="loan-card" :class="loanStatusClass(loan.status)">
            <div class="loan-card-header">
              <span class="lender-name">{{ loan.lenderCompanyName }}</span>
              <span class="loan-status-badge" :class="loanStatusClass(loan.status)">
                {{ t(`bank.statusBadge.${loan.status}`) }}
              </span>
            </div>
            <div class="loan-card-body">
              <div class="loan-stat">
                <span class="stat-label">{{ t('bank.remainingPrincipal') }}</span>
                <span class="stat-value">{{ formatCurrency(loan.remainingPrincipal) }}</span>
              </div>
              <div class="loan-stat">
                <span class="stat-label">{{ t('bank.nextPayment') }}</span>
                <span class="stat-value">{{ formatCurrency(loan.paymentAmount) }}</span>
              </div>
              <div class="loan-stat">
                <span class="stat-label">{{ t('bank.paymentsMade') }}</span>
                <span class="stat-value">{{ loan.paymentsMade }} / {{ loan.totalPayments }}</span>
              </div>
              <div class="loan-stat">
                <span class="stat-label">{{ t('bank.interestRate') }}</span>
                <span class="stat-value">{{ formatPercent(loan.annualInterestRatePercent) }}</span>
              </div>
            </div>
            <div v-if="loan.missedPayments > 0" class="overdue-warning">⚠ {{ loan.missedPayments }} missed payment(s) — penalty accumulated: {{ formatCurrency(loan.accumulatedPenalty) }}</div>
          </div>
        </div>
      </section>

      <!-- Available loan offers -->
      <section class="offers-section">
        <h2 class="section-title">{{ t('bank.loanOffers') }}</h2>
        <div v-if="offers.length === 0" class="empty-state">
          <p>{{ t('bank.noOffers') }}</p>
        </div>
        <div v-else class="offers-grid">
          <div v-for="offer in offers" :key="offer.id" class="offer-card">
            <div class="offer-card-header">
              <div class="offer-lender">
                <span class="offer-bank-name">{{ offer.bankBuildingName }}</span>
                <span class="offer-lender-name">{{ offer.lenderCompanyName }}</span>
              </div>
              <div class="offer-rate">
                <span class="rate-value">{{ formatPercent(offer.annualInterestRatePercent) }}</span>
                <span class="rate-label">{{ t('bank.perYear') }}</span>
              </div>
            </div>
            <div class="offer-card-body">
              <div class="offer-stat">
                <span class="stat-label">{{ t('bank.maxPrincipal') }}</span>
                <span class="stat-value">{{ formatCurrency(offer.maxPrincipalPerLoan) }}</span>
              </div>
              <div class="offer-stat">
                <span class="stat-label">{{ t('bank.remainingCapacity') }}</span>
                <span class="stat-value">{{ formatCurrency(offer.remainingCapacity) }}</span>
              </div>
              <div class="offer-stat">
                <span class="stat-label">{{ t('bank.duration') }}</span>
                <span class="stat-value">{{ formatLoanDuration(offer.durationTicks) }}</span>
              </div>
              <div class="offer-stat">
                <span class="stat-label">{{ t('common.city') }}</span>
                <span class="stat-value">{{ offer.cityName }}</span>
              </div>
            </div>
            <div class="offer-card-footer">
              <button v-if="auth.isAuthenticated" class="btn btn-primary" :disabled="!isCompanyAccountActive" @click="openAcceptModal(offer)">
                {{ t('bank.acceptLoan') }}
              </button>
              <router-link v-else to="/login" class="btn btn-secondary">
                {{ t('auth.loginToAccess') }}
              </router-link>
            </div>
            <p v-if="auth.isAuthenticated && !isCompanyAccountActive" class="offer-context-hint">
              {{ t('bank.companyAccountRequired') }}
            </p>
          </div>
        </div>
      </section>
    </template>

    <!-- Accept Loan Modal -->
    <div v-if="showAcceptModal && selectedOffer" class="modal-overlay" @click.self="closeAcceptModal">
      <div class="modal" role="dialog" :aria-label="t('bank.confirmAccept')">
        <div class="modal-header">
          <h2>{{ t('bank.confirmAccept') }}</h2>
          <button class="modal-close" @click="closeAcceptModal" :aria-label="t('common.close')">✕</button>
        </div>
        <div class="modal-body">
          <div class="loan-summary">
            <div class="summary-row">
              <span>{{ t('bank.lender') }}</span>
              <strong>{{ selectedOffer.lenderCompanyName }}</strong>
            </div>
            <div class="summary-row">
              <span>{{ t('bank.interestRate') }}</span>
              <strong>{{ formatPercent(selectedOffer.annualInterestRatePercent) }} {{ t('bank.perYear') }}</strong>
            </div>
            <div class="summary-row">
              <span>{{ t('bank.duration') }}</span>
              <strong>{{ formatLoanDuration(selectedOffer.durationTicks) }}</strong>
            </div>
          </div>

          <div class="form-group">
            <label for="borrow-company">{{ t('bank.borrower') }}</label>
            <div id="borrow-company" class="active-borrower-company">
              <strong>{{ activeCompany?.name ?? t('bank.activeBorrowerCompany') }}</strong>
              <span>{{ activeCompany ? formatCurrency(activeCompany.cash) : '' }}</span>
            </div>
            <span class="form-hint">{{ t('bank.borrowerHint') }}</span>
          </div>

          <div class="form-group">
            <label for="principal-amount">{{ t('bank.principalAmount') }}</label>
            <input
              id="principal-amount"
              v-model.number="principalAmount"
              type="number"
              :min="1000"
              :max="Math.min(selectedOffer.maxPrincipalPerLoan, selectedOffer.remainingCapacity)"
              step="1000"
              class="form-input"
            />
            <span class="form-hint">{{ t('bank.companyCashAvailable', { amount: formatCurrency(selectedCompanyCash) }) }}</span>
          </div>

          <div class="repayment-summary">
            <div class="summary-row">
              <span>{{ t('bank.originalPrincipal') }}</span>
              <strong>{{ formatCurrency(principalAmount) }}</strong>
            </div>
            <div class="summary-row">
              <span>{{ t('bank.paymentAmount') }}</span>
              <strong>{{ formatCurrency(estimatedPaymentAmount) }} × {{ estimatedTotalPayments }}</strong>
            </div>
            <div class="summary-row total-row">
              <span>{{ t('bank.totalRepayment') }}</span>
              <strong>{{ formatCurrency(estimatedTotalRepayment) }}</strong>
            </div>
          </div>

          <p class="risk-warning">⚠ {{ t('bank.riskWarning') }}</p>

          <div v-if="acceptError" class="error-message">{{ acceptError }}</div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="closeAcceptModal">{{ t('common.cancel') }}</button>
          <button class="btn btn-primary" :disabled="acceptLoading || principalAmount <= 0" @click="confirmAcceptLoan">
            <span v-if="acceptLoading">{{ t('common.loading') }}</span>
            <span v-else>{{ t('bank.acceptLoan') }}</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.loan-marketplace-view {
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
  color: var(--color-text-primary);
}

.page-subtitle {
  color: var(--color-text-secondary);
  margin-top: var(--spacing-xs);
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

.section-title {
  font-size: 1.2rem;
  font-weight: 600;
  margin-bottom: var(--spacing-md);
  color: var(--color-text-primary);
}

.my-loans-section {
  margin-bottom: var(--spacing-xl);
}

.loans-grid,
.offers-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: var(--spacing-md);
}

.loan-card,
.offer-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: var(--spacing-md);
  transition: box-shadow 0.2s;
}

.loan-card:hover,
.offer-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.offer-context-hint {
  margin: 0.75rem 0 0;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
}

.active-borrower-company {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--spacing-sm);
  padding: 0.75rem 0.9rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface-hover);
}

.loan-card.status-overdue {
  border-color: var(--color-warning, #f59e0b);
}

.loan-card.status-defaulted {
  border-color: var(--color-danger, #ef4444);
}

.loan-card-header,
.offer-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--spacing-sm);
}

.loan-status-badge {
  font-size: 0.7rem;
  padding: 2px 8px;
  border-radius: 12px;
  font-weight: 600;
  text-transform: uppercase;
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

.loan-card-body,
.offer-card-body {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--spacing-xs);
  margin-bottom: var(--spacing-sm);
}

.loan-stat,
.offer-stat {
  display: flex;
  flex-direction: column;
}

.stat-label {
  font-size: 0.7rem;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stat-value {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.overdue-warning {
  background: #fef3c7;
  color: #92400e;
  padding: var(--spacing-xs);
  border-radius: var(--radius-sm);
  font-size: 0.8rem;
  margin-top: var(--spacing-xs);
}

.offer-card-header {
  align-items: center;
}

.offer-lender {
  display: flex;
  flex-direction: column;
}

.offer-bank-name {
  font-weight: 600;
  color: var(--color-text-primary);
}

.offer-lender-name {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
}

.offer-rate {
  text-align: right;
}

.rate-value {
  display: block;
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--color-primary, #3b82f6);
}

.rate-label {
  font-size: 0.7rem;
  color: var(--color-text-secondary);
}

.offer-card-footer {
  margin-top: var(--spacing-sm);
}

/* Modal */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: var(--spacing-md);
}

.modal {
  background: var(--color-surface);
  border-radius: var(--radius-lg);
  max-width: 480px;
  width: 100%;
  max-height: 90vh;
  overflow-y: auto;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--spacing-md);
  border-bottom: 1px solid var(--color-border);
}

.modal-close {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 1.2rem;
  color: var(--color-text-secondary);
}

.modal-body {
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-sm);
  padding: var(--spacing-md);
  border-top: 1px solid var(--color-border);
}

.loan-summary,
.repayment-summary {
  background: var(--color-background, #f9fafb);
  border-radius: var(--radius-sm);
  padding: var(--spacing-sm);
}

.summary-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 0;
  font-size: 0.9rem;
}

.total-row {
  border-top: 1px solid var(--color-border);
  margin-top: var(--spacing-xs);
  padding-top: var(--spacing-xs);
  font-weight: 600;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--color-text-primary);
}

.form-select,
.form-input {
  padding: var(--spacing-xs) var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-text-primary);
  font-size: 0.9rem;
}

.risk-warning {
  background: #fef3c7;
  color: #92400e;
  padding: var(--spacing-sm);
  border-radius: var(--radius-sm);
  font-size: 0.8rem;
}

.error-message {
  background: #fee2e2;
  color: #991b1b;
  padding: var(--spacing-sm);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
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
  text-decoration: none;
  width: 100%;
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
  to {
    transform: rotate(360deg);
  }
}

@media (max-width: 600px) {
  .loans-grid,
  .offers-grid {
    grid-template-columns: 1fr;
  }
}
</style>
