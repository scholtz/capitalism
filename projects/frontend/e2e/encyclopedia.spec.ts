import { expect, test } from '@playwright/test'
import { makePlayer, makeStartupPackOffer, setupMockApi } from './helpers/mock-api.js'

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
  test('shows raw and manufactured resources in one list and opens product detail', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')

    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Resources' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()

    await page.getByRole('button', { name: /Electronic Table/ }).click()

    await expect(page).toHaveURL('/encyclopedia/resources/electronic-table')
    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('1 t')
    await expect(page.locator('.composition-node.ingredient').nth(1)).toContainText('10 packs')
  })

  test('localizes encyclopedia resource names when language changes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [{ ...woodResource, imageUrl: null }],
      productTypes: [
        {
          ...electronicTableProduct,
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
      ],
    })

    // Set Slovak locale via localStorage before navigation (more reliable than UI interaction)
    await page.addInitScript(() => localStorage.setItem('app_locale', 'sk'))
    await page.goto('/encyclopedia')

    await expect(page.getByRole('heading', { name: 'Výrobná encyklopédia' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Drevo/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Elektronický Stôl/ })).toBeVisible()
  })

  test('hides Pro products from list and detail when the switch is off', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [
        electronicTableProduct,
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

    await page.goto('/encyclopedia')

    await expect(page.getByLabel('Show Pro subscription products')).not.toBeChecked()
    await expect(page.getByRole('button', { name: /Premium Desk/ })).toHaveCount(0)

    await page.goto('/encyclopedia/resources/premium-desk')
    await expect(page.locator('.not-found')).toContainText('Encyclopedia entry not found')
    await expect(page.locator('.not-found')).toContainText('Turn on the Pro products switch')
  })

  test('updates visible Pro products after claiming startup pack', async ({ page }) => {
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
      resourceTypes: [woodResource],
      productTypes: [
        electronicTableProduct,
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
    await page.getByLabel('Show Pro subscription products').check()

    const premiumDeskCard = page.getByRole('button', { name: /Premium Desk/ })
    await expect(premiumDeskCard).toContainText('Requires Pro')
    await premiumDeskCard.click()
    await expect(page.locator('.resource-description').nth(1)).toContainText('Pro unlocks this product and other advanced goods')

    await page.goto('/dashboard')
    await page.getByRole('button', { name: 'Claim startup pack' }).click()
    await expect(page.getByText('Your Pro access is active until')).toBeVisible()

    await page.goto('/encyclopedia?showPro=1')
    await expect(page.getByRole('button', { name: /Premium Desk/ })).toContainText('Pro unlocked')

    await page.getByRole('button', { name: /Premium Desk/ }).click()
    await expect(page.locator('.resource-description').nth(1)).toContainText('Your active Pro access unlocks this product immediately.')
  })
})

test.describe('Resource detail page', () => {
  test('shows resource metadata and downstream products', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
    await expect(page.locator('.resource-description').first()).toContainText('Timber for furniture.')
    await expect(page.locator('.badge--category')).toBeVisible()
    await expect(page.locator('.resource-meta')).toContainText('$10')
    await expect(page.locator('.resource-meta')).toContainText('5 kg/t')
    await expect(page.getByRole('heading', { name: 'Used in Production Chains' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Electronic Table' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Electronic Components' })).toBeHidden()
  })

  test('moves requirement composition to product detail', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/electronic-table')

    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient')).toHaveCount(2)
    await expect(page.locator('.composition-node.output').first()).toContainText('Electronic Table')
  })

  test('recipe inputs and outputs are clickable from detail pages', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    const downstreamCard = page.locator('.product-card').filter({ hasText: 'Electronic Table' })
    await downstreamCard.click()
    await expect(page).toHaveURL('/encyclopedia/resources/electronic-table')
    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()

    await page.getByRole('link', { name: /Wood/ }).first().click()
    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
  })

  test('product detail shows downstream use for intermediate products', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/electronic-components')

    await expect(page.getByRole('heading', { name: 'Electronic Components', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Used in Production Chains' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Electronic Table' })).toBeVisible()
    await expect(page.locator('.ingredient-highlight')).toContainText('10 packs')
  })

  test('detail page shows empty state when no products use the entry', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.locator('.empty-state')).toContainText('No other resources currently use this entry.')
  })

  test('detail page shows not-found state for unknown slug', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [],
    })

    await page.goto('/encyclopedia/resources/nonexistent-resource')

    await expect(page.locator('.not-found')).toBeVisible()
    await expect(page.locator('.not-found')).toContainText('Encyclopedia entry not found')
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

  test('detail localizes when language changes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [{ ...woodResource, imageUrl: null }],
      productTypes: [
        {
          ...electronicTableProduct,
          recipes: [
            { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 },
          ],
        },
      ],
    })

    // Set Slovak locale via localStorage before navigation (more reliable than UI interaction)
    await page.addInitScript(() => localStorage.setItem('app_locale', 'sk'))
    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Drevo', level: 1 })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Späť na encyklopédiu' })).toBeVisible()
  })
})

test.describe('Encyclopedia search and filter', () => {
  test('search filters resources by name', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await page.getByPlaceholder('Search resources, ingredients, or descriptions').fill('Electronic Table')

    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Wood/ })).toHaveCount(0)
  })

  test('search filters resources by ingredient name', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await page.getByPlaceholder('Search resources, ingredients, or descriptions').fill('Wood')

    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toHaveCount(0)
  })

  test('industry filter shows only matching manufactured resources', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await page.getByLabel('Filter by industry').selectOption('ELECTRONICS')

    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Wood/ })).toHaveCount(0)
  })

  test('cross-navigation from encyclopedia index to detail and back preserves access', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await page.getByRole('button', { name: /Wood/ }).click()

    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    await page.getByRole('button', { name: 'Back to Encyclopedia' }).click()
    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
  })

  test('search with no matches shows empty state message', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    await page.getByPlaceholder('Search resources, ingredients, or descriptions').fill('zzz-no-match-xyz')

    await expect(page.locator('.search-empty-state')).toBeVisible()
    await expect(page.locator('.search-empty-state')).toContainText('No resources or products match your search')
    await expect(page.getByRole('button', { name: /Wood/ })).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Electronic/ })).toHaveCount(0)
  })

  test('clearing search after no-results restores full list', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    const searchInput = page.getByPlaceholder('Search resources, ingredients, or descriptions')
    await searchInput.fill('zzz-no-match-xyz')
    await expect(page.locator('.search-empty-state')).toBeVisible()

    await searchInput.fill('')
    await expect(page.locator('.search-empty-state')).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
  })

  test('chain navigation: resource → product → related resource navigates correctly', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    // Start at wood resource detail
    await page.goto('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    // Navigate downstream to Electronic Table (a product that uses wood)
    const downstreamCard = page.locator('.product-card').filter({ hasText: 'Electronic Table' })
    await downstreamCard.click()
    await expect(page).toHaveURL('/encyclopedia/resources/electronic-table')
    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()

    // From Electronic Table, navigate back upstream to Wood via ingredient link
    await page.getByRole('link', { name: /Wood/ }).first().click()
    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    // Navigate back to encyclopedia list
    await page.getByRole('button', { name: 'Back to Encyclopedia' }).click()
    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
  })
})
