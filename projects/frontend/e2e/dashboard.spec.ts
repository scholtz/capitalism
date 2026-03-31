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
