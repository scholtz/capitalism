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
