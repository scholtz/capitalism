import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

function addPlayerShareholding(state: ReturnType<typeof setupMockApi>, playerId: string, companyId: string, shareCount = 10000) {
  state.shareholdings.push({
    companyId,
    ownerPlayerId: playerId,
    ownerCompanyId: null,
    shareCount,
  })
}

test.describe('Leaderboard page', () => {
  test('shows leaderboard heading and subtitle', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/leaderboard')
    await expect(page.getByRole('heading', { name: 'Wealth Leaderboard' })).toBeVisible()
    await expect(page.getByText('Player rankings show personal wealth')).toBeVisible()
  })

  test('shows empty state when no players', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/leaderboard')
    await expect(page.getByText('No players yet')).toBeVisible()
  })

  test('shows ranked entries for players with companies', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-1', displayName: 'Alice' })
    player1.companies.push({
      id: 'comp-1',
      playerId: 'player-1',
      name: 'Alice Corp',
      cash: 1000000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const player2 = makePlayer({ id: 'player-2', displayName: 'Bob', email: 'bob@test.com' })
    player2.companies.push({
      id: 'comp-2',
      playerId: 'player-2',
      name: 'Bob LLC',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player1, player2] })
    addPlayerShareholding(state, 'player-1', 'comp-1')
    addPlayerShareholding(state, 'player-2', 'comp-2')
    await page.goto('/leaderboard')
    await expect(page.getByText('Alice')).toBeVisible()
    await expect(page.getByText('Bob')).toBeVisible()
    // Alice should rank above Bob (higher cash)
    const cards = page.locator('.rank-card')
    await expect(cards.first()).toContainText('Alice')
  })

  test('shows wealth breakdown per entry', async ({ page }) => {
    const player = makePlayer({ displayName: 'Tycoon' })
    player.companies.push({
      id: 'comp-1',
      playerId: player.id,
      name: 'Tycoon Corp',
      cash: 750000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player] })
    addPlayerShareholding(state, player.id, 'comp-1')
    await page.goto('/leaderboard')
    await expect(page.getByText('Tycoon')).toBeVisible()
    await expect(page.locator('.total-wealth').getByText('$950.0K')).toBeVisible()
  })

  test('shows ranked entries for companies on the companies tab', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-1', displayName: 'Alice' })
    player1.companies.push({
      id: 'comp-1',
      playerId: 'player-1',
      name: 'Alice Holdings',
      cash: 1000000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const player2 = makePlayer({ id: 'player-2', displayName: 'Bob', email: 'bob@test.com' })
    player2.companies.push({
      id: 'comp-2',
      playerId: 'player-2',
      name: 'Bob Ventures',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })

    setupMockApi(page, { players: [player1, player2] })
    await page.goto('/leaderboard')
    await page.getByRole('tab', { name: 'Richest Companies' }).click()

    const cards = page.locator('.rank-card')
    await expect(cards.first()).toContainText('Alice Holdings')
    await expect(page.getByText('Owned by Alice')).toBeVisible()
    await expect(page.getByText('Bob Ventures')).toBeVisible()
  })

  test('keeps player rankings visible when company rankings request fails', async ({ page }) => {
    const player = makePlayer({ displayName: 'Fallback Hero' })
    player.companies.push({
      id: 'comp-1',
      playerId: player.id,
      name: 'Fallback Corp',
      cash: 250000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })

    setupMockApi(page, { players: [player] })
    await page.route('**/graphql', async (route) => {
      const body = route.request().postDataJSON() as { query?: string }
      if (body.query?.includes('companyRankings')) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'company rankings unavailable' }] }),
        })
        return
      }
      await route.fallback()
    })

    await page.goto('/leaderboard')

    await expect(page.getByText('Fallback Hero')).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Richest Players' })).toHaveAttribute('aria-selected', 'true')

    await page.getByRole('tab', { name: 'Richest Companies' }).click()
    await expect(page.getByText('company rankings unavailable')).toBeVisible()
    await page.getByRole('tab', { name: 'Richest Players' }).click()
    await expect(page.getByText('Fallback Hero')).toBeVisible()
  })

  test('highlights current player with YOU badge', async ({ page }) => {
    const player = makePlayer({ displayName: 'Hero' })
    player.companies.push({
      id: 'comp-1',
      playerId: player.id,
      name: 'Hero Corp',
      cash: 1000000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    setupMockApi(page, { players: [player] })
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/leaderboard')
    await expect(page.locator('.you-badge')).toBeVisible()
  })

  test('shows how wealth is calculated explainer', async ({ page }) => {
    const player = makePlayer({ displayName: 'Explainer' })
    player.companies.push({
      id: 'comp-1',
      playerId: player.id,
      name: 'Explainer Corp',
      cash: 100000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    const state = setupMockApi(page, { players: [player] })
    addPlayerShareholding(state, player.id, 'comp-1')
    await page.goto('/leaderboard')
    await expect(page.getByText('How is wealth calculated?')).toBeVisible()
    await expect(page.locator('.formula-list').getByText('Cash', { exact: true })).toBeVisible()
    await expect(page.locator('.formula-list').getByText('Stocks', { exact: true })).toBeVisible()

    await page.getByRole('tab', { name: 'Richest Companies' }).click()
    await expect(page.locator('.formula-list').getByText('Buildings', { exact: true })).toBeVisible()
    await expect(page.locator('.formula-list').getByText('Inventory', { exact: true })).toBeVisible()
  })

  test('medal badges visible for top 3 players', async ({ page }) => {
    const players = ['Gold', 'Silver', 'Bronze'].map((name, i) => {
      const p = makePlayer({ id: `player-${i}`, displayName: name, email: `${name.toLowerCase()}@test.com` })
      p.companies.push({
        id: `comp-${i}`,
        playerId: `player-${i}`,
        name: `${name} Corp`,
        cash: (3 - i) * 1000000,
        foundedAtUtc: '2026-01-01T00:00:00Z',
        buildings: [],
      })
      return p
    })
    setupMockApi(page, { players })
    await page.goto('/leaderboard')
    await expect(page.getByText('🥇')).toBeVisible()
    await expect(page.getByText('🥈')).toBeVisible()
    await expect(page.getByText('🥉')).toBeVisible()
  })
})

test.describe('Leaderboard navigation', () => {
  test('header contains Leaderboard link', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByRole('link', { name: 'Leaderboard', exact: true })).toBeVisible()
  })

  test('home page has View Full Leaderboard link', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByRole('link', { name: 'View Full Leaderboard' })).toBeVisible()
  })

  test('clicking View Full Leaderboard navigates to /leaderboard', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await page.getByRole('link', { name: 'View Full Leaderboard' }).click()
    await expect(page).toHaveURL(/\/leaderboard/)
    await expect(page.getByRole('heading', { name: 'Wealth Leaderboard' })).toBeVisible()
  })

  test('header Leaderboard link navigates to /leaderboard', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await page.getByRole('link', { name: 'Leaderboard' }).first().click()
    await expect(page).toHaveURL(/\/leaderboard/)
  })
})

test.describe('Leaderboard tab URL persistence', () => {
  test('clicking the companies tab adds ?tab=companies to the URL', async ({ page }) => {
    const player = makePlayer({ displayName: 'UrlPlayer' })
    player.companies.push({
      id: 'comp-url',
      playerId: player.id,
      name: 'URL Corp',
      cash: 100000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    setupMockApi(page, { players: [player] })
    await page.goto('/leaderboard')
    await page.getByRole('tab', { name: 'Richest Companies' }).click()
    await expect(page).toHaveURL(/\/leaderboard\?tab=companies/)
  })

  test('switching back to players tab removes ?tab from URL', async ({ page }) => {
    const player = makePlayer({ displayName: 'BackPlayer' })
    player.companies.push({
      id: 'comp-back',
      playerId: player.id,
      name: 'Back Corp',
      cash: 200000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    setupMockApi(page, { players: [player] })
    await page.goto('/leaderboard?tab=companies')
    await page.getByRole('tab', { name: 'Richest Players' }).click()
    // After switching to players tab, the tab query param is removed
    await expect(page).toHaveURL(/\/leaderboard($|\?)/)
    await expect(page).not.toHaveURL(/tab=companies/)
    await expect(page.getByRole('tab', { name: 'Richest Players' })).toHaveAttribute('aria-selected', 'true')
  })

  test('navigating to /leaderboard?tab=companies opens companies tab directly', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-url-1', displayName: 'UrlAlice' })
    player1.companies.push({
      id: 'comp-url-1',
      playerId: 'player-url-1',
      name: 'UrlAlice Holdings',
      cash: 800000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    setupMockApi(page, { players: [player1] })
    await page.goto('/leaderboard?tab=companies')

    await expect(page.getByRole('tab', { name: 'Richest Companies' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByRole('tab', { name: 'Richest Players' })).toHaveAttribute('aria-selected', 'false')
    await expect(page.getByText('UrlAlice Holdings')).toBeVisible()
  })

  test('page reload restores the previously active companies tab', async ({ page }) => {
    const player = makePlayer({ displayName: 'ReloadPlayer' })
    player.companies.push({
      id: 'comp-reload',
      playerId: player.id,
      name: 'Reload Industries',
      cash: 300000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })
    setupMockApi(page, { players: [player] })
    await page.goto('/leaderboard')

    await page.getByRole('tab', { name: 'Richest Companies' }).click()
    await expect(page).toHaveURL(/tab=companies/)

    // Reload the page – companies tab should still be active
    await page.reload()
    await expect(page.getByRole('tab', { name: 'Richest Companies' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByText('Reload Industries')).toBeVisible()
  })
})

test.describe('Leaderboard tick-refresh stability', () => {
  test('background tick refresh does not blank the players tab or show a loading spinner', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-tick-1', displayName: 'Tick Alice' })
    player1.companies.push({
      id: 'comp-tick-1',
      playerId: 'player-tick-1',
      name: 'Tick Alice Corp',
      cash: 1000000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })

    const state = setupMockApi(page, { players: [player1] })
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/leaderboard')
    await expect(page.getByText('Tick Alice')).toBeVisible()

    // Live tick chip must be visible with the current tick value
    await expect(page.locator('.leaderboard-tick-chip')).toBeVisible()
    await expect(page.locator('.leaderboard-tick-value')).toContainText('10')

    // Simulate tick advance — store will pick this up on next poll
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Rankings content must remain visible — no full-page loading flash
    await expect(page.getByText('Tick Alice')).toBeVisible()
    await expect(page.locator('.state-box', { hasText: 'loading' })).toBeHidden()
  })

  test('live tick chip updates its value when a new tick is received', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-tick-chip-1', displayName: 'Chip Tester' })

    const state = setupMockApi(page, { players: [player1] })
    state.gameState.currentTick = 42 // Jan 02, 2000, 18:00
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/leaderboard')
    // Tick 42 = Jan 02, 2000, 18:00 in game time
    await expect(page.locator('.leaderboard-tick-value')).toContainText('18:00')

    // Advance tick in mock state
    state.gameState.currentTick = 43 // Jan 02, 2000, 19:00
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Tick chip must reflect the new game time (19:00 instead of 18:00)
    await expect(page.locator('.leaderboard-tick-value')).toContainText('19:00')
  })

  test('active companies tab is preserved after a background tick refresh', async ({ page }) => {
    const player1 = makePlayer({ id: 'player-tab-1', displayName: 'TabAlice' })
    player1.companies.push({
      id: 'comp-tab-1',
      playerId: 'player-tab-1',
      name: 'Tab Alice Holdings',
      cash: 900000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    })

    const state = setupMockApi(page, { players: [player1] })
    state.gameState.currentTick = 5
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 400).toISOString()

    await page.goto('/leaderboard?tab=companies')
    await expect(page.getByRole('tab', { name: 'Richest Companies' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByText('Tab Alice Holdings')).toBeVisible()

    // Simulate tick advance
    state.gameState.currentTick = 6
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Companies tab must remain active and content still visible after refresh
    await expect(page.getByRole('tab', { name: 'Richest Companies' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByText('Tab Alice Holdings')).toBeVisible()
    // No loading spinner must appear over the existing companies list
    await expect(page.locator('.state-box', { hasText: 'loading' })).toBeHidden()
  })

  test('multiple entries remain visible and content does not jump during a tick refresh', async ({ page }) => {
    // Create enough players to fill the leaderboard
    const players = Array.from({ length: 5 }, (_, i) =>
      makePlayer({
        id: `player-scroll-${i}`,
        email: `scroll${i}@test.com`,
        displayName: `Scroll Player ${i + 1}`,
        companies: [
          {
            id: `comp-scroll-${i}`,
            playerId: `player-scroll-${i}`,
            name: `Scroll Corp ${i + 1}`,
            cash: (5 - i) * 100000,
            foundedAtUtc: '2026-01-01T00:00:00Z',
            buildings: [],
          },
        ],
      }),
    )

    const state = setupMockApi(page, { players })
    state.gameState.currentTick = 50
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/leaderboard')
    // All 5 entries should be visible
    for (let i = 0; i < 5; i++) {
      await expect(page.getByText(`Scroll Player ${i + 1}`)).toBeVisible()
    }

    // Simulate tick advance — wealth order remains the same so deepEqual skips DOM update
    state.gameState.currentTick = 51
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // All entries must remain visible after background refresh — no content jump
    for (let i = 0; i < 5; i++) {
      await expect(page.getByText(`Scroll Player ${i + 1}`)).toBeVisible()
    }
    await expect(page.locator('.state-box', { hasText: 'loading' })).toBeHidden()
  })
})
