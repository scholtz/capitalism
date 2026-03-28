import { expect, test } from '@playwright/test'
import { makePlayer, makeStartupPackOffer, setupMockApi } from './helpers/mock-api'

const woodResource = {
  id: 'res-wood',
  name: 'Wood',
  slug: 'wood',
  category: 'ORGANIC',
  basePrice: 10,
  weightPerUnit: 5,
  unitName: 'Ton',
  unitSymbol: 't',
  imageUrl: 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="brown"/></svg>',
  description: 'Timber for furniture.',
}

const electronicTableProduct = {
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
  description: 'Smart table.',
  recipes: [
    { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 },
    { resourceType: null, inputProductType: { id: 'prod-components', name: 'Electronic Components', slug: 'electronic-components', unitName: 'Pack', unitSymbol: 'packs' }, quantity: 10 },
  ],
}

const electronicComponents = {
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
}

test.describe('Manufacturing encyclopedia', () => {
  test('shows raw material cards and product recipes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')

    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Wood' })).toBeVisible()
    await page.getByRole('button', { name: /Electronic Table/ }).click()
    await expect(page.getByRole('heading', { name: 'Electronic Table' }).first()).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('1 t')
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('Wood')
    await expect(page.locator('.composition-node.ingredient').nth(1)).toContainText('10 packs')
    await expect(page.locator('.composition-node.ingredient').nth(1)).toContainText('Electronic Components')
    await expect(page.locator('.composition-node.ingredient')).toHaveCount(2)
  })

  test('localizes encyclopedia product names when language changes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [
        {
          id: 'res-wood',
          name: 'Wood',
          slug: 'wood',
          category: 'ORGANIC',
          basePrice: 10,
          weightPerUnit: 5,
          unitName: 'Ton',
          unitSymbol: 't',
          imageUrl: null,
          description: 'Timber for furniture.',
        },
      ],
      productTypes: [
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
          description: 'Smart table.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', imageUrl: null, description: 'Wood' }, inputProductType: null, quantity: 1 }],
        },
      ],
    })

    await page.goto('/encyclopedia')
    await page.getByLabel('Language').selectOption('sk')

    await expect(page.getByRole('heading', { name: 'Výrobná encyklopédia' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Drevo' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Elektronický Stôl' }).first()).toBeVisible()
  })

  test('updates locked pro products after claiming startup pack', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T12:00:00Z',
      startupPackOffer: makeStartupPackOffer(),
      companies: [
        {
          id: 'company-pro',
          playerId: 'player-1',
          name: 'Premium Works',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })

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
          description: 'A basic wooden chair.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
        {
          id: 'prod-premium-desk',
          name: 'Premium Desk',
          slug: 'premium-desk',
          industry: 'FURNITURE',
          basePrice: 180,
          baseCraftTicks: 4,
          outputQuantity: 4,
          energyConsumptionMwh: 1.6,
          unitName: 'Desk',
          unitSymbol: 'desks',
          isProOnly: true,
          description: 'Advanced office furniture line.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 2 }],
        },
      ],
    })

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/encyclopedia')
    const premiumDeskCard = page.getByRole('button', { name: /Premium Desk/ })
    await expect(premiumDeskCard).toContainText('Requires Pro')
    await premiumDeskCard.click()
    await expect(page.locator('.selected-product')).toContainText('Pro unlocks this product and other advanced goods')

    await page.goto('/dashboard')
    await page.getByRole('button', { name: 'Claim startup pack' }).click()
    await expect(page.getByText('Your Pro access is active until')).toBeVisible()

    await page.goto('/encyclopedia')
    await expect(page.getByRole('button', { name: /Premium Desk/ })).toContainText('Pro unlocked')
    await page.getByRole('button', { name: /Premium Desk/ }).click()
    await expect(page.locator('.selected-product')).toContainText('Your active Pro access unlocks this product immediately.')
  })
})

test.describe('Resource detail page', () => {
  test('navigates to resource detail when clicking a resource card', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    const woodCard = page.locator('.resource-card').filter({ hasText: 'Wood' })
    await expect(woodCard).toBeVisible()
    await woodCard.click()

    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
  })

  test('resource detail page shows resource metadata', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
    await expect(page.locator('.resource-description')).toContainText('Timber for furniture.')
    await expect(page.locator('.badge--category')).toBeVisible()
    await expect(page.locator('.resource-meta')).toContainText('$10')
    await expect(page.locator('.resource-meta')).toContainText('5 kg/t')
  })

  test('resource detail page lists products that use the resource', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Used in Products' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Electronic Table' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Electronic Components' })).toBeHidden()
  })

  test('resource detail page shows ingredient quantity and recipe for each product', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    const productCard = page.locator('.product-card').filter({ hasText: 'Electronic Table' })
    await expect(productCard).toBeVisible()
    await expect(productCard.locator('.ingredient-highlight')).toContainText('1')
    await expect(productCard.locator('.ingredient-highlight')).toContainText('t')
    await expect(productCard.locator('.ingredient-chips')).toContainText('Electronic Components')
  })

  test('resource detail page shows empty state when no products use the resource', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Used in Products' })).toBeVisible()
    await expect(page.locator('.empty-state')).toContainText('No products currently use this resource.')
  })

  test('resource detail page shows not-found state for unknown slug', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [],
    })

    await page.goto('/encyclopedia/resources/nonexistent-resource')

    await expect(page.locator('.not-found')).toBeVisible()
    await expect(page.locator('.not-found')).toContainText('Resource not found')
  })

  test('back to encyclopedia navigation works', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    await page.getByRole('button', { name: 'Back to Encyclopedia' }).click()

    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
  })

  test('resource detail is deep-linkable (refresh-safe)', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Used in Products' })).toBeVisible()
  })

  test('resource detail localizes when language changes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [
        {
          ...woodResource,
          imageUrl: null,
        },
      ],
      productTypes: [
        {
          ...electronicTableProduct,
          recipes: [
            { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', imageUrl: null, description: 'Wood' }, inputProductType: null, quantity: 1 },
          ],
        },
      ],
    })

    await page.goto('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    await page.getByLabel('Language').selectOption('sk')

    await expect(page.getByRole('heading', { name: 'Drevo', level: 1 })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Späť na encyklopédiu' })).toBeVisible()
  })
})

test.describe('Encyclopedia search and filter', () => {
  test('search filters products by name', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    // Both products should be visible before filtering
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeVisible()

    // Type in the search field to filter
    await page.getByPlaceholder('Search products, ingredients, or descriptions').fill('Electronic Table')

    // Only the matching product should remain
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeHidden()
  })

  test('search filters products by ingredient name', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')

    // Electronic Table uses Wood as ingredient — searching for "wood" should surface it
    await page.getByPlaceholder('Search products, ingredients, or descriptions').fill('Wood')

    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    // Electronic Components has no Wood in its recipes
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeHidden()
  })

  test('industry filter shows only matching products', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    // Select ELECTRONICS industry filter
    await page.getByLabel('Filter by industry').selectOption('ELECTRONICS')

    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeHidden()
  })

  test('cross-navigation: clicking a resource from encyclopedia index goes to detail and back', async ({
    page,
  }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    // Navigate to resource detail
    await page.locator('.resource-card').filter({ hasText: 'Wood' }).click()
    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    // Navigate back
    await page.getByRole('button', { name: 'Back to Encyclopedia' }).click()
    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
  })
})
