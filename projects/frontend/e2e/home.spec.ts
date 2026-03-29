import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

async function authenticate(page: Parameters<typeof test>[0]['page'], token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

test.describe('Home page', () => {
  test('shows hero section with Get Started link when not authenticated', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Capitalism V' }).first()).toBeVisible()
    const getStartedLink = page.getByRole('link', { name: 'Get Started' })
    await expect(getStartedLink).toBeVisible()
    await expect(getStartedLink).toHaveAttribute('href', '/onboarding')
  })

  test('shows leaderboard heading', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Top Players' })).toBeVisible()
  })

  test('shows game status cards when data loads', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByText('Current Tick')).toBeVisible()
    await expect(page.getByText('Tax Rate')).toBeVisible()
    await expect(page.getByText('Active Players')).toBeVisible()
  })

  test('shows leaderboard row for player with company', async ({ page }) => {
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
    await page.goto('/')
    await expect(page.getByText('Tycoon')).toBeVisible()
    await expect(page.getByText('$750,000')).toBeVisible()
  })

  test('shows empty leaderboard message when no players', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByText('No players yet')).toBeVisible()
  })

  test('shows Start Your Empire CTA for authenticated player with unfinished onboarding', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticate(page, `token-${player.id}`)

    await page.goto('/')
    const startLink = page.getByRole('link', { name: 'Start Your Empire' })
    await expect(startLink).toBeVisible()
    await expect(startLink).toHaveAttribute('href', '/onboarding')
  })

  test('shows Go to Dashboard CTA for authenticated player who finished onboarding', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      companies: [
        {
          id: 'comp-home',
          playerId: 'player-1',
          name: 'Finished Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticate(page, `token-${player.id}`)

    await page.goto('/')
    const dashboardLink = page.getByRole('link', { name: 'Go to Dashboard' })
    await expect(dashboardLink).toBeVisible()
    await expect(dashboardLink).toHaveAttribute('href', '/dashboard')
  })
})

test.describe('Header navigation', () => {
  test('shows Login link when logged out', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.getByRole('link', { name: 'Login' })).toBeVisible()
  })

  test('logo navigates to home', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')
    await page.getByRole('link', { name: /CAPITALISM V/i }).first().click()
    await expect(page).toHaveURL(/\/$/)
  })

  test('shows Dashboard link when authenticated', async ({ page }) => {
    const player = makePlayer()
    setupMockApi(page, {
      players: [player],
      currentUserId: player.id,
      currentToken: `token-${player.id}`,
    })
    await authenticate(page, `token-${player.id}`)
    await page.goto('/')
    await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible()
    await expect(page.getByRole('banner').getByText(player.displayName)).toBeVisible()
  })
})
