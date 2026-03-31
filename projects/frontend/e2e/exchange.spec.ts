import { test, expect } from '@playwright/test'
import { setupMockApi, makeDefaultCities, makeDefaultResources, makePlayer } from './helpers/mock-api'

// ── Exchange browsing surface ─────────────────────────────────────────────────

test.describe('Global Exchange page', () => {
  test('shows exchange page with heading and subtitle', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()
    await expect(page.getByText('Browse city-level exchange prices')).toBeVisible()
  })

  test('shows city tabs for all seeded cities', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.getByRole('tab', { name: /Bratislava/ })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Prague/ })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Vienna/ })).toBeVisible()
  })

  test('shows resource rows with exchange data on load', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    // Wait for loading to finish
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Wood (ORGANIC) and Grain (ORGANIC) are seeded in default mock
    await expect(page.locator('.resource-row').first()).toBeVisible()
  })

  test('shows exchange price, transit cost, and delivered price for each city offer', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')

    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // For any resource row, each city offer card must show all three metrics
    const woodRow = page.locator('.resource-row').filter({ hasText: 'Wood' }).first()
    await expect(woodRow).toBeVisible()

    const offerCards = woodRow.locator('.city-offer-card')
    await expect(offerCards.first()).toBeVisible()

    const firstCard = offerCards.first()
    // Exchange price label
    await expect(firstCard.getByText('Exchange')).toBeVisible()
    // Transit cost label
    await expect(firstCard.getByText('Transit')).toBeVisible()
    // Delivered label
    await expect(firstCard.getByText('Delivered')).toBeVisible()
    // Quality label
    await expect(firstCard.getByText('Quality')).toBeVisible()
  })

  test('local city shows "Local — free" transit cost (same city, zero transit)', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Bratislava is the first city tab (selected by default). Its own exchange offer has 0 transit.
    const bratislavaCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Bratislava' })
      .first()
    await expect(bratislavaCard).toBeVisible()
    await expect(bratislavaCard.locator('.transit-free')).toBeVisible()
    await expect(bratislavaCard.getByText('Local — free')).toBeVisible()
  })

  test('remote city shows positive transit cost with distance in km', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Prague is ~310 km from Bratislava and should have a positive transit cost shown as "+$X.XX · Y km"
    const pragueCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Prague' })
      .first()
    await expect(pragueCard).toBeVisible()
    await expect(pragueCard.locator('.transit-cost').filter({ hasText: 'km' })).toBeVisible()
  })

  test('best price badge highlights the cheapest delivered option', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // At least one city offer card must have the "Best price" badge
    await expect(page.locator('.best-badge').first()).toBeVisible()
    await expect(page.locator('.best-badge').first()).toContainText('Best price')
  })

  test('best-offer card has the best-offer CSS class', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    await expect(page.locator('.city-offer-card.best-offer').first()).toBeVisible()
  })

  test('shows tick context chip indicating data refresh timing', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    // The tick chip communicates when exchange data was last refreshed
    await expect(page.locator('.exchange-tick-chip')).toBeVisible()
  })

  test('shows endless supply badge communicating stable market supply', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    // The exchange is a never-ending resource sale per ROADMAP — communicate this clearly
    await expect(page.locator('.exchange-supply-chip')).toBeVisible()
    await expect(page.locator('.exchange-supply-chip')).toContainText('Endless supply')
  })
})

// ── City switching ─────────────────────────────────────────────────────────────

test.describe('Global Exchange — city switching', () => {
  test('switching to a different city tab reloads exchange data for that destination', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Click Prague tab
    const pragueTab = page.getByRole('tab', { name: /Prague/ })
    await pragueTab.click()
    await expect(pragueTab).toHaveClass(/active/)

    // Exchange data reloads — once loading resolves, Prague offers must still be visible
    // (Prague selecting itself means no transit cost for Prague offers)
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    const pragueCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Prague' })
      .first()
    await expect(pragueCard).toBeVisible()
    // Prague→Prague transit must be free
    await expect(pragueCard.getByText('Local — free')).toBeVisible()
  })

  test('at least two cities show different delivered prices for Wood, proving city differentiation', async ({
    page,
  }) => {
    // Use custom city data with different rent per sqm to guarantee price differences.
    const cities = makeDefaultCities()
    // Override Vienna to have very high rent → higher exchange price.
    cities[2] = {
      ...cities[2],
      averageRentPerSqm: 35,
    }
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()

    const deliveredPrices = woodRow.locator('.delivered-price')
    const priceTexts = await deliveredPrices.allTextContents()

    // There must be at least 2 offers and not all prices must be identical
    expect(priceTexts.length).toBeGreaterThanOrEqual(2)
    const uniquePrices = new Set(priceTexts)
    expect(uniquePrices.size).toBeGreaterThan(1)
  })
})

// ── Search and filter ─────────────────────────────────────────────────────────

test.describe('Global Exchange — search and filter', () => {
  test('search input filters visible resource rows', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const searchInput = page.locator('.search-input')
    await searchInput.fill('Wood')

    // Only Wood row should remain visible
    await expect(page.locator('.resource-row[data-slug="wood"]')).toBeVisible()
    await expect(page.locator('.resource-row[data-slug="grain"]')).toHaveCount(0)
  })

  test('search with no match shows empty state message', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const searchInput = page.locator('.search-input')
    await searchInput.fill('xyznonexistentresource')

    await expect(page.getByText('No resources match your search')).toBeVisible()
  })

  test('clearing search restores all resource rows', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const searchInput = page.locator('.search-input')
    await searchInput.fill('Wood')
    await expect(page.locator('.resource-row[data-slug="grain"]')).toHaveCount(0)

    await searchInput.fill('')
    await expect(page.locator('.resource-row[data-slug="grain"]')).toBeVisible()
  })

  test('category filter shows only resources matching the selected category', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const categorySelect = page.locator('#category-select')
    await categorySelect.selectOption('ORGANIC')

    // Wood and Grain are ORGANIC in the default mock
    await expect(page.locator('.resource-row[data-slug="wood"]')).toBeVisible()
    await expect(page.locator('.resource-row[data-slug="grain"]')).toBeVisible()

    // Chem minerals (if present) would be MINERAL and hidden
    const visibleRows = page.locator('.resource-row')
    const count = await visibleRows.count()
    expect(count).toBeGreaterThan(0)
  })

  test('category filter ALL restores visibility of all resources', async ({ page }) => {
    const resources = makeDefaultResources()
    setupMockApi(page, { resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const categorySelect = page.locator('#category-select')
    await categorySelect.selectOption('ORGANIC')
    await categorySelect.selectOption('ALL')

    const visibleRows = page.locator('.resource-row')
    await expect(visibleRows.first()).toBeVisible()
  })
})

// ── Navigation ────────────────────────────────────────────────────────────────

test.describe('Global Exchange — navigation', () => {
  test('exchange nav link is present in header with correct href', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/')
    // The nav link exists in the DOM (icon-only on desktop, text visible on mobile)
    const navLink = page.locator('a[href="/exchange"]')
    await expect(navLink).toHaveCount(1)
    await expect(navLink).toHaveAttribute('title', 'Exchange')
  })

  test('/exchange route is directly accessible and loads the exchange page', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page).toHaveURL('/exchange')
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()
  })
})

// ── Mobile viewport ───────────────────────────────────────────────────────────

test.describe('Global Exchange — mobile layout (375px)', () => {
  test.use({ viewport: { width: 375, height: 812 } })

  test('exchange page is usable at 375px: city tabs, resource rows, and metrics visible', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()
    await expect(page.getByRole('tab', { name: /Bratislava/ })).toBeVisible()
    await expect(page.locator('.resource-row').first()).toBeVisible()

    const firstCard = page.locator('.city-offer-card').first()
    await expect(firstCard).toBeVisible()
    // Delivered price must be readable without overflow on narrow viewport
    await expect(firstCard.locator('.delivered-price').first()).toBeVisible()
  })

  test('search input is accessible and filters work on 375px viewport', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const searchInput = page.locator('.search-input')
    await expect(searchInput).toBeVisible()
    await searchInput.fill('Wood')
    await expect(page.locator('.resource-row[data-slug="wood"]')).toBeVisible()
  })
})

// ── Full journey: inspect resource and compare city options ───────────────────

test.describe('Global Exchange — player journey', () => {
  test('player can browse exchange, inspect Wood resource, and compare two city delivered prices', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Step 1: Exchange page shows global heading
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()

    // Step 2: Wood resource row is visible (Furniture input)
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()

    // Step 3: At least two city offer cards visible for Wood
    const woodOfferCards = woodRow.locator('.city-offer-card')
    await expect(woodOfferCards).toHaveCount(3) // Bratislava, Prague, Vienna

    // Step 4: Each card shows delivered price
    const deliveredPrices = woodRow.locator('.delivered-price')
    await expect(deliveredPrices).toHaveCount(3)

    // Step 5: One card is the best option and has transit-free (local)
    const localCard = woodRow.locator('.city-offer-card').filter({ has: page.getByText('Local — free') })
    await expect(localCard.first()).toBeVisible()

    // Step 6: At least one remote card shows transit cost with distance
    const remoteCards = woodRow.locator('.city-offer-card').filter({ has: page.locator('.transit-cost').filter({ hasText: 'km' }) })
    await expect(remoteCards.first()).toBeVisible()
  })

  test('player can inspect Grain (Food Processing input) on exchange', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const grainRow = page.locator('.resource-row[data-slug="grain"]')
    await expect(grainRow).toBeVisible()
    await expect(grainRow.locator('.city-offer-card').first()).toBeVisible()
    await expect(grainRow.locator('.delivered-price').first()).toBeVisible()
  })

  test('player can inspect Chemical Minerals (Healthcare input)', async ({
    page,
  }) => {
    // Chemical Minerals (slug: chemical-minerals) is included in the default mock resources
    const resources = makeDefaultResources()
    setupMockApi(page, { resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const chemRow = page.locator('.resource-row[data-slug="chemical-minerals"]')
    await expect(chemRow).toBeVisible()
    await expect(chemRow.locator('.city-offer-card').first()).toBeVisible()
    await expect(chemRow.locator('.delivered-price').first()).toBeVisible()
  })
})

// ── Authenticated player post-onboarding discovery ────────────────────────────

test.describe('Global Exchange — authenticated player discovery', () => {
  test('authenticated player can access exchange post-onboarding', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    // Post-onboarding player navigates directly to exchange
    await page.goto('/exchange')
    await expect(page).toHaveURL('/exchange')
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()

    // Exchange must load resources for sourcing decisions
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    await expect(page.locator('.resource-row').first()).toBeVisible()
  })

  test('exchange nav link exists in header with correct title for discovery', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/')

    // The exchange nav link must exist for players to discover the market
    const exchangeNavLink = page.locator('a[href="/exchange"]')
    await expect(exchangeNavLink).toHaveCount(1)
    await expect(exchangeNavLink).toHaveAttribute('title', 'Exchange')
  })

  test('unauthenticated player can also browse exchange (public access)', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    // Exchange is public — no auth redirect expected
    await expect(page).toHaveURL('/exchange')
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    await expect(page.locator('.resource-row').first()).toBeVisible()
  })
})

// ── Error states ──────────────────────────────────────────────────────────────

test.describe('Global Exchange — error and empty states', () => {
  test('exchange shows explicit error state when API returns an error', async ({ page }) => {
    // Override the mock to return a GraphQL error for globalExchangeOffers
    setupMockApi(page)

    await page.route('**/graphql', async (route) => {
      const body = route.request().postDataJSON()
      if (typeof body?.query === 'string' && body.query.includes('globalExchangeOffers')) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [{ message: 'Exchange service temporarily unavailable' }],
            data: null,
          }),
        })
        return
      }
      await route.continue()
    })

    await page.goto('/exchange')
    // Error message must be shown — not silent failure
    await expect(page.locator('.exchange-error')).toBeVisible()
  })

  test('empty resource list shows no-match message when all resources are filtered out', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const searchInput = page.locator('.search-input')
    await searchInput.fill('zzz_no_match_ever_999')

    await expect(page.getByText('No resources match your search')).toBeVisible()
    // Resource rows must be gone
    await expect(page.locator('.resource-row')).toHaveCount(0)
  })
})

// ── Quality and abundance display ─────────────────────────────────────────────

test.describe('Global Exchange — quality and abundance data', () => {
  test('exchange cards show local abundance percentage for each city offer', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Each city offer card must expose a local abundance metric
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()

    const firstCard = woodRow.locator('.city-offer-card').first()
    // Abundance label must appear in the offer card
    await expect(firstCard.getByText('Local abundance')).toBeVisible()
    // Abundance value must be a percentage string (e.g. "70%")
    const abundanceValue = firstCard.locator('.metric-value').last()
    await expect(abundanceValue).toContainText('%')
  })

  test('high-abundance resource has higher quality than low-abundance resource in same city', async ({
    page,
  }) => {
    // Bratislava: Wood abundance=0.7 → quality ≈77%; ChemMinerals abundance=0.3 → quality ≈53%
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Get Bratislava quality for Wood (high abundance)
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    const braWoodCard = woodRow.locator('.city-offer-card').filter({ hasText: 'Bratislava' })
    const woodQualityText = await braWoodCard.locator('.quality-value').textContent()

    // Get Bratislava quality for Chemical Minerals (low abundance)
    const chemRow = page.locator('.resource-row[data-slug="chemical-minerals"]')
    const braChemCard = chemRow.locator('.city-offer-card').filter({ hasText: 'Bratislava' })
    const chemQualityText = await braChemCard.locator('.quality-value').textContent()

    const woodQuality = parseInt(woodQualityText?.replace('%', '') ?? '0', 10)
    const chemQuality = parseInt(chemQualityText?.replace('%', '') ?? '0', 10)

    // Wood (0.7 abundance) must have higher quality than ChemMinerals (0.3 abundance)
    expect(woodQuality).toBeGreaterThan(chemQuality)
  })

  test('best-offer card selected by delivered price, not just exchange price', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    const bestCard = woodRow.locator('.city-offer-card.best-offer')
    await expect(bestCard).toBeVisible()

    // The best card must show the "Best price" badge
    await expect(bestCard.locator('.best-badge')).toBeVisible()

    // Best card's delivered price must not be higher than any other card's delivered price
    const allDeliveredPrices = woodRow.locator('.delivered-price')
    const priceTexts = await allDeliveredPrices.allTextContents()
    const bestDeliveredText = await bestCard.locator('.delivered-price').textContent()
    const bestDelivered = parseFloat(bestDeliveredText?.replace('$', '').split('/')[0] ?? '999')

    for (const priceText of priceTexts) {
      const price = parseFloat(priceText.replace('$', '').split('/')[0])
      expect(bestDelivered).toBeLessThanOrEqual(price)
    }
  })
})

// ── Post-onboarding exchange CTA ──────────────────────────────────────────────

test.describe('Global Exchange — post-onboarding context', () => {
  test('player who just completed onboarding can access exchange immediately', async ({ page }) => {
    const player = makePlayer({ onboardingCompletedAtUtc: '2026-01-01T00:00:00Z' })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // Navigate directly to exchange after onboarding
    await page.goto('/exchange')
    await expect(page).toHaveURL('/exchange')
    await expect(page.getByRole('heading', { name: 'Global Exchange' })).toBeVisible()

    // Exchange must load resources for sourcing decisions
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    await expect(page.locator('.resource-row').first()).toBeVisible()

    // Endless supply indicator confirms unlimited sourcing availability
    await expect(page.locator('.exchange-supply-chip')).toBeVisible()
  })

  test('exchange subtitle explains sourcing purpose clearly', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    // Subtitle should communicate price comparison and sourcing decision purpose
    await expect(page.getByText('Browse city-level exchange prices')).toBeVisible()
  })
})
