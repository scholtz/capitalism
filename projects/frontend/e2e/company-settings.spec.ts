/**
 * Company Settings E2E tests.
 * Covers: settings page load, company rename, salary multiplier adjustment,
 * administration overhead visibility, multi-city salary display, and unauthorized access.
 * Implements issue: Implement company settings with per-city salaries and administration overhead visibility.
 */
import { test, expect, type Page } from '@playwright/test'
import { setupMockApi, makePlayer, makeDefaultCities } from './helpers/mock-api'
import type { MockCompany, MockState } from './helpers/mock-api'

/** The default initial game tick in the mock API (see mock-api.ts default gameState). */
const DEFAULT_MOCK_TICK = 42

/** Creates an authenticated session with a company for company settings tests. */
async function setupPlayerWithCompany(
  page: Page,
  companyOverrides: Partial<MockCompany> = {},
): Promise<{ state: MockState; player: ReturnType<typeof makePlayer>; company: MockCompany }> {
  const player = makePlayer({
    id: 'player-cs',
    email: 'cs@test.com',
    displayName: 'CS Player',
    onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
  })
  const state = setupMockApi(page, { players: [player], cities: makeDefaultCities() })

  const company: MockCompany = {
    id: 'company-cs-1',
    playerId: player.id,
    name: 'My Starter Corp',
    cash: 500_000,
    foundedAtUtc: '2026-01-01T00:00:00Z',
    // Set foundedAtTick to the current mock tick so ageFactor = 0 (no age yet)
    foundedAtTick: DEFAULT_MOCK_TICK,
    buildings: [],
    ...companyOverrides,
  }
  player.companies.push(company)

  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`

  await page.addInitScript((token) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
  }, state.currentToken)

  return { state, player, company }
}

test.describe('Company Settings – page load', () => {
  test('displays company name, overhead, and salary table for each city', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // Company name visible in header
    await expect(page.getByRole('heading', { name: company.name })).toBeVisible()

    // Operating overview section
    await expect(page.getByText('Operating Overview')).toBeVisible()
    await expect(page.getByText('Administration overhead', { exact: true })).toBeVisible()

    // Overhead shows a percentage
    await expect(page.locator('.overhead-value')).toBeVisible()

    // Salary table header columns
    await expect(page.getByRole('columnheader', { name: 'City' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Base wage / hour' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Salary multiplier' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Effective wage / hour' })).toBeVisible()

    // All three seeded cities appear as rows
    await expect(page.locator('.salary-table tbody tr')).toHaveCount(3)
    await expect(page.locator('.salary-table').getByText('Bratislava')).toBeVisible()
    await expect(page.locator('.salary-table').getByText('Prague')).toBeVisible()
    await expect(page.locator('.salary-table').getByText('Vienna')).toBeVisible()
  })

  test('shows low overhead badge for a brand-new company', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page, { foundedAtTick: 0 })
    await page.goto(`/company/${company.id}/settings`)

    // A brand-new company at tick 0 has no age → overhead = 0 → badge should be "Low"
    await expect(page.locator('.overhead-badge')).toBeVisible()
    await expect(page.locator('.overhead-low')).toBeVisible()
  })

  test('shows overhead explanation text', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    await expect(
      page.getByText('Administration overhead increases manufacturing labor costs'),
    ).toBeVisible()
  })

  test('shows overhead driver chips (age factor, scale factor)', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    await expect(page.locator('.driver-chip', { hasText: 'Age factor' })).toBeVisible()
    await expect(page.locator('.driver-chip', { hasText: 'Scale factor' })).toBeVisible()
  })

  test('shows salary impact hint text', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    await expect(page.getByText('Higher multipliers raise ongoing labor costs')).toBeVisible()
  })
})

test.describe('Company Settings – company rename', () => {
  test('can rename the company and see the new name after saving', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // Clear the name field and type a new name
    const nameInput = page.locator('.settings-field input[type="text"]')
    await nameInput.clear()
    await nameInput.fill('Renamed Empire Corp')

    await page.getByRole('button', { name: 'Save' }).click()

    // Success message should appear
    await expect(page.getByText('Company settings saved.')).toBeVisible()

    // After reload the heading reflects the new name returned by the mock
    // (mock re-reads company from state, which was updated by the mutation handler)
    await expect(page.getByRole('heading', { name: 'Renamed Empire Corp' })).toBeVisible()
  })

  test('shows save button and it is enabled when data is loaded', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    await expect(page.getByRole('button', { name: 'Save' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled()
  })
})

test.describe('Company Settings – salary multiplier', () => {
  test('displays default multiplier of 1.00 for each city', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // All three salary inputs should default to 1
    const salaryInputs = page.locator('.salary-input')
    await expect(salaryInputs).toHaveCount(3)

    for (let i = 0; i < 3; i++) {
      const value = await salaryInputs.nth(i).inputValue()
      expect(parseFloat(value)).toBe(1)
    }
  })

  test('can update salary multiplier for a city and save', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // Update the Bratislava salary multiplier (first row)
    const bratislavaInput = page.locator('tr', { hasText: 'Bratislava' }).locator('.salary-input')
    await bratislavaInput.fill('1.5')

    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Company settings saved.')).toBeVisible()

    // After save, the input value should reflect the updated multiplier
    await expect(bratislavaInput).toHaveValue('1.5')
  })

  test('effective salary updates when multiplier changes for a city', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // Bratislava baseSalaryPerManhour = 18; multiplier 1.5 → $27.00
    const bratislavaRow = page.locator('tr', { hasText: 'Bratislava' })
    const multiplierInput = bratislavaRow.locator('.salary-input')

    await multiplierInput.fill('1.5')
    // Effective salary cell (4th column) should update reactively
    // 18 * 1.5 = 27.00
    await expect(bratislavaRow.locator('td').nth(3)).toContainText('27')
  })

  test('shows three separate city salary rows for multi-city scenario', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page, {
      citySalaryMultipliers: { 'city-ba': 1.2, 'city-pr': 0.8, 'city-vi': 1.5 },
    })
    await page.goto(`/company/${company.id}/settings`)

    await expect(page.locator('.salary-table tbody tr')).toHaveCount(3)

    const bratislavaInput = page
      .locator('tr', { hasText: 'Bratislava' })
      .locator('.salary-input')
    const pragueInput = page.locator('tr', { hasText: 'Prague' }).locator('.salary-input')
    const viennaInput = page.locator('tr', { hasText: 'Vienna' }).locator('.salary-input')

    await expect(bratislavaInput).toHaveValue('1.2')
    await expect(pragueInput).toHaveValue('0.8')
    await expect(viennaInput).toHaveValue('1.5')
  })
})

test.describe('Company Settings – dashboard navigation', () => {
  test('dashboard has a settings link for each company', async ({ page }) => {
    const player = makePlayer({ id: 'player-dash', email: 'dash@test.com' })
    const state = setupMockApi(page, { players: [player], cities: makeDefaultCities() })

    const company: MockCompany = {
      id: 'company-dash-1',
      playerId: player.id,
      name: 'Dashboard Test Corp',
      cash: 500_000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      foundedAtTick: 0,
      buildings: [],
    }
    player.companies.push(company)
    player.onboardingCompletedAtUtc = '2026-01-01T00:00:00Z'
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
    }, state.currentToken)

    await page.goto('/dashboard')

    // The settings link for this company must be present
    await expect(page.getByRole('link', { name: 'Settings' }).first()).toBeVisible()
  })

  test('settings link navigates to the company settings page', async ({ page }) => {
    const player = makePlayer({ id: 'player-nav', email: 'nav@test.com' })
    const state = setupMockApi(page, { players: [player], cities: makeDefaultCities() })

    const company: MockCompany = {
      id: 'company-nav-1',
      playerId: player.id,
      name: 'Nav Test Corp',
      cash: 500_000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      foundedAtTick: 0,
      buildings: [],
    }
    player.companies.push(company)
    player.onboardingCompletedAtUtc = '2026-01-01T00:00:00Z'
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
    }, state.currentToken)

    await page.goto('/dashboard')

    const settingsLink = page.getByRole('link', { name: 'Settings' }).first()
    await settingsLink.click()

    await expect(page).toHaveURL(`/company/${company.id}/settings`)
  })
})

test.describe('Company Settings – unauthorized access', () => {
  test('shows not found message when company does not belong to the authenticated player', async ({
    page,
  }) => {
    // Player 1 owns company-1; player 2 is authenticated
    const owner = makePlayer({ id: 'player-owner', email: 'owner@test.com' })
    const intruder = makePlayer({
      id: 'player-intruder',
      email: 'intruder@test.com',
      displayName: 'Intruder',
    })
    const state = setupMockApi(page, {
      players: [owner, intruder],
      cities: makeDefaultCities(),
    })

    const ownerCompany: MockCompany = {
      id: 'company-owner-1',
      playerId: owner.id,
      name: "Owner's Secret Corp",
      cash: 1_000_000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      foundedAtTick: 0,
      buildings: [],
    }
    owner.companies.push(ownerCompany)

    // Authenticate as the intruder
    state.currentUserId = intruder.id
    state.currentToken = `token-${intruder.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
    }, state.currentToken)

    // Navigate to owner's company settings as intruder
    await page.goto(`/company/${ownerCompany.id}/settings`)

    // Should show a not found / access denied message
    await expect(
      page.getByText('Company settings not found or access denied'),
    ).toBeVisible()
  })
})

test.describe('Company Settings – overhead formula visibility', () => {
  test('shows age factor and scale factor in driver chips', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    // Driver chips show 0.0% for a company with no age (foundedAtTick = currentTick)
    const ageChip = page.locator('.driver-chip', { hasText: 'Age factor' })
    await expect(ageChip).toBeVisible()
    await expect(ageChip).toContainText('0.0%')

    const scaleChip = page.locator('.driver-chip', { hasText: 'Scale factor' })
    await expect(scaleChip).toBeVisible()
    // Scale factor depends on relative asset values; just confirm it is visible and shows a %
    await expect(scaleChip).toContainText('%')
  })

  test('overhead percentage is displayed for a company with non-zero overhead', async ({ page }) => {
    const player = makePlayer({ id: 'player-oh', email: 'oh@test.com' })
    const state = setupMockApi(page, { players: [player], cities: makeDefaultCities() })

    // Simulate a mature, large company: many ticks old so ageFactor > 0
    const ticksPerYear = 52 // approximate value used in mock
    const company: MockCompany = {
      id: 'company-oh-1',
      playerId: player.id,
      name: 'Mature Corp',
      cash: 2_000_000,
      foundedAtUtc: '2024-01-01T00:00:00Z',
      foundedAtTick: 0,
      buildings: [],
    }
    player.companies.push(company)
    // Advance the game tick so the company is 2+ years old → ageFactor = 1
    state.gameState.currentTick = ticksPerYear * 2 + 10

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
    }, state.currentToken)

    await page.goto(`/company/${company.id}/settings`)

    // The overhead should be non-zero; confirm % appears
    const overheadValue = page.locator('.overhead-value')
    await expect(overheadValue).toBeVisible()
    const text = await overheadValue.textContent()
    expect(text).toMatch(/\d+\.\d+%/)
  })
})

test.describe('Company Settings – back navigation', () => {
  test('back button navigates to dashboard', async ({ page }) => {
    const { company } = await setupPlayerWithCompany(page)
    await page.goto(`/company/${company.id}/settings`)

    await page.getByRole('button', { name: /back/i }).click()
    await expect(page).toHaveURL('/dashboard')
  })
})

test.describe('Company Settings – regression: onboarding-created company', () => {
  test('a company created via onboarding flow can open the settings page', async ({ page }) => {
    const player = makePlayer({
      id: 'player-ob',
      email: 'ob@test.com',
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
    })
    const state = setupMockApi(page, { players: [player], cities: makeDefaultCities() })

    const obCompany: MockCompany = {
      id: 'company-ob-1',
      playerId: player.id,
      name: 'Onboarding Corp',
      cash: 50_000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      foundedAtTick: 1,
      buildings: [],
    }
    player.companies.push(obCompany)

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
    }, state.currentToken)

    await page.goto(`/company/${obCompany.id}/settings`)

    await expect(page.getByRole('heading', { name: 'Onboarding Corp' })).toBeVisible()
    await expect(page.getByText('Administration overhead', { exact: true })).toBeVisible()
    await expect(page.locator('.salary-table tbody tr')).toHaveCount(3)
  })
})
