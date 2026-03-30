import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer, makeDefaultCities, makeDefaultResources, makeDefaultProducts } from './helpers/mock-api'
import type { MockLedgerSummary, MockLedgerEntry } from './helpers/mock-api'

function makeLedgerCompany(playerId: string) {
  return {
    id: 'company-ledger-test',
    playerId,
    name: 'Test Corp',
    cash: 450000,
    foundedAtUtc: new Date().toISOString(),
    buildings: [
      {
        id: 'building-factory-1',
        companyId: 'company-ledger-test',
        cityId: 'bratislava',
        type: 'FACTORY',
        name: 'Main Factory',
        latitude: 48.1,
        longitude: 17.1,
        level: 1,
        powerConsumption: 0,
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        units: [],
        pendingConfiguration: null,
      },
    ],
  }
}

test.describe('Company Ledger', () => {
  test('dashboard shows View Ledger link for each company', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    player.companies = [makeLedgerCompany(player.id)]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/dashboard')
    await expect(page.getByRole('link', { name: /View Ledger/i }).first()).toBeVisible()
  })

  test('navigates to ledger view and shows company name', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    player.companies = [makeLedgerCompany(player.id)]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/ledger/company-ledger-test')
    await expect(page.getByRole('heading', { name: 'Test Corp' })).toBeVisible()
  })

  test('ledger shows income statement, balance sheet, cash flow', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    const company = {
      id: 'company-rich',
      playerId: player.id,
      name: 'Rich Corp',
      cash: 300000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }
    player.companies = [company]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    const ledger: MockLedgerSummary = {
      companyId: company.id,
      companyName: 'Rich Corp',
      currentCash: 300000,
      totalRevenue: 50000,
      totalPurchasingCosts: 20000,
      totalLaborCosts: 2500,
      totalEnergyCosts: 750,
      totalMarketingCosts: 0,
      totalTaxPaid: 5000,
      totalOtherCosts: 0,
      netIncome: 25000,
      buildingValue: 200000,
      inventoryValue: 10000,
      totalAssets: 510000,
      totalPropertyPurchases: 150000,
      cashFromOperations: 30000,
      cashFromInvestments: -150000,
      firstRecordedTick: 1,
      lastRecordedTick: 42,
      buildingSummaries: [],
    }
    state.ledgerData[company.id] = ledger

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto(`/ledger/${company.id}`)

    await expect(page.getByRole('heading', { name: 'Income Statement' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Balance Sheet' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Cash Flow Statement' })).toBeVisible()
    await expect(page.locator('.statement-row').filter({ hasText: 'Labor Costs' })).toContainText('-$2,500.00')
    await expect(page.locator('.statement-row').filter({ hasText: 'Energy Costs' })).toContainText('-$750.00')
  })

  test('drill-down shows entries when expanded', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    const company = {
      id: 'company-drill',
      playerId: player.id,
      name: 'Drill Corp',
      cash: 400000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }
    player.companies = [company]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    state.ledgerData[company.id] = {
      companyId: company.id,
      companyName: 'Drill Corp',
      currentCash: 400000,
      totalRevenue: 10000,
      totalPurchasingCosts: 0,
      totalLaborCosts: 600,
      totalEnergyCosts: 180,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 10000,
      buildingValue: 0,
      inventoryValue: 0,
      totalAssets: 400000,
      totalPropertyPurchases: 100000,
      cashFromOperations: 10000,
      cashFromInvestments: -100000,
      firstRecordedTick: 5,
      lastRecordedTick: 10,
      buildingSummaries: [],
    }

    const entries: MockLedgerEntry[] = [
      {
        id: 'e-labor',
        category: 'LABOR_COST',
        description: 'Operating labor for MANUFACTURING',
        amount: -600,
        recordedAtTick: 10,
        buildingId: null,
        buildingName: null,
        buildingUnitId: null,
        productTypeId: null,
        productName: null,
        resourceTypeId: null,
        resourceName: null,
      },
      {
        id: 'e1',
        category: 'REVENUE',
        description: 'Public sales',
        amount: 5000,
        recordedAtTick: 10,
        buildingId: null,
        buildingName: null,
        buildingUnitId: null,
        productTypeId: null,
        productName: 'Wooden Chair',
        resourceTypeId: null,
        resourceName: null,
      },
    ]
    state.drillDownData[`${company.id}:REVENUE`] = entries
    state.drillDownData[`${company.id}:LABOR_COST`] = [entries[0]]

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto(`/ledger/${company.id}`)

    const revenueRow = page
      .locator('.statement-row')
      .filter({ hasText: /^Revenue/ })
      .first()
    await revenueRow.getByRole('button').click()

    await expect(page.getByText('Wooden Chair')).toBeVisible()

    const laborRow = page.locator('.statement-row').filter({ hasText: 'Labor Costs' }).first()
    await laborRow.getByRole('button').click()

    await expect(page.getByText('Operating labor for MANUFACTURING')).toBeVisible()
  })

  test('new company shows no-history banner', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    const company = {
      id: 'company-empty',
      playerId: player.id,
      name: 'Empty Corp',
      cash: 500000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }
    player.companies = [company]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto(`/ledger/${company.id}`)
    await expect(page.getByText('No financial history recorded yet')).toBeVisible()
  })

  test('shows tax-year metadata and switches ledger history years', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 8760

    const company = {
      id: 'company-history',
      playerId: player.id,
      name: 'History Corp',
      cash: 420000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }
    player.companies = [company]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    state.ledgerData[company.id] = {
      companyId: company.id,
      companyName: 'History Corp',
      gameYear: 2001,
      isCurrentGameYear: true,
      currentCash: 420000,
      totalRevenue: 3400,
      totalPurchasingCosts: 1000,
      totalLaborCosts: 240,
      totalEnergyCosts: 90,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      taxableIncome: 2400,
      estimatedIncomeTax: 360,
      netIncome: 2400,
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 0,
      inventoryValue: 0,
      totalAssets: 420000,
      totalPropertyPurchases: 0,
      cashFromOperations: 2400,
      cashFromInvestments: 0,
      firstRecordedTick: 8760,
      lastRecordedTick: 8760,
      incomeTaxDueAtTick: 17520,
      incomeTaxDueGameTimeUtc: '2002-01-01T00:00:00.000Z',
      incomeTaxDueGameYear: 2002,
      history: [
        {
          gameYear: 2001,
          isCurrentGameYear: true,
          totalRevenue: 3400,
          totalLaborCosts: 240,
          totalEnergyCosts: 90,
          netIncome: 2400,
          totalTaxPaid: 0,
          taxableIncome: 2400,
          estimatedIncomeTax: 360,
          firstRecordedTick: 8760,
          lastRecordedTick: 8760,
        },
        {
          gameYear: 2000,
          isCurrentGameYear: false,
          totalRevenue: 1200,
          totalLaborCosts: 120,
          totalEnergyCosts: 45,
          netIncome: 900,
          totalTaxPaid: 135,
          taxableIncome: 900,
          estimatedIncomeTax: 135,
          firstRecordedTick: 12,
          lastRecordedTick: 8759,
        },
      ],
      buildingSummaries: [],
    }

    state.ledgerData[`${company.id}:2000`] = {
      ...state.ledgerData[company.id],
      gameYear: 2000,
      isCurrentGameYear: false,
      totalRevenue: 1200,
      taxableIncome: 900,
      estimatedIncomeTax: 135,
      totalTaxPaid: 135,
      netIncome: 765,
      firstRecordedTick: 12,
      lastRecordedTick: 8759,
      incomeTaxDueAtTick: 8760,
      incomeTaxDueGameTimeUtc: '2001-01-01T00:00:00.000Z',
      incomeTaxDueGameYear: 2001,
      isIncomeTaxSettled: true,
    }

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto(`/ledger/${company.id}`)

    await expect(page.locator('.kpi-card').getByText('Year 2001')).toBeVisible()
    await expect(page.getByText('Income Tax Schedule')).toBeVisible()

    await page.getByRole('button', { name: /Year 2000/ }).click()
    await expect(page.locator('.kpi-card').getByText('Year 2000')).toBeVisible()
    await expect(page.getByText('$1,200.00')).toBeVisible()
  })

  test('back button returns to dashboard', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      cities: makeDefaultCities(),
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    const company = {
      id: 'company-back',
      playerId: player.id,
      name: 'Back Corp',
      cash: 500000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }
    player.companies = [company]
    player.onboardingCompletedAtUtc = new Date().toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto(`/ledger/${company.id}`)
    await page.getByRole('button', { name: /back/i }).click()
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })
})
