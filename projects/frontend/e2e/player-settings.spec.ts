import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

test.describe('Player Settings', () => {
  test('shows player alias settings when authenticated', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Aster Nova Finch' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    await expect(page.getByRole('heading', { name: 'Player Settings' })).toBeVisible()
    await expect(page.getByLabel('Your Player Alias')).toBeVisible()
    await expect(page.getByLabel('Your Player Alias')).toHaveValue('Aster Nova Finch')
  })

  test('shows leaderboard preview with current alias', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Nova Ember Hart' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    await expect(page.getByText('Leaderboard preview')).toBeVisible()
    await expect(page.locator('[aria-label="Leaderboard preview"] .preview-name')).toContainText(
      'Nova Ember Hart',
    )
  })

  test('allows generating a random alias', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Old Name Here' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    const input = page.getByLabel('Your Player Alias')
    await expect(input).toHaveValue('Old Name Here')

    await page.getByRole('button', { name: 'Generate random name' }).click()
    const newName = await input.inputValue()
    expect(newName.split(' ')).toHaveLength(3)
    expect(newName.length).toBeLessThanOrEqual(60)
  })

  test('saves updated alias and shows success message', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Original Name' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    await page.getByLabel('Your Player Alias').fill('Bright Star Finch')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Display name updated.')).toBeVisible()
  })

  test('shows validation error for too-short alias', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Valid Name Here' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    await page.getByLabel('Your Player Alias').fill('AB')
    await expect(page.getByText('Display name must be at least 3 characters long.')).toBeVisible()
  })

  test('shows privacy hint about not using real name', async ({ page }) => {
    const player = makePlayer({ personalAccountName: 'Safe Alias Name' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/settings')

    await expect(
      page.getByText('This is your in-game identity — do not use your real name.'),
    ).toBeVisible()
  })
})

test.describe('Referral code UX', () => {
  test('referral banner is NOT shown for unauthenticated visitors without ref param', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/')
    await expect(page.locator('.referral-banner')).toBeHidden()
  })

  test('referral invitation message is NOT shown before login even when ref param is present', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/?ref=WELCOME2026')
    // Referral invitation must be hidden until the user logs in — it MUST NOT be shown pre-login
    await expect(page.locator('.referral-banner')).toBeHidden()
  })

  test('referral code is stored in localStorage when captured from URL', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/?ref=TESTCODE99')

    const stored = await page.evaluate(() => localStorage.getItem('referral_code'))
    expect(stored).toBe('TESTCODE99')
  })

  test('referral code is removed from localStorage after user logs in', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    // Pre-store a referral code and then authenticate
    await page.addInitScript(
      ({ token, expires }) => {
        localStorage.setItem('referral_code', 'LOGINCLEARS')
        localStorage.setItem('auth_token', token)
        localStorage.setItem('auth_expires', expires)
      },
      {
        token: `token-${player.id}`,
        expires: new Date(Date.now() + 7200000).toISOString(),
      },
    )
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/')

    // After mounting with auth token present, the referral code is cleared from localStorage
    await expect(page.locator('.referral-banner--pending')).toBeHidden()
    const stored = await page.evaluate(() => localStorage.getItem('referral_code'))
    expect(stored).toBeNull()
  })

  test('referral welcome banner is shown after login when a referral code was present', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    // Pre-store a referral code and authenticate — the app detects the code on mount and marks it applied
    await page.addInitScript(
      ({ token, expires }) => {
        localStorage.setItem('referral_code', 'SHOWBANNER')
        localStorage.setItem('auth_token', token)
        localStorage.setItem('auth_expires', expires)
      },
      {
        token: `token-${player.id}`,
        expires: new Date(Date.now() + 7200000).toISOString(),
      },
    )
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/')

    // The welcome banner MUST be shown after login when a referral code was present
    await expect(page.locator('.referral-banner')).toBeVisible()
    await expect(page.locator('.referral-banner')).toContainText('referral code has been applied')
  })

  test('referral welcome banner can be dismissed', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    await page.addInitScript(
      ({ token, expires }) => {
        localStorage.setItem('referral_code', 'DISMISSTEST')
        localStorage.setItem('auth_token', token)
        localStorage.setItem('auth_expires', expires)
      },
      {
        token: `token-${player.id}`,
        expires: new Date(Date.now() + 7200000).toISOString(),
      },
    )
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/')
    await expect(page.locator('.referral-banner')).toBeVisible()
    await page.locator('.referral-dismiss').click()
    await expect(page.locator('.referral-banner')).toBeHidden()
  })
})
