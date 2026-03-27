import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

test.describe('Leaderboard page', () => {
  test('shows leaderboard heading and subtitle', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/leaderboard')
    await expect(page.getByRole('heading', { name: 'Wealth Leaderboard' })).toBeVisible()
    await expect(page.getByText('Rankings are based on total wealth')).toBeVisible()
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
    setupMockApi(page, { players: [player1, player2] })
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
    setupMockApi(page, { players: [player] })
    await page.goto('/leaderboard')
    await expect(page.getByText('Tycoon')).toBeVisible()
    // Total wealth displayed — 750000 formats as $750.0K
    await expect(page.locator('.total-wealth').getByText('$750.0K')).toBeVisible()
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
    setupMockApi(page, { players: [player] })
    await page.goto('/leaderboard')
    await expect(page.getByText('How is wealth calculated?')).toBeVisible()
    await expect(page.locator('.formula-list').getByText('Cash', { exact: true })).toBeVisible()
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
