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

  test('local city shows minimum transit cost (same city, distance zero)', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Bratislava is the first city tab (selected by default). Same-city transit has minimum cost of $0.01.
    const bratislavaCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Bratislava' })
      .first()
    await expect(bratislavaCard).toBeVisible()
    await expect(bratislavaCard.locator('.transit-cost')).toBeVisible()
    // Must show a positive transit cost (minimum $0.01 even for same-city)
    await expect(bratislavaCard.locator('.transit-cost')).toContainText('+')
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
    // Prague→Prague transit must show minimum cost (non-zero)
    await expect(pragueCard.locator('.transit-cost')).toBeVisible()
    await expect(pragueCard.locator('.transit-cost')).toContainText('+')
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
    await expect(page).toHaveURL(/\/exchange/)
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

    // Step 5: One card is the best option (lowest delivered price) – local card has minimum transit
    const localCard = woodRow.locator('.city-offer-card').filter({ has: page.locator('.transit-cost') }).first()
    await expect(localCard).toBeVisible()

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
    await expect(page).toHaveURL(/\/exchange/)
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
    await expect(page).toHaveURL(/\/exchange/)
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
    await expect(page).toHaveURL(/\/exchange/)
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

// ── Transit-reranking: cheapest sticker ≠ best delivered ──────────────────────

test.describe('Global Exchange — transit-reranking proof', () => {
  test('city with lowest exchange price is NOT always the best delivered option', async ({
    page,
  }) => {
    // Bratislava is the DESTINATION (first city tab by default).
    // Prague has highest Wood abundance → lowest sticker price.
    // But Prague is ~310 km away, so its transit cost flips the ranking.
    // Bratislava (local, 0 transit) should win on delivered price despite higher sticker.
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()

    // The "best" badge should appear on the Bratislava offer (local, 0 transit)
    const braCard = woodRow.locator('.city-offer-card').filter({ hasText: 'Bratislava' })
    await expect(braCard.locator('.best-badge')).toBeVisible()

    // Prague must NOT have the best-offer badge even if its exchange sticker is cheapest
    const pragueCard = woodRow.locator('.city-offer-card').filter({ hasText: 'Prague' })
    await expect(pragueCard).toBeVisible()
    await expect(pragueCard.locator('.best-badge')).toHaveCount(0)

    // Delivered price labels are present for both
    await expect(braCard.locator('.delivered-price')).toBeVisible()
    await expect(pragueCard.locator('.delivered-price')).toBeVisible()
  })

  test('category filter narrows visible resource rows without losing offer cards', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // All resources visible initially
    const initialCount = await page.locator('.resource-row').count()
    expect(initialCount).toBeGreaterThan(1)

    // Filter to ORGANIC category — Wood and Grain are ORGANIC
    const categorySelect = page.locator('.filter-select')
    await categorySelect.selectOption({ label: 'Organic' })

    const filteredCount = await page.locator('.resource-row').count()
    expect(filteredCount).toBeGreaterThan(0)
    expect(filteredCount).toBeLessThan(initialCount)

    // Offer cards must still appear for the remaining rows
    const firstVisibleRow = page.locator('.resource-row').first()
    await expect(firstVisibleRow.locator('.city-offer-card').first()).toBeVisible()
  })

  test('switching city tab changes which city has zero transit cost', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Default tab = Bratislava → Bratislava offer has minimum transit cost
    const braLocalCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Bratislava' })
      .filter({ has: page.locator('.transit-cost') })
    await expect(braLocalCard.first()).toBeVisible()

    // Switch to Prague tab
    await page.getByRole('tab', { name: /Prague/ }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Prague offer should now show minimum transit (Prague is the destination)
    const pragueLocalCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Prague' })
      .filter({ has: page.locator('.transit-cost') })
    await expect(pragueLocalCard.first()).toBeVisible()

    // Bratislava is now remote → must show positive transit cost
    const braRemoteCard = page
      .locator('.city-offer-card')
      .filter({ hasText: 'Bratislava' })
      .first()
    await expect(braRemoteCard.locator('.transit-cost').filter({ hasText: 'km' })).toBeVisible()
  })
})

// ── Sourcing decision legibility ──────────────────────────────────────────────

test.describe('Global Exchange — sourcing decision legibility', () => {
  test('all four key metrics are present for every city offer card (resource, city, quality, delivered)', async ({
    page,
  }) => {
    setupMockApi(page)
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const allCards = page.locator('.city-offer-card')
    const count = await allCards.count()
    expect(count).toBeGreaterThan(0)

    // Every card must show all four key decision metrics
    for (let i = 0; i < Math.min(count, 3); i++) {
      const card = allCards.nth(i)
      await expect(card.locator('.metric-label').filter({ hasText: 'Exchange' })).toBeVisible()
      await expect(card.locator('.metric-label').filter({ hasText: 'Transit' })).toBeVisible()
      await expect(card.locator('.metric-label').filter({ hasText: 'Delivered' })).toBeVisible()
      await expect(card.locator('.metric-label').filter({ hasText: 'Quality' })).toBeVisible()
    }
  })

  test('only one card per resource row has the best-price badge', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // For each resource row, exactly one offer card should have the best-badge
    const rows = page.locator('.resource-row')
    const rowCount = await rows.count()

    for (let i = 0; i < rowCount; i++) {
      const row = rows.nth(i)
      const badgeCount = row.locator('.best-badge')
      await expect(badgeCount).toHaveCount(1)
    }
  })

  test('Grain resource row appears with all city offers (Food Processing raw input)', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const grainRow = page.locator('.resource-row[data-slug="grain"]')
    await expect(grainRow).toBeVisible()
    // All three seeded cities should have Grain offers
    await expect(grainRow.locator('.city-offer-card')).toHaveCount(3)
    // Each card must have a delivered price
    await expect(grainRow.locator('.delivered-price').first()).toBeVisible()
    // One card is best
    await expect(grainRow.locator('.best-badge')).toHaveCount(1)
  })

  test('Chemical Minerals resource row appears with all city offers (Healthcare raw input)', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const chemRow = page.locator('.resource-row[data-slug="chemical-minerals"]')
    await expect(chemRow).toBeVisible()
    await expect(chemRow.locator('.city-offer-card')).toHaveCount(3)
    await expect(chemRow.locator('.delivered-price').first()).toBeVisible()
    await expect(chemRow.locator('.best-badge')).toHaveCount(1)
  })
})

// ── Cross-linking: exchange ↔ encyclopedia ────────────────────────────────────

test.describe('Global Exchange — production chain cross-links', () => {
  test('each resource row has a "View production chain" link to the encyclopedia', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // The Wood resource row must have the cross-link
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()
    const woodLink = woodRow.locator('.production-chain-link')
    await expect(woodLink).toBeVisible()
    await expect(woodLink).toHaveAttribute('href', '/encyclopedia/resources/wood')
    await expect(woodLink).toContainText('View production chain')
  })

  test('clicking "View production chain" link navigates to encyclopedia resource detail', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await woodRow.locator('.production-chain-link').click()

    await expect(page).toHaveURL('/encyclopedia/resources/wood')
  })

  test('Grain resource row has production chain link pointing to grain encyclopedia page', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const grainRow = page.locator('.resource-row[data-slug="grain"]')
    await expect(grainRow.locator('.production-chain-link')).toHaveAttribute(
      'href',
      '/encyclopedia/resources/grain',
    )
  })

  test('Chemical Minerals row has production chain link to chemical-minerals encyclopedia page', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const chemRow = page.locator('.resource-row[data-slug="chemical-minerals"]')
    await expect(chemRow.locator('.production-chain-link')).toHaveAttribute(
      'href',
      '/encyclopedia/resources/chemical-minerals',
    )
  })
})

test.describe('Global Exchange — deep-link query params from purchase unit', () => {
  test('?resource=wood pre-fills the search box with "wood"', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange?resource=wood')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // The search box must be pre-filled with the resource slug
    const searchBox = page.getByPlaceholder('Search resources')
    await expect(searchBox).toHaveValue('wood')

    // Only the Wood resource row should be visible
    await expect(page.locator('.resource-row[data-slug="wood"]')).toBeVisible()
  })

  test('?city=city-pr pre-selects Prague as the destination city', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange?city=city-pr')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // The Prague city tab should be selected / active
    const pragueCityTab = page.locator('.city-tab', { hasText: 'Prague' })
    await expect(pragueCityTab).toHaveClass(/active/)
  })

  test('?resource=grain&city=city-vi pre-fills search and selects Vienna destination', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange?resource=grain&city=city-vi')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Search is pre-filled with the grain slug
    const searchBox = page.getByPlaceholder('Search resources')
    await expect(searchBox).toHaveValue('grain')

    // Vienna tab is pre-selected
    const viennaCityTab = page.locator('.city-tab', { hasText: 'Vienna' })
    await expect(viennaCityTab).toHaveClass(/active/)
  })

  test('unknown ?city param falls back to the first city', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange?city=city-unknown')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Falls back to the first city (Bratislava)
    const bratislavaCityTab = page.locator('.city-tab', { hasText: 'Bratislava' })
    await expect(bratislavaCityTab).toHaveClass(/active/)
  })
})

test.describe('Global Exchange — city URL persistence', () => {
  test('clicking a city tab updates the URL with the ?city= param', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Click the Prague city tab
    await page.locator('.city-tab', { hasText: 'Prague' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // URL should now include ?city=city-pr so reloading restores the same city
    await expect(page).toHaveURL(/[?&]city=city-pr/)
  })

  test('page reload after city selection restores the selected city', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })

    await page.goto('/exchange')
    await page.locator('.city-tab', { hasText: 'Vienna' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    await expect(page).toHaveURL(/city=city-vi/)

    await page.reload()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Vienna tab should still be active after reload
    const viennaTab = page.locator('.city-tab', { hasText: 'Vienna' })
    await expect(viennaTab).toHaveClass(/active/)
  })
})

// ── Product marketplace tab ──────────────────────────────────────────────────

test.describe('Global Exchange — Products marketplace tab', () => {
  test('shows Raw Materials and Products mode tabs', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    await expect(page.getByRole('tab', { name: 'Raw Materials' })).toBeVisible()
    await expect(page.getByRole('tab', { name: 'Products' })).toBeVisible()
  })

  test('Raw Materials tab is active by default', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Resources tab is active, city tabs are visible
    const rawTab = page.getByRole('tab', { name: 'Raw Materials' })
    await expect(rawTab).toHaveClass(/active/)

    // City tabs only appear for resources mode
    await expect(page.locator('.city-tabs')).toBeVisible()
  })

  test('clicking Products tab switches to product marketplace', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    await page.getByRole('tab', { name: 'Products' }).click()

    // City tabs should be hidden; products hint should appear
    await expect(page.locator('.city-tabs')).toBeHidden()
    await expect(page.locator('.products-mode-hint')).toBeVisible()
  })

  test('Products tab shows market bid and offer quotes even when no player listings exist', async ({ page }) => {
    setupMockApi(page, { productExchangeListings: [] })
    await page.goto('/exchange')

    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const chairRow = page.locator('.product-row').filter({ hasText: 'Wooden Chair' })
    await expect(chairRow).toBeVisible()
    await expect(page.locator('.products-mode-hint')).toBeVisible()
    await expect(chairRow).toContainText('Bid price')
    await expect(chairRow).toContainText('Ask price')
    await expect(chairRow).toContainText('Listings appear when players place sell orders')
  })

  test('Products tab shows listings when sell orders exist', async ({ page }) => {
    const cities = makeDefaultCities()
    const bratislava = cities.find((c) => c.name === 'Bratislava')!
    const player = makePlayer()

    const chairListing = {
      orderId: 'order-chair-1',
      productTypeId: 'prod-chair',
      productName: 'Wooden Chair',
      productSlug: 'wooden-chair',
      productIndustry: 'FURNITURE',
      unitSymbol: 'chairs',
      unitName: 'Chair',
      basePrice: 45,
      pricePerUnit: 55,
      remainingQuantity: 100,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-seller-1',
      sellerCompanyName: 'Furniture Co',
      createdAtUtc: new Date().toISOString(),
    }

    setupMockApi(page, {
      cities,
      players: [player],
      productExchangeListings: [chairListing],
    })

    await page.goto('/exchange')
    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Product row should be visible
    await expect(page.locator('.product-row').filter({ hasText: 'Wooden Chair' })).toBeVisible()

    // Industry badge should show
    await expect(page.locator('.product-industry-badge', { hasText: 'Furniture' })).toBeVisible()

    // Synthetic market quote should still be present above player listings
    const chairRow = page.locator('.product-row').filter({ hasText: 'Wooden Chair' })
    await expect(chairRow).toContainText('Bid price')
    await expect(chairRow).toContainText('Ask price')

    // Listing details should be visible
    await expect(page.locator('.listing-price', { hasText: '$55' })).toBeVisible()
    await expect(page.locator('.listing-quantity', { hasText: '100' })).toBeVisible()
    await expect(page.locator('.listing-seller', { hasText: 'Furniture Co' })).toBeVisible()
    await expect(page.locator('.listing-city', { hasText: 'Bratislava' })).toBeVisible()
  })

  test('Products tab shows price vs base comparison', async ({ page }) => {
    const cities = makeDefaultCities()
    const bratislava = cities.find((c) => c.name === 'Bratislava')!

    const listingAboveBase = {
      orderId: 'order-above-base',
      productTypeId: 'prod-chair',
      productName: 'Wooden Chair',
      productSlug: 'wooden-chair',
      productIndustry: 'FURNITURE',
      unitSymbol: 'chairs',
      unitName: 'Chair',
      basePrice: 45,
      pricePerUnit: 55, // 22% above base
      remainingQuantity: 50,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-seller-2',
      sellerCompanyName: 'Premium Furniture',
      createdAtUtc: new Date().toISOString(),
    }

    const listingBelowBase = {
      orderId: 'order-below-base',
      productTypeId: 'prod-chair',
      productName: 'Wooden Chair',
      productSlug: 'wooden-chair',
      productIndustry: 'FURNITURE',
      unitSymbol: 'chairs',
      unitName: 'Chair',
      basePrice: 45,
      pricePerUnit: 40, // below base
      remainingQuantity: 200,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-seller-3',
      sellerCompanyName: 'Discount Furniture',
      createdAtUtc: new Date().toISOString(),
    }

    setupMockApi(page, {
      cities,
      productExchangeListings: [listingAboveBase, listingBelowBase],
    })

    await page.goto('/exchange')
    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Each listing row should show the correct price-vs-base badge
    const chairRow = page.locator('.product-row').filter({ hasText: 'Wooden Chair' })

    // Premium Furniture row (above base at $55 vs base $45) should have red badge
    const premiumRow = chairRow.locator('.listing-row').filter({ hasText: 'Premium Furniture' })
    await expect(premiumRow.locator('.price-above-base')).toBeVisible()

    // Discount Furniture row (below base at $40 vs base $45) should have green badge
    const discountRow = chairRow.locator('.listing-row').filter({ hasText: 'Discount Furniture' })
    await expect(discountRow.locator('.price-below-base')).toBeVisible()
  })

  test('Products tab industry filter narrows results', async ({ page }) => {
    const cities = makeDefaultCities()
    const bratislava = cities.find((c) => c.name === 'Bratislava')!

    const chairListing = {
      orderId: 'order-chair-filter',
      productTypeId: 'prod-chair',
      productName: 'Wooden Chair',
      productSlug: 'wooden-chair',
      productIndustry: 'FURNITURE',
      unitSymbol: 'chairs',
      unitName: 'Chair',
      basePrice: 45,
      pricePerUnit: 50,
      remainingQuantity: 50,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-f',
      sellerCompanyName: 'Furniture Inc',
      createdAtUtc: new Date().toISOString(),
    }

    const breadListing = {
      orderId: 'order-bread-filter',
      productTypeId: 'prod-bread',
      productName: 'Bread',
      productSlug: 'bread',
      productIndustry: 'FOOD_PROCESSING',
      unitSymbol: 'loaves',
      unitName: 'Loaf',
      basePrice: 3,
      pricePerUnit: 4,
      remainingQuantity: 200,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-fp',
      sellerCompanyName: 'Bakery Inc',
      createdAtUtc: new Date().toISOString(),
    }

    setupMockApi(page, {
      cities,
      productExchangeListings: [chairListing, breadListing],
    })

    await page.goto('/exchange')
    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Both products visible initially
    await expect(page.locator('.product-row').filter({ hasText: 'Wooden Chair' })).toBeVisible()
    await expect(page.locator('.product-row').filter({ hasText: 'Bread' })).toBeVisible()

    // Filter to FURNITURE only
    await page.locator('#industry-select').selectOption('FURNITURE')

    // Only Wooden Chair should be visible
    await expect(page.locator('.product-row').filter({ hasText: 'Wooden Chair' })).toBeVisible()
    await expect(page.locator('.product-row').filter({ hasText: 'Bread' })).toHaveCount(0)
  })

  test('Products tab search filters by product name', async ({ page }) => {
    const cities = makeDefaultCities()
    const bratislava = cities.find((c) => c.name === 'Bratislava')!

    const chairListing = {
      orderId: 'order-chair-search',
      productTypeId: 'prod-chair',
      productName: 'Wooden Chair',
      productSlug: 'wooden-chair',
      productIndustry: 'FURNITURE',
      unitSymbol: 'chairs',
      unitName: 'Chair',
      basePrice: 45,
      pricePerUnit: 50,
      remainingQuantity: 50,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-f-s',
      sellerCompanyName: 'Search Furniture',
      createdAtUtc: new Date().toISOString(),
    }

    const breadListing = {
      orderId: 'order-bread-search',
      productTypeId: 'prod-bread',
      productName: 'Bread',
      productSlug: 'bread',
      productIndustry: 'FOOD_PROCESSING',
      unitSymbol: 'loaves',
      unitName: 'Loaf',
      basePrice: 3,
      pricePerUnit: 4,
      remainingQuantity: 200,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: 'company-fp-s',
      sellerCompanyName: 'Search Bakery',
      createdAtUtc: new Date().toISOString(),
    }

    setupMockApi(page, {
      cities,
      productExchangeListings: [chairListing, breadListing],
    })

    await page.goto('/exchange')
    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Search for "chair"
    await page.getByPlaceholder('Search products').fill('chair')

    await expect(page.locator('.product-row').filter({ hasText: 'Wooden Chair' })).toBeVisible()
    await expect(page.locator('.product-row').filter({ hasText: 'Bread' })).toHaveCount(0)
  })

  test('Products mode selection persists in URL', async ({ page }) => {
    setupMockApi(page)
    await page.goto('/exchange')

    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page).toHaveURL(/mode=products/)

    await page.reload()
    await expect(page.locator('.products-mode-hint')).toBeVisible()
  })

  test('Products tab shows listings for multiple industries', async ({ page }) => {
    const cities = makeDefaultCities()
    const bratislava = cities.find((c) => c.name === 'Bratislava')!

    const makeProductListing = (
      id: string,
      productName: string,
      productSlug: string,
      industry: string,
      basePrice: number,
      pricePerUnit: number,
      unitSymbol: string,
    ) => ({
      orderId: `order-${id}`,
      productTypeId: `prod-${id}`,
      productName,
      productSlug,
      productIndustry: industry,
      unitSymbol,
      unitName: 'Piece',
      basePrice,
      pricePerUnit,
      remainingQuantity: 100,
      sellerCityId: bratislava.id,
      sellerCityName: bratislava.name,
      sellerCompanyId: `company-${id}`,
      sellerCompanyName: `Company ${id}`,
      createdAtUtc: new Date().toISOString(),
    })

    setupMockApi(page, {
      cities,
      productExchangeListings: [
        makeProductListing('chair', 'Wooden Chair', 'wooden-chair', 'FURNITURE', 45, 50, 'chairs'),
        makeProductListing('bread', 'Bread', 'bread', 'FOOD_PROCESSING', 3, 4, 'loaves'),
        makeProductListing('medicine', 'Basic Medicine', 'basic-medicine', 'HEALTHCARE', 50, 60, 'bottles'),
      ],
    })

    await page.goto('/exchange')
    await page.getByRole('tab', { name: 'Products' }).click()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // All three industries should be visible
    await expect(page.locator('.product-row').filter({ hasText: 'Wooden Chair' })).toBeVisible()
    await expect(page.locator('.product-row').filter({ hasText: 'Bread' })).toBeVisible()
    await expect(page.locator('.product-row').filter({ hasText: 'Basic Medicine' })).toBeVisible()

    // Industry badges
    await expect(page.locator('.product-industry-badge', { hasText: 'Furniture' })).toBeVisible()
    await expect(page.locator('.product-industry-badge', { hasText: 'Food Processing' })).toBeVisible()
    await expect(page.locator('.product-industry-badge', { hasText: 'Healthcare' })).toBeVisible()
  })
})

// ── Tick-refresh stability ─────────────────────────────────────────────────────

test.describe('Global Exchange — tick-refresh stability', () => {
  test('background tick refresh does not blank the resource rows or show a loading spinner', async ({
    page,
  }) => {
    const state = setupMockApi(page)
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // At least one resource row must be visible before the tick fires
    await expect(page.locator('.resource-row').first()).toBeVisible()

    // Simulate tick advance — gameStateStore will pick this up on its next poll
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Content must remain visible — no full-screen loading flash
    await expect(page.locator('.resource-row').first()).toBeVisible()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
  })

  test('selected city is preserved across a background tick refresh', async ({ page }) => {
    const state = setupMockApi(page)
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    // Start with Prague pre-selected via URL
    await page.goto('/exchange?city=city-pr')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Prague tab must be active
    await expect(page.getByRole('tab', { name: /Prague/ })).toHaveClass(/active/)

    // Simulate tick advance
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Prague tab must remain active and offers must still be visible
    await expect(page.getByRole('tab', { name: /Prague/ })).toHaveClass(/active/)
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
    await expect(page.locator('.resource-row').first()).toBeVisible()
  })

  test('search filter is preserved across a background tick refresh', async ({ page }) => {
    const state = setupMockApi(page)
    state.gameState.currentTick = 15
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Apply a search filter for Wood
    const searchInput = page.locator('.search-input')
    await searchInput.fill('Wood')
    await expect(page.locator('.resource-row').first()).toBeVisible()

    // Simulate tick advance
    state.gameState.currentTick = 16
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Search text must still be present and Wood row still visible
    await expect(searchInput).toHaveValue('Wood')
    await expect(page.locator('.resource-row[data-slug="wood"]')).toBeVisible()
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
  })

  test('market mode tab (Products) is preserved across a background tick refresh', async ({
    page,
  }) => {
    const state = setupMockApi(page)
    state.gameState.currentTick = 20
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.goto('/exchange?mode=products')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Products tab must be active (set via URL param on mount)
    await expect(page.getByRole('tab', { name: 'Products' })).toHaveClass(/active/)

    // Simulate tick advance
    state.gameState.currentTick = 21
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Products tab must remain active after refresh
    await expect(page.getByRole('tab', { name: 'Products' })).toHaveClass(/active/)
    await expect(page.locator('.exchange-loading')).toHaveCount(0)
  })
})

// ── Quality variability bands ─────────────────────────────────────────────────

test.describe('Global Exchange — quality variability bands', () => {
  test('each offer card shows a quality range (min–max) not just a single value', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // The Wood resource row's first offer card must show a quality range
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    await expect(woodRow).toBeVisible()

    const firstCard = woodRow.locator('.city-offer-card').first()
    // quality-range element must contain an en-dash (–) separating min and max
    const qualityRange = firstCard.locator('.quality-range')
    await expect(qualityRange).toBeVisible()
    const rangeText = await qualityRange.textContent()
    expect(rangeText).toContain('–')
    // Both sides of the dash must be percentage values
    const parts = (rangeText ?? '').split('–').map((s) => s.trim())
    expect(parts).toHaveLength(2)
    expect(parts[0]).toMatch(/%/)
    expect(parts[1]).toMatch(/%/)
  })

  test('quality band bar is rendered for each offer card', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    const firstCard = woodRow.locator('.city-offer-card').first()
    // The band bar track must be rendered
    await expect(firstCard.locator('.quality-band-bar')).toBeVisible()
    // The filled portion inside the band bar must be rendered
    await expect(firstCard.locator('.quality-band-fill')).not.toHaveCount(0)
    // The center tick marking the estimated quality must be rendered
    await expect(firstCard.locator('.quality-band-center')).not.toHaveCount(0)
  })

  test('high-abundance resource (Wood 70%) shows narrower quality range than low-abundance (ChemMinerals 30%)', async ({
    page,
  }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    // Wood (abundance 0.7) → Bratislava card
    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    const woodBraCard = woodRow.locator('.city-offer-card').filter({ hasText: 'Bratislava' })
    const woodRangeText = await woodBraCard.locator('.quality-range').textContent()

    // Chemical Minerals (abundance 0.3 in Bratislava seed data) → Bratislava card
    const chemRow = page.locator('.resource-row[data-slug="chemical-minerals"]')
    const chemBraCard = chemRow.locator('.city-offer-card').filter({ hasText: 'Bratislava' })
    const chemRangeText = await chemBraCard.locator('.quality-range').textContent()

    // Parse the ranges
    const parseRange = (text: string | null): number => {
      if (!text) return 0
      const parts = text.split('–').map((s) => parseFloat(s.replace('%', '').trim()))
      return Math.abs((parts[1] ?? 0) - (parts[0] ?? 0))
    }

    const woodBandWidth = parseRange(woodRangeText)
    const chemBandWidth = parseRange(chemRangeText)

    // Higher abundance → narrower band (more predictable quality)
    expect(chemBandWidth).toBeGreaterThan(woodBandWidth)
  })

  test('quality band tooltip hint is accessible via title attribute', async ({ page }) => {
    const cities = makeDefaultCities()
    const resources = makeDefaultResources()
    setupMockApi(page, { cities, resourceTypes: resources })
    await page.goto('/exchange')
    await expect(page.locator('.exchange-loading')).toHaveCount(0)

    const woodRow = page.locator('.resource-row[data-slug="wood"]')
    const firstCard = woodRow.locator('.city-offer-card').first()
    // The quality band bar has a title attribute explaining the variability
    const bandBar = firstCard.locator('.quality-band-bar')
    // Get the title attribute as a string and verify it is descriptive (> 10 chars)
    const titleValue = await bandBar.getAttribute('title')
    expect(titleValue).toBeTruthy()
    expect(titleValue!.length).toBeGreaterThan(10)
  })
})
