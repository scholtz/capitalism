/**
 * Onboarding flow E2E tests.
 * Covers: registration, login, onboarding wizard, and dashboard verification.
 */
import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer, makeStartupPackOffer } from './helpers/mock-api'

test.describe('Authentication', () => {
  test('register new account and redirect to home', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')

    // Switch to register mode
    await page.getByRole('button', { name: 'Create Account' }).click()
    await expect(page.getByRole('heading', { name: 'Create Account' })).toBeVisible()

    // Fill form
    await page.getByLabel('Email').fill('new@test.com')
    await page.getByLabel('Display Name').fill('New Player')
    await page.getByLabel('Password').fill('TestPass1!')

    // Submit
    await page.getByRole('button', { name: 'Create Account' }).first().click()
    await page.waitForURL('/')
    await expect(page).toHaveURL('/')
  })

  test('login existing account', async ({ page }) => {
    const player = makePlayer({ email: 'existing@test.com', password: 'TestPass1!' })
    setupMockApi(page, { players: [player] })
    await page.goto('/login')

    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
    await page.getByLabel('Email').fill('existing@test.com')
    await page.getByLabel('Password').fill('TestPass1!')
    await page.getByRole('button', { name: 'Sign In' }).click()
    await page.waitForURL('/')
    await expect(page).toHaveURL('/')
  })

  test('login with wrong password shows error', async ({ page }) => {
    const player = makePlayer({ email: 'existing@test.com', password: 'TestPass1!' })
    setupMockApi(page, { players: [player] })
    await page.goto('/login')

    await page.getByLabel('Email').fill('existing@test.com')
    await page.getByLabel('Password').fill('WrongPass!')
    await page.getByRole('button', { name: 'Sign In' }).click()
    await expect(page.getByRole('alert')).toBeVisible()
  })

  test('toggle between login and register forms', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/login')

    // Initially login
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()

    // Switch to register
    await page.getByRole('button', { name: 'Create Account' }).click()
    await expect(page.getByRole('heading', { name: 'Create Account' })).toBeVisible()
    await expect(page.getByLabel('Display Name')).toBeVisible()

    // Switch back to login
    await page.getByRole('button', { name: 'Sign In' }).click()
    await expect(page.getByRole('heading', { name: 'Sign In' })).toBeVisible()
  })
})

test.describe('Onboarding wizard', () => {
  test('complete full onboarding flow', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    // Authenticate first
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/onboarding')

    // Step 1: Choose industry
    await expect(page.getByRole('heading', { name: 'Start Your Empire' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Select Furniture industry
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: Name company and pick product
    await expect(page.getByRole('heading', { name: 'Name Your Company & Pick a Product' })).toBeVisible()
    await page.getByLabel('Company Name').fill('My Empire Inc')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()

    // Verify summary appears
    await expect(page.locator('.summary', { hasText: 'My Empire Inc' })).toBeVisible()
    await expect(page.getByText('Bratislava')).toBeVisible()

    // Complete onboarding
    await page.getByRole('button', { name: 'Start Playing' }).click()

    // Step 4: Completion screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'Scale faster while your first business is taking off' }),
    ).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'View Leaderboard' })).toBeVisible()

    // Navigate to dashboard
    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })

  test('renders mixed resource and intermediate-product recipes without runtime errors', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, {
      players: [player],
      productTypes: [
        {
          id: 'prod-chair',
          name: 'Wooden Chair',
          slug: 'wooden-chair',
          industry: 'FURNITURE',
          basePrice: 45,
          baseCraftTicks: 2,
          outputQuantity: 20,
          energyConsumptionMwh: 1,
          unitName: 'Chair',
          unitSymbol: 'chairs',
          isProOnly: false,
          description: 'Starter furniture product.',
          recipes: [
            { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', imageUrl: null, description: 'Wood' }, inputProductType: null, quantity: 1 },
          ],
        },
        {
          id: 'prod-components',
          name: 'Electronic Components',
          slug: 'electronic-components',
          industry: 'ELECTRONICS',
          basePrice: 50,
          baseCraftTicks: 3,
          outputQuantity: 16,
          energyConsumptionMwh: 1.2,
          unitName: 'Pack',
          unitSymbol: 'packs',
          isProOnly: false,
          description: 'Intermediate electronics input.',
          recipes: [],
        },
        {
          id: 'prod-electronic-table',
          name: 'Electronic Table',
          slug: 'electronic-table',
          industry: 'FURNITURE',
          basePrice: 520,
          baseCraftTicks: 6,
          outputQuantity: 1,
          energyConsumptionMwh: 2,
          unitName: 'Piece',
          unitSymbol: 'pcs',
          isProOnly: false,
          description: 'Smart table with electronics.',
          recipes: [
            { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', imageUrl: null, description: 'Wood' }, inputProductType: null, quantity: 1 },
            { resourceType: null, inputProductType: { id: 'prod-components', name: 'Electronic Components', slug: 'electronic-components', unitName: 'Pack', unitSymbol: 'packs' }, quantity: 10 },
          ],
        },
      ],
    })

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const pageErrors: string[] = []
    page.on('pageerror', (error) => {
      pageErrors.push(error.message)
    })

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.getByRole('heading', { name: 'Name Your Company & Pick a Product' })).toBeVisible()
    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card', { hasText: 'Wooden Chair' })).toBeVisible()
    expect(pageErrors).toEqual([])

    await page.getByLabel('Company Name').fill('Mixed Recipe Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'Start Playing' }).click()
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
    expect(pageErrors).toEqual([])
  })

  test('step 1 Next button disabled until industry selected', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Next button should be disabled
    await expect(page.getByRole('button', { name: 'Next' })).toBeDisabled()

    // Select industry
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await expect(page.getByRole('button', { name: 'Next' })).toBeEnabled()
  })

  test('shows only the simple starter product for the selected industry', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card')).toContainText('Wooden Chair')
  })

  test('back button navigates to previous step', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')

    // Go to step 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()

    // Back to step 1
    await page.getByRole('button', { name: 'Back' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
  })

  test('redirects to login if not authenticated', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await page.waitForURL('/login')
    await expect(page).toHaveURL('/login')
  })
})

test.describe('Dashboard', () => {
  test('shows empty state with onboarding link when no companies', async ({ page }) => {
    const player = makePlayer({ companies: [] })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    await expect(page.getByText('You have no companies yet')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Start Onboarding' })).toBeVisible()
  })

  test('shows company card after onboarding', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'comp-1',
      playerId: player.id,
      name: 'Test Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        { id: 'b1', companyId: 'comp-1', cityId: 'city-ba', type: 'FACTORY', name: 'Test Corp Factory', latitude: 48.15, longitude: 17.11, level: 1, powerConsumption: 2, isForSale: false, units: [], pendingConfiguration: null },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    const companyCard = page.locator('.company-card').first()
    await expect(companyCard.getByRole('heading', { name: 'Test Corp' })).toBeVisible()
    await expect(companyCard.getByText('$500,000')).toBeVisible()
    await expect(companyCard.locator('.building-card', { hasText: 'Test Corp Factory' })).toBeVisible()
    await expect(companyCard.locator('.building-type-label', { hasText: 'Factory' })).toBeVisible()
  })

  test('redirects to login if not authenticated', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/dashboard')
    await page.waitForURL('/login')
    await expect(page).toHaveURL('/login')
  })
})

test.describe('Full onboarding journey', () => {
  test('register → onboard → dashboard shows new company', async ({ page }) => {
    setupMockApi(page)

    // Start at home, click Get Started
    await page.goto('/')
    await page.getByRole('link', { name: 'Get Started' }).click()
    await page.waitForURL('/login')

    // Register
    await page.getByRole('button', { name: 'Create Account' }).click()
    await page.getByLabel('Email').fill('journey@test.com')
    await page.getByLabel('Display Name').fill('Journey Player')
    await page.getByLabel('Password').fill('TestPass1!')
    await page.getByRole('button', { name: 'Create Account' }).first().click()
    await page.waitForURL('/')
    await page.waitForFunction(() => !!localStorage.getItem('auth_token'))

    // Go to onboarding
    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Step 1: Choose industry
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Choose city
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: Company name + product
    await page.getByLabel('Company Name').fill('Journey Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()

    // Complete
    await page.getByRole('button', { name: 'Start Playing' }).click()

    // Step 4: Completion screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()

    // Go to dashboard
    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')

    // Verify dashboard has company
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    await expect(page.locator('.company-card').first().getByRole('heading', { name: 'Journey Corp' })).toBeVisible()
  })
})

test.describe('Onboarding resume and progress persistence', () => {
  test('resumes at correct step after page refresh mid-onboarding', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')

    // Select industry and proceed to step 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()

    // Simulate a page refresh
    await page.reload()

    // Should resume on step 2 (Choose Your City) with industry already selected
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
  })

  test('resumes on step 3 with company name and product preserved after refresh', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')

    // Complete steps 1 and 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Name Your Company & Pick a Product' })).toBeVisible()

    // Fill in company name and select product
    await page.getByLabel('Company Name').fill('Resume Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()

    // Simulate a page refresh
    await page.reload()

    // Should resume on step 3 with previously entered data
    await expect(page.getByRole('heading', { name: 'Name Your Company & Pick a Product' })).toBeVisible()
    await expect(page.getByLabel('Company Name')).toHaveValue('Resume Corp')
  })

  test('completion state shows achievement details and links to dashboard and leaderboard', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')

    // Complete all 3 steps
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Celebration Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'Start Playing' }).click()

    // Verify completion step UI
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'Scale faster while your first business is taking off' }),
    ).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'View Leaderboard' })).toBeVisible()
    await expect(page.getByText('3 months of Pro', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Should show achievement items (factory and shop names)
    await expect(page.locator('.achievement-item')).toHaveCount(4)

    // Click leaderboard link
    await page.getByRole('link', { name: 'View Leaderboard' }).click()
    await page.waitForURL('/leaderboard')
    await expect(page).toHaveURL('/leaderboard')
  })

  test('player can claim the startup pack after onboarding and see claimed entitlement state', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Claim Pack Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'Start Playing' }).click()

    await page.getByRole('button', { name: 'Claim startup pack' }).click()

    await expect(page.getByText('Startup pack activated')).toBeVisible()
    await expect(page.getByText('$750,000', { exact: true })).toBeVisible()

    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page.getByText('Startup pack activated')).toBeVisible()
  })

  test('player can dismiss the startup pack and revisit it from the dashboard', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Maybe Later Corp')
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'Start Playing' }).click()

    await page.getByRole('button', { name: 'Maybe later' }).click()
    await expect(page.getByText('The offer has been saved to your dashboard until it expires.')).toBeVisible()

    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page.getByRole('heading', { name: 'Your startup pack is still available' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
  })

  test('already-onboarded player visiting /onboarding is redirected to dashboard', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer({
        status: 'EXPIRED',
        expiresAtUtc: '2026-01-02T12:00:00Z',
      }),
      companies: [
        {
          id: 'comp-existing',
          playerId: 'player-1',
          name: 'Existing Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
    await expect(page.getByText('Startup pack expired')).toBeVisible()
  })
})
