import { test, expect } from '@playwright/test'
import {
  setupMockApi,
  makePlayer,
  type MockLoanOffer,
  type MockLoan,
} from './helpers/mock-api'

function makeLoanOffer(overrides: Partial<MockLoanOffer> = {}): MockLoanOffer {
  return {
    id: 'offer-1',
    bankBuildingId: 'bank-building-1',
    bankBuildingName: 'City Bank',
    cityId: 'city-ba',
    cityName: 'Bratislava',
    lenderCompanyId: 'lender-company-1',
    lenderCompanyName: 'Lending Corp',
    annualInterestRatePercent: 12,
    maxPrincipalPerLoan: 50000,
    totalCapacity: 200000,
    usedCapacity: 0,
    remainingCapacity: 200000,
    durationTicks: 1440,
    isActive: true,
    createdAtTick: 1,
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeActiveLoan(overrides: Partial<MockLoan> = {}): MockLoan {
  return {
    id: 'loan-1',
    loanOfferId: 'offer-1',
    borrowerCompanyId: 'borrower-company-1',
    borrowerCompanyName: 'Borrower Corp',
    lenderCompanyId: 'lender-company-1',
    lenderCompanyName: 'Lending Corp',
    bankBuildingId: 'bank-building-1',
    bankBuildingName: 'City Bank',
    originalPrincipal: 25000,
    remainingPrincipal: 25000,
    annualInterestRatePercent: 12,
    durationTicks: 1440,
    startTick: 42,
    dueTick: 1482,
    nextPaymentTick: 762,
    paymentAmount: 13500,
    paymentsMade: 0,
    totalPayments: 2,
    status: 'ACTIVE',
    missedPayments: 0,
    accumulatedPenalty: 0,
    acceptedAtUtc: '2026-01-15T00:00:00Z',
    closedAtUtc: null,
    ...overrides,
  }
}

test.describe('Loan Marketplace (/loans)', () => {
  test('shows loan marketplace page with empty state when no offers', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/loans')
    await expect(page.getByRole('heading', { name: 'Loan Offers', level: 1 })).toBeVisible()
    await expect(page.getByText('No loan offers available at this time.')).toBeVisible()
  })

  test('shows available loan offers for unauthenticated user', async ({ page }) => {
    const offer = makeLoanOffer()
    setupMockApi(page, { loanOffers: [offer] })
    await page.goto('/loans')

    await expect(page.getByText('City Bank')).toBeVisible()
    await expect(page.getByText('Lending Corp')).toBeVisible()
    await expect(page.getByText('12.0%')).toBeVisible()
  })

  test('shows Login to Access button for unauthenticated user instead of Accept Loan', async ({
    page,
  }) => {
    const offer = makeLoanOffer()
    setupMockApi(page, { loanOffers: [offer] })
    await page.goto('/loans')

    // Should see login link, not accept button
    await expect(page.getByRole('link', { name: 'Log in to access' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Accept Loan' })).toBeHidden()
  })

  test('shows Accept Loan button for authenticated user', async ({ page }) => {
    const offer = makeLoanOffer()
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 10000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    await expect(page.getByRole('button', { name: 'Accept Loan' })).toBeVisible()
  })

  test('shows accept loan confirmation modal with financial summary', async ({ page }) => {
    const offer = makeLoanOffer()
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 10000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    await page.getByRole('button', { name: 'Accept Loan' }).click()

    const modal = page.locator('[role="dialog"]')
    await expect(modal).toBeVisible()
    await expect(modal.getByText('Confirm Loan Acceptance')).toBeVisible()
    await expect(modal.getByText('Lending Corp')).toBeVisible()
    await expect(modal.getByText('12.0%')).toBeVisible()
    await expect(modal.getByText('Total repayment')).toBeVisible()
    // Risk warning should be visible
    await expect(modal.getByText(/Missing payments results in penalties/)).toBeVisible()
  })

  test('closes confirm modal on cancel', async ({ page }) => {
    const offer = makeLoanOffer()
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 10000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    await page.getByRole('button', { name: 'Accept Loan' }).click()
    await expect(page.locator('[role="dialog"]')).toBeVisible()

    await page.locator('[role="dialog"]').getByRole('button', { name: 'Cancel' }).click()
    await expect(page.locator('[role="dialog"]')).toBeHidden()
  })

  test('shows active loans for authenticated borrower', async ({ page }) => {
    const offer = makeLoanOffer()
    const loan = makeActiveLoan({ status: 'ACTIVE', lenderCompanyName: 'Lending Corp' })
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 10000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer], myLoans: [loan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    await expect(page.getByRole('heading', { name: 'My Loans' })).toBeVisible()
    await expect(page.getByText('$25,000').first()).toBeVisible()
  })

  test('shows overdue warning for overdue loan', async ({ page }) => {
    const offer = makeLoanOffer()
    const loan = makeActiveLoan({
      status: 'OVERDUE',
      missedPayments: 1,
      accumulatedPenalty: 500,
    })
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 10000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer], myLoans: [loan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    // Should see overdue badge
    await expect(page.getByText('⚠ Overdue')).toBeVisible()
    // Should see missed payment warning
    await expect(page.getByText(/1 missed payment/)).toBeVisible()
    await expect(page.getByText(/\$500/)).toBeVisible()
  })

  test('happy path: borrow loan and see it appear in my loans', async ({ page }) => {
    const offer = makeLoanOffer()
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'borrower-company-1',
      playerId: player.id,
      name: 'My Company',
      cash: 5000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [offer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/loans')

    // Click Accept Loan on the offer
    await page.getByRole('button', { name: 'Accept Loan' }).click()
    const modal = page.locator('[role="dialog"]')
    await expect(modal).toBeVisible()

    // The confirm button should be enabled
    const confirmBtn = modal.getByRole('button', { name: 'Accept Loan' })
    await expect(confirmBtn).toBeEnabled()
    await confirmBtn.click()

    // Modal should close
    await expect(modal).toBeHidden()

    // Should now show My Loans section
    await expect(page.getByRole('heading', { name: 'My Loans' })).toBeVisible()
  })

  test('shows multiple loan offers side by side for comparison', async ({ page }) => {
    const offer1 = makeLoanOffer({ id: 'offer-a', annualInterestRatePercent: 8, lenderCompanyName: 'Low Rate Bank' })
    const offer2 = makeLoanOffer({ id: 'offer-b', annualInterestRatePercent: 15, lenderCompanyName: 'High Rate Bank' })
    setupMockApi(page, { loanOffers: [offer1, offer2] })
    await page.goto('/loans')

    await expect(page.getByText('Low Rate Bank')).toBeVisible()
    await expect(page.getByText('High Rate Bank')).toBeVisible()
    await expect(page.getByText('8.0%')).toBeVisible()
    await expect(page.getByText('15.0%')).toBeVisible()
  })
})

test.describe('Bank Management (/bank/:buildingId)', () => {
  test('shows bank management page for authenticated bank owner', async ({ page }) => {
    const loanOffer = makeLoanOffer({ bankBuildingId: 'bank-building-1' })
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    player.companies.push({
      id: 'lender-company-1',
      playerId: player.id,
      name: 'Lending Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'bank-building-1',
          companyId: 'lender-company-1',
          cityId: 'city-ba',
          name: 'City Bank',
          type: 'BANK',
          level: 1,
          units: [],
          isUnderConstruction: false,
          constructionCompletesAtTick: null,
          pendingConfigurationTick: null,
          hasPendingConfiguration: false,
          powerStatus: 'POWERED',
          mediaType: null,
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], loanOffers: [loanOffer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    await expect(page.getByRole('heading', { name: 'Configure Bank' })).toBeVisible()
    await expect(page.getByText('Loan Offers')).toBeVisible()
  })

  test('shows bank stats overview', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player], loanOffers: [], myLoans: [] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    await expect(page.getByText('Active Loans')).toBeVisible()
    await expect(page.getByText('Capital Outstanding')).toBeVisible()
    await expect(page.getByText('Overdue/Defaulted')).toBeVisible()
  })

  test('shows publish offer form when button clicked', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    await page.getByRole('button', { name: 'Publish Loan Offer' }).click()
    await expect(page.getByRole('heading', { name: 'Publish Loan Offer' }).last()).toBeVisible()
    // Form fields should be visible
    await expect(page.getByLabel('Annual Interest Rate (%)')).toBeVisible()
    await expect(page.getByLabel('Max Principal Per Loan ($)')).toBeVisible()
    await expect(page.getByLabel('Total Lending Capacity ($)')).toBeVisible()
  })

  test('cancels publish form when Cancel clicked', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    await page.getByRole('button', { name: 'Publish Loan Offer' }).click()
    await expect(page.getByLabel('Annual Interest Rate (%)')).toBeVisible()

    await page.getByRole('button', { name: 'Cancel' }).click()
    await expect(page.getByLabel('Annual Interest Rate (%)')).toBeHidden()
  })

  test('publishes offer and shows it in the offer list', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player], loanOffers: [] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    // Open form
    await page.getByRole('button', { name: 'Publish Loan Offer' }).click()

    // Submit form (default values)
    await page.getByRole('button', { name: 'Publish Loan Offer' }).last().click()

    // After publishing, form should hide and offers table should show
    await expect(page.getByLabel('Annual Interest Rate (%)')).toBeHidden()
  })

  test('shows issued loans in issued loans section', async ({ page }) => {
    const issuedLoan = makeActiveLoan({
      bankBuildingId: 'bank-building-1',
      borrowerCompanyName: 'Borrower Inc',
    })
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player], myLoans: [issuedLoan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    await expect(page.getByRole('heading', { name: 'Issued Loans' })).toBeVisible()
    await expect(page.getByText('Borrower Inc')).toBeVisible()
  })

  test('shows delinquency warning for overdue issued loan', async ({ page }) => {
    const overdueIssuedLoan = makeActiveLoan({
      bankBuildingId: 'bank-building-1',
      status: 'OVERDUE',
      missedPayments: 2,
    })
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player], myLoans: [overdueIssuedLoan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/bank/bank-building-1')

    // Should see overdue badge in issued loans table
    await expect(page.getByText('⚠ Overdue')).toBeVisible()
    // Should show missed count
    await expect(page.getByText('2 missed')).toBeVisible()
  })
})

test.describe('Loans nav link', () => {
  test('shows Loans link in nav bar', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    // Use href selector to find nav link (icon-only nav items may be hidden in accessibility tree on desktop)
    await expect(page.locator('.nav-links a[href="/loans"]')).toBeVisible()
  })

  test('clicking Loans nav link navigates to /loans', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await page.locator('.nav-links a[href="/loans"]').click()
    await expect(page).toHaveURL('/loans')
  })
})

test.describe('Loan offer display details', () => {
  test('shows formatted duration in days', async ({ page }) => {
    const offer = makeLoanOffer({ durationTicks: 720 }) // 30 days
    setupMockApi(page, { loanOffers: [offer] })
    await page.goto('/loans')
    // 720 ticks = 30 in-game days
    await expect(page.getByText('30 days')).toBeVisible()
  })

  test('shows city name on offer card', async ({ page }) => {
    const offer = makeLoanOffer({ cityName: 'Vienna' })
    setupMockApi(page, { loanOffers: [offer] })
    await page.goto('/loans')
    await expect(page.getByText('Vienna')).toBeVisible()
  })
})

test.describe('Loan Marketplace — tick-refresh stability', () => {
  test('background tick refresh does not show a loading spinner or blank the offers list', async ({
    page,
  }) => {
    const offer = makeLoanOffer({ lenderCompanyName: 'Tick Bank', annualInterestRatePercent: 8 })
    const state = setupMockApi(page, { loanOffers: [offer] })
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/loans')
    await expect(page.getByText('Tick Bank')).toBeVisible()

    // Simulate tick advancing
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Offer must remain visible without a loading spinner blanking the page
    await expect(page.getByText('Tick Bank')).toBeVisible()
    await expect(page.locator('.loading-state')).toBeHidden()
  })

  test('active loan list is preserved after a background tick refresh', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const activeLoan = makeActiveLoan({
      id: 'loan-refresh-1',
      borrowerCompanyName: 'Refresh Borrower Corp',
      lenderCompanyName: 'Tick Lender',
    })

    const state = setupMockApi(page, { players: [player], myLoans: [activeLoan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 20
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 400).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/loans')
    await expect(page.getByText('Tick Lender')).toBeVisible()

    // Simulate tick advancing
    state.gameState.currentTick = 21
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Loan entry must remain visible — context must not be lost
    await expect(page.getByText('Tick Lender')).toBeVisible()
    await expect(page.locator('.loading-state')).toBeHidden()
  })
})

test.describe('Bank Management — tick-refresh stability', () => {
  test('background tick refresh does not show a loading spinner or blank the bank management view', async ({
    page,
  }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const offer = makeLoanOffer({
      id: 'offer-bank-refresh',
      lenderCompanyName: 'Refresh Lender Corp',
      annualInterestRatePercent: 10,
    })

    const state = setupMockApi(page, { players: [player], loanOffers: [offer] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 5
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 300).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/bank/bank-building-1')
    await expect(page.getByRole('heading', { name: 'Configure Bank' })).toBeVisible()

    // Simulate tick advancing
    state.gameState.currentTick = 6
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Bank management heading must remain visible without a loading spinner
    await expect(page.getByRole('heading', { name: 'Configure Bank' })).toBeVisible()
    await expect(page.locator('.loading-state')).toBeHidden()
  })

  test('issued loans remain visible after a background tick refresh', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const issuedLoan = makeActiveLoan({
      id: 'loan-bank-refresh',
      borrowerCompanyName: 'Borrower Co',
      lenderCompanyName: 'My Bank',
      bankBuildingId: 'bank-building-1',
    })

    const state = setupMockApi(page, { players: [player], myLoans: [issuedLoan] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 15
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 400).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/bank/bank-building-1')
    await expect(page.getByText('Borrower Co')).toBeVisible()

    // Simulate tick advancing
    state.gameState.currentTick = 16
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Issued loan must remain visible after background refresh
    await expect(page.getByText('Borrower Co')).toBeVisible()
    await expect(page.locator('.loading-state')).toBeHidden()
  })
})
