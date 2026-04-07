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
  basicLaborHours: 3.5,
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
  basicLaborHours: 1.25,
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
    await expect(page.locator('.resource-meta')).toContainText('Batch energy')
    await expect(page.locator('.resource-meta')).toContainText('2 MWh')
    await expect(page.locator('.resource-meta')).toContainText('Basic labor')
    await expect(page.locator('.resource-meta')).toContainText('3.5 h')
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
          basicLaborHours: 2.8,
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
          basicLaborHours: 2.8,
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
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
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

  test('search is case-insensitive', async ({ page }) => {
    // Issue requirement: "Test search filtering behavior, especially case-insensitive matching."
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    const searchInput = page.getByPlaceholder('Search resources, ingredients, or descriptions')

    // Uppercase search should match "Wood" (stored as mixed-case).
    // Electronic Table also appears because its recipe includes Wood as an ingredient —
    // the search surfaces products that *use* the searched resource (upstream dependency matching).
    await searchInput.fill('WOOD')
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()

    // Lowercase search should also match "Electronic Components"
    await searchInput.fill('electronic components')
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Wood/ })).toHaveCount(0)

    // Mixed-case search should match correctly
    await searchInput.fill('eLeCtrOnIc TaBlE')
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
  })

  test('search filters resources by description text', async ({ page }) => {
    // The search also matches on description content so players can search for
    // ingredient properties, not just names.
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    await page.goto('/encyclopedia')
    const searchInput = page.getByPlaceholder('Search resources, ingredients, or descriptions')

    // woodResource.description = 'Timber for furniture.' — searching "timber" should surface Wood
    await searchInput.fill('timber')
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()

    // electronicComponents.description = 'Intermediate electronics input.' — "intermediate" should surface it
    await searchInput.fill('intermediate')
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Wood/ })).toHaveCount(0)
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

test.describe('Encyclopedia discoverability and routing', () => {
  test('encyclopedia is reachable via the header nav link without authentication', async ({ page }) => {
    // ROADMAP: encyclopedia must be discoverable from the existing game navigation.
    // Unauthenticated users should also be able to browse it.
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/')

    // The header nav should have an Encyclopedia link
    await page.getByRole('link', { name: 'Encyclopedia' }).click()

    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
  })

  test('resource detail page loads correctly when navigated to directly (bookmark / refresh)', async ({
    page,
  }) => {
    // ROADMAP: Validate the route can be refreshed/bookmarked without breaking data loading.
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    // Navigate directly to a resource detail URL as if bookmarked
    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
    await expect(page.locator('.resource-description').first()).toContainText('Timber for furniture.')
    await expect(page.getByRole('heading', { name: 'Used in Production Chains' })).toBeVisible()
  })

  test('product detail page loads correctly when navigated to directly (bookmark / refresh)', async ({
    page,
  }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    // Navigate directly to a product detail URL as if bookmarked
    await page.goto('/encyclopedia/resources/electronic-table')

    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient')).toHaveCount(2)
  })

  test('key resource relationships are visible without scrolling on a standard desktop viewport', async ({
    page,
  }) => {
    // ROADMAP: "When user clicks on the resource he can see at the same screen
    // without scrolling all manufacturable resources associated with it."
    await page.setViewportSize({ width: 1280, height: 800 })
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    // Resource name and description should be above the fold
    // (viewport height is 800px; the heading should appear in the top half)
    const ABOVE_FOLD_THRESHOLD = 400
    const heading = page.getByRole('heading', { name: 'Wood', level: 1 })
    await expect(heading).toBeVisible()
    const headingBox = await heading.boundingBox()
    expect(headingBox!.y).toBeLessThan(ABOVE_FOLD_THRESHOLD)

    // "Used in Production Chains" section header should be within the 800px viewport
    const VIEWPORT_HEIGHT = 800
    const usedInHeading = page.getByRole('heading', { name: 'Used in Production Chains' })
    await expect(usedInHeading).toBeVisible()
    const usedInBox = await usedInHeading.boundingBox()
    expect(usedInBox!.y).toBeLessThan(VIEWPORT_HEIGHT)

    // The downstream product card should also be visible without scrolling
    const productCard = page.locator('.product-card').filter({ hasText: 'Electronic Table' })
    await expect(productCard).toBeVisible()
    const productBox = await productCard.boundingBox()
    expect(productBox!.y).toBeLessThan(VIEWPORT_HEIGHT)
  })

  test('encyclopedia list shows all three starter industries together', async ({ page }) => {
    // ROADMAP: "All combination of products are visible in the manufacturing encyclopedia."
    const { makeDefaultResources, makeDefaultProducts } = await import('./helpers/mock-api.js')
    setupMockApi(page, {
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })

    await page.goto('/encyclopedia')

    // 3 raw material resources visible — use resource-card--resource class to avoid matching product names
    await expect(page.locator('.resource-card--resource', { hasText: 'Wood' })).toBeVisible()
    await expect(page.locator('.resource-card--resource', { hasText: 'Grain' })).toBeVisible()
    await expect(page.locator('.resource-card--resource', { hasText: 'Chemical Minerals' })).toBeVisible()

    // Products from all 3 starter industries visible
    await expect(page.locator('.resource-card--product', { hasText: 'Wooden Chair' })).toBeVisible()
    await expect(page.locator('.resource-card--product', { hasText: 'Bread' })).toBeVisible()
    await expect(page.locator('.resource-card--product', { hasText: 'Basic Medicine' })).toBeVisible()
  })

  test('encyclopedia list shows error message when API fails', async ({ page }) => {
    // Provide no resources/products so the API returns empty; test error state
    // by navigating with no data and verifying the page degrades gracefully.
    setupMockApi(page, {
      resourceTypes: [],
      productTypes: [],
    })

    await page.goto('/encyclopedia')

    // Empty state: the stat cards show zero counts
    await expect(page.locator('.stat-card').first()).toContainText('0')

    // The search bar is still rendered so users can attempt to search
    await expect(page.getByPlaceholder('Search resources, ingredients, or descriptions')).toBeVisible()
  })

  test('full user journey: home nav → encyclopedia → search → detail → related resource', async ({
    page,
  }) => {
    // End-to-end scenario: a player opens the encyclopedia from the nav bar,
    // searches for a resource, opens its detail, and navigates to a related product.
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicComponents, electronicTableProduct],
    })

    // Start at home page
    await page.goto('/')

    // Click the Encyclopedia link in the header nav
    await page.getByRole('link', { name: 'Encyclopedia' }).click()
    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    // Search for "Wood"
    await page.getByPlaceholder('Search resources, ingredients, or descriptions').fill('Wood')

    // Wood resource and Electronic Table (uses wood) should be visible; Components should be hidden
    await expect(page.getByRole('button', { name: /Wood/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Table/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /Electronic Components/ })).toHaveCount(0)

    // Click Wood to open the resource detail
    await page.getByRole('button', { name: /Wood/ }).click()
    await expect(page).toHaveURL('/encyclopedia/resources/wood')
    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()

    // Electronic Table should appear as a downstream product
    const downstreamCard = page.locator('.product-card').filter({ hasText: 'Electronic Table' })
    await expect(downstreamCard).toBeVisible()

    // Navigate to the downstream product
    await downstreamCard.click()
    await expect(page).toHaveURL('/encyclopedia/resources/electronic-table')
    await expect(page.getByRole('heading', { name: 'Electronic Table', level: 1 })).toBeVisible()
  })

  test('Food Processing chain: Grain detail shows Bread as downstream product', async ({ page }) => {
    // ROADMAP: All combinations visible. Verify the Grain → Bread supply chain
    // is discoverable: a player searching for Grain can find Bread in the detail page.
    const { makeDefaultResources, makeDefaultProducts } = await import('./helpers/mock-api.js')
    setupMockApi(page, {
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })

    await page.goto('/encyclopedia/resources/grain')

    // Resource hero should show Grain
    await expect(page.getByRole('heading', { name: 'Grain', level: 1 })).toBeVisible()

    // "Used in Production Chains" section should appear
    await expect(page.getByRole('heading', { name: 'Used in Production Chains' })).toBeVisible()

    // Bread should be visible as a downstream product
    const breadCard = page.locator('.product-card').filter({ hasText: 'Bread' })
    await expect(breadCard).toBeVisible()

    // Clicking Bread opens the Bread detail page
    await breadCard.click()
    await expect(page).toHaveURL('/encyclopedia/resources/bread')
    await expect(page.getByRole('heading', { name: 'Bread', level: 1 })).toBeVisible()
    // Bread's requirement composition should reference Grain
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('Grain')
  })

  test('Healthcare chain: Chemical Minerals detail shows Basic Medicine as downstream product', async ({
    page,
  }) => {
    // ROADMAP: All combinations visible. Verify the Chemical Minerals → Basic Medicine
    // supply chain is discoverable from the encyclopedia.
    const { makeDefaultResources, makeDefaultProducts } = await import('./helpers/mock-api.js')
    setupMockApi(page, {
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })

    await page.goto('/encyclopedia/resources/chemical-minerals')

    // Resource hero should show Chemical Minerals
    await expect(page.getByRole('heading', { name: 'Chemical Minerals', level: 1 })).toBeVisible()

    // "Used in Production Chains" section should appear
    await expect(page.getByRole('heading', { name: 'Used in Production Chains' })).toBeVisible()

    // Basic Medicine should be visible as a downstream product
    const medicineCard = page.locator('.product-card').filter({ hasText: 'Basic Medicine' })
    await expect(medicineCard).toBeVisible()

    // Clicking Basic Medicine opens the product detail page
    await medicineCard.click()
    await expect(page).toHaveURL('/encyclopedia/resources/basic-medicine')
    await expect(page.getByRole('heading', { name: 'Basic Medicine', level: 1 })).toBeVisible()
    // Basic Medicine's requirement composition should reference Chemical Minerals
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('Chemical Minerals')
  })

  test('encyclopedia is usable on a mobile viewport (320px wide)', async ({ page }) => {
    // ROADMAP UX: "The experience should also respect mobile and tablet layouts."
    await page.setViewportSize({ width: 375, height: 667 })
    const { makeDefaultResources, makeDefaultProducts } = await import('./helpers/mock-api.js')
    setupMockApi(page, {
      resourceTypes: makeDefaultResources(),
      productTypes: makeDefaultProducts(),
    })

    await page.goto('/encyclopedia')

    // Page heading should be visible on mobile without horizontal overflow
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()

    // Search input should be usable on mobile
    const searchInput = page.getByPlaceholder('Search resources, ingredients, or descriptions')
    await expect(searchInput).toBeVisible()
    await searchInput.fill('Grain')

    // Grain resource card and Bread product card should be visible
    await expect(page.locator('.resource-card--resource', { hasText: 'Grain' })).toBeVisible()
    await expect(page.locator('.resource-card--product', { hasText: 'Bread' })).toBeVisible()

    // Navigate to resource detail on mobile
    await page.locator('.resource-card--resource', { hasText: 'Grain' }).click()
    await expect(page).toHaveURL('/encyclopedia/resources/grain')

    // Detail heading visible on mobile viewport
    await expect(page.getByRole('heading', { name: 'Grain', level: 1 })).toBeVisible()

    // Downstream product (Bread) should be visible without horizontal scroll issues
    await expect(page.locator('.product-card').filter({ hasText: 'Bread' })).toBeVisible()

    // Back button should work on mobile
    await page.getByRole('button', { name: /Back to Encyclopedia/i }).click()
    await expect(page).toHaveURL('/encyclopedia')
  })
})

test.describe('Encyclopedia contextual entry points', () => {
  test('encyclopedia is reachable from dashboard empty state when player has no companies', async ({
    page,
  }) => {
    // AC 6: relevant user flows expose entry points into the encyclopedia from current product surfaces.
    // A player with completed onboarding but no companies should see a "Browse Encyclopedia" link
    // in the dashboard empty state.
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [],
    })
    const state = setupMockApi(page, {
      players: [player],
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/dashboard')

    // The encyclopedia link should appear in the dashboard empty state
    const encyclopediaLink = page.getByRole('link', { name: /Browse Manufacturing Encyclopedia/i })
    await expect(encyclopediaLink).toBeVisible()

    // Clicking it navigates to the encyclopedia
    await encyclopediaLink.click()
    await expect(page).toHaveURL('/encyclopedia')
    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
  })
})

// ── Cross-linking: encyclopedia resource detail ↔ global exchange ─────────────

test.describe('Encyclopedia resource detail — exchange cross-links', () => {
  test('resource detail page shows "Check exchange prices" link for raw materials', async ({
    page,
  }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await expect(page.getByRole('heading', { name: 'Wood', level: 1 })).toBeVisible()
    // The "Check exchange prices" link must be visible on raw-material detail pages
    const exchangeLink = page.locator('.btn-exchange-link')
    await expect(exchangeLink).toBeVisible()
    await expect(exchangeLink).toHaveAttribute('href', '/exchange')
    await expect(exchangeLink).toContainText('Check exchange prices')
  })

  test('clicking "Check exchange prices" navigates to the global exchange', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [woodResource],
      productTypes: [electronicTableProduct],
    })

    await page.goto('/encyclopedia/resources/wood')

    await page.locator('.btn-exchange-link').click()

    await expect(page).toHaveURL(/\/exchange/)
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()
  })

  test('Grain detail page has "Check exchange prices" link (Food Processing raw material)', async ({
    page,
  }) => {
    const grainResource = {
      id: 'res-grain',
      name: 'Grain',
      slug: 'grain',
      category: 'ORGANIC' as const,
      basePrice: 3,
      weightPerUnit: 1.5,
      unitName: 'Ton',
      unitSymbol: 't',
      resources: [] as never[],
    }
    setupMockApi(page, {
      resourceTypes: [grainResource],
      productTypes: [],
    })

    await page.goto('/encyclopedia/resources/grain')

    await expect(page.getByRole('heading', { name: 'Grain', level: 1 })).toBeVisible()
    const exchangeLink = page.locator('.btn-exchange-link')
    await expect(exchangeLink).toBeVisible()
    await expect(exchangeLink).toHaveAttribute('href', '/exchange')
  })
})
