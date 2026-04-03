import { expect, test } from '@playwright/test'
import {
  loginAs,
  makePlayer,
  makeServer,
  makeSubscription,
  setupMockApi,
} from './helpers/mock-api'

// ── Unauthenticated home page ───────────────────────────────────────────────

test.describe('Unauthenticated home page', () => {
  test('shows hero headline and Sign in CTA', async ({ page }) => {
    setupMockApi(page, { servers: [] })
    await page.goto('/')

    await expect(page.getByRole('heading', { level: 1 })).toContainText(
      'One master website, multiple live economies.',
    )
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Get started free →' })).toBeVisible()
  })

  test('shows "How it works" pitch column for guest users', async ({ page }) => {
    setupMockApi(page, { servers: [] })
    await page.goto('/')

    await expect(
      page.getByRole('heading', { name: 'Master infrastructure keeps discovery separate from simulation.' }),
    ).toBeVisible()
    await expect(page.getByRole('link', { name: 'Register free' })).toBeVisible()
  })

  test('shows empty server list state when no servers registered', async ({ page }) => {
    setupMockApi(page, { servers: [] })
    await page.goto('/')

    await expect(page.getByText('No servers have registered yet')).toBeVisible()
  })

  test('renders server card with correct info when server is online', async ({ page }) => {
    const server = makeServer()
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    await expect(page.getByText('Capitalism EU #1')).toBeVisible()
    await expect(page.getByText('EU · production · v1.0.0')).toBeVisible()
    await expect(page.locator('.status-pill.status-online')).toBeVisible()
    await expect(page.getByText('Play on server')).toBeVisible()
  })

  test('renders offline server with offline badge', async ({ page }) => {
    const server = makeServer({
      displayName: 'Offline Server',
      isOnline: false,
      lastHeartbeatAtUtc: '2020-01-01T00:00:00.000Z',
    })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    await expect(page.getByText('Offline Server')).toBeVisible()
    await expect(page.locator('.status-pill.status-offline')).toBeVisible()
  })

  test('shows player count, company count, and tick for each server', async ({ page }) => {
    const server = makeServer({ playerCount: 42, companyCount: 128, currentTick: 5000 })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    await expect(page.getByText('42')).toBeVisible()
    await expect(page.getByText('128')).toBeVisible()
    await expect(page.getByText('5000')).toBeVisible()
  })

  test('shows multiple server cards when multiple servers registered', async ({ page }) => {
    const servers = [
      makeServer({ id: 's1', serverKey: 'k1', displayName: 'EU Server', region: 'EU' }),
      makeServer({ id: 's2', serverKey: 'k2', displayName: 'US Server', region: 'US' }),
    ]
    setupMockApi(page, { servers })
    await page.goto('/')

    await expect(page.getByText('EU Server')).toBeVisible()
    await expect(page.getByText('US Server')).toBeVisible()
  })

  test('shows error state when API fails', async ({ page }) => {
    page.route('**/graphql', async (route) => {
      await route.abort('failed')
    })
    await page.goto('/')
    await expect(page.locator('.state-error')).toBeVisible()
  })

  test('refresh button reloads server list', async ({ page }) => {
    const state = setupMockApi(page, { servers: [] })
    await page.goto('/')

    await expect(page.getByText('No servers have registered yet')).toBeVisible()

    // Add a server then click refresh
    state.servers.push(makeServer())
    await page.getByRole('button', { name: 'Refresh' }).click()

    await expect(page.getByText('Capitalism EU #1')).toBeVisible()
  })
})

// ── Login page ──────────────────────────────────────────────────────────────

test.describe('Login page', () => {
  test('shows sign in form by default', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: /email/i })).toBeVisible()
    await expect(page.getByRole('textbox', { name: /password/i })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  })

  test('switches to register form when Register clicked', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')

    await page.getByRole('button', { name: 'Register' }).click()

    await expect(page.getByRole('heading', { name: 'Create account' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: /display name/i })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Create account' })).toBeVisible()
  })

  test('login success redirects to home page', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.currentPlayer = player
    await page.goto('/login')

    await page.getByRole('textbox', { name: /email/i }).fill(player.email)
    await page.getByRole('textbox', { name: /password/i }).fill('password123')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await page.waitForURL('/')
    await expect(page.getByText(player.displayName)).toBeVisible()
  })

  test('register success redirects to home page', async ({ page }) => {
    setupMockApi(page, { servers: [] })
    await page.goto('/login')

    await page.getByRole('button', { name: 'Register' }).click()
    await page.getByRole('textbox', { name: /email/i }).fill('new@example.com')
    await page.getByRole('textbox', { name: /display name/i }).fill('New Player')
    await page.getByRole('textbox', { name: /password/i }).fill('password123')
    await page.getByRole('button', { name: 'Create account' }).click()

    await page.waitForURL('/')
  })

  test('shows error on bad credentials', async ({ page }) => {
    setupMockApi(page, { servers: [] }) // no currentPlayer set → login will fail
    await page.goto('/login')

    await page.getByRole('textbox', { name: /email/i }).fill('wrong@example.com')
    await page.getByRole('textbox', { name: /password/i }).fill('badpass')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.locator('.form-error')).toBeVisible()
  })

  test('back to server directory link works', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')

    await page.getByRole('link', { name: '← Back to server directory' }).click()
    await expect(page).toHaveURL('/')
  })
})

// ── Authenticated home page ─────────────────────────────────────────────────

test.describe('Authenticated home page', () => {
  test('shows player display name and Sign out button', async ({ page }) => {
    const player = makePlayer({ displayName: 'Bob' })
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.getByText('Bob')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  })

  test('shows subscription dashboard for authenticated user', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'Subscription' })).toBeVisible()
    await expect(page.locator('.tier-badge.tier-pro')).toContainText('Pro')
    await expect(page.locator('.status-pill.status-online')).toContainText('Active')
  })

  test('shows Pro perks list for active Pro subscriber', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription({ tier: 'PRO', isActive: true })
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.getByText('Advanced market analytics')).toBeVisible()
    await expect(page.getByText('Unlimited company creation')).toBeVisible()
  })

  test('shows upgrade prompt for free-tier user', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = {
      tier: 'FREE',
      status: 'NONE',
      isActive: false,
      daysRemaining: null,
      canProlong: true,
      expiresAtUtc: null,
      startsAtUtc: null,
    }
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.locator('.upgrade-prompt')).toBeVisible()
    await expect(page.getByText(/Upgrade to/)).toBeVisible()
  })

  test('shows prolong controls for authenticated user', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.locator('#months-select')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Confirm' })).toBeVisible()
  })

  test('sign out clears auth and shows Sign in', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    await loginAs(page, state, player)
    await page.goto('/')

    await page.getByRole('button', { name: 'Sign out' }).click()

    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Get started free →' })).toBeVisible()
  })
})

// ── Subscription prolong flow ───────────────────────────────────────────────

test.describe('Subscription prolong', () => {
  test('clicking Confirm prolongs subscription and shows success message', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    await page.getByRole('button', { name: 'Confirm' }).click()

    await expect(page.locator('.prolong-success')).toBeVisible()
    await expect(page.locator('.prolong-success')).toContainText('successfully')
  })

  test('can select different month options', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    const select = page.locator('#months-select')
    await select.selectOption('6')
    await expect(select).toHaveValue('6')
  })

  test('free-tier user can subscribe via prolong CTA', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = {
      tier: 'FREE',
      status: 'NONE',
      isActive: false,
      daysRemaining: null,
      canProlong: true,
      expiresAtUtc: null,
      startsAtUtc: null,
    }
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.getByText('Subscribe to Pro')).toBeVisible()
    await page.getByRole('button', { name: 'Confirm' }).click()
    await expect(page.locator('.prolong-success')).toBeVisible()
  })
})

// ── Mobile viewport ─────────────────────────────────────────────────────────

test.describe('Mobile viewport', () => {
  test('renders hero and server list on narrow viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    setupMockApi(page, { servers: [makeServer()] })
    await page.goto('/')

    await expect(
      page.getByRole('heading', { name: 'One master website, multiple live economies.' }),
    ).toBeVisible()
    await expect(page.getByText('Capitalism EU #1')).toBeVisible()
  })

  test('renders login form usable on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    setupMockApi(page)
    await page.goto('/login')

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  })

  test('renders authenticated subscription dashboard on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = makeSubscription()
    await loginAs(page, state, player)
    await page.goto('/')

    await expect(page.getByRole('heading', { name: 'Subscription' })).toBeVisible()
    await expect(page.locator('#months-select')).toBeVisible()
  })
})

// ── Expired subscription state ────────────────────────────────────────────────

test.describe('Expired subscription', () => {
  test('shows expired status label for expired subscriber', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = {
      tier: 'PRO',
      status: 'EXPIRED',
      isActive: false,
      daysRemaining: null,
      canProlong: true,
      expiresAtUtc: '2020-01-01T00:00:00.000Z',
      startsAtUtc: '2019-10-01T00:00:00.000Z',
    }
    await loginAs(page, state, player)
    await page.goto('/')

    // Status should show expired
    await expect(page.locator('.status-pill.status-offline')).toBeVisible()
    // Can still prolong
    await expect(page.locator('#months-select')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Confirm' })).toBeVisible()
  })

  test('expired user can renew subscription via prolong CTA', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { servers: [] })
    state.subscription = {
      tier: 'PRO',
      status: 'EXPIRED',
      isActive: false,
      daysRemaining: null,
      canProlong: true,
      expiresAtUtc: '2020-01-01T00:00:00.000Z',
      startsAtUtc: '2019-10-01T00:00:00.000Z',
    }
    await loginAs(page, state, player)
    await page.goto('/')

    await page.getByRole('button', { name: 'Confirm' }).click()
    await expect(page.locator('.prolong-success')).toBeVisible()
    await expect(page.locator('.prolong-success')).toContainText('successfully')
  })
})

// ── Server card details ────────────────────────────────────────────────────────

test.describe('Server card details', () => {
  test('shows server version', async ({ page }) => {
    const server = makeServer({ version: '2.1.0' })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    // Version is shown in the server meta
    await expect(page.getByText(/v2\.1\.0/)).toBeVisible()
  })

  test('shows heartbeat distance for recently active server', async ({ page }) => {
    const server = makeServer({ isOnline: true, lastHeartbeatAtUtc: new Date().toISOString() })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    // Should show some recent heartbeat indicator
    await expect(page.getByText('Capitalism EU #1')).toBeVisible()
    // The heartbeat section should exist in the card
    await expect(page.locator('.server-stats')).toBeVisible()
  })

  test('play button links to frontendUrl', async ({ page }) => {
    const server = makeServer({ frontendUrl: 'https://game.example.com/app' })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    const playLink = page.getByText('Play on server')
    await expect(playLink).toBeVisible()
    await expect(playLink).toHaveAttribute('href', 'https://game.example.com/app')
  })

  test('region and environment shown in server meta', async ({ page }) => {
    const server = makeServer({ region: 'US', environment: 'staging', version: '1.5.0' })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    await expect(page.getByText('US · staging · v1.5.0')).toBeVisible()
  })

  test('server description is visible', async ({ page }) => {
    const server = makeServer({
      description: 'Premium economy server for serious players',
    })
    setupMockApi(page, { servers: [server] })
    await page.goto('/')

    await expect(page.getByText('Premium economy server for serious players')).toBeVisible()
  })
})
