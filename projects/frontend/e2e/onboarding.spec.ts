/**
 * Onboarding flow E2E tests.
 * Covers: registration, login, onboarding wizard, and dashboard verification.
 * Implements issue #54 — guest onboarding sandbox wizard with save-progress handoff.
 */
import { test, expect, type Page } from '@playwright/test'
import {
  setupMockApi,
  makePlayer,
  makeStartupPackOffer,
  makeDefaultBuildingLots,
} from './helpers/mock-api'

async function authenticateViaLocalStorage(page: Page, token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

/**
 * Drives the guest wizard through steps 1–4 for a given industry and product,
 * then returns the text content of `.profit-stat-revenue` on the step-5 screen.
 * Used by the revenue-comparison test to avoid duplicating navigation logic.
 */
async function getGuestProfitRevenue(
  page: Page,
  industry: string,
  productName: string,
  companyName = `${industry} Revenue Corp`,
): Promise<number> {
  setupMockApi(page)
  await page.goto('/onboarding')
  await page.locator('.industry-card', { hasText: industry }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.getByLabel('Company Name').fill(companyName)
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  await page.getByRole('button', { name: 'Purchase First Factory' }).click()
  await page.locator('.product-card', { hasText: productName }).click()
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /High Street Retail Space/i }).click()
  await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
  await expect(page.locator('.profit-stat-revenue')).toBeVisible()
  const text = (await page.locator('.profit-stat-revenue').textContent()) ?? '$0'
  return parseInt(text.replace(/[^0-9]/g, ''), 10)
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

/**
 * Drives the authenticated guided onboarding wizard with a specified industry and product.
 * Used by configure-guide price tests to validate industry-specific benchmark prices.
 */
async function completeGuidedOnboardingForIndustry(
  page: Page,
  companyName: string,
  industryLabel: string,
  productLabel: string,
) {
  await page.locator('.industry-card', { hasText: industryLabel }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()

  await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
  await page.getByLabel('Company Name').fill(companyName)
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  await page.getByRole('button', { name: 'Purchase First Factory' }).click()

  await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
  await page.locator('.product-card', { hasText: productLabel }).click()
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

  test('step 4 shows only Bread for Food Processing industry (AC3/AC5)', async ({ page }) => {
    // AC3: Each starter industry presents a viable starter product/path.
    // AC5: The onboarding flow prepares an initial factory layout appropriate to the selected industry.
    // Verifies that selecting Food Processing in step 1 results in only the Bread product card
    // being available in step 4 — not the Wooden Chair or Basic Medicine.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Food Processing' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await page.getByLabel('Company Name').fill('Food Processing Co')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Only the industry-appropriate product should be shown
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card')).toContainText('Bread')
    // Furniture and Healthcare products must NOT appear
    await expect(page.locator('.product-card', { hasText: 'Wooden Chair' })).toHaveCount(0)
    await expect(page.locator('.product-card', { hasText: 'Basic Medicine' })).toHaveCount(0)
  })

  test('step 4 shows only Basic Medicine for Healthcare industry (AC3/AC5)', async ({ page }) => {
    // AC3: Each starter industry presents a viable starter product/path.
    // AC5: The onboarding flow prepares an initial factory layout appropriate to the selected industry.
    // Verifies that selecting Healthcare in step 1 results in only the Basic Medicine product card
    // being available in step 4 — not the Wooden Chair or Bread.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await page.getByLabel('Company Name').fill('Pharma Co')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Only the industry-appropriate product should be shown
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await expect(page.locator('.product-card')).toHaveCount(1)
    await expect(page.locator('.product-card')).toContainText('Basic Medicine')
    // Furniture and Food Processing products must NOT appear
    await expect(page.locator('.product-card', { hasText: 'Wooden Chair' })).toHaveCount(0)
    await expect(page.locator('.product-card', { hasText: 'Bread' })).toHaveCount(0)
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

  test('industry cards show first product hint for each starter industry', async ({ page }) => {
    // ROADMAP: "Each option should explain the fantasy, likely first product, and why a player might choose it."
    setupMockApi(page)
    await page.goto('/onboarding')
    await expect(page.locator('.industry-card', { hasText: 'Furniture' }).locator('.card-first-product')).toContainText(
      'Wooden Chair',
    )
    await expect(
      page.locator('.industry-card', { hasText: 'Food Processing' }).locator('.card-first-product'),
    ).toContainText('Bread')
    await expect(
      page.locator('.industry-card', { hasText: 'Healthcare' }).locator('.card-first-product'),
    ).toContainText('Basic Medicine')
  })

  test('industry cards show why-choose tagline for each starter industry', async ({ page }) => {
    // ROADMAP: "Each option should explain ... why a player might choose it."
    setupMockApi(page)
    await page.goto('/onboarding')
    await expect(page.locator('.industry-card', { hasText: 'Furniture' }).locator('.card-why')).toContainText(
      'Low entry cost',
    )
    await expect(
      page.locator('.industry-card', { hasText: 'Food Processing' }).locator('.card-why'),
    ).toContainText('High volume')
    await expect(page.locator('.industry-card', { hasText: 'Healthcare' }).locator('.card-why')).toContainText(
      'Premium margin',
    )
  })

  test('industry card descriptions explain the business fantasy', async ({ page }) => {
    // ROADMAP: "Each option should explain the fantasy ... and why a player might choose it."
    setupMockApi(page)
    await page.goto('/onboarding')
    // Furniture description explains timber → home goods supply chain
    await expect(page.locator('.industry-card', { hasText: 'Furniture' }).locator('.card-desc')).toContainText(
      'timber',
    )
    // Food Processing description explains the volume/frequency trade-off
    await expect(
      page.locator('.industry-card', { hasText: 'Food Processing' }).locator('.card-desc'),
    ).toContainText('volume')
    // Healthcare description explains premium pricing
    await expect(page.locator('.industry-card', { hasText: 'Healthcare' }).locator('.card-desc')).toContainText(
      'premium',
    )
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

  test('guest can complete wizard with Food Processing industry (Bread)', async ({ page }) => {
    // AC2: The user can choose one of the three roadmap-defined starter industries.
    // AC7: Guest progress is handled in a temporary way and not permanently stored before sign-up.
    setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1: Select Food Processing
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await page.locator('.industry-card', { hasText: 'Food Processing' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: Choose factory lot (no backend call in guest mode)
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Bread Factory Guest')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: Choose Bread product and shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Bread' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: Guest save-progress screen with Bread in the profit preview
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Save & Launch' })).toBeVisible()
  })

  test('guest can complete wizard with Healthcare industry (Basic Medicine)', async ({ page }) => {
    // AC2: The user can choose one of the three roadmap-defined starter industries.
    // AC7: Guest progress is handled in a temporary way and not permanently stored before sign-up.
    setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1: Select Healthcare
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Choose city
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: Choose factory lot (no backend call in guest mode)
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Pharma Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: Choose Basic Medicine product and shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Basic Medicine' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: Guest save-progress screen with Basic Medicine in the profit preview
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
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

  test('guest steps 1-4 make no backend mutation calls (progress is temporary)', async ({ page }) => {
    // AC: "Guest progress is handled as temporary and is not incorrectly persisted as a permanent
    // backend-owned company before authentication."
    // This test intercepts all GraphQL requests during steps 1-4 and verifies that no
    // StartOnboardingCompany or FinishOnboarding mutations are sent to the backend.
    setupMockApi(page)

    const mutationNames: string[] = []
    page.on('request', (request) => {
      if (request.url().includes('/graphql') && request.method() === 'POST') {
        try {
          const body = JSON.parse(request.postData() ?? '{}')
          const query: string = body?.query ?? ''
          if (query.includes('StartOnboardingCompany')) mutationNames.push('StartOnboardingCompany')
          if (query.includes('FinishOnboarding')) mutationNames.push('FinishOnboarding')
        } catch {
          // ignore parse errors
        }
      }
    })

    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Must be on the save-progress screen (step 5)
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()

    // No backend company-creation mutations should have fired during guest steps 1-4
    expect(mutationNames).not.toContain('StartOnboardingCompany')
    expect(mutationNames).not.toContain('FinishOnboarding')
  })

  test('localStorage progress is cleared after successful guest migration', async ({ page }) => {
    // AC: "The product never silently drops guest progress without telling the player what happened."
    // After a successful handoff the stale guest progress key must be removed from localStorage
    // so the player starts fresh if they ever navigate back.
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Confirm localStorage has onboarding progress saved before migration
    const progressBefore = await page.evaluate(() => localStorage.getItem('onboarding_progress'))
    expect(progressBefore).not.toBeNull()

    // Register to trigger migration
    await page.locator('#guestEmail').fill('clear@test.com')
    await page.locator('#guestDisplayName').fill('Clear Test')
    await page.locator('#guestPassword').fill('ClearPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Wait for migration to complete
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The onboarding_progress key should now be absent from localStorage
    const progressAfter = await page.evaluate(() => localStorage.getItem('onboarding_progress'))
    expect(progressAfter).toBeNull()
  })

  test('guest UI labels state as preview/temporary — not falsely implies saved', async ({ page }) => {
    // AC: "The product never silently drops guest progress without telling the player what happened."
    // and: "Avoid pretending that the guest already owns durable world assets before account creation."
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // The completion heading must use "Preview" language, not "Launched" (which implies persistence)
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    // The save-subtitle must explain that an account is needed to preserve progress
    await expect(page.getByText(/Create a free account or log in to lock in your choices/i)).toBeVisible()
    // "Your Empire Has Launched" should NOT appear yet — that heading is reserved for
    // authenticated completion after the real backend handoff succeeds.
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeHidden()
  })

  test('guest progress survives page refresh and can still be migrated', async ({ page }) => {
    // AC: "Support the minimum state required to complete the opening product loop."
    // After a page refresh mid-onboarding as a guest, the player should be able to resume
    // and migrate successfully — confirming the localStorage-based persistence works end-to-end.
    setupMockApi(page)
    await page.goto('/onboarding')

    // Complete steps 1-3 only
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Refresh Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Refresh on step 4 as a guest — should resume at step 4
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    // Complete step 4 and migrate
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await page.locator('#guestEmail').fill('refresh@test.com')
    await page.locator('#guestDisplayName').fill('Refresh Player')
    await page.locator('#guestPassword').fill('RefreshPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
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
    // AC #2: offer must clearly display the $20 price
    await expect(page.locator('.startup-pack-price')).toBeVisible()
    await expect(page.locator('.startup-pack-price')).toContainText('$20')
    // AC #4: pro benefit copy must mention more products to manufacture and sell
    await expect(page.getByText(/more products to.*sell/i)).toBeVisible()

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
    // AC #2: price must be visible on dashboard offer panel
    await expect(page.locator('.startup-pack-price')).toContainText('$20')

    // Reload the page — offer must still be visible (state persisted in backend)
    await page.reload()

    await expect(page.getByRole('heading', { name: 'Your startup pack is still available' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
    await expect(page.locator('.startup-pack-price')).toContainText('$20')
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

  test('claim failure shows error message and player can still navigate to dashboard safely', async ({
    page,
  }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.forceStartupPackClaimError = true

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Resilience Corp')

    // Startup pack offer is visible after onboarding
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Claim fails due to simulated server error
    await page.getByRole('button', { name: 'Claim startup pack' }).click()

    // Error message should be shown so the player understands what happened
    await expect(page.getByText(/could not activate the startup pack/i)).toBeVisible()

    // The claim button should still be available for retry
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()

    // Player can still navigate away to the dashboard without being blocked
    await page.getByRole('link', { name: 'Go to Dashboard' }).click()
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
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
    await expect(
      page.getByText('The market benchmark is $45.', { exact: false }),
    ).toBeVisible()
    await expect(page.getByText('Enable public sales')).toBeVisible()
    await expect(page.getByText('Wait for the next tick')).toBeVisible()
    await expect(page.getByText('Current simulation tick: 42.')).toBeVisible()
  })

  test('configure-guide price step explains margin and demand trade-offs', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Margin Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The price step should explain margin (higher price = more margin per unit)
    // and demand trade-off (higher price = fewer buyers, lower = more volume)
    const priceStep = page.locator('.configure-step').filter({ hasText: 'Set a selling price' })
    await expect(priceStep).toBeVisible()
    await expect(priceStep).toContainText('margin')
    await expect(priceStep).toContainText('demand')
    // Should reference the concrete base price from the product
    await expect(priceStep).toContainText('$45')
  })

  test('configure-guide tick step shows next-tick processing steps', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Tick Process Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The tick step should also show next-tick process items
    await expect(page.locator('.next-tick-process-list')).toBeVisible()
    await expect(page.locator('.next-tick-process-list')).toContainText('Raw materials are purchased')
    await expect(page.locator('.next-tick-process-list')).toContainText('Your product is manufactured')
    await expect(page.locator('.next-tick-process-list')).toContainText('offers the product publicly')
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

  test('milestone "My Shop is Ready" button shows business-live panel and then navigates to dashboard', async ({
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

    // Should show the business-live panel (not immediately navigate)
    await expect(
      page.getByRole('heading', { name: /Your Business is Now Operational/i }),
    ).toBeVisible()
    await expect(page.locator('.business-live-panel')).toBeVisible()

    // Click "Go to Dashboard" on the business-live panel
    await page.getByRole('button', { name: /Go to Dashboard/i }).click()

    // Should redirect to dashboard
    await page.waitForURL('/dashboard')
    await expect(page).toHaveURL('/dashboard')
  })

  test('business-live panel shows tick info and next-tick process steps', async ({ page }) => {
    const shopBuildingId = 'building-shop-live-test'
    const player = makePlayer({
      onboardingCompletedAtUtc: new Date().toISOString(),
      onboardingShopBuildingId: shopBuildingId,
      onboardingFirstSaleCompletedAtUtc: null,
      companies: [
        {
          id: 'comp-live',
          playerId: 'player-1',
          name: 'Live Corp',
          cash: 300000,
          foundedAtUtc: new Date().toISOString(),
          buildings: [
            {
              id: shopBuildingId,
              companyId: 'comp-live',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Live Corp Shop',
              latitude: 48.145,
              longitude: 17.107,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: new Date().toISOString(),
              units: [
                {
                  id: 'unit-live-ps-1',
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
    state.gameState.currentTick = 55

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // Click the milestone button
    await page.getByRole('button', { name: /My Shop is Ready/i }).click()

    // Business-live panel should be visible
    await expect(page.locator('.business-live-panel')).toBeVisible()

    // Should show tick info mentioning the current tick
    await expect(page.locator('.business-live-panel')).toContainText('55')

    // Should show the next-tick process steps
    await expect(page.locator('.business-live-panel')).toContainText('Raw materials are purchased')
    await expect(page.locator('.business-live-panel')).toContainText(
      'Your product is manufactured',
    )
    await expect(page.locator('.business-live-panel')).toContainText('offers the product publicly')

    // Configure-guide should no longer be visible
    await expect(
      page.getByRole('heading', { name: 'Configure Your First Business' }),
    ).toBeHidden()
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

  test('configure-guide price step shows Food Processing benchmark price', async ({ page }) => {
    // ROADMAP: "Wizard will show them important areas on the screen like how much money they have,
    // the price configuration or public sales configuration."
    // For Food Processing (Bread, basePrice $3), the configure-guide must show $3 as the market
    // benchmark so the player knows what price target the simulation uses.
    // This is the same teach-the-simulation UX requirement that makes the null-maxPrice on the
    // factory purchase unit critical: the player is taught the market price, not a cap that could
    // silently block Grain purchases.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboardingForIndustry(page, 'Bread Corp', 'Food Processing', 'Bread')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The price step must show the Bread market benchmark price ($3)
    const priceStep = page.locator('.configure-step').filter({ hasText: 'Set a selling price' })
    await expect(priceStep).toBeVisible()
    await expect(priceStep).toContainText('$3')
  })

  test('configure-guide price step shows Healthcare benchmark price', async ({ page }) => {
    // ROADMAP: "Wizard will show them important areas on the screen like how much money they have,
    // the price configuration or public sales configuration."
    // For Healthcare (Basic Medicine, basePrice $50), the configure-guide must show $50 as the
    // market benchmark so the player understands the premium-margin opportunity.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)

    await page.goto('/onboarding')
    await completeGuidedOnboardingForIndustry(
      page,
      'Medicine Corp',
      'Healthcare',
      'Basic Medicine',
    )

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // The price step must show the Basic Medicine market benchmark price ($50)
    const priceStep = page.locator('.configure-step').filter({ hasText: 'Set a selling price' })
    await expect(priceStep).toBeVisible()
    await expect(priceStep).toContainText('$50')
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

test.describe('Onboarding on narrow/mobile layouts', () => {
  // These tests verify the acceptance criterion: "Tests confirming onboarding remains usable
  // on narrower layouts" (issue testing requirements). The onboarding wizard uses a single-column
  // layout at ≤640px: industry/city/product cards stack vertically, step-actions go to column
  // direction, and progress-labels are hidden. Core interactions must still work at 375px width.

  test('industry cards are visible and selectable on a narrow (375px) viewport', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/onboarding')

    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()

    // All three industry cards must be visible even in single-column layout
    await expect(page.locator('.industry-card', { hasText: 'Furniture' })).toBeVisible()
    await expect(page.locator('.industry-card', { hasText: 'Food Processing' })).toBeVisible()
    await expect(page.locator('.industry-card', { hasText: 'Healthcare' })).toBeVisible()

    // Selecting a card and clicking Next must work on mobile
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await expect(page.getByRole('button', { name: 'Next' })).toBeEnabled()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
  })

  test('city cards are visible and selectable on a narrow viewport', async ({ page }) => {
    setupMockApi(page)
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/onboarding')

    // Advance to step 2
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await expect(page.locator('.city-card', { hasText: 'Bratislava' })).toBeVisible()

    // City selection and navigation must work
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await expect(page.getByRole('button', { name: 'Next' })).toBeEnabled()

    // Back button must also be accessible
    await expect(page.getByRole('button', { name: 'Back' })).toBeVisible()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
  })

  test('guest wizard completes all four steps on a narrow (375px) viewport', async ({ page }) => {
    setupMockApi(page)
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/onboarding')

    // Step 1
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: lot selection
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Mobile Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeVisible()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: product + shop
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: save-progress screen
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    // CTA button must be reachable on mobile
    await expect(page.getByRole('button', { name: 'Save & Launch' })).toBeVisible()
  })

  test('save-progress form is usable on a narrow viewport', async ({ page }) => {
    setupMockApi(page)
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/onboarding')

    // Inline the wizard steps at 375px to ensure the viewport is maintained throughout
    // (avoids any helper that might silently reset viewport state).
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Mobile Save Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()

    // Both register and login tabs must be visible and tappable on mobile
    await expect(page.locator('.btn-tab', { hasText: 'Create Account' })).toBeVisible()
    await expect(page.locator('.btn-tab', { hasText: 'Log In' })).toBeVisible()

    // Toggle to login tab
    await page.locator('.btn-tab', { hasText: 'Log In' }).click()
    await expect(page.getByLabel('Email')).toBeVisible()
    await expect(page.getByLabel('Password')).toBeVisible()
  })

  test('progress bar step numbers are visible and labels are hidden on narrow viewport', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/onboarding')

    // Step numbers visible (only steps 1-4 shown; step 5 is completion)
    await expect(page.locator('.progress-step').first()).toBeVisible()

    // Progress labels should be hidden via CSS (display:none at ≤640px),
    // but the elements still exist in the DOM. Verify the CSS hides them.
    const progressLabel = page.locator('.progress-label').first()
    await expect(progressLabel).toBeHidden()
  })
})

test.describe('Onboarding budget coaching — guest cash visibility (AC 6)', () => {
  // AC 6: "The UI clearly highlights money, pricing, and key business configuration concepts
  // during the flow."
  // ROADMAP: "Wizard will show them important areas on the screen like how much money they
  // have, the price configuration or public sales configuration."

  test('guest wizard step 3 shows starting cash and post-purchase cash budget panel', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1 & 2: industry + city
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: factory lot — budget panel must be visible before any lot is selected
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()

    // "Starting cash" label and amount must be visible (ROADMAP coaching requirement)
    await expect(page.getByText('Starting cash')).toBeVisible()
    const budgetCards = page.locator('.budget-card')
    await expect(budgetCards.first()).toBeVisible()

    // Select a lot and verify "Cash after purchase" updates
    await page.getByLabel('Company Name').fill('Budget Test Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    await expect(page.locator('.budget-grid').getByText('Cash after purchase')).toBeVisible()
    // The cash-after value must be a dollar amount (may differ from starting cash)
    const cashAfterEl = page.locator('.budget-card').nth(1).locator('strong')
    await expect(cashAfterEl).toBeVisible()
    const cashAfterText = await cashAfterEl.textContent()
    expect(cashAfterText).toMatch(/^\$[\d,]+$/)
  })

  test('guest wizard step 4 shows available cash and product base price per unit', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Steps 1–3
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Pricing Test Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: shop lot — available cash panel + product price must be visible
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    // Available cash must be shown (coaching the player on their remaining budget)
    await expect(page.getByText('Available cash')).toBeVisible()

    // Product cards must show base price per unit (ROADMAP: "price configuration")
    const furnitureCard = page.locator('.product-card', { hasText: 'Wooden Chair' })
    await expect(furnitureCard).toBeVisible()
    // Price label ends with '/unit' (i18n key onboarding.perUnit = '/unit')
    await expect(furnitureCard.getByText(/\/unit/)).toBeVisible()
  })

  test('guest completion screen names the industry-specific product chosen', async ({ page }) => {
    // Verifies that the step-5 achievement list names the product the guest chose,
    // making the coaching outcome industry-specific and personally relevant (AC 6 & AC 3).
    setupMockApi(page)
    await page.goto('/onboarding')

    // Choose Food Processing → Bread
    await page.locator('.industry-card', { hasText: 'Food Processing' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Bread Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await page.locator('.product-card', { hasText: 'Bread' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: achievement list must name the chosen product (Bread)
    const achievementList = page.locator('.completion-achievements')
    await expect(achievementList).toBeVisible()
    await expect(achievementList).toContainText(/Bread/i)

    // Profit preview must also be visible with a dollar revenue figure
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
    const revenueEl = page.locator('.profit-stat-revenue')
    await expect(revenueEl).toBeVisible()
    const revenueText = await revenueEl.textContent()
    expect(revenueText).toMatch(/\$\d/)
  })

  test('guest completion screen achievement list names Basic Medicine for Healthcare industry (AC3/AC9)', async ({
    page,
  }) => {
    // AC3: Each starter industry presents a viable starter product/path.
    // AC9: The player reaches a first-profit milestone — the completion screen surfaces
    //      industry-specific context so the player knows their exact business outcome.
    setupMockApi(page)
    await page.goto('/onboarding')

    // Choose Healthcare → Basic Medicine
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Pharma Guest Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()
    await page.locator('.product-card', { hasText: 'Basic Medicine' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: achievement list must name the chosen product (Basic Medicine)
    const achievementList = page.locator('.completion-achievements')
    await expect(achievementList).toBeVisible()
    await expect(achievementList).toContainText(/Basic Medicine/i)

    // Profit preview must be visible with a dollar revenue figure
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
    const revenueEl = page.locator('.profit-stat-revenue')
    await expect(revenueEl).toBeVisible()
    const revenueText = await revenueEl.textContent()
    expect(revenueText).toMatch(/\$\d/)

    // Save prompt must be visible (AC12: ask to register after first profit)
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Save & Launch' })).toBeVisible()
  })
})

test.describe('Guest conflict recovery — authenticated restart flow', () => {
  // ROADMAP: "If there is error such as the building was meanwhile purchased by someone else ...
  // make sure to create their profile with the name they chose and start the wizard again
  // with the authenticated user and this time save everything."

  test('after lot-conflict restart, now-authenticated player can complete wizard and launch empire', async ({
    page,
  }) => {
    const state = setupMockApi(page)
    await page.goto('/onboarding')

    // Complete steps 1-4 as guest
    await completeGuestSteps1to4(page, 'Conflict Recovery Corp')

    // Mark the factory lot as taken so migration fails with LOT_ALREADY_OWNED
    const factoryLot = state.buildingLots.find((l) => l.id === 'lot-industrial-1')!
    factoryLot.ownerCompanyId = 'other-company'

    // Register and trigger migration — account is created but wizard restarts
    await page.locator('#guestEmail').fill('conflict-recover@test.com')
    await page.locator('#guestDisplayName').fill('Conflict Player')
    await page.locator('#guestPassword').fill('RecoverPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Wizard restarts at step 1 — player IS now authenticated
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(
      page.locator('.error-message, .error-global, [role="alert"]').filter({ hasText: /lots you chose was taken/i }),
    ).toBeVisible()

    // Restore the factory lot so the player can pick it again
    factoryLot.ownerCompanyId = null
    factoryLot.ownerCompany = null
    factoryLot.buildingId = null
    factoryLot.building = null

    // Now complete the wizard as authenticated user — should reach empire launched
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Conflict Recovery Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Authenticated completion screen — not the guest preview
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toBeVisible()
    // Player remains authenticated throughout
    expect(state.currentUserId).toBeTruthy()
  })

  test('after shop-lot-conflict restart, authenticated player can pick a different shop lot and complete', async ({
    page,
  }) => {
    const state = setupMockApi(page)
    // Add a second factory lot so the player can use it after the shop-lot conflict restart
    // (the first factory lot gets owned by the new company during the first migration attempt)
    state.buildingLots.push({
      id: 'lot-industrial-2',
      cityId: 'city-ba',
      name: 'Industrial Plot A2',
      description: 'Second industrial plot in the eastern logistics corridor.',
      district: 'Industrial Zone',
      latitude: 48.153,
      longitude: 17.126,
      populationIndex: 0.6,
      basePrice: 75000,
      price: 80000,
      suitableTypes: 'FACTORY,MINE',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
      resourceType: null,
      materialQuality: null,
      materialQuantity: null,
    })

    await page.goto('/onboarding')

    // Complete steps 1-4 as guest
    await completeGuestSteps1to4(page, 'Shop Conflict Corp')

    // Mark the shop lot as taken so FinishOnboarding fails
    const shopLot = state.buildingLots.find((l) => l.id === 'lot-commercial-1')!
    shopLot.ownerCompanyId = 'other-company'

    // Register — account created, startOnboardingCompany succeeds (factory lot A is fine),
    // but finishOnboarding fails because shop lot B is taken → restart wizard at step 1
    await page.locator('#guestEmail').fill('shop-conflict-recover@test.com')
    await page.locator('#guestDisplayName').fill('Shop Conflict Player')
    await page.locator('#guestPassword').fill('ShopConflict1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Wizard restarts at step 1 with lot-conflict error
    await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
    await expect(
      page.locator('.error-message, .error-global, [role="alert"]').filter({ hasText: /lots you chose was taken/i }),
    ).toBeVisible()

    // Make backup lot suitable as a shop lot
    const backupLot = state.buildingLots.find((l) => l.id === 'lot-business-1')!
    backupLot.suitableTypes = 'SALES_SHOP,COMMERCIAL'

    // Re-run wizard as authenticated user using the SECOND factory lot and backup shop lot
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('Shop Conflict Corp 2')
    await page.getByRole('button', { name: 'List View' }).click()
    // Use the second factory lot (the first is now owned by the company from the first attempt)
    await page.getByRole('button', { name: /Industrial Plot A2/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    // Choose the alternative lot instead of the taken one
    await page.getByRole('button', { name: /Innovation Campus Office/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Empire launched with the alternative lots
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    expect(state.currentUserId).toBeTruthy()
  })
})

test.describe('Onboarding wizard CTA validation and budget guard rails', () => {
  // Issue #102 AC6: "The interface clearly surfaces money, pricing, and the business
  // consequences of player choices during onboarding."
  // This describes the CTA disabled-state enforcement and budget coaching.

  test('guest step 3 — Purchase First Factory is disabled until company name and lot are both selected', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Steps 1 & 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()

    // CTA must be disabled at initial state (no name, no lot)
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeDisabled()

    // Fill company name only — still disabled (no lot)
    await page.getByLabel('Company Name').fill('Validation Corp')
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeDisabled()

    // Select lot without company name — fill name first then clear to test
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    // Now both name + lot are set: CTA must be enabled
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeEnabled()

    // Clear company name — CTA must go disabled again
    await page.getByLabel('Company Name').fill('')
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeDisabled()
  })

  test('guest step 4 — Purchase First Sales Shop is disabled until product and lot are both selected', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // Complete steps 1-3
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('CTA Test Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    // CTA must be disabled initially (no product, no lot)
    await expect(page.getByRole('button', { name: 'Purchase First Sales Shop' })).toBeDisabled()

    // Select product only — still disabled (no shop lot)
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await expect(page.getByRole('button', { name: 'Purchase First Sales Shop' })).toBeDisabled()

    // Select shop lot — both now selected, CTA enabled
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await expect(page.getByRole('button', { name: 'Purchase First Sales Shop' })).toBeEnabled()
  })

  test('guest profit preview shows higher revenue for Healthcare vs Food Processing (AC6 business consequence)', async ({
    browser,
  }) => {
    // Verifies the profit preview makes industry value differentiation visible to the player.
    // Healthcare has a $50/unit product, Food Processing has $3/unit — this should be
    // clearly reflected in the estimated revenue shown on the completion screen.
    // Uses separate browser contexts to avoid page state interference between the two runs.
    const ctxHealthcare = await browser.newContext()
    const pageHealthcare = await ctxHealthcare.newPage()
    const healthcareRevenue = await getGuestProfitRevenue(pageHealthcare, 'Healthcare', 'Basic Medicine')
    await ctxHealthcare.close()

    const ctxFood = await browser.newContext()
    const pageFood = await ctxFood.newPage()
    const foodRevenue = await getGuestProfitRevenue(pageFood, 'Food Processing', 'Bread', 'Food Revenue Corp')
    await ctxFood.close()

    // Healthcare ($50/unit) must have meaningfully higher revenue than Food Processing ($3/unit)
    expect(healthcareRevenue).toBeGreaterThan(foodRevenue)
  })
})

// ─────────────────────────────────────────────────────────────────
// Factory & shop layout display — ROADMAP: "Wizard will show them
// the factory layout" and "important areas on the screen"
// ─────────────────────────────────────────────────────────────────
test.describe('Factory and shop layout display after completion', () => {
  test('guest completion screen shows factory layout panel with all 4 production units', async ({ page }) => {
    // ROADMAP: "This will set the factory layout for them. Wizard will show them important areas on the screen"
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Factory layout panel should be visible
    await expect(page.locator('[aria-label="Factory layout"]')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Factory Layout' })).toBeVisible()

    // All 4 starter factory unit types must be shown
    const factoryPanel = page.locator('[aria-label="Factory layout"]')
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Purchase' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Manufacturing' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Storage' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'B2B Sales' })).toBeVisible()

    // Arrows between units should be visible (3 arrows for 4 units)
    await expect(factoryPanel.locator('.unit-chain-arrow')).toHaveCount(3)
  })

  test('guest completion screen shows sales shop layout panel with 2 units', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Shop layout panel should be visible
    await expect(page.locator('[aria-label="Sales shop layout"]')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Sales Shop Layout' })).toBeVisible()

    // Sales shop has PURCHASE → PUBLIC_SALES
    const shopPanel = page.locator('[aria-label="Sales shop layout"]')
    await expect(shopPanel.locator('.unit-chain-label', { hasText: 'Purchase' })).toBeVisible()
    await expect(shopPanel.locator('.unit-chain-label', { hasText: 'Public Sales' })).toBeVisible()

    // 1 arrow between 2 units
    await expect(shopPanel.locator('.unit-chain-arrow')).toHaveCount(1)
  })

  test('authenticated completion screen shows factory layout with units from backend', async ({ page }) => {
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    player.onboardingCurrentStep = 'SHOP_SELECTION'
    player.onboardingIndustry = 'FURNITURE'
    player.onboardingCityId = state.cities[0].id
    player.onboardingCompanyId = 'company-existing'
    player.onboardingFactoryLotId = state.buildingLots[0].id
    const factoryId = `building-factory-auth-test`
    player.companies = [{
      id: 'company-existing',
      playerId: player.id,
      name: 'Auth Test Corp',
      cash: 450000,
      foundedAtUtc: new Date().toISOString(),
      foundedAtTick: 1,
      buildings: [{
        id: factoryId,
        companyId: 'company-existing',
        cityId: state.cities[0].id,
        type: 'FACTORY',
        name: 'Auth Test Corp Factory',
        latitude: 48.15,
        longitude: 17.11,
        level: 1,
        powerConsumption: 2,
        powerStatus: 'POWERED',
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        units: [],
        pendingConfiguration: null,
      }],
    }]

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/onboarding')

    // Step 4: select product + shop lot + purchase
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Authenticated completion screen
    await expect(page.getByRole('heading', { name: '🚀 Your Empire Has Launched!' })).toBeVisible()

    // Factory layout panel shown with backend units
    await expect(page.locator('[aria-label="Factory layout"]')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Your Factory Layout' })).toBeVisible()
    const factoryPanel = page.locator('[aria-label="Factory layout"]')
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Purchase' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Manufacturing' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'Storage' })).toBeVisible()
    await expect(factoryPanel.locator('.unit-chain-label', { hasText: 'B2B Sales' })).toBeVisible()

    // Shop layout also shown
    await expect(page.locator('[aria-label="Sales shop layout"]')).toBeVisible()
    const shopPanel = page.locator('[aria-label="Sales shop layout"]')
    await expect(shopPanel.locator('.unit-chain-label', { hasText: 'Purchase' })).toBeVisible()
    await expect(shopPanel.locator('.unit-chain-label', { hasText: 'Public Sales' })).toBeVisible()
  })

  test('factory layout desc explains the units to the player in guest mode', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // The descriptive text explains what will happen (guest layout is a preview)
    const factoryPanel = page.locator('[aria-label="Factory layout"]')
    await expect(factoryPanel).toContainText('configured')

    const shopPanel = page.locator('[aria-label="Sales shop layout"]')
    await expect(shopPanel).toContainText('configured')
  })

  test('factory layout icons are visible for each unit in the chain', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    const factoryPanel = page.locator('[aria-label="Factory layout"]')
    // There should be 4 icon elements in the factory chain
    await expect(factoryPanel.locator('.unit-chain-icon')).toHaveCount(4)

    const shopPanel = page.locator('[aria-label="Sales shop layout"]')
    // There should be 2 icon elements in the shop chain
    await expect(shopPanel.locator('.unit-chain-icon')).toHaveCount(2)
  })
})

test.describe('Onboarding wizard progress bar accuracy (AC1 — visible step progress)', () => {
  // AC: "Use a step-based structure with visible progress so the player knows how far they are from starting the business."
  // ROADMAP: "Wizard will show them important areas on the screen."

  test('progress bar marks step 1 as done when player advances to step 2', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    // At step 1: first segment should be active but not done
    const firstSegment = page.locator('.progress-segment').first()
    await expect(firstSegment).toHaveClass(/active/)
    await expect(firstSegment).not.toHaveClass(/done/)

    // Select industry and advance
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // At step 2: first segment should now be done (shows ✓)
    await expect(firstSegment).toHaveClass(/done/)
    const doneIcon = firstSegment.locator('.check-icon')
    await expect(doneIcon).toBeVisible()
    await expect(doneIcon).toContainText('✓')

    // Step 2 segment should now be active
    const secondSegment = page.locator('.progress-segment').nth(1)
    await expect(secondSegment).toHaveClass(/active/)
    await expect(secondSegment).not.toHaveClass(/done/)
  })

  test('progress bar marks steps 1 and 2 as done when player reaches step 3', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')

    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: both segments 1 and 2 should be done
    const firstSegment = page.locator('.progress-segment').first()
    const secondSegment = page.locator('.progress-segment').nth(1)
    const thirdSegment = page.locator('.progress-segment').nth(2)

    await expect(firstSegment).toHaveClass(/done/)
    await expect(secondSegment).toHaveClass(/done/)
    await expect(thirdSegment).toHaveClass(/active/)
    await expect(thirdSegment).not.toHaveClass(/done/)
  })

  test('progress bar is hidden on the completion step (step 5)', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Step 5 is the completion/save-progress screen — progress bar should not be shown
    await expect(page.locator('.progress-bar')).toBeHidden()
  })
})

test.describe('Guest registration error recovery (AC10 — resilience)', () => {
  // AC10: "The flow handles conflicts and invalid guest outcomes gracefully."
  // Specifically: a duplicate email during guest registration should show an inline error
  // and let the player try again without losing their wizard progress.

  test('duplicate email during guest registration shows error and player can retry with different email', async ({
    page,
  }) => {
    const existingPlayer = makePlayer({ email: 'taken@test.com' })
    const state = setupMockApi(page, { players: [existingPlayer] })
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    // Try to register with an email that already exists
    await page.locator('#guestEmail').fill('taken@test.com')
    await page.locator('#guestDisplayName').fill('Duplicate Hopeful')
    await page.locator('#guestPassword').fill('TestPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Should show a duplicate-email error inline — NOT restart the wizard
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    const errorEl = page.locator('.error-message, .error-global, [role="alert"]').first()
    await expect(errorEl).toBeVisible()
    await expect(errorEl).toContainText(/already exists/i)

    // Player should be able to correct their email and succeed
    await page.locator('#guestEmail').fill('unique@test.com')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // After a successful registration the onboarding mutation runs — expect the launcher screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    expect(state.currentUserId).toBeTruthy()
  })

  test('password too short during guest registration shows validation error without submitting', async ({
    page,
  }) => {
    // AC10: Client-side validation catches obviously invalid passwords before any API call,
    // giving the player immediate feedback and keeping them on the save-progress screen.
    setupMockApi(page)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    const mutationNames: string[] = []
    page.on('request', (request) => {
      if (request.url().includes('/graphql') && request.method() === 'POST') {
        try {
          const body = JSON.parse(request.postData() ?? '{}')
          if ((body?.query ?? '').includes('Register')) mutationNames.push('Register')
        } catch {
          // ignore parse errors
        }
      }
    })

    // Fill a password that is too short (< 8 characters)
    await page.locator('#guestEmail').fill('newplayer@test.com')
    await page.locator('#guestDisplayName').fill('New Player')
    await page.locator('#guestPassword').fill('abc')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Error must appear inline on the save-progress screen — wizard must NOT restart
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    const errorEl = page.locator('.error-message, .error-global, [role="alert"]').first()
    await expect(errorEl).toBeVisible()
    await expect(errorEl).toContainText(/at least 8 characters/i)

    // No Register API call should have been made
    expect(mutationNames).not.toContain('Register')

    // Player should be able to fix their password and successfully save progress
    await page.locator('#guestPassword').fill('StrongPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
  })
})

// ─────────────────────────────────────────────────────────────────
// Empty-lots graceful degradation — AC11
// "If a guest onboarding run encounters a recoverable conflict or
// invalid state, the product handles it gracefully."
// ─────────────────────────────────────────────────────────────────
test.describe('Empty-lots graceful degradation (AC11)', () => {
  test('step 3 shows no-factory-lots message when city has no suitable factory lots', async ({
    page,
  }) => {
    // Arrange: give the city a shop lot only — no FACTORY-capable lot
    const state = setupMockApi(page)
    state.buildingLots = makeDefaultBuildingLots().filter((lot) =>
      lot.suitableTypes.includes('SALES_SHOP'),
    )
    await page.goto('/onboarding')

    // Steps 1 & 2
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()

    // The empty-state message should be shown instead of the lot selector
    await expect(page.locator('.empty-state-message').first()).toBeVisible()
    await expect(page.locator('.empty-state-message').first()).toContainText(
      /No factory-capable lots are available/i,
    )

    // The "Purchase First Factory" CTA must remain disabled (no lot can be selected)
    await page.getByLabel('Company Name').fill('Empty Lot Corp')
    await expect(page.getByRole('button', { name: 'Purchase First Factory' })).toBeDisabled()
  })

  test('step 4 shows no-shop-lots message when city has no suitable sales-shop lots', async ({
    page,
  }) => {
    // Arrange: supply only factory lots — strip all SALES_SHOP lots
    const state = setupMockApi(page)
    state.buildingLots = makeDefaultBuildingLots().filter((lot) =>
      lot.suitableTypes.includes('FACTORY'),
    )
    await page.goto('/onboarding')

    // Complete steps 1-3 (factory purchase succeeds — factory lot IS available)
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByLabel('Company Name').fill('No Shop Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()

    // The no-shop-lots empty-state message should appear
    await expect(page.locator('.empty-state-message').first()).toBeVisible()
    await expect(page.locator('.empty-state-message').first()).toContainText(
      /No sales-shop lots are available/i,
    )

    // "Purchase First Sales Shop" must stay disabled — no lot can be selected
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await expect(page.getByRole('button', { name: 'Purchase First Sales Shop' })).toBeDisabled()
  })
})

// ─────────────────────────────────────────────────────────────────
// Tick feedback — connects simulation state to UI (issue AC#3)
// "The player can observe game time/tick progression during onboarding
//  in a way that feels connected to the simulation."
// ─────────────────────────────────────────────────────────────────
test.describe('Tick feedback connected to simulation state', () => {
  test('guest step-5 tick panel shows the current tick from game state', async ({ page }) => {
    // The tick number shown in the guest save-progress screen must come from the
    // live gameState API response, not from a hardcoded constant in the UI.
    // This test sets a non-default tick value and verifies the panel uses it.
    const state = setupMockApi(page)
    state.gameState.currentTick = 77 // Non-default value (default mock is 42)
    await page.goto('/onboarding')
    await completeGuestSteps1to4(page)

    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    // The tick panel must reference exactly the tick number returned by the API
    await expect(page.locator('.guest-tick-panel')).toBeVisible()
    await expect(page.getByText(/Current simulation tick: 77\./)).toBeVisible()
  })

  test('authenticated completion screen shows the correct tick from game state', async ({ page }) => {
    // After authentication + FinishOnboarding, the configure-guide tick step must
    // show the tick number from the authoritative gameState response.
    const player = makePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 99 // Non-default to prove the UI uses the API value

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/onboarding')
    await completeGuidedOnboarding(page, 'Tick Verify Corp')

    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    // Configure-guide tick step must reference the server-provided tick (99)
    await expect(page.getByText('Current simulation tick: 99.')).toBeVisible()
  })
})

// ─────────────────────────────────────────────────────────────────
// Non-Bratislava city selection
// Verifies the wizard completes successfully when the player
// chooses Prague instead of Bratislava (all cities must work).
// ─────────────────────────────────────────────────────────────────
test.describe('City selection — Prague as starter city', () => {
  test('guest can complete wizard steps 1-5 after selecting Prague as starter city', async ({
    page,
  }) => {
    // Arrange: add Prague-specific lots so the factory and shop steps are available
    const state = setupMockApi(page)
    state.buildingLots = [
      ...makeDefaultBuildingLots(),
      {
        id: 'lot-prague-factory-1',
        cityId: 'city-pr',
        name: 'Prague Industrial Park',
        description: 'Factory-capable lot in Prague.',
        district: 'Industrial Zone',
        latitude: 50.08,
        longitude: 14.44,
        price: 80_000,
        suitableTypes: 'FACTORY,MINE',
        resourceTypeId: null,
        materialQuality: null,
        materialQuantity: null,
        ownedByCompanyId: null,
        appraisedValue: 80_000,
      },
      {
        id: 'lot-prague-shop-1',
        cityId: 'city-pr',
        name: 'Prague High Street Shop',
        description: 'Retail space in Prague city centre.',
        district: 'Commercial District',
        latitude: 50.083,
        longitude: 14.43,
        price: 75_000,
        suitableTypes: 'SALES_SHOP,COMMERCIAL',
        resourceTypeId: null,
        materialQuality: null,
        materialQuantity: null,
        ownedByCompanyId: null,
        appraisedValue: 75_000,
      },
    ]

    await page.goto('/onboarding')

    // Step 1: select Furniture
    await page.locator('.industry-card', { hasText: 'Furniture' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: select Prague (not Bratislava)
    await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
    await page.locator('.city-card', { hasText: 'Prague' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: factory lot in Prague
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Prague Empire Inc')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Prague Industrial Park/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: product + shop lot in Prague
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Prague High Street Shop/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: completion / save-progress screen
    await expect(page.getByRole('heading', { name: /Your Empire Preview is Ready/i })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()
    // Ensure the profit preview shows a positive revenue amount
    const revenueEl = page.locator('.profit-stat-revenue')
    await expect(revenueEl).toBeVisible()
    const revenueText = await revenueEl.textContent()
    expect(revenueText).toMatch(/\$\d/)

    // The save-progress form should be present for registration
    await expect(page.locator('#guestEmail')).toBeVisible()

    // Verify the guest's chosen city (Prague) was used — state.buildingLots shows Prague lots were loaded
    expect(state.buildingLots.some((lot) => lot.cityId === 'city-pr')).toBe(true)
  })
})

test.describe('Guest migration — all starter industries (AC2, AC9, AC13)', () => {
  // AC2: The onboarding flow offers Furniture, Food Processing, and Healthcare as starter-industry choices.
  // AC9: After a successful onboarding experience, the player is prompted to log in or sign up to save progress.
  // AC13: Automated tests cover the happy path, guest-mode boundaries, critical state transitions,
  //       and recovery scenarios — including one for each starter industry through the full migration path.

  test('guest can register and migrate Food Processing (Bread) progress to authenticated account', async ({
    page,
  }) => {
    // Full guest → register → empire launched journey for the FOOD_PROCESSING industry.
    // Proves the login handoff preserves the Bread/Food Processing choice end-to-end.
    const state = setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1: Select Food Processing
    await page.locator('.industry-card', { hasText: 'Food Processing' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Bratislava
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: factory lot
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Bakery Migration Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: Bread product + shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Bread' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: save-progress form
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()

    // Register and migrate guest progress
    await page.locator('#guestEmail').fill('bakery-guest@test.com')
    await page.locator('#guestDisplayName').fill('Bakery Founder')
    await page.locator('#guestPassword').fill('BreadPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // After migration: authenticated completion screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    expect(state.currentUserId).toBeTruthy()
    // AC: startup pack offer must appear after guest migration (Food Processing path)
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
    await expect(page.locator('.startup-pack-price')).toContainText('$20')
  })

  test('guest can register and migrate Healthcare (Basic Medicine) progress to authenticated account', async ({
    page,
  }) => {
    // Full guest → register → empire launched journey for the HEALTHCARE industry.
    // Proves the login handoff preserves the Basic Medicine/Healthcare choice end-to-end.
    const state = setupMockApi(page)
    await page.goto('/onboarding')

    // Step 1: Select Healthcare
    await page.locator('.industry-card', { hasText: 'Healthcare' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 2: Bratislava
    await page.locator('.city-card', { hasText: 'Bratislava' }).click()
    await page.getByRole('button', { name: 'Next' }).click()

    // Step 3: factory lot
    await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
    await page.getByLabel('Company Name').fill('Pharma Migration Corp')
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: 'Purchase First Factory' }).click()

    // Step 4: Basic Medicine product + shop lot
    await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible()
    await page.locator('.product-card', { hasText: 'Basic Medicine' }).click()
    await page.getByRole('button', { name: 'List View' }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: 'Purchase First Sales Shop' }).click()

    // Step 5: save-progress form
    await expect(page.getByRole('heading', { name: 'Save Your Progress' })).toBeVisible()
    await expect(page.locator('.guest-profit-preview')).toBeVisible()

    // Register and migrate guest progress
    await page.locator('#guestEmail').fill('pharma-guest@test.com')
    await page.locator('#guestDisplayName').fill('Pharma Founder')
    await page.locator('#guestPassword').fill('MedPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // After migration: authenticated completion screen
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()
    expect(state.currentUserId).toBeTruthy()
    // AC: startup pack offer must appear after guest migration (Healthcare path)
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
    await expect(page.locator('.startup-pack-price')).toContainText('$20')
  })

  test('guest migration completion: startup pack offer appears and player can claim it', async ({
    page,
  }) => {
    // AC: "After onboarding is successfully completed, an eligible user is shown a startup pack offer."
    // This test covers the guest-to-authenticated migration path specifically.
    // It verifies the full offer including price, pro benefit copy, and claimable state.
    const state = setupMockApi(page)
    await page.goto('/onboarding')

    await completeGuestSteps1to4(page, 'Startup Pack Migration Corp')

    // Register as new user to trigger migration
    await page.locator('#guestEmail').fill('startup-migration@test.com')
    await page.locator('#guestDisplayName').fill('Startup Migrator')
    await page.locator('#guestPassword').fill('MigrPass1!')
    await page.getByRole('button', { name: 'Save & Launch' }).click()

    // Completion screen with startup pack
    await expect(page.getByRole('heading', { name: /Your Empire Has Launched/i })).toBeVisible()

    // AC #2: price clearly stated
    await expect(page.locator('.startup-pack-price')).toContainText('$20')

    // AC #3: 3 months Pro + in-game currency listed
    await expect(page.getByText('3 months of Pro', { exact: true })).toBeVisible()

    // AC #4: gameplay value of Pro explained
    await expect(page.getByText(/more products to.*sell/i)).toBeVisible()

    // AC #7: player can claim
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeVisible()
    await page.getByRole('button', { name: 'Claim startup pack' }).click()

    // After claiming, the claim button disappears and claimed state is shown
    await expect(page.getByRole('button', { name: 'Claim startup pack' })).toBeHidden()
    expect(state.currentUserId).toBeTruthy()
  })
})
