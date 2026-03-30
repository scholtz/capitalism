/**
 * Onboarding flow E2E tests.
 * Covers: registration, login, onboarding wizard, and dashboard verification.
 * Implements issue #54 — guest onboarding sandbox wizard with save-progress handoff.
 */
import { test, expect, type Page } from '@playwright/test'
import { setupMockApi, makePlayer, makeStartupPackOffer } from './helpers/mock-api'

async function authenticateViaLocalStorage(page: Page, token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

/** Drives the guest wizard through steps 1–4 (no auth) and lands on the step-5 save-progress screen. */
async function completeGuestSteps1to4(page: Page, companyName = 'Guest Corp') {
  await page.locator('.industry-card', { hasText: 'Furniture' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.getByLabel('Company Name').fill(companyName)
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  await page.getByRole('button', { name: 'Purchase First Factory' }).click()
  await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /High Street Retail Space/i }).click()
  await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
  await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
}

async function completeGuidedOnboarding(page: Page, companyName: string) {
  await page.locator('.industry-card', { hasText: 'Furniture' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()

  await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
  await page.getByLabel('Company Name').fill(companyName)
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  await page.getByRole('button', { name: 'Purchase First Factory' }).click()

  await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
  await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /High Street Retail Space/i }).click()
  await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
}

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

    await authenticateViaLocalStorage(page, `token-${player.id}`)
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

    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('My Empire Inc')
    await expect(page.getByText('Starting cash')).toBeVisible()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await expect(page.locator('.budget-card').getByText('Cash after purchase')).toBeVisible()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await expect(page.getByText('Factory secured')).toBeVisible()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await expect(page.locator('.summary', { hasText: 'My Empire Inc' })).toBeVisible()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    const pageErrors: string[] = []
    page.on('pageerror', (error) => {
      pageErrors.push(error.message)
    })

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Mixed Recipe Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card', { hasText: 'Wooden Chair' })).toBeVisible()
    expect(pageErrors).toEqual([])

    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await page.getByLabel('Company Name').fill('Starter Product Co')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card')).toContainText('Wooden Chair')
    await expect(page.getByText('Your starter company uses the free catalog.')).toBeVisible()
  })

  test('back button navigates to previous step', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')

    // Go to step 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()

    // Back to step 1
    await page.getByRole('button', { name: 'Back' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
  })

  test('shows wizard step 1 to unauthenticated visitors', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(page).toHaveURL(/\/onboarding/)
  })

  test('can complete onboarding with Food Processing industry', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/onboarding')

    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Select Food Processing industry
    await page.locator('.industry-card', { hasText: 'Food Processing' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: choose factory lot
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Bread Empire Inc')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: choose product (Bread) and shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Bread' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Completion
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
  })

  test('can complete onboarding with Healthcare industry', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.goto('/onboarding')

    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Select Healthcare industry
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: choose factory lot
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Pharma Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: choose product (Basic Medicine) and shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Basic Medicine' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Completion
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
  })
})

test.describe('Guest onboarding wizard', () => {
  test('unauthenticated visitor can complete steps 1-4 without login', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1: Choose industry
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: Choose factory lot (no company created on backend)
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: Choose product and shop lot (no backend call)
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: Guest save-progress screen
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Save & Launch' })).toBeVisible()
  })

  test('guest save-progress form shows register and login tabs', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Quick completion through steps 1-4
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Save-progress section should show register/login tabs
    await expect(page.locator('.btn-tab', { hasText: 'Create Account' })).toBeVisible()
    await expect(page.locator('.btn-tab', { hasText: 'Log In' })).toBeVisible()

    // Switch to login tab
    await page.locator('.btn-tab', { hasText: 'Log In' }).click()
    // Display Name field should be hidden in login mode
    await expect(page.locator('#guestDisplayName')).toBeHidden()
  })

  test('guest can register and migrate progress', async ({ page }) => {
    const state = setupMockApi(page)
    await page.goto('/onboarding')

    // Complete steps 1-4 as guest
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Fill register form and submit
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await page.locator('#guestEmail').fill('guest@test.com')
    await page.locator('#guestDisplayName').fill('Guest Player')
    await page.locator('#guestPassword').fill('GuestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // After registration, the backend mutations run and completion screen shows
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    // Should be authenticated now
    expect(state.currentUserId).toBeTruthy()
  })

  test('factory lot taken: restarts wizard at step 1 with lot-conflict message', async ({ page }) => {
    const state = setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Mark the factory lot as already owned before the guest submits
    const factoryLot = state.buildingLots.find((l) => l.id === 'lot-industrial-1')!
    factoryLot.ownerCompanyId = 'other-company'

    await page.locator('#guestEmail').fill('newguest@test.com')
    await page.locator('#guestDisplayName').fill('New Guest')
    await page.locator('#guestPassword').fill('GuestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Wizard restarts at step 1 with the retry message
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(page.locator('.error-message, .error-global, [role="alert"]').filter({ hasText: /lots you chose was taken/i })).toBeVisible()
  })

  test('shop lot taken: restarts wizard at step 1 with lot-conflict message', async ({ page }) => {
    const state = setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Mark the shop lot as already owned before the guest submits (factory lot is fine)
    const shopLot = state.buildingLots.find((l) => l.id === 'lot-commercial-1')!
    shopLot.ownerCompanyId = 'other-company'

    await page.locator('#guestEmail').fill('newguest2@test.com')
    await page.locator('#guestDisplayName').fill('New Guest 2')
    await page.locator('#guestPassword').fill('GuestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Wizard restarts at step 1 with the retry message
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(page.locator('.error-message, .error-global, [role="alert"]').filter({ hasText: /lots you chose was taken/i })).toBeVisible()
  })

  test('non-conflict backend error: shows inline error without restarting', async ({ page }) => {
    const state = setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Inject a generic non-lot-conflict error by removing all products (causes INVALID_PRODUCT)
    state.productTypes = []

    await page.locator('#guestEmail').fill('newguest3@test.com')
    await page.locator('#guestDisplayName').fill('New Guest 3')
    await page.locator('#guestPassword').fill('GuestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Must NOT restart the wizard — step-5 heading still visible
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    // An explicit error message must appear
    await expect(page.locator('.error-message, .error-global, [role="alert"]').first()).toBeVisible()
    // The error must NOT say "lot was taken"
    await expect(page.locator('[role="alert"], .error-message, .error-global').filter({ hasText: /lots you chose was taken/i })).toHaveCount(0)
  })

  test('login-mode migration: existing player logs in and migrates guest progress', async ({ page }) => {
    const existingPlayer = makePlayer({ email: 'existing@test.com', password: 'TestPass1!' })
    const state = setupMockApi(page, { players: [existingPlayer] })
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Switch to login tab
    await page.locator('.btn-tab', { hasText: 'Log In' }).click()
    await page.locator('#guestEmail').fill('existing@test.com')
    await page.locator('#guestPassword').fill('TestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // After login, migrations run and completion screen shows
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    expect(state.currentUserId).toBe(existingPlayer.id)
  })

  test('already-onboarded login: redirects to dashboard without attempting migration', async ({
    page,
  }) => {
    const completedPlayer = makePlayer({
      email: 'done@test.com',
      password: 'TestPass1!',
      onboardingCompletedAtUtc: new Date().toISOString(),
      onboardingShopBuildingId: 'building-shop-done',
    })
    const state = setupMockApi(page, { players: [completedPlayer] })
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Switch to login tab
    await page.locator('.btn-tab', { hasText: 'Log In' }).click()
    await page.locator('#guestEmail').fill('done@test.com')
    await page.locator('#guestPassword').fill('TestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Already completed — redirect to dashboard immediately
    await page.waitForURL(/\/dashboard/)
    expect(state.currentUserId).toBe(completedPlayer.id)
  })

  test('guest can start onboarding from home page Get Started button', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')

    // Click Get Started — should go to /onboarding, not /login (unauthenticated guest entry point)
    const getStartedLink = page.getByRole('link', { name: 'Get Started' })
    await expect(getStartedLink).toBeVisible()
    await getStartedLink.click()
    await page.waitForURL(/\/onboarding/)

    // Should land on step 1 with no auth required
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(page).not.toHaveURL(/\/login/)
  })

  test('guest completion screen shows simulated first-profit preview', async ({ page }) => {
    // No auth setup — tests that the guest (unauthenticated) flow shows the profit preview on step 5
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Step 5 is the guest save-progress screen (no auth required to reach it)
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()

    // Profit preview panel should be visible after completing all guest steps
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
    await expect(page.getByText('First-Tick Profit Preview')).toBeVisible()
    await expect(page.getByText('Estimated revenue')).toBeVisible()
    await expect(page.getByText('Estimated materials cost')).toBeVisible()
    await expect(page.getByText('Projected net profit')).toBeVisible()

    // Revenue should be a positive dollar amount
    const revenueEl = page.locator('.profit-stat-revenue')
    await expect(revenueEl).toBeVisible()
    const revenueText = await revenueEl.textContent()
    expect(revenueText).toMatch(/\$\d/)
  })

  test('guest completion screen shows tick panel explaining simulation time', async ({ page }) => {
    // No auth setup — tests that the guest (unauthenticated) flow shows tick info on step 5
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Step 5 is the guest save-progress screen (no auth required to reach it)
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()

    // Tick panel should explain time progression
    await expect(page.locator('.guest-tick-panel')).toBeVisible()
    await expect(page.getByText('Wait for the next tick')).toBeVisible()
    // Current tick should be displayed — use a regex so the test is resilient to mock data changes
    await expect(page.getByText(/Current simulation tick: \d+\./)).toBeVisible()
  })
})

test.describe('Dashboard', () => {
  test('redirects unfinished player from dashboard back into onboarding', async ({ page }) => {
    const player = makePlayer({ companies: [] })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/dashboard')
    await page.waitForURL(/\/onboarding/)
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
  })

  test('shows company card after onboarding', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T12:00:00Z' })
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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

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

    // Navigate to login page to register (Get Started now routes to /onboarding for guest flow)
    await page.goto('/login')

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

    await completeGuidedOnboarding(page, 'Journey Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()

    // Go to dashboard
    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')

    // Verify dashboard has company
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    await expect(page.locator('.company-card').first().getByRole('heading', { name: 'Journey Corp' })).toBeVisible()
  })

  test('guest → home page Get Started → complete wizard → register → empire launched', async ({
    page,
  }) => {
    setupMockApi(page)

    // Start at home page as unauthenticated visitor
    await page.goto('/')
    // Click Get Started — now routes to /onboarding (guest mode entry)
    await page.getByRole('link', { name: 'Get Started' }).click()
    await page.waitForURL(/\/onboarding/)
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // Complete steps 1-4 as guest (no auth)
    await completeGuestSteps1to4(page, 'Home Guest Corp')

    // Step 5: save-progress screen with profit preview and registration form
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()

    // Register to save progress
    await page.locator('#guestEmail').fill('homeguest@test.com')
    await page.locator('#guestDisplayName').fill('Home Guest')
    await page.locator('#guestPassword').fill('TestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Migration completes → authenticated completion screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
  })
})

test.describe('Onboarding resume and progress persistence', () => {
  test('resumes at correct step after page refresh mid-onboarding', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

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

  test('resumes after first factory purchase and keeps the onboarding company state', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')

    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Resume Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    await page.reload()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await expect(page.getByText('Factory secured')).toBeVisible()
    await expect(page.getByText('Available cash')).toBeVisible()
  })

  test('player can recover when the selected shop lot becomes unavailable', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Recovery Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const shopLot = state.buildingLots.find((lot) => lot.name === 'High Street Retail Space')
    expect(shopLot).toBeTruthy()
    shopLot!.ownerCompanyId = 'other-company'
    shopLot!.ownerCompany = { id: 'other-company', name: 'Other Corp' }
    shopLot!.buildingId = 'other-shop'
    shopLot!.building = { id: 'other-shop', name: 'Other Shop', type: 'SALES_SHOP' }

    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
    await expect(page.getByRole('alert')).toContainText('already been purchased')

    await page.getByRole('button', { name: 'List View' }).click()
    const backupLot = state.buildingLots.find((lot) => lot.id === 'lot-business-1')
    expect(backupLot).toBeTruthy()
    backupLot!.suitableTypes = 'SALES_SHOP,COMMERCIAL'
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Innovation Campus Office/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
  })

  test('completion state shows achievement details and links to dashboard and leaderboard', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Celebration Corp')

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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Claim Pack Corp')

    await page.getByRole('button', { name: 'Claim startup pack' }).click()

    await expect(page.getByText('Startup pack activated')).toBeVisible()
    await expect(page.getByText('$250,000 will be credited to Claim Pack Corp.')).toBeVisible()

    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page.getByText('Startup pack activated')).toBeVisible()
  })

  test('player can dismiss the startup pack and revisit it from the dashboard', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Maybe Later Corp')

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

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
    await expect(page.getByText('Startup pack expired')).toBeVisible()
  })

  test('startup pack offer remains visible after page reload when in SHOWN state', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer({
        status: 'SHOWN',
        shownAtUtc: '2026-01-01T13:00:00Z',
        expiresAtUtc: new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString(),
      }),
      companies: [
        {
          id: 'comp-shown',
          playerId: 'player-1',
          name: 'Reload Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'Your startup pack is still available' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Reload the page — offer must still be visible (state persisted in backend)
    await page.reload()

    await expect(page.getByRole('heading', { name: 'Your startup pack is still available' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
  })

  test('expired offer on dashboard shows expired message without claim button', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer({
        status: 'EXPIRED',
        expiresAtUtc: '2026-01-01T12:00:00Z',
      }),
      companies: [
        {
          id: 'comp-expired',
          playerId: 'player-1',
          name: 'Expired Offer Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByText('Startup pack expired')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeHidden()
  })

  test('player can claim startup pack directly from the dashboard and see entitlement state', async ({
    page,
  }) => {
    const company = {
      id: 'comp-dashboard-claim',
      playerId: 'player-1',
      name: 'Dashboard Claim Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    }
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer({
        status: 'SHOWN',
        shownAtUtc: '2026-01-01T13:00:00Z',
        expiresAtUtc: new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString(),
      }),
      companies: [company],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // The revisit banner should be visible since the offer is SHOWN
    await expect(page.getByRole('heading', { name: 'Your startup pack is still available' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Claim from dashboard
    await page.getByRole('button', { name: 'Claim startup pack' }).click()

    // Confirmed claimed state appears
    await expect(page.getByText('Startup pack activated')).toBeVisible()
    // Claim button should no longer be visible after successful claim
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeHidden()
  })

  test('player can dismiss startup pack from the dashboard and offer is no longer shown as active', async ({
    page,
  }) => {
    const company = {
      id: 'comp-dashboard-dismiss',
      playerId: 'player-1',
      name: 'Dashboard Dismiss Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [],
    }
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer({
        status: 'ELIGIBLE',
        expiresAtUtc: new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString(),
      }),
      companies: [company],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Offer should be visible (ELIGIBLE transitions to SHOWN on dashboard load)
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Dismiss the offer
    await page.getByRole('button', { name: 'Maybe later' }).click()

    // Confirmation message appears, offer is still accessible (dismissed but not removed)
    await expect(
      page.getByText('The offer has been saved to your dashboard until it expires.'),
    ).toBeVisible()
  })
})

test.describe('Guided first-profit onboarding (post-completion)', () => {
  test('completion screen shows configure-guide panel with all four steps', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Guide Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The configure-guide panel must be present
    await expect(page.getByRole('heading', { name: 'Configure Your First Business' })).toBeVisible()

    // All four guidance steps should be visible
    await expect(page.getByText('Review your cash')).toBeVisible()
    await expect(page.getByText('Set a selling price', { exact: true })).toBeVisible()
    await expect(page.getByText('Start near the base market price of $45.')).toBeVisible()
    await expect(page.getByText('Enable public sales')).toBeVisible()
    await expect(page.getByText('Wait for the next tick')).toBeVisible()
    await expect(page.getByText('Current simulation tick: 42.')).toBeVisible()
  })

  test('completion screen shows Configure My Sales Shop CTA linking to shop building', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Shop Link Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The CTA should be a link pointing to /building/<shopId>
    const shopCta = page.getByRole('link', { name: 'Configure My Sales Shop' })
    await expect(shopCta).toBeVisible()
    const href = await shopCta.getAttribute('href')
    expect(href).toMatch(/\/building\/building-shop-/)
  })

  test('completion screen shows tick countdown', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    // Set a lastTickAtUtc in the past so the countdown shows remaining seconds
    state.gameState.lastTickAtUtc = new Date(Date.now() - 10000).toISOString()
    state.gameState.tickIntervalSeconds = 60

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Tick Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The tick countdown should be visible somewhere in the configure-guide
    const countdownEl = page.locator('.tick-countdown')
    await expect(countdownEl).toBeVisible()
    const text = await countdownEl.textContent()
    expect(text).toMatch(/tick/i)
  })

  test('Go to Dashboard CTA is available as secondary action on completion screen', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Dashboard Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
  })

  test('configure-guide resumes at step 5 after page refresh when onboarding is completed but first-sale milestone is not', async ({
    page,
  }) => {
    // Simulate a player who already completed the lot flow (has onboardingCompletedAtUtc
    // and onboardingShopBuildingId) but has not yet called completeFirstSaleMilestone
    const shopBuildingId = 'building-shop-resume-test'
    const player = makePlayer({
      onboardingCompletedAtUtc: new Date().toISOString(),
      onboardingShopBuildingId: shopBuildingId,
      onboardingFirstSaleCompletedAtUtc: null,
      companies: [
        {
          id: 'comp-resume',
          playerId: 'player-1',
          name: 'Resume Corp',
          cash: 350000,
          foundedAtUtc: new Date().toISOString(),
          buildings: [
            {
              id: shopBuildingId,
              companyId: 'comp-resume',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Resume Corp Shop',
              latitude: 48.145,
              longitude: 17.107,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: new Date().toISOString(),
              units: [
                {
                  id: 'unit-ps-1',
                  buildingId: shopBuildingId,
                  unitType: 'PUBLIC_SALES',
                  gridX: 1,
                  gridY: 0,
                  level: 1,
                  linkRight: false,
                  linkDown: false,
                  linkDiagonal: false,
                  minPrice: 45,
                },
              ],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    // Navigate to /onboarding — should resume at step 5, not redirect to /dashboard
    await page.goto('/onboarding')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Configure Your First Business' })).toBeVisible()

    // Shop CTA should link to the correct building
    const shopCta = page.getByRole('link', { name: 'Configure My Sales Shop' })
    await expect(shopCta).toBeVisible()
    const href = await shopCta.getAttribute('href')
    expect(href).toContain(shopBuildingId)

    // "My Shop is Ready" button should be present
    await expect(page.getByRole('button', { name: /My Shop is Ready/i })).toBeVisible()
  })

  test('milestone "My Shop is Ready" button calls completeFirstSaleMilestone and redirects to dashboard', async ({
    page,
  }) => {
    const shopBuildingId = 'building-shop-milestone-test'
    const player = makePlayer({
      onboardingCompletedAtUtc: new Date().toISOString(),
      onboardingShopBuildingId: shopBuildingId,
      onboardingFirstSaleCompletedAtUtc: null,
      companies: [
        {
          id: 'comp-milestone',
          playerId: 'player-1',
          name: 'Milestone Corp',
          cash: 300000,
          foundedAtUtc: new Date().toISOString(),
          buildings: [
            {
              id: shopBuildingId,
              companyId: 'comp-milestone',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Milestone Corp Shop',
              latitude: 48.145,
              longitude: 17.107,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: new Date().toISOString(),
              units: [
                {
                  id: 'unit-ms-ps-1',
                  buildingId: shopBuildingId,
                  unitType: 'PUBLIC_SALES',
                  gridX: 1,
                  gridY: 0,
                  level: 1,
                  linkRight: false,
                  linkDown: false,
                  linkDiagonal: false,
                  minPrice: 45,
                },
              ],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // Click the milestone button
    await page.getByRole('button', { name: /My Shop is Ready/i }).click()

    // Should redirect to dashboard
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })

  test('delayed-result: milestone shows error when shop has no configured sales unit', async ({
    page,
  }) => {
    const shopBuildingId = 'building-shop-not-configured'
    const player = makePlayer({
      onboardingCompletedAtUtc: new Date().toISOString(),
      onboardingShopBuildingId: shopBuildingId,
      onboardingFirstSaleCompletedAtUtc: null,
      companies: [
        {
          id: 'comp-not-configured',
          playerId: 'player-1',
          name: 'Not Ready Corp',
          cash: 300000,
          foundedAtUtc: new Date().toISOString(),
          buildings: [
            {
              id: shopBuildingId,
              companyId: 'comp-not-configured',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Not Ready Corp Shop',
              latitude: 48.145,
              longitude: 17.107,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: new Date().toISOString(),
              // No units — shop is not configured
              units: [],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Configure Your First Business' })).toBeVisible()

    // Click the milestone button before configuring the shop
    await page.getByRole('button', { name: /My Shop is Ready/i }).click()

    // Should stay on onboarding and show the error
    await expect(page.locator('.milestone-error')).toBeVisible()
    await expect(page.locator('.milestone-error')).toContainText(/configure/i)

    // Should NOT navigate away — URL stays on onboarding (with step=complete query param from resume)
    await expect(page).toHaveURL('/onboarding?step=complete')
  })

  test('already-fully-onboarded player visiting /onboarding is redirected to dashboard immediately', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      onboardingShopBuildingId: null,
      onboardingFirstSaleCompletedAtUtc: '2026-01-01T13:00:00Z',
      companies: [
        {
          id: 'comp-done',
          playerId: 'player-1',
          name: 'Done Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })
})

test.describe('Onboarding skip and re-entry behavior', () => {
  test('player who navigates away mid-flow can re-enter and resume from correct step', async ({
    page,
  }) => {
    // Issue #45 requirement: "Add at least one skip-path test verifying that the player can proceed
    // into the game without broken state."
    // This test simulates a player who starts step 2 (city), then navigates to the home page,
    // then returns to /onboarding — they should resume on the step they left off.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/onboarding')

    // Advance to step 2 (city selection)
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()

    // Navigate away to home
    await page.goto('/')
    await expect(page).toHaveURL('/')

    // Re-enter onboarding — should resume on step 2 (the page stores step in query params / sessionStorage)
    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
  })

  test('player who navigates away from the dashboard link after completing can still see dashboard', async ({
    page,
  }) => {
    // Player with completed onboarding, has a company and a shop building.
    // Navigating to /onboarding should redirect to /dashboard (not crash or loop).
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      onboardingShopBuildingId: 'building-shop-skip-test',
      onboardingFirstSaleCompletedAtUtc: null,
      companies: [
        {
          id: 'comp-skip',
          playerId: 'player-1',
          name: 'Skip Corp',
          cash: 400000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-shop-skip-test',
              companyId: 'comp-skip',
              name: 'Skip Shop',
              type: 'SALES_SHOP',
              cityId: 'city-1',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 10,
              isForSale: false,
              units: [],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    // Visiting /dashboard should work normally without erroring
    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
    await expect(page.locator('.company-card').first()).toBeVisible()
  })
})
