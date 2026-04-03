import { test, expect } from '@playwright/test'
import {
  setupMockApi,
  makePlayer,
  makeDefaultBuildingLots,
  type MockBuildingLot,
} from './helpers/mock-api'

// ── Helper to set up an authenticated player with a company ──────────────────

function setupAuthenticatedPlayer(page: import('@playwright/test').Page) {
  const player = makePlayer({
    onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
    companies: [
      {
        id: 'company-1',
        playerId: 'player-1',
        name: 'Test Empire',
        cash: 500000,
        foundedAtUtc: '2026-01-01T00:00:00Z',
        buildings: [
          {
            id: 'building-1',
            companyId: 'company-1',
            cityId: 'city-ba',
            type: 'FACTORY',
            name: 'Test Factory',
            latitude: 48.15,
            longitude: 17.11,
            level: 1,
            powerConsumption: 1,
            isForSale: false,
            builtAtUtc: '2026-01-01T00:00:00Z',
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
  return { state, player }
}

async function authenticateViaLocalStorage(
  page: import('@playwright/test').Page,
  playerId: string,
) {
  await page.addInitScript(
    (token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    },
    `token-${playerId}`,
  )
}

// ── Tests ────────────────────────────────────────────────────────────────────

test.describe('City Map View', () => {
  test('renders city map with building lots', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Should show the city name and lot count
    await expect(page.getByRole('heading', { name: /Bratislava/i })).toBeVisible()
    await expect(page.getByText(/4 lots/i)).toBeVisible()
  })

  test('shows lot details when clicking a lot in list view', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Click on a lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Should show lot details in the detail panel
    await expect(page.getByRole('heading', { name: 'Industrial Plot A1' })).toBeVisible()
    await expect(
      page.getByRole('complementary').getByText('Industrial Zone'),
    ).toBeVisible()
    await expect(
      page.getByRole('complementary').getByText(/96,900|96900/),
    ).toBeVisible()
    await expect(page.locator('.type-tag', { hasText: 'Factory' })).toBeVisible()
  })

  test('shows available status for unowned lots', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Click an unowned lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Should show Available badge
    await expect(page.locator('.status-badge.available')).toBeVisible()
  })

  test('can initiate purchase flow for available lot', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Select a lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Click Purchase Lot button
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Should show purchase form
    await expect(page.getByText('Building Type', { exact: true })).toBeVisible()
    await expect(page.getByText('Building Name', { exact: true })).toBeVisible()
  })

  test('completes purchase flow successfully', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Select a lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Click Purchase Lot button
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Fill in purchase form
    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.getByRole('complementary').locator('input[type="text"]').fill('My New Factory')

    // Click confirm
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Should show success message or the lot should become owned
    await expect(
      page.getByText(/purchased successfully/i).or(page.locator('.status-badge.yours')),
    ).toBeVisible()
  })

  test('shows owned lots with different status', async ({ page }) => {
    const lots: MockBuildingLot[] = makeDefaultBuildingLots()
    // Mark one lot as owned by another player's company
    lots[0]!.ownerCompanyId = 'other-company-id'
    lots[0]!.buildingId = 'other-building-id'
    lots[0]!.ownerCompany = { id: 'other-company-id', name: 'Other Corp' }
    lots[0]!.building = { id: 'other-building-id', name: 'Rival Factory', type: 'FACTORY' }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Click on the owned lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Should show "Owned" badge
    await expect(page.locator('.status-badge.owned')).toBeVisible()

    // Purchase button should NOT be present for owned lots
    await expect(page.getByRole('button', { name: /Purchase Lot/i })).toBeHidden()
  })

  test('handles already-purchased lot error gracefully', async ({ page }) => {
    // Set up a lot that becomes purchased between selection and purchase attempt
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Now mark the lot as owned before form submission to simulate race condition
    const lot = state.buildingLots.find((l) => l.id === 'lot-commercial-1')!
    lot.ownerCompanyId = 'other-company'
    lot.ownerCompany = { id: 'other-company', name: 'Rival Corp' }

    await page.locator('.building-type-card').filter({ hasText: /Sales Shop/i }).click()
    await page.locator('.form-input').fill('My Shop')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Should show actionable guidance — stale lot, not just a generic error
    await expect(page.getByText(/just claimed by another player/i)).toBeVisible()
    await expect(page.getByText(/select a different available lot/i)).toBeVisible()

    // Purchase form should be dismissed; lot should now show as Owned
    await expect(page.locator('.status-badge.owned')).toBeVisible()
  })

  test('filter toggle shows only available lots', async ({ page }) => {
    const lots: MockBuildingLot[] = makeDefaultBuildingLots()
    // Mark one lot as owned
    lots[0]!.ownerCompanyId = 'other-company-id'
    lots[0]!.ownerCompany = { id: 'other-company-id', name: 'Other Corp' }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Initially shows all 4 lots
    await expect(page.getByText(/4 lots/i)).toBeVisible()

    // Click "Available Only" filter
    await page.getByRole('button', { name: /Available Only/i }).click()

    // Should show 3 lots (one is owned)
    await expect(page.getByText(/3 lots/i)).toBeVisible()
  })

  test('unauthenticated user sees login required notice', async ({ page }) => {
    // No authentication setup
    setupMockApi(page)

    await page.goto('/city/city-ba')

    // Switch to list view
    await page.getByRole('button', { name: /List View/i }).click()

    // Select a lot
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Should show login required notice
    await expect(page.getByText(/Log in to purchase/i)).toBeVisible()
  })

  test('detail panel shows population index with contextual label', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view and select an industrial lot (low pop index = 0.65x in mock data)
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // The detail panel should show Population Index label and a numeric value
    const panel = page.getByRole('complementary')
    await expect(panel.getByText('Population Index', { exact: true })).toBeVisible()
    // Mock data has populationIndex: 0.65 → formatted as "0.65x"
    await expect(panel.getByText('0.65x')).toBeVisible()
    // Should show a tier label (Low for 0.65)
    await expect(panel.getByText('Low', { exact: true })).toBeVisible()
    // Should show the explanatory hint about why location matters
    await expect(panel.getByText(/stronger demand for retail/i)).toBeVisible()
  })

  test('commercial lot shows high population index label', async ({ page }) => {
    // Lot lot-commercial-1 has populationIndex: 1.42 in mock data → High
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const panel = page.getByRole('complementary')
    await expect(panel.getByText('Population Index', { exact: true })).toBeVisible()
    await expect(panel.getByText('1.42x')).toBeVisible()
    // 1.42 is in the "High" band (>= 1.3, < 1.8)
    await expect(panel.getByText('High', { exact: true })).toBeVisible()
  })

  test('dashboard links to city map', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-1',
              companyId: 'company-1',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'Test Factory',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
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
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/dashboard')

    // Should show city map link
    const cityMapLink = page.getByRole('link', { name: /City Map/i })
    await expect(cityMapLink).toBeVisible()

    // Click it and verify navigation
    await cityMapLink.click()
    await page.waitForURL(/\/city\//)
  })

  test('post-purchase shows setup building CTA and navigates to building detail', async ({
    page,
  }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')

    // Switch to list view and select an available lot
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Open purchase form
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Select building type via card picker, fill name, and submit
    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.getByRole('complementary').locator('input[type="text"]').fill('Victory Factory')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // After purchase the post-purchase banner should appear with "Set Up Your Building" CTA
    await expect(page.getByRole('link', { name: /Set Up Your Building/i })).toBeVisible()
    // Post-purchase guidance text should be visible
    await expect(page.getByText(/Building acquired/i)).toBeVisible()

    // Clicking it navigates to the building detail page
    await page.getByRole('link', { name: /Set Up Your Building/i }).click()
    await page.waitForURL(/\/building\//)
  })

  test('previously owned lot shows manage building link (not post-purchase banner)', async ({
    page,
  }) => {
    const lots = makeDefaultBuildingLots()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-owned',
              companyId: 'company-1',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'Old Factory',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              units: [],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
    // Mark the first lot as already owned by the player
    lots[0]!.ownerCompanyId = 'company-1'
    lots[0]!.buildingId = 'building-owned'
    lots[0]!.ownerCompany = { id: 'company-1', name: 'Test Empire' }
    lots[0]!.building = { id: 'building-owned', name: 'Old Factory', type: 'FACTORY' }

    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Already-owned lot shows "Manage Building" (not the post-purchase banner)
    await expect(page.getByRole('link', { name: /Manage Building/i })).toBeVisible()
    await expect(page.locator('.post-purchase-banner')).toBeHidden()
  })

  test('shows insufficient funds error when company cash is too low', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Broke Corp',
          cash: 0, // no money
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.locator('.form-input').fill('Bankrupt Factory')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Should show actionable insufficient funds guidance
    await expect(page.getByText(/does not have enough cash/i)).toBeVisible()
    await expect(page.getByText(/Review your finances/i)).toBeVisible()
  })

  // ── Raw Material & Placement Guidance ──────────────────────────────────────

  test('shows raw material panel for MINE-eligible lots with resource data', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Industrial Plot A1 has iron ore raw material in makeDefaultBuildingLots
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Raw material panel should be visible
    const panel = page.locator('[data-testid="raw-material-panel"]')
    await expect(panel).toBeVisible()

    // Should show the resource name
    await expect(panel.getByText(/Iron Ore/i)).toBeVisible()
    // Should show material quality
    await expect(panel.getByText(/72%/)).toBeVisible()
    // Should show material quantity
    await expect(panel.getByText(/18[,.]?000/)).toBeVisible()
  })

  test('hides raw material panel for non-extraction lots', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Commercial lot has no raw material
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    // Raw material panel should NOT be visible
    await expect(page.locator('[data-testid="raw-material-panel"]')).toBeHidden()
  })

  test('shows placement guidance panel for selected lot', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const guidancePanel = page.locator('[data-testid="placement-guidance-panel"]')
    await expect(guidancePanel).toBeVisible()

    // Should mention Factory guidance (Industrial Plot A1 has FACTORY,MINE suitableTypes)
    await expect(guidancePanel.locator('.guidance-building-type').filter({ hasText: /Factory/i })).toBeVisible()
    // Should mention Mine guidance
    await expect(guidancePanel.locator('.guidance-building-type').filter({ hasText: /Mine/i })).toBeVisible()
    // Should show transport cost note (scroll into view since panel may be long)
    const transportNote = guidancePanel.locator('.transport-cost-note')
    await transportNote.scrollIntoViewIfNeeded()
    await expect(transportNote).toBeVisible()
  })

  test('shows retail-specific placement guidance for commercial lots', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const guidancePanel = page.locator('[data-testid="placement-guidance-panel"]')
    await expect(guidancePanel).toBeVisible()
    // Should mention retail-specific guidance (SALES_SHOP)
    await expect(guidancePanel.getByText(/demand/i)).toBeVisible()
  })

  test('raw material quality badge shows correct label', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    // Customize lot to have excellent quality (0.85)
    const lots = makeDefaultBuildingLots()
    lots[0]!.materialQuality = 0.85
    lots[0]!.materialQuantity = 25000
    lots[0]!.resourceType = { id: 'res-gold', name: 'Gold', slug: 'gold' }

    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const panel = page.locator('[data-testid="raw-material-panel"]')
    await expect(panel).toBeVisible()
    // 85% quality = Excellent
    await expect(panel.getByText(/Excellent/i)).toBeVisible()
    await expect(panel.getByText(/Gold/i)).toBeVisible()
  })

  test('placement guidance mine hint mentions resource extraction strategy', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const guidancePanel = page.locator('[data-testid="placement-guidance-panel"]')
    await expect(guidancePanel).toBeVisible()
    // Mine guidance should mention exchange (transport cost vs exchange comparison)
    await expect(guidancePanel.getByText(/exchange/i)).toBeVisible()
  })

  test('narrow viewport still shows raw material and placement guidance', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // On narrow viewport the detail panel should still be scrollable/accessible
    const rawMaterialPanel = page.locator('[data-testid="raw-material-panel"]')
    const guidancePanel = page.locator('[data-testid="placement-guidance-panel"]')
    await expect(rawMaterialPanel).toBeVisible()
    await expect(guidancePanel).toBeVisible()
  })
})

// ── Invalid/stale selection paths ────────────────────────────────────────────

test.describe('City Map — invalid and stale selection paths', () => {
  test('shows error when trying to purchase lot with unsuitable building type', async ({
    page,
  }) => {
    // SALES_SHOP lot only allows SALES_SHOP,COMMERCIAL — not FACTORY
    const lots = makeDefaultBuildingLots()
    // Patch industrial lot to only accept MINE (so FACTORY is unsuitable)
    lots[0]!.suitableTypes = 'MINE'

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-unsuitable',
          playerId: 'player-1',
          name: 'Wrong Type Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // The card picker only shows suitable types — Mine card should be present, Factory should not
    await expect(page.locator('.building-type-card').filter({ hasText: /Mine/i })).toBeVisible()
    await expect(page.locator('.building-type-card').filter({ hasText: /Factory/i })).toHaveCount(0)
  })

  test('shows stale-lot error when lot was claimed by another player before purchase completes', async ({
    page,
  }) => {
    // Player selects a lot, another player claims it, then the first player submits purchase
    const lots = makeDefaultBuildingLots()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-stale',
          playerId: 'player-1',
          name: 'Slow Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Another player claims the lot between selection and purchase
    lots[0]!.ownerCompanyId = 'other-company-99'

    // Now submit the purchase
    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.locator('.form-input').fill('Too Late Factory')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Should show stale lot / already owned error message
    await expect(
      page.getByText(/just claimed by another player/i, { exact: false }),
    ).toBeVisible()
    // Should prompt the player to choose a different lot
    await expect(page.getByText(/select a different available lot/i, { exact: false })).toBeVisible()
  })

  test('lot detail shows district information to help player understand location context', async ({
    page,
  }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // District name is shown (Industrial Zone for lot-industrial-1)
    await expect(page.getByRole('complementary').getByText(/Industrial Zone/i)).toBeVisible()
  })

  test('lot detail shows price to help player understand land valuation', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Price is visible (Industrial lot has price=96900 with Iron Ore resource premium in mock data)
    const detailPanel = page.getByRole('complementary')
    await expect(detailPanel.getByText(/96,900|96900/)).toBeVisible()
  })

  test('lot detail shows appraised value and asking price separately', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // Industrial lot has basePrice=75000 and price=96900 (includes Iron Ore resource premium)
    const detailPanel = page.getByRole('complementary')
    // Appraised value label shows base land value
    await expect(detailPanel.getByText(/Appraised Value/i)).toBeVisible()
    // Both values are shown
    await expect(detailPanel.getByTestId('appraised-value')).toBeVisible()
    await expect(detailPanel.getByTestId('asking-price')).toBeVisible()
  })

  test('mine lot with raw material shows resource premium badge on asking price', async ({
    page,
  }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Industrial Plot A1 has Iron Ore (resourceType set) + price > basePrice
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const detailPanel = page.getByRole('complementary')
    // The resource premium badge is shown next to the asking price
    await expect(detailPanel.locator('.resource-premium-badge')).toBeVisible()
  })

  test('non-resource lot does NOT show resource premium badge', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // High Street Retail Space has no resourceType (null)
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const detailPanel = page.getByRole('complementary')
    // No resource premium badge for non-extraction lots
    await expect(detailPanel.locator('.resource-premium-badge')).toHaveCount(0)
  })

  test('population index hint explains retail demand to player', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const detailPanel = page.getByRole('complementary')
    // The population index educational hint text is shown
    await expect(
      detailPanel.getByText(/Higher index.*more nearby residents/i, { exact: false }),
    ).toBeVisible()
  })

  test('placement guidance panel shows transport cost note', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const detailPanel = page.getByRole('complementary')
    // Transport cost note is shown in the placement guidance panel
    await expect(detailPanel.getByTestId('placement-guidance-panel')).toBeVisible()
    await expect(
      detailPanel.getByText(/Distance from your other buildings/i, { exact: false }),
    ).toBeVisible()
  })

  test('lot detail shows GPS coordinates for logistics context', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Industrial Plot A1 has latitude: 48.152, longitude: 17.125 in mock data
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const detailPanel = page.getByRole('complementary')
    // GPS coordinates section is visible
    await expect(detailPanel.getByText('GPS Coordinates')).toBeVisible()
    // Coordinate value matches the lot's actual lat/lon from mock data
    const coordEl = detailPanel.getByTestId('lot-coordinates')
    await expect(coordEl).toBeVisible()
    await expect(coordEl).toContainText('48.15200°N')
    await expect(coordEl).toContainText('17.12500°E')
    // Logistics hint is shown
    await expect(
      detailPanel.getByText(/Coordinates are used for logistics/i, { exact: false }),
    ).toBeVisible()
  })
})

// ── Building type card picker ────────────────────────────────────────────────

test.describe('City Map — building type card picker', () => {
  test('purchase form shows card-based building type picker with icon and description', async ({
    page,
  }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Cards should be visible for each suitable type (Industrial Plot A1 has FACTORY,MINE)
    const factoryCard = page.locator('.building-type-card').filter({ hasText: /Factory/i })
    const mineCard = page.locator('.building-type-card').filter({ hasText: /Mine/i })
    await expect(factoryCard).toBeVisible()
    await expect(mineCard).toBeVisible()

    // Each card shows an icon and description
    await expect(factoryCard.locator('.card-type-icon')).toBeVisible()
    await expect(factoryCard.locator('.card-type-desc')).toBeVisible()
    await expect(mineCard.locator('.card-type-icon')).toBeVisible()
  })

  test('selecting a building type card marks it as selected and shows strategic guidance', async ({
    page,
  }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Click the Factory card
    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()

    // Factory card should be selected
    await expect(
      page.locator('.building-type-card.selected').filter({ hasText: /Factory/i }),
    ).toBeVisible()

    // Strategic guidance for factory should appear below the cards
    await expect(page.locator('.selected-type-guidance')).toBeVisible()
    await expect(page.locator('.selected-type-guidance')).toContainText(/industrial/i)
  })

  test('card picker only shows building types suitable for the lot', async ({ page }) => {
    // High Street Retail Space is suitable for SALES_SHOP and COMMERCIAL only
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // Should show Sales Shop card (suitable)
    await expect(
      page.locator('.building-type-card').filter({ hasText: /Sales Shop/i }),
    ).toBeVisible()

    // Should NOT show Factory card (not suitable for retail space)
    await expect(
      page.locator('.building-type-card').filter({ hasText: /Factory/i }),
    ).toHaveCount(0)
  })

  test('post-purchase banner shows factory-specific next-step guidance', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.locator('.form-input').fill('Supply Chain Factory')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Post-purchase banner should show factory-specific guidance
    const banner = page.locator('.post-purchase-banner')
    await expect(banner).toBeVisible()
    await expect(banner).toContainText(/Purchasing unit/i)
    await expect(banner).toContainText(/Manufacturing unit/i)
  })

  test('mobile viewport can complete purchase flow using card picker', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // On mobile the card picker should still be visible and usable
    const factoryCard = page.locator('.building-type-card').filter({ hasText: /Factory/i })
    await expect(factoryCard).toBeVisible()
    await factoryCard.click()
    await expect(factoryCard).toHaveClass(/selected/)

    await page.locator('.form-input').fill('Mobile Factory')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Success: post-purchase banner appears
    await expect(page.locator('.post-purchase-banner')).toBeVisible()
    await expect(page.getByRole('link', { name: /Set Up Your Building/i })).toBeVisible()
  })
})

test.describe('City Map — purchase cost summary and cash delta', () => {
  test('purchase form shows lot price and available cash before confirming', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Select an available lot (Industrial Plot A1 costs $96,900)
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // The purchase cost summary should be visible
    const summary = page.locator('[aria-label="Purchase cost summary"]')
    await expect(summary).toBeVisible()

    // Lot price is shown
    await expect(summary.getByText(/Lot price/i)).toBeVisible()
    // Current cash is shown (player has $500,000)
    await expect(summary.getByText(/Available cash/i)).toBeVisible()
    // Remaining after purchase is shown
    await expect(summary.getByText(/Remaining after purchase/i)).toBeVisible()
  })

  test('remaining cash after purchase shows positive value for affordable lot', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Industrial Plot A1 costs $96,900; player has $500,000 → remaining is $403,100
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    const summary = page.locator('[aria-label="Purchase cost summary"]')
    await expect(summary).toBeVisible()
    // Remaining cash should have cost-positive class (green)
    await expect(summary.locator('.cost-positive')).toBeVisible()
  })

  test('cash balance decreases after successful purchase', async ({ page }) => {
    const lots: MockBuildingLot[] = makeDefaultBuildingLots()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Test Empire',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
        {
          id: 'company-2',
          playerId: 'player-1',
          name: 'Second Empire',
          cash: 200000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player], buildingLots: lots })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    // Industrial Plot A1 costs $96,900
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // With 2 companies the company selector shows cash values
    // Select company-1 ($500,000)
    const companySelect = page.locator('.form-select')
    await expect(companySelect).toBeVisible()
    // The selector shows cash before purchase (contains $500,000)
    await expect(companySelect).toContainText('500,000')

    await page.locator('.building-type-card').filter({ hasText: /Factory/i }).click()
    await page.locator('.form-input').fill('Iron Works')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Success banner appears — confirms the purchase went through
    await expect(page.locator('.post-purchase-banner')).toBeVisible()

    // Now select a second available lot and open the purchase form to verify the company
    // cash has been reduced (500,000 - 96,900 = 403,100)
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await page.getByRole('button', { name: /Purchase Lot/i }).click()

    // The company selector should now reflect the updated cash (contains $403,100)
    await expect(companySelect).toBeVisible()
    await expect(companySelect).toContainText('403,100')
  })
})

test.describe('City Map — multi-city navigation and graceful empty state', () => {
  test('Prague city map shows city name with no lots available', async ({ page }) => {
    const { state, player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    // Clear lots so Prague has no building lots (not yet seeded in the game)
    state.buildingLots = []

    await page.goto('/city/city-pr')

    // City name should appear
    await expect(page.getByRole('heading', { name: /Prague/i })).toBeVisible()
    // No lots: shows 0 lots or "no lots" message
    await expect(
      page.getByText(/0 lots/i).or(page.getByText(/No building lots/i)),
    ).toBeVisible()
  })

  test('Vienna city map shows city name with no lots available', async ({ page }) => {
    const { state, player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    // Clear lots so Vienna has no building lots (not yet seeded in the game)
    state.buildingLots = []

    await page.goto('/city/city-vi')

    // City name should appear
    await expect(page.getByRole('heading', { name: /Vienna/i })).toBeVisible()
    // No lots: shows 0 lots or "no lots" message
    await expect(
      page.getByText(/0 lots/i).or(page.getByText(/No building lots/i)),
    ).toBeVisible()
  })
})

test.describe('City Map — strategic recommendation badge (decision support)', () => {
  test('resource-oriented lot shows "Resource-oriented" recommendation badge', async ({ page }) => {
    // Industrial Plot A1 has Iron Ore → should show resource-oriented label
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    const badge = page.locator('[data-testid="strategic-recommendation"]')
    await expect(badge).toBeVisible()
    await expect(badge).toContainText(/Resource-oriented/i)
  })

  test('high-population retail lot shows "Strong for retail demand" recommendation badge', async ({
    page,
  }) => {
    // High Street Retail Space has populationIndex 1.42 + SALES_SHOP → strong retail
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const badge = page.locator('[data-testid="strategic-recommendation"]')
    await expect(badge).toBeVisible()
    await expect(badge).toContainText(/Strong for retail demand/i)
  })

  test('recommendation badge changes when switching between lots (decision support comparison)', async ({
    page,
  }) => {
    // Players compare two lots and see how the recommendation changes —
    // this is the core "why location matters" decision-support feature.
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()

    // Select the industrial lot first
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    const badge = page.locator('[data-testid="strategic-recommendation"]')
    await expect(badge).toBeVisible()
    await expect(badge).toContainText(/Resource-oriented/i)

    // Switch to the commercial lot — recommendation must update immediately
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()
    await expect(badge).toContainText(/Strong for retail demand/i)

    // Switch back to industrial — should revert
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
    await expect(badge).toContainText(/Resource-oriented/i)
  })

  test('mobile viewport shows recommendation badge and population index decision-support', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /High Street Retail Space/i }).click()

    const panel = page.getByRole('complementary')
    // Recommendation badge visible on mobile
    await expect(panel.locator('[data-testid="strategic-recommendation"]')).toBeVisible()
    await expect(
      panel.locator('[data-testid="strategic-recommendation"]'),
    ).toContainText(/Strong for retail demand/i)
    // Population index educational hint visible on mobile
    await expect(panel.getByText(/Higher index.*more nearby residents/i)).toBeVisible()
  })

  test('residential lot shows "Balanced starter location" recommendation', async ({ page }) => {
    const { player } = setupAuthenticatedPlayer(page)
    await authenticateViaLocalStorage(page, player.id)

    await page.goto('/city/city-ba')
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Riverside Apartment Block/i }).click()

    const badge = page.locator('[data-testid="strategic-recommendation"]')
    await expect(badge).toBeVisible()
    await expect(badge).toContainText(/Balanced starter location/i)
  })
})
