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
      page.getByRole('complementary').getByText('$80,000'),
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
    await page.getByRole('complementary').locator('select').selectOption('FACTORY')
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

    await page.locator('.form-select').selectOption('SALES_SHOP')
    await page.locator('.form-input').fill('My Shop')
    await page.getByRole('button', { name: /Confirm Purchase/i }).click()

    // Should show error message
    await expect(page.getByText(/already been purchased/i)).toBeVisible()
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

    // Switch to list view and select an industrial lot (low pop index = 0.78x in mock data)
    await page.getByRole('button', { name: /List View/i }).click()
    await page.getByRole('button', { name: /Industrial Plot A1/i }).click()

    // The detail panel should show Population Index label and a numeric value
    const panel = page.getByRole('complementary')
    await expect(panel.getByText('Population Index', { exact: true })).toBeVisible()
    // Mock data has populationIndex: 0.78 → formatted as "0.78x"
    await expect(panel.getByText('0.78x')).toBeVisible()
    // Should show a tier label (Low for 0.78)
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
})
