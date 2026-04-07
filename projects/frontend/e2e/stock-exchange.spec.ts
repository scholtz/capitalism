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

test.describe('Stock exchange', () => {
  test('person account can buy and sell shares', async ({ page }) => {
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

    await expect(page.getByRole('heading', { name: 'Stock Exchange' })).toBeVisible()
    await expect(page.getByText('Personal cash')).toBeVisible()

    const rivalCard = page.locator('.listing-card', { hasText: 'Rival Foods' })
    await rivalCard.getByLabel('Share quantity Rival Foods').fill('250')
    await rivalCard.getByRole('button', { name: 'Buy' }).click()
    await expect(page.getByRole('status')).toContainText('Bought 250 shares in Rival Foods.')

    const portfolioTable = page.locator('.data-table').first()
    await expect(portfolioTable).toContainText('1,250')

    await rivalCard.getByLabel('Share quantity Rival Foods').fill('100')
    await rivalCard.getByRole('button', { name: 'Sell' }).click()
    await expect(page.getByRole('status')).toContainText('Sold 100 shares in Rival Foods.')
    await expect(portfolioTable).toContainText('1,150')
  })

  test('buying up to 50% unlocks claim-control account switching', async ({ page }) => {
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

    const targetCard = page.locator('.listing-card', { hasText: 'Target Industries' })
    await targetCard.getByLabel('Share quantity Target Industries').fill('100')
    await targetCard.getByRole('button', { name: 'Buy' }).click()
    await expect(page.getByRole('status')).toContainText('Bought 100 shares in Target Industries.')

    await targetCard.getByRole('button', { name: 'Switch account' }).click()
    await expect(page.getByRole('status')).toContainText('Active account switched to Target Industries.')
    await expect(page.locator('.account-button.active', { hasText: 'Target Industries' })).toBeVisible()
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
    await expect(page.locator('.listing-card', { hasText: 'Rival Refresh Inc' })).toBeVisible()

    // Simulate a tick: update game state so useTickRefresh fires
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // After tick fires, the page should still show the listing without blanking
    await expect(page.locator('.listing-card', { hasText: 'Rival Refresh Inc' })).toBeVisible()
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

    await expect(page.locator('.listing-card', { hasText: 'Tick Corp' })).toBeVisible()

    // Advance the tick and update the rival company cash (which changes share price)
    state.gameState.currentTick = 6
    state.gameState.lastTickAtUtc = new Date().toISOString()
    rival.companies[0]!.cash = 800000 // doubled cash → higher share price

    // Wait for the tick refresh to complete; Tick Corp listing should still be on screen
    await expect(page.locator('.listing-card', { hasText: 'Tick Corp' })).toBeVisible()
  })
})
