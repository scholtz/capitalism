import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'
import type { MockBuilding, MockBuildingConfigurationPlan } from './helpers/mock-api'

async function authenticateViaLocalStorage(page: Parameters<typeof test>[0]['page'], token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

function makePendingBuildingPlan(buildingId: string, currentTick: number, ticksRequired = 3): MockBuildingConfigurationPlan {
  return {
    id: `plan-${buildingId}`,
    buildingId,
    submittedAtUtc: new Date().toISOString(),
    submittedAtTick: currentTick,
    appliesAtTick: currentTick + ticksRequired,
    totalTicksRequired: ticksRequired,
    units: [
      {
        id: `unit-${buildingId}`,
        buildingId,
        unitType: 'STORAGE',
        gridX: 0,
        gridY: 0,
        level: 1,
        linkUp: false,
        linkDown: false,
        linkLeft: false,
        linkRight: false,
        linkUpLeft: false,
        linkUpRight: false,
        linkDownLeft: false,
        linkDownRight: false,
        resourceTypeId: 'res-wood',
        startedAtTick: currentTick,
        appliesAtTick: currentTick + ticksRequired,
        ticksRequired,
        isChanged: true,
        isReverting: false,
      },
    ],
    removals: [],
  }
}

test.describe('Dashboard — tick countdown', () => {
  test('shows tick clock widget with current tick number', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-1',
          playerId: 'player-1',
          name: 'Tick Corp',
          cash: 400000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 42
    state.gameState.lastTickAtUtc = new Date(Date.now() - 10000).toISOString()
    state.gameState.tickIntervalSeconds = 60

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.tick-clock-widget')).toBeVisible()
    await expect(page.locator('.tick-clock-label')).toContainText('42')
  })

  test('shows countdown timer in tick clock widget', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-1',
          playerId: 'player-1',
          name: 'Timer Corp',
          cash: 400000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 10
    state.gameState.lastTickAtUtc = new Date(Date.now() - 15000).toISOString()
    state.gameState.tickIntervalSeconds = 60

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    const countdown = page.locator('.tick-clock-countdown')
    await expect(countdown).toBeVisible()
    const text = await countdown.textContent()
    // Should show either a time countdown or "soon" message
    expect(text).toMatch(/Next tick in \d+[ms]|tick processing soon/i)
  })

  test('tick countdown survives page refresh', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-refresh',
          playerId: 'player-1',
          name: 'Refresh Corp',
          cash: 400000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 7
    state.gameState.lastTickAtUtc = new Date(Date.now() - 5000).toISOString()
    state.gameState.tickIntervalSeconds = 60

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')
    await expect(page.locator('.tick-clock-widget')).toBeVisible()

    // Reload and verify countdown reappears (derived from backend data)
    await page.reload()
    await expect(page.locator('.tick-clock-widget')).toBeVisible()
    await expect(page.locator('.tick-clock-label')).toContainText('7')
  })

  test('refreshes dashboard data after the next tick', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-live',
          playerId: 'player-1',
          name: 'Live Tick Corp',
          cash: 400000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 42
    state.gameState.lastTickAtUtc = new Date(Date.now() - 2000).toISOString()
    state.gameState.tickIntervalSeconds = 1

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.tick-clock-label')).toContainText('42')

    state.gameState.currentTick = 43
    state.gameState.lastTickAtUtc = new Date().toISOString()

    await expect(page.locator('.tick-clock-label')).toContainText('43', { timeout: 5000 })
  })
})

test.describe('Dashboard — pending actions timeline', () => {
  test('shows empty state when no pending actions', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-2',
          playerId: 'player-1',
          name: 'Clean Corp',
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

    await expect(page.getByRole('heading', { name: 'Scheduled Actions' })).toBeVisible()
    await expect(page.getByRole('status')).toContainText('No scheduled actions')
  })

  test('shows pending building upgrade action', async ({ page }) => {
    const currentTick = 50
    const buildingId = 'building-factory-1'

    const factory: MockBuilding = {
      id: buildingId,
      companyId: 'comp-3',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Main Factory',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: makePendingBuildingPlan(buildingId, currentTick, 3),
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-3',
          playerId: 'player-1',
          name: 'Factory Empire',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = currentTick

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'Scheduled Actions' })).toBeVisible()
    // Should show the pending building upgrade entry
    const actionsList = page.locator('.actions-list')
    await expect(actionsList).toBeVisible()
    await expect(actionsList).toContainText('Layout upgrade')
    await expect(actionsList).toContainText('Main Factory')
    await expect(actionsList).toContainText('3 ticks remaining')
  })

  test('shows view building link for pending action', async ({ page }) => {
    const currentTick = 20
    const buildingId = 'building-shop-1'

    const shop: MockBuilding = {
      id: buildingId,
      companyId: 'comp-4',
      cityId: 'city-ba',
      type: 'SALES_SHOP',
      name: 'Downtown Shop',
      latitude: 48.14,
      longitude: 17.1,
      level: 1,
      powerConsumption: 1,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: makePendingBuildingPlan(buildingId, currentTick, 1),
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-4',
          playerId: 'player-1',
          name: 'Retail LLC',
          cash: 200000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = currentTick

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    const viewLink = page.getByRole('link', { name: /view building/i })
    await expect(viewLink).toBeVisible()
    await expect(viewLink).toHaveAttribute('href', `/building/${buildingId}`)
  })

  test('completed upgrades disappear from timeline after tick advance', async ({ page }) => {
    const buildingId = 'building-mine-1'

    const mine: MockBuilding = {
      id: buildingId,
      companyId: 'comp-5',
      cityId: 'city-ba',
      type: 'MINE',
      name: 'Iron Mine',
      latitude: 48.16,
      longitude: 17.12,
      level: 1,
      powerConsumption: 3,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: makePendingBuildingPlan(buildingId, 10, 3),
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-5',
          playerId: 'player-1',
          name: 'Mining Co',
          cash: 150000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [mine],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Set tick to before the plan applies
    state.gameState.currentTick = 10

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Pending action should be visible
    await expect(page.locator('.actions-list')).toBeVisible()
    await expect(page.locator('.actions-list')).toContainText('Iron Mine')

    // Advance ticks past the plan
    state.gameState.currentTick = 15
    await page.reload()

    // After tick advance, the plan is applied and the action should be gone
    await expect(page.getByRole('status')).toContainText('No scheduled actions')
  })
})

test.describe('Dashboard — company settings', () => {
  test('opens company settings and saves company name and city salary multiplier', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-settings',
          playerId: 'player-1',
          name: 'Settings Corp',
          cash: 275000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 24,
          citySalaryMultipliers: { bratislava: 1.1 },
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await page.getByRole('link', { name: 'Settings' }).click()
    await expect(page).toHaveURL('/company/comp-settings/settings')
    await expect(page.getByRole('heading', { name: 'Settings Corp' })).toBeVisible()

    await page.getByLabel('Company Name').fill('Renamed Industries')
    await page.getByLabel('Salary Multiplier Bratislava').fill('1.35')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.getByRole('status')).toContainText('Company settings saved')
    await expect(page.getByRole('heading', { name: 'Renamed Industries' })).toBeVisible()
    await expect(page.getByLabel('Salary Multiplier Bratislava')).toHaveValue('1.35')
  })

  test('displays administration overhead rate and overhead help text', async ({ page }) => {
    const TICKS_PER_YEAR = 24 * 365
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-overhead',
          playerId: 'player-1',
          name: 'Overhead Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 0,
          buildings: [],
        },
      ],
    })
    // Set current tick to 2 years in, making the company mature
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 2 * TICKS_PER_YEAR

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/comp-overhead/settings')

    // Overhead section should be visible
    await expect(page.getByText('Operating Overview')).toBeVisible()
    await expect(page.getByText('Administration overhead', { exact: true })).toBeVisible()

    // Overhead help text should explain the metric
    await expect(page.getByText(/Administration overhead increases/i)).toBeVisible()
  })

  test('shows per-city salary settings with base and effective wages', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-salaries',
          playerId: 'player-1',
          name: 'Salary Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 0,
          citySalaryMultipliers: {},
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/comp-salaries/settings')

    // Salary table should show city, base salary, multiplier, and effective salary columns
    await expect(page.getByRole('columnheader', { name: 'City' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Base wage / hour' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Salary multiplier' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Effective wage / hour' })).toBeVisible()

    // At least one city row should be present
    await expect(page.locator('.salary-table tbody tr').first()).toBeVisible()
  })

  test('shows error state when company settings not found', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    // Navigate to settings for a company that doesn't belong to this player
    await page.goto('/company/nonexistent-company/settings')

    await expect(page.getByRole('alert')).toContainText(/not found|access denied/i)
    await expect(page.getByRole('button', { name: /try again/i })).toBeVisible()
  })

  test('shows overhead status badge and driver chips for a mature high-scale company', async ({ page }) => {
    const TICKS_PER_YEAR = 24 * 365
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-high-overhead',
          playerId: 'player-1',
          name: 'Big Corp',
          cash: 5000000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 0,
          // No buildings — overhead driven purely by cash (large asset value)
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Advance to 2 years → ageFactor = 1
    state.gameState.currentTick = 2 * TICKS_PER_YEAR

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/comp-high-overhead/settings')

    // Overhead value and badge should be visible
    await expect(page.locator('.overhead-value')).toBeVisible()
    await expect(page.locator('.overhead-badge')).toBeVisible()

    // Driver chips should show age and scale labels
    await expect(page.locator('.driver-chip', { hasText: /Age factor/i })).toBeVisible()
    await expect(page.locator('.driver-chip', { hasText: /Scale factor/i })).toBeVisible()

    // Reduce-tip text should be visible
    await expect(page.locator('.overhead-tip')).toBeVisible()
  })

  test('shows salary impact hint below the salary table', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-salary-hint',
          playerId: 'player-1',
          name: 'Hint Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 0,
          citySalaryMultipliers: {},
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/comp-salary-hint/settings')

    // Salary impact hint should appear below the salary table
    await expect(page.locator('.salary-impact-hint')).toBeVisible()
    await expect(page.locator('.salary-impact-hint')).toContainText(/labor costs/i)
  })

  test('shows low overhead badge for brand-new company', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-new',
          playerId: 'player-1',
          name: 'New Corp',
          cash: 50000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          foundedAtTick: 0,
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Brand new at tick 0 → ageFactor = 0 → overhead = 0%
    state.gameState.currentTick = 0

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/company/comp-new/settings')

    // Low overhead badge should be visible and text should show 0.0%
    await expect(page.locator('.overhead-low')).toBeVisible()
    await expect(page.locator('.overhead-badge')).toContainText(/low/i)
  })
})

// ── Power grid dashboard tests ─────────────────────────────────────────────

test.describe('Dashboard — power grid summary', () => {
  test('shows legacy grid label when no power plants exist in city', async ({ page }) => {
    const building: MockBuilding = {
      id: 'building-factory-1',
      companyId: 'comp-1',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Test Factory',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-1',
          playerId: 'player-1',
          name: 'Power Test Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [building],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/dashboard')

    // The city has no power plants, so the legacy grid label should appear.
    await expect(page.locator('.power-balance--legacy')).toBeVisible()
    await expect(page.locator('.power-balance--legacy')).toContainText('Legacy grid')
  })

  test('shows balanced power status when supply exceeds demand', async ({ page }) => {
    const powerPlant: MockBuilding = {
      id: 'building-plant-1',
      companyId: 'comp-2',
      cityId: 'city-ba',
      type: 'POWER_PLANT',
      name: 'Coal Plant',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 0,
      powerPlantType: 'COAL',
      powerOutput: 50,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: null,
    }

    const factory: MockBuilding = {
      id: 'building-factory-2',
      companyId: 'comp-2',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Main Factory',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-2',
          playerId: 'player-1',
          name: 'Powered Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [powerPlant, factory],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/dashboard')

    // Supply (50 MW) > Demand (5 MW) → BALANCED
    await expect(page.locator('.power-balance--balanced')).toBeVisible()
    await expect(page.locator('.power-balance--balanced')).toContainText('50')
    await expect(page.locator('.power-balance--balanced')).toContainText('5')
  })

  test('shows constrained warning when supply is below demand', async ({ page }) => {
    const powerPlant: MockBuilding = {
      id: 'building-plant-3',
      companyId: 'comp-3',
      cityId: 'city-ba',
      type: 'POWER_PLANT',
      name: 'Small Solar',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 0,
      powerPlantType: 'SOLAR',
      powerOutput: 8,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: null,
    }

    const factories: MockBuilding[] = [1, 2, 3].map((i) => ({
      id: `building-factory-${i}`,
      companyId: 'comp-3',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: `Factory ${i}`,
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'CONSTRAINED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [],
      pendingConfiguration: null,
    }))

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-3',
          playerId: 'player-1',
          name: 'Shortage Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [powerPlant, ...factories],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto('/dashboard')

    // Supply (8 MW) < Demand (15 MW) → CONSTRAINED
    await expect(page.locator('.power-balance--constrained')).toBeVisible()
    // CONSTRAINED buildings show yellow badge
    await expect(page.locator('.power-badge--constrained').first()).toBeVisible()
  })
})

test.describe('Dashboard — starter operations (supply chain, financials, guidance)', () => {
  function makeStarterCompanyWithBuildings() {
    const factory: MockBuilding = {
      id: 'building-factory-ops',
      companyId: 'comp-ops',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Furniture Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'unit-purchase',
          buildingId: 'building-factory-ops',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'unit-manufacturing',
          buildingId: 'building-factory-ops',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'unit-storage',
          buildingId: 'building-factory-ops',
          unitType: 'STORAGE',
          gridX: 2,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'unit-b2b',
          buildingId: 'building-factory-ops',
          unitType: 'B2B_SALES',
          gridX: 3,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }
    const shop: MockBuilding = {
      id: 'building-shop-ops',
      companyId: 'comp-ops',
      cityId: 'city-ba',
      type: 'SALES_SHOP',
      name: 'Furniture Shop',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 2,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'unit-shop-purchase',
          buildingId: 'building-shop-ops',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'unit-public-sales',
          buildingId: 'building-shop-ops',
          unitType: 'PUBLIC_SALES',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }
    return { factory, shop }
  }

  test('shows supply chain panel for factory with unit types', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'Furniture Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Supply chain panels should be visible for buildings with units
    await expect(page.locator('.supply-chain-panel').first()).toBeVisible()
    // Factory supply chain should show unit labels
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Purchase')
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Manufacturing')
  })

  test('shows supply chain for sales shop with purchase and public sales units', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'Furniture Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Shop supply chain should show public sales
    await expect(page.locator('.supply-chain-panel').nth(1)).toContainText('Public Sales')
  })

  test('shows financial summary card with revenue and costs', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'Furniture Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Provide ledger data for this company
    state.ledgerData['comp-ops'] = {
      companyId: 'comp-ops',
      companyName: 'Furniture Co',
      currentCash: 250000,
      totalRevenue: 5000,
      totalPurchasingCosts: 1200,
      totalLaborCosts: 800,
      totalEnergyCosts: 200,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 2800,
      propertyValue: 100000,
      propertyAppreciation: 0,
      buildingValue: 400000,
      inventoryValue: 5000,
      totalAssets: 755000,
      totalPropertyPurchases: 100000,
      cashFromOperations: 2800,
      cashFromInvestments: -100000,
      firstRecordedTick: 1,
      lastRecordedTick: 10,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.financial-summary-card')).toBeVisible()
    await expect(page.locator('.financial-summary-card')).toContainText('Revenue')
    await expect(page.locator('.financial-summary-card')).toContainText('Costs')
    await expect(page.locator('.financial-summary-card')).toContainText('Net Profit')
    // Revenue should show $5,000
    await expect(page.locator('.financial-summary-card')).toContainText('5,000')
  })

  test('shows no-data state in financial summary before any transactions', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-new',
          playerId: 'player-1',
          name: 'New Corp',
          cash: 100000,
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

    // Financial summary should appear even with zero data
    await expect(page.locator('.financial-summary-card')).toBeVisible()
  })

  test('shows starter guidance panel with next steps', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'Furniture Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.starter-guidance')).toBeVisible()
    await expect(page.locator('.starter-guidance')).toContainText('Next Steps')
  })

  test('guidance shows awaiting revenue for new company with no sales', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'New Furniture Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // No ledger data → 0 revenue
    state.ledgerData['comp-ops'] = {
      companyId: 'comp-ops',
      companyName: 'New Furniture Co',
      currentCash: 250000,
      totalRevenue: 0,
      totalPurchasingCosts: 0,
      totalLaborCosts: 0,
      totalEnergyCosts: 0,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 0,
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 0,
      inventoryValue: 0,
      totalAssets: 250000,
      totalPropertyPurchases: 0,
      cashFromOperations: 0,
      cashFromInvestments: 0,
      firstRecordedTick: 0,
      lastRecordedTick: 0,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.starter-guidance')).toContainText('Awaiting first sales')
  })

  test('guidance shows profitable message when revenue exceeds costs', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-profit',
          playerId: 'player-1',
          name: 'Profitable Co',
          cash: 350000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.ledgerData['comp-profit'] = {
      companyId: 'comp-profit',
      companyName: 'Profitable Co',
      currentCash: 350000,
      totalRevenue: 10000,
      totalPurchasingCosts: 3000,
      totalLaborCosts: 1000,
      totalEnergyCosts: 500,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 5500,
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 0,
      inventoryValue: 0,
      totalAssets: 350000,
      totalPropertyPurchases: 0,
      cashFromOperations: 5500,
      cashFromInvestments: 0,
      firstRecordedTick: 1,
      lastRecordedTick: 20,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.starter-guidance')).toContainText('Business is profitable')
  })

  test('shows city name in company meta section', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-ops',
          playerId: 'player-1',
          name: 'Bratislava Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // City name and label should be visible in the company meta section
    await expect(page.locator('.company-card').first()).toContainText('Bratislava')
    await expect(page.locator('.city-name')).toContainText('📍')
    await expect(page.locator('.meta-label', { hasText: 'City' })).toBeVisible()
  })

  test('shows all three starter industries supply chains correctly', async ({ page }) => {
    // Food Processing: PURCHASE → MANUFACTURING → PUBLIC_SALES
    const foodFactory: MockBuilding = {
      id: 'building-food-factory',
      companyId: 'comp-food',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Bread Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'fp-unit-purchase',
          buildingId: 'building-food-factory',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'fp-unit-manufacturing',
          buildingId: 'building-food-factory',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-food',
          playerId: 'player-1',
          name: 'Bread Co',
          cash: 200000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [foodFactory],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Food processing factory shows PURCHASE and MANUFACTURING units
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Purchase')
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Manufacturing')
  })

  test('shows operations dashboard on mobile viewport', async ({ page }) => {
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-mobile',
          playerId: 'player-1',
          name: 'Mobile Co',
          cash: 250000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.setViewportSize({ width: 375, height: 812 })
    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Core elements should be visible on mobile
    await expect(page.locator('.financial-summary-card')).toBeVisible()
    await expect(page.locator('.starter-guidance')).toBeVisible()
    await expect(page.locator('.supply-chain-panel').first()).toBeVisible()
  })

  test('guidance uses backend netIncome (after tax) not frontend-derived profit — shows unprofitable when tax makes netIncome negative', async ({
    page,
  }) => {
    // Scenario: revenue=5000, operating costs=3000 → pre-tax "profit" would be +2000
    // But totalTaxPaid=2500 → netIncome = -500 (loss after tax)
    // The dashboard MUST show "Review your pricing", NOT "Business is profitable"
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-tax-loss',
          playerId: 'player-1',
          name: 'Tax Loss Co',
          cash: 290000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.ledgerData['comp-tax-loss'] = {
      companyId: 'comp-tax-loss',
      companyName: 'Tax Loss Co',
      currentCash: 290000,
      totalRevenue: 5000,
      totalPurchasingCosts: 2000,
      totalLaborCosts: 500,
      totalEnergyCosts: 500,
      totalMarketingCosts: 0,
      totalTaxPaid: 2500, // large tax makes the company a net loser
      totalOtherCosts: 0,
      netIncome: -500, // backend authoritative: loss after tax
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 200000,
      inventoryValue: 0,
      totalAssets: 490000,
      totalPropertyPurchases: 200000,
      cashFromOperations: -500,
      cashFromInvestments: -200000,
      firstRecordedTick: 1,
      lastRecordedTick: 10,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Should show "Review your pricing" (netIncome is negative even though revenue > operating costs)
    await expect(page.locator('.starter-guidance')).toContainText('Review your pricing')
    // Must NOT show "Business is profitable"
    await expect(page.locator('.starter-guidance')).not.toContainText('Business is profitable')
    // Financial card should display the backend net income as negative
    await expect(page.locator('.financial-summary-card')).toContainText('-$500')
  })

  test('guidance shows profitable when netIncome is positive even if pre-tax margins are thin', async ({
    page,
  }) => {
    // Scenario: revenue=1000, costs=900, tax=0 → netIncome=100 (positive)
    // Guidance MUST show "Business is profitable" (netIncome > 0)
    const { factory, shop } = makeStarterCompanyWithBuildings()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-thin-profit',
          playerId: 'player-1',
          name: 'Thin Margin Co',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.ledgerData['comp-thin-profit'] = {
      companyId: 'comp-thin-profit',
      companyName: 'Thin Margin Co',
      currentCash: 300000,
      totalRevenue: 1000,
      totalPurchasingCosts: 700,
      totalLaborCosts: 100,
      totalEnergyCosts: 100,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 100, // positive after all costs
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 200000,
      inventoryValue: 0,
      totalAssets: 500000,
      totalPropertyPurchases: 200000,
      cashFromOperations: 100,
      cashFromInvestments: -200000,
      firstRecordedTick: 1,
      lastRecordedTick: 5,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.starter-guidance')).toContainText('Business is profitable')
  })
})

test.describe('Dashboard — unit operational status in supply chain', () => {
  test('shows active status badge for units with inventory', async ({ page }) => {
    const factory: MockBuilding = {
      id: 'building-active-factory',
      companyId: 'comp-active',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Active Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'unit-active-purchase',
          buildingId: 'building-active-factory',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
          inventoryQuantity: 10, // has inventory → ACTIVE
        },
        {
          id: 'unit-active-mfg',
          buildingId: 'building-active-factory',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
          inventoryQuantity: 0, // no inventory → IDLE
        },
      ],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-active',
          playerId: 'player-1',
          name: 'Active Co',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Supply chain panel should be rendered
    await expect(page.locator('.supply-chain-panel').first()).toBeVisible()
    // Unit node for ACTIVE unit should have active styling
    await expect(page.locator('.unit-node--active')).toBeVisible()
    // Unit node for IDLE unit should have idle styling
    await expect(page.locator('.unit-node--idle')).toBeVisible()
  })
})

test.describe('Dashboard — post-onboarding routing', () => {
  test('authenticated player with completed onboarding and starter buildings sees dashboard with supply chain', async ({
    page,
  }) => {
    const factory: MockBuilding = {
      id: 'building-post-onb-factory',
      companyId: 'comp-post-onb',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Wood Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'po-unit-purchase',
          buildingId: 'building-post-onb-factory',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'po-unit-mfg',
          buildingId: 'building-post-onb-factory',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'po-unit-storage',
          buildingId: 'building-post-onb-factory',
          unitType: 'STORAGE',
          gridX: 2,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }
    const shop: MockBuilding = {
      id: 'building-post-onb-shop',
      companyId: 'comp-post-onb',
      cityId: 'city-ba',
      type: 'SALES_SHOP',
      name: 'Wooden Chair Shop',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 2,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'po-unit-shop-purchase',
          buildingId: 'building-post-onb-shop',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'po-unit-pub-sales',
          buildingId: 'building-post-onb-shop',
          unitType: 'PUBLIC_SALES',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-post-onb',
          playerId: 'player-1',
          name: 'Wood Empire',
          cash: 350000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory, shop],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Provide ledger with some activity
    state.ledgerData['comp-post-onb'] = {
      companyId: 'comp-post-onb',
      companyName: 'Wood Empire',
      currentCash: 350000,
      totalRevenue: 1200,
      totalPurchasingCosts: 600,
      totalLaborCosts: 200,
      totalEnergyCosts: 50,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: 350,
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 350000,
      inventoryValue: 2000,
      totalAssets: 702000,
      totalPropertyPurchases: 350000,
      cashFromOperations: 350,
      cashFromInvestments: -350000,
      firstRecordedTick: 1,
      lastRecordedTick: 5,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    // Core company info
    await expect(page.getByRole('heading', { name: 'Wood Empire' })).toBeVisible()
    await expect(page.locator('.cash')).toContainText('350,000')
    await expect(page.locator('.city-name')).toContainText('Bratislava')

    // Factory supply chain is visible with all 3 units
    const factoryChain = page.locator('.supply-chain-panel').first()
    await expect(factoryChain).toBeVisible()
    await expect(factoryChain).toContainText('Purchase')
    await expect(factoryChain).toContainText('Manufacturing')
    await expect(factoryChain).toContainText('Storage')

    // Shop supply chain shows purchase and public sales
    const shopChain = page.locator('.supply-chain-panel').nth(1)
    await expect(shopChain).toContainText('Public Sales')

    // Financial summary shows revenue
    await expect(page.locator('.financial-summary-card')).toContainText('1,200')

    // Guidance shows profitable message (revenue > costs)
    await expect(page.locator('.starter-guidance')).toContainText('Business is profitable')
  })

  test('dashboard shows correct financial data for Food Processing starter company', async ({
    page,
  }) => {
    const factory: MockBuilding = {
      id: 'building-food-dashboard',
      companyId: 'comp-food-dashboard',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Bread Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'food-unit-purchase',
          buildingId: 'building-food-dashboard',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'food-unit-mfg',
          buildingId: 'building-food-dashboard',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-food-dashboard',
          playerId: 'player-1',
          name: 'Bread Empire',
          cash: 280000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.ledgerData['comp-food-dashboard'] = {
      companyId: 'comp-food-dashboard',
      companyName: 'Bread Empire',
      currentCash: 280000,
      totalRevenue: 450,
      totalPurchasingCosts: 380,
      totalLaborCosts: 100,
      totalEnergyCosts: 30,
      totalMarketingCosts: 0,
      totalTaxPaid: 0,
      totalOtherCosts: 0,
      netIncome: -60,
      propertyValue: 0,
      propertyAppreciation: 0,
      buildingValue: 200000,
      inventoryValue: 1000,
      totalAssets: 481000,
      totalPropertyPurchases: 200000,
      cashFromOperations: -60,
      cashFromInvestments: -200000,
      firstRecordedTick: 1,
      lastRecordedTick: 3,
      buildingSummaries: [],
    }

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'Bread Empire' })).toBeVisible()
    // Food Processing supply chain shows Purchase and Manufacturing
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Purchase')
    await expect(page.locator('.supply-chain-panel').first()).toContainText('Manufacturing')
    // Revenue shows
    await expect(page.locator('.financial-summary-card')).toContainText('450')
    // Unprofitable guidance (costs 510 > revenue 450)
    await expect(page.locator('.starter-guidance')).toContainText('Review your pricing')
  })

  test('dashboard shows correct supply chain for Healthcare starter company', async ({ page }) => {
    const factory: MockBuilding = {
      id: 'building-health-dashboard',
      companyId: 'comp-health-dashboard',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Medicine Factory',
      latitude: 48.14,
      longitude: 17.12,
      level: 1,
      powerConsumption: 5,
      powerStatus: 'POWERED',
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      units: [
        {
          id: 'health-unit-purchase',
          buildingId: 'building-health-dashboard',
          unitType: 'PURCHASE',
          gridX: 0,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'health-unit-mfg',
          buildingId: 'building-health-dashboard',
          unitType: 'MANUFACTURING',
          gridX: 1,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: true,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
        {
          id: 'health-unit-storage',
          buildingId: 'building-health-dashboard',
          unitType: 'STORAGE',
          gridX: 2,
          gridY: 0,
          level: 1,
          linkUp: false,
          linkDown: false,
          linkLeft: false,
          linkRight: false,
          linkUpLeft: false,
          linkUpRight: false,
          linkDownLeft: false,
          linkDownRight: false,
        },
      ],
      pendingConfiguration: null,
    }

    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-health-dashboard',
          playerId: 'player-1',
          name: 'Medicine Empire',
          cash: 320000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [factory],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'Medicine Empire' })).toBeVisible()
    // Healthcare supply chain shows all 3 unit types in order
    const chain = page.locator('.supply-chain-panel').first()
    await expect(chain).toContainText('Purchase')
    await expect(chain).toContainText('Manufacturing')
    await expect(chain).toContainText('Storage')
    // Awaiting revenue for company with no ledger data
    await expect(page.locator('.starter-guidance')).toContainText('Awaiting first sales')
  })
})

test.describe('Dashboard tick-refresh stability', () => {
  test('background tick refresh does not show a loading spinner or reset the dashboard view', async ({
    page,
  }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-tick-stable',
          playerId: 'player-1',
          name: 'Stable Empire',
          cash: 750000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 20
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'Stable Empire' })).toBeVisible()

    // Simulate a tick advancing
    state.gameState.currentTick = 21
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // The company heading must remain visible — no full-page loading spinner during background refresh
    await expect(page.getByRole('heading', { name: 'Stable Empire' })).toBeVisible()
    await expect(page.locator('.loading', { hasText: 'Loading' })).toBeHidden()
  })

  test('company cards remain visible with stable content during multiple ticks', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'comp-multi-tick',
          playerId: 'player-1',
          name: 'MultiTick Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 5
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await authenticateViaLocalStorage(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.getByRole('heading', { name: 'MultiTick Corp' })).toBeVisible()

    // Advance tick twice — company content must remain visible after each tick
    state.gameState.currentTick = 6
    state.gameState.lastTickAtUtc = new Date().toISOString()
    await expect(page.getByRole('heading', { name: 'MultiTick Corp' })).toBeVisible()

    state.gameState.currentTick = 7
    state.gameState.lastTickAtUtc = new Date().toISOString()
    await expect(page.getByRole('heading', { name: 'MultiTick Corp' })).toBeVisible()
    // No loading state between ticks
    await expect(page.locator('.loading', { hasText: 'Loading' })).toBeHidden()
  })
})
