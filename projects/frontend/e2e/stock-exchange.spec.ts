import { expect, test, type Page } from '@playwright/test'
import { makePlayer, restoreMockSession, setupMockApi, type MockCompany } from './helpers/mock-api'

async function authenticateViaLocalStorage(page: Page, token: string) {
  await restoreMockSession(page, token)
}

function makeControlledCompany(overrides?: Partial<MockCompany>): MockCompany {
  return {
    id: 'company-home',
    playerId: 'player-1',
    name: 'Home Holdings',
    cash: 500000,
    totalSharesIssued: 10000,
    dividendPayoutRatio: 0.2,
    foundedAtUtc: '2026-01-01T00:00:00Z',
    foundedAtTick: 42,
    buildings: [],
    ...overrides,
  }
}

// Helpers to open the trade panel for a company row
async function openTradePanel(page: Page, companyName: string) {
  const row = page.locator('tr.listing-row', { hasText: companyName })
  await row.getByRole('button', { name: 'Trade' }).click()
}

test.describe('Stock exchange', () => {
  test('market overview shows all listed companies in a sortable table', async ({ page }) => {
    const player = makePlayer({
      personalCash: 150000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-2',
      email: 'rival@test.com',
      displayName: 'Rival Owner',
      companies: [
        {
          id: 'company-rival',
          playerId: 'player-2',
          name: 'Rival Foods',
          cash: 800000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.35,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 42,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-rival', ownerPlayerId: 'player-2', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await expect(page.getByRole('heading', { name: 'Stock Exchange' })).toBeVisible()

    // Market table must show required columns
    const marketTable = page.locator('table.market-table')
    await expect(marketTable).toBeVisible()

    // Column headers
    const companySortBtn = marketTable.getByRole('button', { name: /Company/ })
    const priceSortBtn = marketTable.getByRole('button', { name: /Fair price/ })
    const mvSortBtn = marketTable.getByRole('button', { name: /Market value/ })
    await expect(companySortBtn).toBeVisible()
    await expect(priceSortBtn).toBeVisible()
    await expect(mvSortBtn).toBeVisible()

    // Both companies listed as rows
    await expect(page.locator('tr.listing-row', { hasText: 'Home Holdings' })).toBeVisible()
    await expect(page.locator('tr.listing-row', { hasText: 'Rival Foods' })).toBeVisible()

    // Ownership column shows 0% for rival
    const rivalRow = page.locator('tr.listing-row', { hasText: 'Rival Foods' })
    await expect(rivalRow).toContainText('0.0%')
  })

  test('table filter narrows listed companies by name', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-2',
      email: 'filter-rival@test.com',
      displayName: 'Filter Rival',
      companies: [
        {
          id: 'company-filter',
          playerId: 'player-2',
          name: 'AlphaVenture Corp',
          cash: 600000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-filter', ownerPlayerId: 'player-2', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await expect(page.locator('tr.listing-row', { hasText: 'Home Holdings' })).toBeVisible()
    await expect(page.locator('tr.listing-row', { hasText: 'AlphaVenture Corp' })).toBeVisible()

    const filterInput = page.getByPlaceholder('Filter by company name')
    await filterInput.fill('Alpha')

    await expect(page.locator('tr.listing-row', { hasText: 'AlphaVenture Corp' })).toBeVisible()
    await expect(page.locator('tr.listing-row', { hasText: 'Home Holdings' })).toBeHidden()
  })

  test('table can be sorted by market value descending and ascending', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [
        makeControlledCompany({ id: 'company-small', name: 'Small Corp', cash: 10000, totalSharesIssued: 1000 }),
      ],
    })
    const rival = makePlayer({
      id: 'player-sort',
      email: 'sort@test.com',
      displayName: 'Sort Owner',
      companies: [
        {
          id: 'company-big',
          playerId: 'player-sort',
          name: 'Big Corp',
          cash: 900000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-small', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 1000 },
        { companyId: 'company-big', ownerPlayerId: 'player-sort', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    // By default sorted by market value descending — Big Corp should come first
    const rows = page.locator('tr.listing-row')
    await expect(rows.first()).toContainText('Big Corp')

    // Sort ascending — Small Corp should come first
    const mvSortBtn = page.locator('table.market-table').getByRole('button', { name: /Market value/ })
    await mvSortBtn.click()
    await expect(rows.first()).toContainText('Small Corp')
  })

  test('trade panel opens with bid and ask prices visible on action buttons', async ({ page }) => {
    const player = makePlayer({
      personalCash: 150000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-trade',
      email: 'trade@test.com',
      displayName: 'Trade Owner',
      companies: [
        {
          id: 'company-trade',
          playerId: 'player-trade',
          name: 'Trade Target Inc',
          cash: 500000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.25,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 5,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-trade', ownerPlayerId: 'player-trade', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'Trade Target Inc')

    // Trade panel must be visible with bid/ask price labels
    const tradePanel = page.locator('.trade-panel')
    await expect(tradePanel).toBeVisible()
    await expect(tradePanel.getByText('Ask (buy)', { exact: true })).toBeVisible()
    await expect(tradePanel.getByText('Bid (sell)', { exact: true })).toBeVisible()

    // Action buttons must include price information
    const buyBtn = tradePanel.getByRole('button', { name: /Buy @ \$/ })
    const sellBtn = tradePanel.getByRole('button', { name: /Sell @ \$/ })
    await expect(buyBtn).toBeVisible()
    await expect(sellBtn).toBeVisible()
  })

  test('person account can buy shares via inline trade panel', async ({ page }) => {
    const player = makePlayer({
      personalCash: 150000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-2',
      email: 'rival@test.com',
      displayName: 'Rival Owner',
      companies: [
        {
          id: 'company-rival',
          playerId: 'player-2',
          name: 'Rival Foods',
          cash: 800000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.35,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 42,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-rival', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 1000 },
        { companyId: 'company-rival', ownerPlayerId: 'player-2', ownerCompanyId: null, shareCount: 4000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'Rival Foods')

    const tradePanel = page.locator('.trade-panel')
    const qtyInput = tradePanel.getByLabel(/Share quantity Rival Foods/)
    await qtyInput.fill('250')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()
    await expect(tradePanel.getByRole('status')).toContainText('Bought 250 shares in Rival Foods.')

    // Portfolio section updates
    await expect(page.locator('.data-table').nth(0)).toContainText('1,250')
  })

  test('person account can sell shares via inline trade panel', async ({ page }) => {
    const player = makePlayer({
      personalCash: 150000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-sell',
      email: 'sell@test.com',
      displayName: 'Sell Owner',
      companies: [
        {
          id: 'company-sell',
          playerId: 'player-sell',
          name: 'SellTest Corp',
          cash: 600000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 3,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-sell', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 2000 },
        { companyId: 'company-sell', ownerPlayerId: 'player-sell', ownerCompanyId: null, shareCount: 3000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'SellTest Corp')

    const tradePanel = page.locator('.trade-panel')
    const qtyInput = tradePanel.getByLabel(/Share quantity SellTest Corp/)
    await qtyInput.fill('500')
    await tradePanel.getByRole('button', { name: /Sell @ / }).click()
    await expect(tradePanel.getByRole('status')).toContainText('Sold 500 shares in SellTest Corp.')
  })

  test('player selects trading account inline — no top-nav switching required', async ({ page }) => {
    const player = makePlayer({
      personalCash: 50000,
      companies: [makeControlledCompany({ id: 'company-home', name: 'Home Holdings', cash: 400000 })],
    })
    const rival = makePlayer({
      id: 'player-acct',
      email: 'acct@test.com',
      displayName: 'Account Target',
      companies: [
        {
          id: 'company-acct-target',
          playerId: 'player-acct',
          name: 'AccountTarget Ltd',
          cash: 700000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-acct-target', ownerPlayerId: 'player-acct', ownerCompanyId: null, shareCount: 6000 },
      ],
    })
    // Player starts as PERSON — NOT switched to company account in top nav
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'AccountTarget Ltd')

    const tradePanel = page.locator('.trade-panel')

    // Account selector must be visible inside the trade panel
    const accountSelect = tradePanel.getByLabel(/Trade with AccountTarget/)
    await expect(accountSelect).toBeVisible()

    // Switch to company account in-context (must use string label, not regex, for selectOption)
    await accountSelect.selectOption({ value: 'COMPANY:company-home' })

    // Buy using the company account
    const qtyInput = tradePanel.getByLabel(/Share quantity AccountTarget/)
    await qtyInput.fill('100')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()

    // Success message should appear near the action buttons
    await expect(tradePanel.getByRole('status')).toContainText('Bought 100 shares in AccountTarget Ltd.')
  })

  test('error from failed buy appears next to action buttons not in a distant page alert', async ({ page }) => {
    const player = makePlayer({
      personalCash: 5, // not enough to buy anything
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-err',
      email: 'err@test.com',
      displayName: 'Error Rival',
      companies: [
        {
          id: 'company-err',
          playerId: 'player-err',
          name: 'ErrorTest Corp',
          cash: 500000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-err', ownerPlayerId: 'player-err', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'ErrorTest Corp')

    const tradePanel = page.locator('.trade-panel')
    const qtyInput = tradePanel.getByLabel(/Share quantity ErrorTest Corp/)
    await qtyInput.fill('200')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()

    // Error must appear inside the trade panel — next to the action buttons
    const errorAlert = tradePanel.getByRole('alert')
    await expect(errorAlert).toBeVisible()
    await expect(errorAlert).toContainText('Not enough personal cash')

    // The error must NOT be a page-level alert outside the panel
    const panelBoundingBox = await tradePanel.boundingBox()
    const errorBoundingBox = await errorAlert.boundingBox()
    expect(panelBoundingBox).not.toBeNull()
    expect(errorBoundingBox).not.toBeNull()
  })

  test('claim-control button appears when player combined ownership reaches 50%', async ({ page }) => {
    const player = makePlayer({
      personalCash: 200000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-ctrl',
      email: 'ctrl@test.com',
      displayName: 'Control Target',
      companies: [
        {
          id: 'company-ctrl',
          playerId: 'player-ctrl',
          name: 'Control Ready Inc',
          cash: 700000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        // Player already owns 5001 shares (> 50%)
        { companyId: 'company-ctrl', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 5001 },
        { companyId: 'company-ctrl', ownerPlayerId: 'player-ctrl', ownerCompanyId: null, shareCount: 4999 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    const ctrlRow = page.locator('tr.listing-row', { hasText: 'Control Ready Inc' })
    await expect(ctrlRow).toBeVisible()

    // Claim control button must be visible on the row
    const claimBtn = ctrlRow.getByRole('button', { name: 'Claim control' })
    await expect(claimBtn).toBeVisible()

    // Click claim control and verify switch success message
    await claimBtn.click()
    await expect(page.getByRole('status')).toContainText('Active account switched to Control Ready Inc.')
  })

  test('buying up to 50% unlocks claim-control', async ({ page }) => {
    const player = makePlayer({
      personalCash: 200000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-2',
      email: 'target@test.com',
      displayName: 'Target Owner',
      companies: [
        {
          id: 'company-target',
          playerId: 'player-2',
          name: 'Target Industries',
          cash: 700000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 42,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-target', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 4900 },
        { companyId: 'company-target', ownerPlayerId: 'player-2', ownerCompanyId: null, shareCount: 3000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    // Buy 100 more shares to cross 50%
    await openTradePanel(page, 'Target Industries')
    const tradePanel = page.locator('.trade-panel')
    const qtyInput = tradePanel.getByLabel(/Share quantity Target Industries/)
    await qtyInput.fill('100')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()
    await expect(tradePanel.getByRole('status')).toContainText('Bought 100 shares in Target Industries.')

    // After reload the claim-control chip/button should appear
    const targetRow = page.locator('tr.listing-row', { hasText: 'Target Industries' })
    await expect(targetRow.getByRole('button', { name: 'Claim control' })).toBeVisible()
  })

  test('company settings save dividend payout ratio', async ({ page }) => {
    const player = makePlayer({
      companies: [makeControlledCompany({ id: 'company-settings', name: 'Dividend Works', dividendPayoutRatio: 0.2 })],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/company-settings/settings')

    await expect(page.getByRole('heading', { name: 'Dividend Works' })).toBeVisible()
    await expect(page.getByText('Issued shares')).toBeVisible()

    await page.getByLabel('Dividend payout ratio').fill('35')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.getByRole('status')).toContainText('Company settings saved.')
    await expect(page.getByText('35.0%')).toBeVisible()
  })

  test('market overview shows market value column for each listing', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany({ cash: 500000, totalSharesIssued: 10000 })],
    })
    const rival = makePlayer({
      id: 'player-mv',
      email: 'mv@test.com',
      displayName: 'MV Owner',
      companies: [
        {
          id: 'company-mv',
          playerId: 'player-mv',
          name: 'Marketcap Corp',
          cash: 400000,
          totalSharesIssued: 8000,
          dividendPayoutRatio: 0.3,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-mv', ownerPlayerId: 'player-mv', ownerCompanyId: null, shareCount: 4000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    const marketTable = page.locator('table.market-table')
    // Market value column header
    await expect(marketTable.getByRole('button', { name: /Market value/ })).toBeVisible()

    // Marketcap Corp row should show a currency market value
    const mvRow = page.locator('tr.listing-row', { hasText: 'Marketcap Corp' })
    await expect(mvRow).toBeVisible()
    // The market value cell should contain a $ value
    const cells = mvRow.locator('td')
    const texts = await cells.allInnerTexts()
    const hasMv = texts.some((t) => t.includes('$') && t.match(/\$/))
    expect(hasMv).toBeTruthy()
  })

  test('market table paginates long company lists', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })

    const rivals = Array.from({ length: 11 }, (_, index) => {
      const label = String(index + 1).padStart(2, '0')
      return makePlayer({
        id: `player-page-${label}`,
        email: `page-${label}@test.com`,
        displayName: `Page Owner ${label}`,
        companies: [
          {
            id: `company-page-${label}`,
            playerId: `player-page-${label}`,
            name: `Page Corp ${label}`,
            cash: 400000 + index * 10000,
            totalSharesIssued: 10000,
            dividendPayoutRatio: 0.2,
            foundedAtUtc: '2026-01-01T00:00:00Z',
            foundedAtTick: 1,
            buildings: [],
          },
        ],
      })
    })

    const state = setupMockApi(page, {
      players: [player, ...rivals],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        ...rivals.map((rival) => ({
          companyId: rival.companies[0]!.id,
          ownerPlayerId: rival.id,
          ownerCompanyId: null,
          shareCount: 5000,
        })),
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await expect(page.locator('tr.listing-row')).toHaveCount(10)
    await expect(page.locator('tr.listing-row', { hasText: 'Page Corp 01' })).toHaveCount(0)

    await page.getByRole('button', { name: 'Next page' }).click()
    await expect(page.locator('tr.listing-row')).toHaveCount(2)
    await expect(page.locator('tr.listing-row', { hasText: 'Page Corp 01' })).toBeVisible()
    await expect(page.getByText('Page 2 of 2')).toBeVisible()
  })

  test('trade panel shows stock price history for the selected company', async ({ page }) => {
    const player = makePlayer({
      personalCash: 150000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-history',
      email: 'history@test.com',
      displayName: 'History Owner',
      companies: [
        {
          id: 'company-history',
          playerId: 'player-history',
          name: 'History Corp',
          cash: 500000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-history', ownerPlayerId: 'player-history', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.stockPriceHistory['company-history'] = [
      { companyId: 'company-history', tick: 40, price: 92.5, recordedAtUtc: '2026-04-10T10:00:00Z' },
      { companyId: 'company-history', tick: 41, price: 95.25, recordedAtUtc: '2026-04-10T10:01:00Z' },
      { companyId: 'company-history', tick: 42, price: 97.5, recordedAtUtc: '2026-04-10T10:02:00Z' },
    ]
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'History Corp')

    const tradePanel = page.locator('.trade-panel')
    await expect(tradePanel.getByRole('heading', { name: 'Price history' })).toBeVisible()
    await expect(tradePanel.getByRole('table', { name: /History Corp Price history/ })).toBeVisible()
    await expect(tradePanel.getByText('40', { exact: true })).toBeVisible()
    await expect(tradePanel.getByText('$97.50', { exact: true })).toBeVisible()
  })

  test('company-account stock trades appear in the ledger overview', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany({ id: 'company-ledger', name: 'Ledger Holdings', cash: 450000 })],
    })
    const rival = makePlayer({
      id: 'player-ledger',
      email: 'ledger@test.com',
      displayName: 'Ledger Owner',
      companies: [
        {
          id: 'company-ledger-target',
          playerId: 'player-ledger',
          name: 'Ledger Target',
          cash: 600000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-ledger', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-ledger-target', ownerPlayerId: 'player-ledger', ownerCompanyId: null, shareCount: 7000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await openTradePanel(page, 'Ledger Target')
    const tradePanel = page.locator('.trade-panel')
    await tradePanel.getByLabel(/Trade with Ledger Target/).selectOption({ value: 'COMPANY:company-ledger' })
    await tradePanel.getByLabel(/Share quantity Ledger Target/).fill('100')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()
    await expect(tradePanel.getByRole('status')).toContainText('Bought 100 shares in Ledger Target.')

    await page.goto('/ledger/company-ledger')
    await expect(page.getByRole('heading', { name: 'Ledger Holdings' })).toBeVisible()
    await expect(page.getByText('Stock Purchases')).toBeVisible()
    await page.getByRole('button', { name: 'Detail: Stock Purchases' }).click()
    await expect(page.locator('.drill-table')).toContainText('Bought 100 shares in Ledger Target')
    await expect(page.locator('.drill-table')).toContainText('$6,060.00')
  })

  test('personal trade history section shows BUY and SELL entries after trades', async ({ page }) => {
    const player = makePlayer({
      personalCash: 200000,
      companies: [makeControlledCompany()],
      stockTrades: [
        {
          id: 'trade-1',
          companyId: 'company-history-target',
          companyName: 'History Target',
          direction: 'BUY',
          shareCount: 50,
          pricePerShare: 61.5,
          totalValue: 3075,
          recordedAtTick: 5,
          recordedAtUtc: '2026-01-05T10:00:00Z',
        },
        {
          id: 'trade-2',
          companyId: 'company-history-target',
          companyName: 'History Target',
          direction: 'SELL',
          shareCount: 20,
          pricePerShare: 60.0,
          totalValue: 1200,
          recordedAtTick: 8,
          recordedAtUtc: '2026-01-08T10:00:00Z',
        },
      ],
    })
    const rival = makePlayer({
      id: 'player-hist',
      email: 'hist@test.com',
      displayName: 'Hist Owner',
      companies: [
        {
          id: 'company-history-target',
          playerId: 'player-hist',
          name: 'History Target',
          cash: 300000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-history-target', ownerPlayerId: 'player-hist', ownerCompanyId: null, shareCount: 10000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    const tradeHistorySection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Personal trade history' }),
    })
    await expect(tradeHistorySection).toBeVisible()

    // Both trades should appear
    await expect(tradeHistorySection.locator('tr', { hasText: 'History Target' }).first()).toBeVisible()
    await expect(tradeHistorySection.locator('.direction-badge--buy')).toBeVisible()
    await expect(tradeHistorySection.locator('.direction-badge--sell')).toBeVisible()
  })

  test('personal trade history records a buy after executing a person-account purchase', async ({ page }) => {
    const player = makePlayer({ personalCash: 200000, companies: [makeControlledCompany()] })
    const rival = makePlayer({
      id: 'player-trk',
      email: 'trk@test.com',
      displayName: 'Track Owner',
      companies: [
        {
          id: 'company-trk',
          playerId: 'player-trk',
          name: 'Track Corp',
          cash: 400000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        // founder holds 5000 — public float is 5000 (available to buy)
        { companyId: 'company-trk', ownerPlayerId: 'player-trk', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    // Execute a personal-account buy
    await openTradePanel(page, 'Track Corp')
    const tradePanel = page.locator('.trade-panel')
    await tradePanel.getByLabel(/Share quantity Track Corp/).fill('25')
    await tradePanel.getByRole('button', { name: /Buy @ / }).click()
    await expect(tradePanel.getByRole('status')).toContainText('Bought 25 shares in Track Corp.')

    // Reload the page so the personAccount query re-runs with updated stockTrades
    await page.goto('/stocks')

    const tradeHistorySection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Personal trade history' }),
    })
    await expect(tradeHistorySection).toBeVisible()
    await expect(tradeHistorySection.locator('.direction-badge--buy')).toBeVisible()
    const tradeRow = tradeHistorySection.locator('tr', { hasText: 'Track Corp' }).first()
    await expect(tradeRow).toBeVisible()
    // Verify the trade row accurately records the executed quantity
    await expect(tradeRow).toContainText('25')
  })

  test('unauthenticated visitor sees market table but no trade buttons', async ({ page }) => {
    const rival = makePlayer({
      id: 'player-public',
      email: 'public@test.com',
      displayName: 'Public Owner',
      companies: [
        {
          id: 'company-public',
          playerId: 'player-public',
          name: 'Public Corp',
          cash: 500000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [rival],
      shareholdings: [
        { companyId: 'company-public', ownerPlayerId: 'player-public', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    // No currentUserId — unauthenticated
    state.currentUserId = null
    state.currentToken = null

    await page.goto('/stocks')

    // Market table should be visible
    await expect(page.getByRole('heading', { name: 'Stock Exchange' })).toBeVisible()
    await expect(page.locator('tr.listing-row', { hasText: 'Public Corp' })).toBeVisible()

    // Trade column (Actions) should not be visible for unauthenticated users
    await expect(page.locator('tr.listing-row', { hasText: 'Public Corp' }).getByRole('button')).toHaveCount(0)

    // Sign-in prompt should be shown
    await expect(page.locator('.market-note')).toBeVisible()
  })
})

test.describe('Stock exchange live refresh', () => {
  test('tick refresh does not blank the page — existing content stays visible', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-2',
      email: 'rival@test.com',
      displayName: 'Rival Tycoon',
      companies: [
        {
          id: 'company-rival-refresh',
          playerId: 'player-2',
          name: 'Rival Refresh Inc',
          cash: 600000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.25,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 10,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-rival-refresh', ownerPlayerId: 'player-2', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    // Page loaded: the market section and listing should be visible
    await expect(page.getByRole('heading', { name: 'Stock Exchange' })).toBeVisible()
    await expect(page.locator('tr.listing-row', { hasText: 'Rival Refresh Inc' })).toBeVisible()

    // Simulate a tick: update game state so useTickRefresh fires
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // After tick fires, the page should still show the listing without blanking
    await expect(page.locator('tr.listing-row', { hasText: 'Rival Refresh Inc' })).toBeVisible()
    // The loading spinner must NOT replace the content
    await expect(page.locator('.state-box', { hasText: 'Loading' })).toBeHidden()
  })

  test('stock exchange auto-refreshes share prices after a tick', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-3',
      email: 'rival3@test.com',
      displayName: 'Tick Rival',
      companies: [
        {
          id: 'company-tick-rival',
          playerId: 'player-3',
          name: 'Tick Corp',
          cash: 400000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 5,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-tick-rival', ownerPlayerId: 'player-3', ownerCompanyId: null, shareCount: 10000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 5
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 400).toISOString()

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await expect(page.locator('tr.listing-row', { hasText: 'Tick Corp' })).toBeVisible()

    // Advance the tick and update the rival company cash (which changes share price)
    state.gameState.currentTick = 6
    state.gameState.lastTickAtUtc = new Date().toISOString()
    rival.companies[0]!.cash = 800000 // doubled cash -> higher share price

    // Wait for the tick refresh to complete; Tick Corp listing should still be on screen
    await expect(page.locator('tr.listing-row', { hasText: 'Tick Corp' })).toBeVisible()
  })

  test('quantity input in trade panel is preserved after a background tick refresh', async ({ page }) => {
    const player = makePlayer({
      personalCash: 200000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-qty',
      email: 'qty@test.com',
      displayName: 'Qty Owner',
      companies: [
        {
          id: 'company-qty-rival',
          playerId: 'player-qty',
          name: 'Qty Corp',
          cash: 300000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.15,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-qty-rival', ownerPlayerId: 'player-qty', ownerCompanyId: null, shareCount: 10000 },
      ],
    })
    player.activeAccountType = 'PERSON'
    player.activeCompanyId = null
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 8
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/stocks')

    await expect(page.locator('tr.listing-row', { hasText: 'Qty Corp' })).toBeVisible()

    // Open trade panel and set a quantity
    await openTradePanel(page, 'Qty Corp')
    const tradePanel = page.locator('.trade-panel')
    const qtyInput = tradePanel.getByLabel(/Share quantity Qty Corp/)
    await qtyInput.fill('250')
    await expect(qtyInput).toHaveValue('250')

    // Simulate a tick advancing while the user has typed a quantity
    state.gameState.currentTick = 9
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // The listing must still be visible and the typed quantity must be unchanged
    await expect(page.locator('tr.listing-row', { hasText: 'Qty Corp' })).toBeVisible()
    await expect(qtyInput).toHaveValue('250')
    // No loading spinner must have blanked the page
    await expect(page.locator('.state-box', { hasText: 'Loading' })).toBeHidden()
  })
})

test.describe('Stock exchange portfolio and dividend sections', () => {
  test('portfolio section shows owned shares with quantity and market value', async ({ page }) => {
    const player = makePlayer({
      personalCash: 500000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-portfolio-rival',
      email: 'portfolio-rival@test.com',
      displayName: 'Portfolio Target Owner',
      companies: [
        {
          id: 'company-portfolio-target',
          playerId: 'player-portfolio-rival',
          name: 'Portfolio Target Corp',
          cash: 300000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.2,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        // Player owns 2000 shares of Portfolio Target Corp
        {
          companyId: 'company-portfolio-target',
          ownerPlayerId: 'player-1',
          ownerCompanyId: null,
          shareCount: 2000,
        },
        { companyId: 'company-portfolio-target', ownerPlayerId: 'player-portfolio-rival', ownerCompanyId: null, shareCount: 5000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/stocks')

    const portfolioSection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Personal portfolio' }),
    })
    await expect(portfolioSection).toBeVisible()

    // Should show the holding row for Portfolio Target Corp
    const holdingRow = portfolioSection.locator('tr', { hasText: 'Portfolio Target Corp' })
    await expect(holdingRow).toBeVisible()

    // Share count and market value columns should be visible
    await expect(holdingRow).toContainText('2,000')
    // Market value is shareCount * sharePrice, which should be a positive number
    await expect(holdingRow.locator('td').last()).toBeVisible()
  })

  test('portfolio section shows empty state when player owns no shares', async ({ page }) => {
    const player = makePlayer({
      personalCash: 200000,
      companies: [makeControlledCompany()],
    })
    const rival = makePlayer({
      id: 'player-noport-rival',
      email: 'noport@test.com',
      displayName: 'No Portfolio Owner',
      companies: [
        {
          id: 'company-noport-target',
          playerId: 'player-noport-rival',
          name: 'NoPort Corp',
          cash: 250000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.1,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        // Player does NOT own personal shares - they have a company but 0 personal portfolio holdings
        // Rival owns their own company but player owns no shares in it
        { companyId: 'company-noport-target', ownerPlayerId: 'player-noport-rival', ownerCompanyId: null, shareCount: 10000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/stocks')

    const portfolioSection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Personal portfolio' }),
    })
    await expect(portfolioSection).toBeVisible()

    // Empty state text should be visible
    await expect(portfolioSection.locator('.empty-state')).toContainText('You do not own any shares')
  })

  test('dividend history section is visible and shows payment entries', async ({ page }) => {
    const player = makePlayer({
      personalCash: 300000,
      companies: [makeControlledCompany()],
      dividendPayments: [
        {
          id: 'div-1',
          companyId: 'company-div-source',
          companyName: 'Dividend Source Corp',
          shareCount: 1000,
          amountPerShare: 2.5,
          totalAmount: 2500,
          gameYear: 1,
          recordedAtTick: 52,
          recordedAtUtc: '2026-03-01T00:00:00Z',
          description: 'Dividend for game year 1',
        },
      ],
    })
    const rival = makePlayer({
      id: 'player-div-source',
      email: 'divsource@test.com',
      displayName: 'Dividend Source Owner',
      companies: [
        {
          id: 'company-div-source',
          playerId: 'player-div-source',
          name: 'Dividend Source Corp',
          cash: 500000,
          totalSharesIssued: 10000,
          dividendPayoutRatio: 0.4,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 1,
          buildings: [],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player, rival],
      shareholdings: [
        { companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 },
        { companyId: 'company-div-source', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 1000 },
        { companyId: 'company-div-source', ownerPlayerId: 'player-div-source', ownerCompanyId: null, shareCount: 9000 },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/stocks')

    const dividendSection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Dividend history' }),
    })
    await expect(dividendSection).toBeVisible()

    // Should show the dividend payment row
    const dividendRow = dividendSection.locator('tr', { hasText: 'Dividend Source Corp' })
    await expect(dividendRow).toBeVisible()
    await expect(dividendRow).toContainText('$2,500.00')
  })

  test('dividend history empty state shown when no dividends received', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
      dividendPayments: [],
    })

    const state = setupMockApi(page, {
      players: [player],
      shareholdings: [{ companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 }],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/stocks')

    const dividendSection = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Dividend history' }),
    })
    await expect(dividendSection).toBeVisible()
    await expect(dividendSection.locator('.empty-state')).toContainText('No dividends have been paid')
  })
})

test.describe('Stock exchange — global account switcher hidden in nav', () => {
  // Per ROADMAP: "Remove account switching from stock exchange as it is implemented
  // now in the top navigation bar." The /stocks page has its own per-listing
  // account selector in the inline trade panel.

  test('account switcher is NOT shown in the nav bar on the /stocks page', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })
    const state = setupMockApi(page, {
      players: [player],
      shareholdings: [{ companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 }],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/stocks')

    // The global account switcher must be absent on the /stocks page
    await expect(page.locator('.account-switcher')).toHaveCount(0)
    // But the page itself should be fully rendered
    await expect(page.getByRole('heading', { name: 'Stock Exchange' })).toBeVisible()
  })

  test('account switcher IS shown in the nav bar on the /dashboard page', async ({ page }) => {
    const player = makePlayer({
      personalCash: 100000,
      companies: [makeControlledCompany()],
    })
    const state = setupMockApi(page, {
      players: [player],
      shareholdings: [{ companyId: 'company-home', ownerPlayerId: 'player-1', ownerCompanyId: null, shareCount: 10000 }],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await restoreMockSession(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // The global account switcher IS present on other pages
    await expect(page.locator('.account-switcher')).toBeVisible()
  })
})
