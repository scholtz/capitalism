import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'
import type { MockBuilding, MockBuildingConfigurationPlan } from './helpers/mock-api'

async function authenticateViaLocalStorage(page: Parameters<typeof test>[0]['page'], token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

function makePendingBuildingPlan(
  buildingId: string,
  currentTick: number,
  ticksRequired = 3,
): MockBuildingConfigurationPlan {
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
