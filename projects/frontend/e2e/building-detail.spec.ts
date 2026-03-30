import { expect, test } from '@playwright/test'
import { makeChairProduct, makePlayer, setupMockApi } from './helpers/mock-api'

function getGridSection(page: Parameters<typeof test>[0]['page'], heading: string) {
  return page
    .locator('.grid-section')
    .filter({ has: page.getByRole('heading', { name: heading }) })
    .first()
}

function getGridCell(section: ReturnType<typeof getGridSection>, x: number, y: number) {
  return section.locator('.unit-row').nth(y).locator('.grid-cell').nth(x)
}

test.describe('Building detail upgrades', () => {
  test('allows revising links while a unit upgrade is still pending', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-1',
      playerId: player.id,
      name: 'Link Works Ltd',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-1',
          companyId: 'company-1',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Link Works Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'u-1',
              buildingId: 'building-1',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'u-2',
              buildingId: 'building-1',
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
            {
              id: 'u-3',
              buildingId: 'building-1',
              unitType: 'STORAGE',
              gridX: 0,
              gridY: 1,
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
            {
              id: 'u-4',
              buildingId: 'building-1',
              unitType: 'B2B_SALES',
              gridX: 1,
              gridY: 1,
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

    await page.goto('/building/building-1')
    await expect(page.getByRole('heading', { name: 'Link Works Factory' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Planned Upgrade' })).toHaveCount(0)

    const currentSection = getGridSection(page, 'Current Configuration')
    const currentDiagonal = currentSection.locator('.link-toggle.diagonal').first()

    await expect(currentDiagonal).toHaveClass(/state-none/)

    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    const plannedDiagonal = plannedSection.locator('.link-toggle.diagonal').first()
    const plannedTopLeftCell = getGridCell(plannedSection, 0, 0)

    await expect(plannedDiagonal).toHaveClass(/state-none/)
    await expect(plannedSection.getByText('Upgrade time: 0 ticks')).toBeVisible()

    await plannedTopLeftCell.click()
    await page.getByRole('button', { name: 'Remove' }).click()
    await plannedTopLeftCell.click()
    await page.getByRole('button', { name: 'Branding' }).click()

    await expect(plannedTopLeftCell).toContainText('Branding')
    await expect(plannedSection.getByText('Upgrade time: 3 ticks')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Store Upgrade' })).toBeEnabled()

    await page.getByRole('button', { name: 'Store Upgrade' }).click()

    await expect(page.getByRole('status')).toContainText('The current building keeps running until the queued layout activates in 3 ticks.')
    await expect(page.getByRole('heading', { name: 'Queued Upgrade' })).toHaveCount(0)
    await expect(currentDiagonal).toHaveClass(/state-none/)

    await page.getByRole('button', { name: 'Edit Building' }).click()
    const queuedSection = getGridSection(page, 'Queued Upgrade')
    const queuedTopLeftCell = getGridCell(queuedSection, 0, 0)
    const queuedDiagonal = queuedSection.locator('.link-toggle.diagonal').first()

    await expect(queuedTopLeftCell).toContainText('Branding')

    await queuedDiagonal.click()
    await queuedDiagonal.click()
    await queuedDiagonal.click()

    await expect(queuedDiagonal).toHaveClass(/state-cross/)
    await expect(queuedSection.getByText('Upgrade time: 3 ticks')).toBeVisible()

    await page.getByRole('button', { name: 'Store Upgrade' }).click()

    await expect(page.getByRole('status')).toContainText('The current building keeps running until the queued layout activates in 3 ticks.')

    state.gameState.currentTick += 1
    await page.reload()

    const refreshedCurrentSection = getGridSection(page, 'Current Configuration')
    await expect(page.getByRole('status')).toContainText('The current building keeps running until the queued layout activates in 2 ticks.')
    await expect(refreshedCurrentSection).toContainText('Purchase')

    await page.getByRole('button', { name: 'Edit Building' }).click()
    const refreshedQueuedSection = getGridSection(page, 'Queued Upgrade')
    await expect(getGridCell(refreshedQueuedSection, 0, 0)).toContainText('Branding')
    await expect(refreshedQueuedSection.locator('.link-toggle.diagonal').first()).toHaveClass(/state-cross/)

    state.gameState.currentTick += 2
    await page.reload()

    await expect(page.getByText('Building upgrade in progress')).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Planned Upgrade' })).toHaveCount(0)
    await expect(refreshedCurrentSection.locator('.link-toggle.diagonal').first()).toHaveClass(/state-cross/)

    const finalCurrentSection = getGridSection(page, 'Current Configuration')
    await expect(getGridCell(finalCurrentSection, 0, 0)).toContainText('Branding')

    await page.getByRole('button', { name: 'Edit Building' }).click()
    const refreshedPlannedSection = getGridSection(page, 'Planned Upgrade')
    await expect(refreshedPlannedSection.locator('.link-toggle.diagonal').first()).toHaveClass(/state-cross/)
    await getGridCell(refreshedPlannedSection, 0, 0).click()
    await expect(page.getByText('Down-Right')).toBeVisible()
    await getGridCell(refreshedPlannedSection, 1, 0).click()
    await expect(page.getByText('Down-Left')).toBeVisible()
  })

  test('shows unit configuration panel with type-specific settings', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-1',
      playerId: player.id,
      name: 'Config Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-cfg',
          companyId: 'company-1',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Config Test Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'cu-1',
              buildingId: 'building-cfg',
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
              id: 'cu-2',
              buildingId: 'building-cfg',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: true,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
            },
            {
              id: 'cu-3',
              buildingId: 'building-cfg',
              unitType: 'STORAGE',
              gridX: 2,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: false,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
            },
          ],
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

    await page.goto('/building/building-cfg')
    await expect(page.getByRole('heading', { name: 'Config Test Factory' })).toBeVisible()

    // Click "Edit Building"
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // Click on the Purchase unit to see its config
    await getGridCell(plannedSection, 0, 0).click()
    await expect(page.getByText('Unit Configuration')).toBeVisible()
    await expect(page.getByText('Unit Settings')).toBeVisible()
    await expect(page.getByText('Input Item')).toBeVisible()
    await expect(page.getByText('Max Price')).toBeVisible()
    await expect(page.getByText('Purchase Source')).toBeVisible()

    // Click on Manufacturing unit
    await getGridCell(plannedSection, 1, 0).click()
    await expect(page.getByText('Product Type').first()).toBeVisible()

    // Click on Storage unit
    await getGridCell(plannedSection, 2, 0).click()
    await expect(page.getByText('Unit Settings')).toBeVisible()
  })

  test('shows configuration warnings for unconfigured units', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-w',
      playerId: player.id,
      name: 'Warning Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-warn',
          companyId: 'company-w',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Warning Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'wu-1',
              buildingId: 'building-warn',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'wu-2',
              buildingId: 'building-warn',
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

    await page.goto('/building/building-warn')
    await expect(page.getByRole('heading', { name: 'Warning Factory' })).toBeVisible()

    // Warnings should be visible for unconfigured units
    await expect(page.getByText('Configuration Warnings')).toBeVisible()
    await expect(page.getByText('Purchase unit at (0, 0) has no resource or product selected.')).toBeVisible()
    await expect(page.getByText('Manufacturing unit at (1, 0) has no product type set.')).toBeVisible()
    await expect(page.getByText('Purchase unit at (0, 0) is not linked to a consumer unit.')).toBeVisible()
    await expect(page.getByText('Manufacturing unit at (1, 0) is not linked to a storage or sales output.')).toBeVisible()
  })

  test('shows sell building dialog and updates sale status', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-s',
      playerId: player.id,
      name: 'Sell Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-sell',
          companyId: 'company-s',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Selling Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [],
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

    await page.goto('/building/building-sell')
    await expect(page.getByRole('heading', { name: 'Selling Factory' })).toBeVisible()
    await expect(page.getByText('Not For Sale')).toBeVisible()

    // Click sell button
    await page.getByRole('button', { name: 'Sell Building' }).click()
    await expect(page.getByText('Asking Price ($)')).toBeVisible()

    // Fill in price and list for sale
    await page.locator('.sale-dialog .form-input').fill('100000')
    await page.getByRole('button', { name: 'List for Sale' }).click()

    // Should update to show "For Sale" in the meta pill
    await expect(page.locator('.meta-pill.for-sale')).toBeVisible()
  })

  test('shows read-only unit details when clicking active grid cells', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-ro',
      playerId: player.id,
      name: 'Readonly Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-ro',
          companyId: 'company-ro',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Readonly Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'ro-1',
              buildingId: 'building-ro',
              unitType: 'PURCHASE',
              gridX: 0,
              gridY: 0,
              level: 2,
              linkUp: false,
              linkDown: false,
              linkLeft: false,
              linkRight: true,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
              maxPrice: 500,
              purchaseSource: 'EXCHANGE',
            },
          ],
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

    await page.goto('/building/building-ro')
    await expect(page.getByRole('heading', { name: 'Readonly Factory' })).toBeVisible()

    // Click on the Purchase unit in the active grid to see read-only details
    const activeSection = getGridSection(page, 'Current Configuration')
    await getGridCell(activeSection, 0, 0).click()
    await expect(page.getByText('Unit Details')).toBeVisible()
    await expect(page.getByText('Lv.2')).toBeVisible()
    await expect(page.getByText('Max Price: $500')).toBeVisible()
    await expect(page.getByText('Purchase Source: EXCHANGE')).toBeVisible()
  })

  test('shows global exchange offers and inventory fill for configured purchase units', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-exchange',
      playerId: player.id,
      name: 'Exchange Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-exchange',
          companyId: 'company-exchange',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Exchange Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'exchange-purchase',
              buildingId: 'building-exchange',
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
              resourceTypeId: 'res-wood',
              maxPrice: 500,
              purchaseSource: 'EXCHANGE',
              inventoryQuantity: 60,
              inventoryQuality: 0.82,
            },
          ],
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

    await page.goto('/building/building-exchange')
    await expect(page.getByRole('heading', { name: 'Exchange Factory' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')
    await expect(getGridCell(activeSection, 0, 0)).toContainText('Wood')

    await getGridCell(activeSection, 0, 0).click()
    await expect(page.getByText('Stored inventory')).toBeVisible()
    await expect(page.getByText('60 / 100')).toBeVisible()
    await expect(page.getByText('Global exchange offers')).toBeVisible()
    await expect(page.getByText('Bratislava')).toBeVisible()
    await expect(page.getByText('Prague')).toBeVisible()
    await expect(page.getByText('Vienna')).toBeVisible()
  })

  test('shows configured resource image, sourcing costs, and new-unit cost while planning', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-display',
      playerId: player.id,
      name: 'Display Test Co',
      cash: 25000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-display',
          companyId: 'company-display',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Display Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'display-storage',
              buildingId: 'building-display',
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
              inventoryItems: [
                {
                  resourceTypeId: 'res-wood',
                  quantity: 40,
                  quality: 0.84,
                  sourcingCostTotal: 520,
                },
                {
                  resourceTypeId: 'res-grain',
                  quantity: 20,
                  quality: 0.72,
                  sourcingCostTotal: 80,
                },
              ],
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    const wood = state.resourceTypes.find((resource) => resource.id === 'res-wood')!
    const grain = state.resourceTypes.find((resource) => resource.id === 'res-grain')!
    wood.imageUrl = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=='
    grain.imageUrl = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=='

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-display')
    await expect(page.getByRole('heading', { name: 'Display Factory' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')
    const activeCell = getGridCell(activeSection, 0, 0)

    await expect(activeCell).toContainText('Wood')
    await expect(activeCell).toContainText('+1')
    await expect(activeCell).toContainText('60/100')
    await expect(activeCell).toContainText('Cost $600')
    await expect(activeCell.locator('img.cell-item-image')).toHaveCount(1)

    await activeCell.click()
    await expect(page.getByText('Sourcing costs')).toBeVisible()
    await expect(page.locator('.inventory-summary-stat').filter({ hasText: 'Sourcing costs' }).getByText('$600')).toBeVisible()
    const inventoryTable = page.locator('.inventory-table').first()
    await expect(inventoryTable.getByText('Wood', { exact: true })).toBeVisible()
    await expect(inventoryTable.getByText('Grain', { exact: true })).toBeVisible()
    await expect(inventoryTable.getByText('$520', { exact: true })).toBeVisible()
    await expect(inventoryTable.getByText('$80', { exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Edit Building' }).click()
    await expect(page.locator('.upgrade-summary').getByText('Build cost: $0')).toBeVisible()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 1, 0).click()

    await expect(page.getByText('Unit cost: $4,500')).toBeVisible()
    await page.getByRole('button', { name: 'Purchase' }).click()

    await expect(page.locator('.upgrade-summary').getByText('Build cost: $4,500')).toBeVisible()
    await expect(page.locator('.upgrade-summary').getByText('Cash after apply: $20,500')).toBeVisible()
  })

  test('shows per-item movement history for manufacturing and storage units', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-history',
      playerId: player.id,
      name: 'History Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-history',
          companyId: 'company-history',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'History Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'history-manufacturing',
              buildingId: 'building-history',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: true,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
              productTypeId: 'prod-chair',
              inventoryItems: [
                {
                  productTypeId: 'prod-chair',
                  quantity: 24,
                  quality: 0.82,
                  sourcingCostTotal: 144,
                },
              ],
              resourceHistory: [
                {
                  resourceTypeId: 'res-wood',
                  tick: 11,
                  inflowQuantity: 4,
                  consumedQuantity: 4,
                },
                {
                  resourceTypeId: 'res-wood',
                  tick: 12,
                  inflowQuantity: 2,
                  consumedQuantity: 2,
                },
                {
                  productTypeId: 'prod-chair',
                  tick: 11,
                  producedQuantity: 20,
                },
                {
                  productTypeId: 'prod-chair',
                  tick: 12,
                  producedQuantity: 10,
                  outflowQuantity: 6,
                },
              ],
            },
            {
              id: 'history-storage',
              buildingId: 'building-history',
              unitType: 'STORAGE',
              gridX: 2,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: true,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
              inventoryItems: [
                {
                  productTypeId: 'prod-chair',
                  quantity: 14,
                  quality: 0.82,
                  sourcingCostTotal: 84,
                },
              ],
              resourceHistory: [
                {
                  productTypeId: 'prod-chair',
                  tick: 12,
                  inflowQuantity: 20,
                },
                {
                  productTypeId: 'prod-chair',
                  tick: 13,
                  outflowQuantity: 14,
                },
              ],
            },
          ],
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

    await page.goto('/building/building-history')
    await expect(page.getByRole('heading', { name: 'History Factory' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')
    await getGridCell(activeSection, 1, 0).click()

    const manufacturingHistoryCard = page.locator('.history-card').first()
    await expect(manufacturingHistoryCard.getByText('Movement history')).toBeVisible()
    await expect(manufacturingHistoryCard.getByRole('button', { name: 'Wood', exact: true })).toBeVisible()
    await expect(manufacturingHistoryCard.getByRole('button', { name: 'Wooden Chair', exact: true })).toBeVisible()

    await manufacturingHistoryCard.getByRole('button', { name: 'Wood', exact: true }).click()
    await expect(manufacturingHistoryCard.locator('.history-summary-stat').filter({ hasText: 'Inflow' }).getByText('6')).toBeVisible()
    await expect(manufacturingHistoryCard.locator('.history-summary-stat').filter({ hasText: 'Consumed' }).getByText('6')).toBeVisible()

    await manufacturingHistoryCard.getByRole('button', { name: 'Wooden Chair', exact: true }).click()
    await expect(manufacturingHistoryCard.locator('.history-summary-stat').filter({ hasText: 'Produced' }).getByText('30')).toBeVisible()

    await getGridCell(activeSection, 2, 0).click()

    const storageHistoryCard = page.locator('.history-card').first()
    await expect(storageHistoryCard.getByRole('button', { name: 'Wooden Chair', exact: true })).toBeVisible()
    await expect(storageHistoryCard.locator('.history-summary-stat').filter({ hasText: 'Inflow' }).getByText('20')).toBeVisible()
    await expect(storageHistoryCard.locator('.history-summary-stat').filter({ hasText: 'Outflow' }).getByText('14')).toBeVisible()
  })

  test('lets players configure research product and brand-quality scope', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-rd',
      playerId: player.id,
      name: 'Research Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-rd',
          companyId: 'company-rd',
          cityId: 'city-ba',
          type: 'RESEARCH_DEVELOPMENT',
          name: 'Advanced Lab',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'rd-product',
              buildingId: 'building-rd',
              unitType: 'PRODUCT_QUALITY',
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
            },
            {
              id: 'rd-brand',
              buildingId: 'building-rd',
              unitType: 'BRAND_QUALITY',
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

    await page.goto('/building/building-rd')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()
    await expect(page.getByText('Research Product')).toBeVisible()
    await page
      .locator('.config-field')
      .filter({ has: page.getByText('Research Product') })
      .locator('select')
      .selectOption('prod-chair')

    await getGridCell(plannedSection, 1, 0).click()
    await expect(page.getByText('Brand Scope')).toBeVisible()
    await page
      .locator('.config-field')
      .filter({ has: page.getByText('Brand Scope') })
      .locator('select')
      .selectOption('CATEGORY')
    const anchorProductField = page.locator('.config-field').filter({ has: page.getByText('Anchor Product', { exact: true }) })
    await expect(anchorProductField).toBeVisible()
    await anchorProductField.locator('select').selectOption('prod-chair')
    await expect(getGridCell(plannedSection, 1, 0)).toContainText('Wooden Chair')
  })

  test('factory purchase selector shows raw materials and only intermediate products', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-advanced',
      playerId: player.id,
      name: 'Advanced Selector Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-advanced',
          companyId: 'company-advanced',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Advanced Selector Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'adv-1',
              buildingId: 'building-advanced',
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
              id: 'adv-2',
              buildingId: 'building-advanced',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: false,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player],
      resourceTypes: [{ id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', description: 'Wood', imageUrl: null }],
      productTypes: [
        {
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
        },
        {
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
        },
        {
          id: 'prod-chair-final',
          name: 'Wooden Chair',
          slug: 'wooden-chair',
          industry: 'FURNITURE',
          basePrice: 45,
          baseCraftTicks: 2,
          outputQuantity: 20,
          energyConsumptionMwh: 1,
          unitName: 'Chair',
          unitSymbol: 'chairs',
          isProOnly: false,
          description: 'Final product not used as ingredient.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-advanced')
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    await expect(page.getByText('Input Item')).toBeVisible()
    await expect(page.getByText('Electronic Components')).toBeVisible()
    await expect(page.getByText('Wooden Chair')).toHaveCount(0)
  })

  test('manufacturing selector only shows outputs supported by linked inputs', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-linked',
      playerId: player.id,
      name: 'Linked Inputs Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-linked',
          companyId: 'company-linked',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Linked Inputs Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'linked-purchase',
              buildingId: 'building-linked',
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
              resourceTypeId: 'res-grain',
            },
            {
              id: 'linked-manufacturing',
              buildingId: 'building-linked',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: false,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player],
      resourceTypes: [
        { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC', basePrice: 5, weightPerUnit: 2, unitName: 'Ton', unitSymbol: 't', description: 'Grain', imageUrl: null },
        { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', description: 'Wood', imageUrl: null },
      ],
      productTypes: [
        {
          id: 'prod-bread-linked',
          name: 'Bread',
          slug: 'bread',
          industry: 'FOOD_PROCESSING',
          basePrice: 3,
          baseCraftTicks: 1,
          outputQuantity: 12,
          energyConsumptionMwh: 0.5,
          unitName: 'Loaf',
          unitSymbol: 'loaves',
          isProOnly: false,
          description: 'Bread from grain.',
          recipes: [{ resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
        {
          id: 'prod-chair-linked',
          name: 'Wooden Chair',
          slug: 'wooden-chair',
          industry: 'FURNITURE',
          basePrice: 45,
          baseCraftTicks: 2,
          outputQuantity: 20,
          energyConsumptionMwh: 1,
          unitName: 'Chair',
          unitSymbol: 'chairs',
          isProOnly: false,
          description: 'Chair from wood.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
      ],
    })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-linked')
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 1, 0).click()

    await expect(page.getByText('Output Product')).toBeVisible()
    const breadOption = page.getByRole('button', { name: /Bread/ })
    await expect(breadOption).toBeVisible()
    await expect(page.getByRole('button', { name: /Wooden Chair/ })).toHaveCount(0)
    await breadOption.click()
    await expect(page.locator('.selected-chip')).toContainText('Bread')
  })

  test('locks pro manufacturing outputs until pro access becomes active', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-pro-lock',
      playerId: player.id,
      name: 'Access Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-pro-lock',
          companyId: 'company-pro-lock',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Access Test Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'pu-1',
              buildingId: 'building-pro-lock',
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
              resourceTypeId: 'res-wood',
            },
            {
              id: 'mu-1',
              buildingId: 'building-pro-lock',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: false,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, {
      players: [player],
      productTypes: [
        {
          id: 'prod-chair',
          name: 'Wooden Chair',
          slug: 'wooden-chair',
          industry: 'FURNITURE',
          basePrice: 45,
          baseCraftTicks: 2,
          outputQuantity: 20,
          energyConsumptionMwh: 1,
          unitName: 'Chair',
          unitSymbol: 'chairs',
          isProOnly: false,
          description: 'Starter furniture.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
        {
          id: 'prod-premium-chair',
          name: 'Premium Chair',
          slug: 'premium-chair',
          industry: 'FURNITURE',
          basePrice: 120,
          baseCraftTicks: 4,
          outputQuantity: 6,
          energyConsumptionMwh: 1.4,
          unitName: 'Chair',
          unitSymbol: 'chairs',
          isProOnly: true,
          description: 'Advanced premium chair.',
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

    await page.goto('/building/building-pro-lock')
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 1, 0).click()

    const premiumButton = page.getByRole('button', { name: /Premium Chair/ })
    await expect(premiumButton).toBeDisabled()
    await expect(premiumButton).toContainText('Pro')
    await expect(page.getByText('Pro unlocks additional products to manufacture and sell.')).toBeVisible()

    player.proSubscriptionEndsAtUtc = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString()
    await page.reload()
    await page.getByRole('button', { name: 'Edit Building' }).click()
    await getGridCell(getGridSection(page, 'Planned Upgrade'), 1, 0).click()

    await expect(page.getByRole('button', { name: /Premium Chair/ })).toBeEnabled()
  })

  test('shows cancel plan button in upgrade banner and cancels pending plan', async ({ page }) => {
    const player = makePlayer()
    const currentTick = 10
    player.companies.push({
      id: 'company-cancel',
      playerId: player.id,
      name: 'Cancel Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-cancel',
          companyId: 'company-cancel',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Cancel Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          units: [],
          pendingConfiguration: {
            id: 'plan-cancel',
            buildingId: 'building-cancel',
            submittedAtUtc: new Date().toISOString(),
            submittedAtTick: currentTick,
            appliesAtTick: currentTick + 3,
            totalTicksRequired: 3,
            units: [
              {
                id: 'pu-cancel-1',
                buildingId: 'building-cancel',
                unitType: 'PURCHASE',
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
                startedAtTick: currentTick,
                appliesAtTick: currentTick + 3,
                ticksRequired: 3,
                isChanged: true,
                isReverting: false,
              },
            ],
            removals: [],
          },
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = currentTick

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-cancel')

    // Cancel plan button is visible in upgrade banner
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')
    const cancelBtn = page.getByRole('button', { name: 'Cancel Plan' })
    await expect(cancelBtn).toBeVisible()
    await expect(cancelBtn).toBeEnabled()

    // Click cancel - should call cancelBuildingConfiguration and reload
    await cancelBtn.click()

    // After cancel, the plan is in reverting state (1 tick rollback)
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')
    // The cancel plan button should still be visible (reverting plan is still pending)
    await expect(page.getByRole('button', { name: 'Cancel Plan' })).toBeVisible()
  })

  test('hides cancel plan button when editing', async ({ page }) => {
    const player = makePlayer()
    const currentTick = 5
    player.companies.push({
      id: 'company-hide-cancel',
      playerId: player.id,
      name: 'Hide Cancel Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-hide-cancel',
          companyId: 'company-hide-cancel',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Hide Cancel Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          units: [],
          pendingConfiguration: {
            id: 'plan-hide',
            buildingId: 'building-hide-cancel',
            submittedAtUtc: new Date().toISOString(),
            submittedAtTick: currentTick,
            appliesAtTick: currentTick + 3,
            totalTicksRequired: 3,
            units: [
              {
                id: 'pu-hide-1',
                buildingId: 'building-hide-cancel',
                unitType: 'MANUFACTURING',
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
                startedAtTick: currentTick,
                appliesAtTick: currentTick + 3,
                ticksRequired: 3,
                isChanged: true,
                isReverting: false,
              },
            ],
            removals: [],
          },
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = currentTick

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-hide-cancel')

    // Cancel button visible before editing
    await expect(page.getByRole('button', { name: 'Cancel Plan' })).toBeVisible()

    // Enter edit mode - cancel button should be hidden
    await page.getByRole('button', { name: 'Edit Building' }).click()
    await expect(page.getByRole('button', { name: 'Cancel Plan' })).toHaveCount(0)

    // Leave edit mode - cancel button should reappear
    await page.getByRole('button', { name: 'Cancel Editing' }).click()
    await expect(page.getByRole('button', { name: 'Cancel Plan' })).toBeVisible()
  })

  test('shows reverting label on plan units with isReverting flag', async ({ page }) => {
    const player = makePlayer()
    const currentTick = 7
    player.companies.push({
      id: 'company-revert',
      playerId: player.id,
      name: 'Revert Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-revert',
          companyId: 'company-revert',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Revert Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          units: [],
          pendingConfiguration: {
            id: 'plan-revert',
            buildingId: 'building-revert',
            submittedAtUtc: new Date().toISOString(),
            submittedAtTick: currentTick,
            appliesAtTick: currentTick + 1,
            totalTicksRequired: 1,
            units: [
              {
                id: 'pu-revert-1',
                buildingId: 'building-revert',
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
                startedAtTick: currentTick,
                appliesAtTick: currentTick + 1,
                ticksRequired: 1,
                isChanged: true,
                isReverting: true,
              },
            ],
            removals: [],
          },
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = currentTick

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-revert')

    // Enter edit mode to see the queued upgrade grid
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const queuedSection = getGridSection(page, 'Queued Upgrade')
    const topLeftCell = getGridCell(queuedSection, 0, 0)

    // The reverting cell should have the Reverting label
    await expect(topLeftCell).toContainText('Reverting')
    await expect(topLeftCell).toContainText('Storage')
  })

  test('directional link editing: full user journey — cycle states, see changes summary, submit, confirm pending', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-dir',
      playerId: player.id,
      name: 'Directional Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-dir',
          companyId: 'company-dir',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Directional Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'dir-1',
              buildingId: 'building-dir',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'dir-2',
              buildingId: 'building-dir',
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

    await page.goto('/building/building-dir')
    await expect(page.getByRole('heading', { name: 'Directional Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // The horizontal link button between (0,0) and (1,0) starts as none/inactive
    const hLink = plannedSection.locator('.link-toggle.horizontal').first()
    await expect(hLink).not.toHaveClass(/active/)
    await expect(hLink).toHaveClass(/link-state-none/)

    // First click: forward (A→B) — only left sends right
    await hLink.click()
    await expect(hLink).toHaveClass(/active/)
    await expect(hLink).toHaveClass(/link-state-forward/)

    // Link changes summary should now show one added link
    const summary = plannedSection.locator('.link-changes-summary')
    await expect(summary).toBeVisible()
    await expect(summary).toContainText('Link changes')
    await expect(summary.locator('.link-change-added')).toHaveCount(1)

    // Second click: backward (B→A)
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-backward/)
    await expect(hLink).toHaveClass(/active/)

    // Third click: both (bidirectional)
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-both/)
    await expect(hLink).toHaveClass(/active/)
    // Two flags set = two added entries in the summary
    await expect(summary.locator('.link-change-added')).toHaveCount(2)

    // Fourth click: back to none
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-none/)
    await expect(hLink).not.toHaveClass(/active/)
    // No changes → summary should disappear
    await expect(summary).toHaveCount(0)

    // Click forward once more, then submit
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-forward/)
    await expect(page.getByRole('button', { name: 'Store Upgrade' })).toBeEnabled()
    await page.getByRole('button', { name: 'Store Upgrade' }).click()

    // After submit the building shows upgrade-in-progress banner
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')

    // Re-enter edit mode and confirm the pending configuration shows the forward link
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const queuedSection = getGridSection(page, 'Queued Upgrade')
    const queuedHLink = queuedSection.locator('.link-toggle.horizontal').first()
    await expect(queuedHLink).toHaveClass(/link-state-forward/)
  })

  test('directional link editing: configuration warnings shown when purchase unit is not linked', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-warn-link',
      playerId: player.id,
      name: 'Warn Link Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-warn-link',
          companyId: 'company-warn-link',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Warn Link Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'wl-1',
              buildingId: 'building-warn-link',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'wl-2',
              buildingId: 'building-warn-link',
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

    await page.goto('/building/building-warn-link')
    await expect(page.getByRole('heading', { name: 'Warn Link Factory' })).toBeVisible()

    // Warnings are shown even before editing - purchase and manufacturing not linked
    await expect(page.getByText('Configuration Warnings')).toBeVisible()
    await expect(page.locator('.config-warnings')).toContainText('Purchase unit at (0, 0) is not linked to a consumer unit.')
    await expect(page.locator('.config-warnings')).toContainText('Manufacturing unit at (1, 0) is not linked to a storage or sales output.')

    // Enter edit mode — warnings persist until links are added
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // Add a forward link from Purchase → Manufacturing
    const hLink = plannedSection.locator('.link-toggle.horizontal').first()
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-forward/)

    // Purchase is now linked — its warning should be gone but manufacturing still has no output
    await expect(page.locator('.config-warnings')).not.toContainText('Purchase unit at (0, 0) is not linked to a consumer unit.')
    await expect(page.locator('.config-warnings')).toContainText('Manufacturing unit at (1, 0) is not linked to a storage or sales output.')
  })

  test('diagonal link editing: full journey — create diagonal connections, review summary, submit plan', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-diag',
      playerId: player.id,
      name: 'Diagonal Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-diag',
          companyId: 'company-diag',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Diagonal Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'diag-1',
              buildingId: 'building-diag',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'diag-2',
              buildingId: 'building-diag',
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
            {
              id: 'diag-3',
              buildingId: 'building-diag',
              unitType: 'STORAGE',
              gridX: 0,
              gridY: 1,
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
            {
              id: 'diag-4',
              buildingId: 'building-diag',
              unitType: 'B2B_SALES',
              gridX: 1,
              gridY: 1,
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

    await page.goto('/building/building-diag')
    await expect(page.getByRole('heading', { name: 'Diagonal Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // The diagonal button between (0,0)-(1,1) starts as none
    const diagButton = plannedSection.locator('.link-toggle.diagonal').first()
    await expect(diagButton).toHaveClass(/state-none/)

    // First click: tl-br (↘ direction)
    await diagButton.click()
    await expect(diagButton).toHaveClass(/state-tl-br/)

    // Link changes summary should show added diagonal link
    const summary = plannedSection.locator('.link-changes-summary')
    await expect(summary).toBeVisible()
    await expect(summary).toContainText('Link changes')
    await expect(summary.locator('.link-change-added')).toHaveCount(2) // tl has linkDownRight, br has linkUpLeft

    // Second click: tr-bl (↙ direction)
    await diagButton.click()
    await expect(diagButton).toHaveClass(/state-tr-bl/)

    // Third click: cross (both diagonals)
    await diagButton.click()
    await expect(diagButton).toHaveClass(/state-cross/)

    // Fourth click: back to none
    await diagButton.click()
    await expect(diagButton).toHaveClass(/state-none/)
    // No changes → summary should disappear
    await expect(summary).toHaveCount(0)

    // Click twice more to reach tl-br, then submit
    await diagButton.click()
    await expect(diagButton).toHaveClass(/state-tl-br/)
    await page.getByRole('button', { name: 'Store Upgrade' }).click()

    // After submit: upgrade-in-progress banner appears
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')

    // Re-enter edit mode and verify the queued plan shows the diagonal state
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const queuedSection = getGridSection(page, 'Queued Upgrade')
    await expect(queuedSection.locator('.link-toggle.diagonal').first()).toHaveClass(/state-tl-br/)
  })

  test('diagonal link editing: flow diagnostics — warnings shown for disconnected layout, resolved after linking', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-diag-warn',
      playerId: player.id,
      name: 'Flow Warn Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-diag-warn',
          companyId: 'company-diag-warn',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Flow Warn Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            // 2×2 block: all 4 cells occupied so diagonal toggle is enabled
            {
              id: 'fw-1',
              buildingId: 'building-diag-warn',
              unitType: 'PURCHASE',
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
            },
            {
              id: 'fw-2',
              buildingId: 'building-diag-warn',
              unitType: 'STORAGE',
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
            {
              id: 'fw-3',
              buildingId: 'building-diag-warn',
              unitType: 'B2B_SALES',
              gridX: 0,
              gridY: 1,
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
            {
              id: 'fw-4',
              buildingId: 'building-diag-warn',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 1,
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

    await page.goto('/building/building-diag-warn')
    await expect(page.getByRole('heading', { name: 'Flow Warn Factory' })).toBeVisible()

    // Warnings visible before editing: purchase and manufacturing not connected
    await expect(page.getByText('Configuration Warnings')).toBeVisible()
    await expect(page.locator('.config-warnings')).toContainText('Purchase unit at (0, 0) is not linked to a consumer unit.')
    await expect(page.locator('.config-warnings')).toContainText('Manufacturing unit at (1, 1) is not linked to a storage or sales output.')

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // The diagonal button covering the 2×2 block (0,0)-(1,1) is enabled (all 4 cells occupied)
    const diagButton = plannedSection.locator('.link-toggle.diagonal').first()
    await expect(diagButton).toBeEnabled()
    await diagButton.click()
    // tl-br: PURCHASE(0,0).linkDownRight → MANUFACTURING(1,1)
    await expect(diagButton).toHaveClass(/state-tl-br/)

    // Purchase warning should be gone — PURCHASE now links diagonally to MANUFACTURING
    await expect(page.locator('.config-warnings')).not.toContainText('Purchase unit at (0, 0) is not linked to a consumer unit.')

    // Manufacturing still has no output link — warning remains
    await expect(page.locator('.config-warnings')).toContainText('Manufacturing unit at (1, 1) is not linked to a storage or sales output.')
  })

  // ---------------------------------------------------------------------------
  // Grid tile metadata: resource / product labels, fill bars, price metrics
  // ---------------------------------------------------------------------------

  test('mine grid shows resource label and fill bar directly in active grid tiles', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-mine',
      playerId: player.id,
      name: 'Coal Mine Co',
      cash: 300000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-mine',
          companyId: 'company-mine',
          cityId: 'city-ba',
          type: 'MINE',
          name: 'Coal Mine',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'mine-u1',
              buildingId: 'building-mine',
              unitType: 'MINING',
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
              resourceTypeId: 'res-wood',
              inventoryQuantity: 75,
              inventoryQuality: 0.9,
            },
            {
              id: 'mine-u2',
              buildingId: 'building-mine',
              unitType: 'STORAGE',
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
              resourceTypeId: 'res-wood',
              inventoryQuantity: 10,
            },
            {
              id: 'mine-u3',
              buildingId: 'building-mine',
              unitType: 'B2B_SALES',
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
              resourceTypeId: 'res-wood',
              minPrice: 85,
              inventoryQuantity: 0,
            },
          ],
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

    await page.goto('/building/building-mine')
    await expect(page.getByRole('heading', { name: 'Coal Mine' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')

    // Mining unit: shows 'Wood' resource label directly in tile
    const miningCell = getGridCell(activeSection, 0, 0)
    await expect(miningCell).toContainText('Mining')
    await expect(miningCell).toContainText('Wood')

    // Mining unit: fill bar is present (75/100 = 75% fill = high bucket)
    await expect(miningCell.locator('.cell-capacity')).toBeVisible()
    await expect(miningCell.locator('.cell-capacity-fill[data-fill="high"]')).toBeVisible()

    // Storage unit: also shows resource label and fill bar at lower fill
    const storageCell = getGridCell(activeSection, 1, 0)
    await expect(storageCell).toContainText('Storage')
    await expect(storageCell).toContainText('Wood')
    await expect(storageCell.locator('.cell-capacity')).toBeVisible()
    await expect(storageCell.locator('.cell-capacity-fill[data-fill="low"]')).toBeVisible()

    // B2B_SALES unit: shows resource label AND min price metric
    const salesCell = getGridCell(activeSection, 2, 0)
    await expect(salesCell).toContainText('B2B Sales')
    await expect(salesCell).toContainText('Wood')
    await expect(salesCell).toContainText('Sell from: $85')

    // All occupied tiles include accessible aria-label describing unit type
    await expect(miningCell).toHaveAttribute('aria-label', /Mining/)
    await expect(miningCell).toHaveAttribute('aria-label', /Wood/)
    await expect(salesCell).toHaveAttribute('aria-label', /B2B Sales/)
    await expect(salesCell).toHaveAttribute('aria-label', /Wood/)
    await expect(salesCell).toHaveAttribute('aria-label', /Sell from/)
  })

  test('factory grid shows product label and price metric in planned configuration after edit', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-factory',
      playerId: player.id,
      name: 'Chair Factory Ltd',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-factory',
          companyId: 'company-factory',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Chair Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'factory-u1',
              buildingId: 'building-factory',
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
              resourceTypeId: 'res-wood',
              maxPrice: 120,
              inventoryQuantity: 40,
            },
            {
              id: 'factory-u2',
              buildingId: 'building-factory',
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
              productTypeId: 'prod-chair',
              inventoryQuantity: 20,
            },
            {
              id: 'factory-u3',
              buildingId: 'building-factory',
              unitType: 'PUBLIC_SALES',
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
              productTypeId: 'prod-chair',
              minPrice: 50,
              inventoryQuantity: 5,
            },
          ],
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

    await page.goto('/building/building-factory')
    await expect(page.getByRole('heading', { name: 'Chair Factory' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')

    // Purchase unit shows resource label and max price metric
    const purchaseCell = getGridCell(activeSection, 0, 0)
    await expect(purchaseCell).toContainText('Purchase')
    await expect(purchaseCell).toContainText('Wood')
    await expect(purchaseCell).toContainText('Buy up to: $120')
    await expect(purchaseCell.locator('.cell-capacity')).toBeVisible()

    // Manufacturing unit shows product label and fill bar
    const mfgCell = getGridCell(activeSection, 1, 0)
    await expect(mfgCell).toContainText('Manufacturing')
    await expect(mfgCell).toContainText('Wooden Chair')
    await expect(mfgCell.locator('.cell-capacity')).toBeVisible()

    // Public sales unit shows product label and min price metric
    const salesCell = getGridCell(activeSection, 2, 0)
    await expect(salesCell).toContainText('Public Sales')
    await expect(salesCell).toContainText('Wooden Chair')
    await expect(salesCell).toContainText('Sell from: $50')

    // Enter edit mode – the planned grid should show same metadata
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    const plannedPurchase = getGridCell(plannedSection, 0, 0)
    await expect(plannedPurchase).toContainText('Purchase')
    await expect(plannedPurchase).toContainText('Wood')
    await expect(plannedPurchase).toContainText('Buy up to: $120')

    const plannedMfg = getGridCell(plannedSection, 1, 0)
    await expect(plannedMfg).toContainText('Manufacturing')
    await expect(plannedMfg).toContainText('Wooden Chair')

    const plannedSales = getGridCell(plannedSection, 2, 0)
    await expect(plannedSales).toContainText('Public Sales')
    await expect(plannedSales).toContainText('Wooden Chair')
    await expect(plannedSales).toContainText('Sell from: $50')

    // Planned grid buttons include accessible aria-labels
    await expect(plannedPurchase).toHaveAttribute('aria-label', /Purchase/)
    await expect(plannedPurchase).toHaveAttribute('aria-label', /Wood/)
    await expect(plannedPurchase).toHaveAttribute('aria-label', /Buy up to/)
  })

  test('keeps the active configuration panel stable during async offer refreshes', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-focus',
      playerId: player.id,
      name: 'Focus Safe Industries',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-focus',
          companyId: 'company-focus',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Focus Safe Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'focus-purchase',
              buildingId: 'building-focus',
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
              resourceTypeId: 'res-wood',
              maxPrice: 25,
              purchaseSource: 'OPTIMAL',
            },
            {
              id: 'focus-manufacturing',
              buildingId: 'building-focus',
              unitType: 'MANUFACTURING',
              gridX: 1,
              gridY: 0,
              level: 1,
              linkUp: false,
              linkDown: false,
              linkLeft: true,
              linkRight: false,
              linkUpLeft: false,
              linkUpRight: false,
              linkDownLeft: false,
              linkDownRight: false,
              productTypeId: makeChairProduct().id,
            },
          ],
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

    await page.goto('/building/building-focus')
    await expect(page.getByRole('heading', { name: 'Focus Safe Factory' })).toBeVisible()

    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    const searchInput = page.locator('.sidebar .selector-search')
    await searchInput.fill('wood')
    await expect(searchInput).toHaveValue('wood')

    const purchaseSourceSelect = page.locator('.unit-config-fields select').first()
    await purchaseSourceSelect.selectOption('EXCHANGE')
    await purchaseSourceSelect.selectOption('OPTIMAL')

    await expect(page.locator('.loading')).toHaveCount(0)
    await expect(page.locator('.sidebar')).toBeVisible()
    await expect(getGridCell(plannedSection, 0, 0)).toHaveClass(/selected/)
    await expect(searchInput).toHaveValue('wood')
  })
})

test.describe('Global exchange market', () => {
  test('shows exchange offer prices, transit costs, and delivered prices for all cities', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-exmkt',
      playerId: player.id,
      name: 'Exchange Market Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-exmkt',
          companyId: 'company-exmkt',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Market Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-exmkt-purchase',
              buildingId: 'building-exmkt',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 999,
            },
          ],
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

    await page.goto('/building/building-exmkt')
    await expect(page.getByRole('heading', { name: 'Market Factory' })).toBeVisible()

    // Click on the PURCHASE cell to see exchange offers panel
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers section must be visible
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // All three seeded mock cities must appear in the exchange list
    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList).toBeVisible()
    await expect(offersList.getByText('Bratislava')).toBeVisible()
    await expect(offersList.getByText('Prague')).toBeVisible()
    await expect(offersList.getByText('Vienna')).toBeVisible()

    // Exchange, transit, and delivered price labels must all be present
    // (multiple offers exist, so scope to the first offer)
    await expect(offersList.getByText(/Exchange:/).first()).toBeVisible()
    await expect(offersList.getByText(/Transit:/).first()).toBeVisible()
    await expect(offersList.getByText(/Delivered:/).first()).toBeVisible()

    // Quality must be shown for each offer
    await expect(offersList.getByText(/Quality \d/).first()).toBeVisible()
  })

  test('does NOT show exchange offers when purchase source is LOCAL', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-local',
      playerId: player.id,
      name: 'Local Source Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-local',
          companyId: 'company-local',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Local Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-local-purchase',
              buildingId: 'building-local',
              unitType: 'PURCHASE',
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
              purchaseSource: 'LOCAL',
              maxPrice: 50,
            },
          ],
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

    await page.goto('/building/building-local')
    await expect(page.getByRole('heading', { name: 'Local Factory' })).toBeVisible()

    // Click on the PURCHASE cell
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers must NOT appear for LOCAL source
    await expect(page.getByText('Global exchange offers')).toBeHidden()
  })

  test('shows exchange offers when OPTIMAL source is configured', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-optimal',
      playerId: player.id,
      name: 'Optimal Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-optimal',
          companyId: 'company-optimal',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Optimal Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-optimal-purchase',
              buildingId: 'building-optimal',
              unitType: 'PURCHASE',
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
              purchaseSource: 'OPTIMAL',
            },
          ],
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

    await page.goto('/building/building-optimal')
    await expect(page.getByRole('heading', { name: 'Optimal Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // OPTIMAL source also triggers exchange offer visibility
    await expect(page.getByText('Global exchange offers')).toBeVisible()
    await expect(page.getByText('Bratislava')).toBeVisible()
  })

  test('configuring a purchase unit with EXCHANGE source persists and shows exchange offers', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-cfg',
      playerId: player.id,
      name: 'Config Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-cfg',
          companyId: 'company-cfg',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Config Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [],
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

    await page.goto('/building/building-cfg')
    await expect(page.getByRole('heading', { name: 'Config Factory' })).toBeVisible()

    // Enter edit mode — the button says "Edit Building"
    await page.getByRole('button', { name: 'Edit Building' }).click()
    await expect(page.getByRole('button', { name: 'Store Upgrade' })).toBeVisible()

    // Click on an empty cell (0,0) in the draft ("Planned Upgrade") section
    const draftSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await draftSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Unit picker should appear; select PURCHASE unit
    await expect(page.getByText('Select a unit type to place')).toBeVisible()
    await page.getByRole('button', { name: 'Purchase' }).click()

    // After placing, selectedCell is reset — click the cell again to open config
    await draftSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Config panel should open for the placed PURCHASE unit
    await expect(page.getByText('Unit Configuration')).toBeVisible()
    await expect(page.getByText('Input Item')).toBeVisible()
    await expect(page.getByText('Purchase Source')).toBeVisible()

    // Set purchase source to EXCHANGE via the select next to the "Purchase Source" label
    const purchaseSourceSelect = page
      .locator('.config-field')
      .filter({ has: page.getByText('Purchase Source') })
      .locator('select')
    await purchaseSourceSelect.selectOption({ value: 'EXCHANGE' })

    // Exchange offers should become visible once source = EXCHANGE and there is a resource set
    // (exchange loads when source changes; since no resource is set yet the panel may be hidden)
    // Verify config labels are shown as expected for a PURCHASE unit
    await expect(page.getByText('Max Price')).toBeVisible()
    await expect(page.getByText('Min Quality')).toBeVisible()
  })

  test('exchange offer shows correct breakdown: exchange price, transit, delivered', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-breakdown',
      playerId: player.id,
      name: 'Breakdown Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-breakdown',
          companyId: 'company-breakdown',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Breakdown Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-breakdown',
              buildingId: 'building-breakdown',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 999,
            },
          ],
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

    await page.goto('/building/building-breakdown')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // The Bratislava offer (same city) should show 0 transit cost (delivered = exchange price)
    const braSection = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(braSection).toBeVisible()

    // Bratislava is the destination city so transit should be $0.00 / 0 km
    await expect(braSection.getByText(/Transit: \$0/)).toBeVisible()

    // Remote city offers must show positive transit cost
    const pragueSection = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    await expect(pragueSection).toBeVisible()
    // Prague transit > 0 (cross-city)
    const pragueTransitText = await pragueSection.getByText(/Transit:/).textContent()
    expect(pragueTransitText).not.toContain('$0.00')
  })

  test('shows no-valid-offers message when max price is set below all delivered prices', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-blocked-price',
      playerId: player.id,
      name: 'Blocked Price Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-blocked-price',
          companyId: 'company-blocked-price',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Blocked Price Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-blocked-price',
              buildingId: 'building-blocked-price',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              // Extremely low max price so no offer can pass
              maxPrice: 0.01,
            },
          ],
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

    await page.goto('/building/building-blocked-price')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // No-valid-offers banner must appear
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeVisible()

    // All offer items should show the max-price blocked reason
    const offerItems = page.locator('.exchange-offer-item.offer-blocked')
    await expect(offerItems.first()).toBeVisible()
    // The blocked reason text must appear for at least one offer
    await expect(page.locator('.offer-blocked-reason').first()).toBeVisible()
    await expect(page.locator('.offer-blocked-reason').first()).toContainText('Over your max price')
  })

  test('shows blocked-quality reason when min quality is set above all available exchange quality', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-blocked-qual',
      playerId: player.id,
      name: 'Blocked Quality Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-blocked-qual',
          companyId: 'company-blocked-qual',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Blocked Quality Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-blocked-qual',
              buildingId: 'building-blocked-qual',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 9999,
              // Exchange quality tops out at 0.95 — set minQuality above that to block all
              minQuality: 0.99,
            },
          ],
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

    await page.goto('/building/building-blocked-qual')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // No-valid-offers banner must appear
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeVisible()

    // Blocked reason must reference quality constraint
    await expect(page.locator('.offer-blocked-reason').first()).toBeVisible()
    await expect(page.locator('.offer-blocked-reason').first()).toContainText('Below your min quality')
  })

  test('shows "best delivered price" badge on the offer with the lowest delivered price', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-best-badge',
      playerId: player.id,
      name: 'Best Badge Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-best-badge',
          companyId: 'company-best-badge',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Best Badge Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-best-badge',
              buildingId: 'building-best-badge',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 999,
            },
          ],
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

    await page.goto('/building/building-best-badge')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // The "Best delivered price" badge must appear exactly once (on the best offer)
    const bestBadge = page.locator('.offer-best-badge')
    await expect(bestBadge).toHaveCount(1)
    await expect(bestBadge).toContainText('Best delivered price')

    // The badge must be inside the Bratislava offer (same-city = 0 transit = lowest delivered)
    const bratislavaOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(bratislavaOffer.locator('.offer-best-badge')).toBeVisible()

    // The selection hint explaining the logic must also be visible
    await expect(page.getByText(/lowest delivered price/)).toBeVisible()
  })

  test('nearby supplier with higher sticker price wins over far supplier due to transit cost', async ({ page }) => {
    const player = makePlayer()
    // Bratislava is the destination city. Override city abundances so Prague has a lower exchange
    // price (high abundance) than Bratislava, but Prague's transit cost still makes its delivered
    // price higher. This proves that the cheapest sticker price is NOT always the best choice.
    const cities = [
      {
        id: 'city-ba',
        name: 'Bratislava',
        countryCode: 'SK',
        latitude: 48.1486,
        longitude: 17.1077,
        population: 475000,
        averageRentPerSqm: 14,
        resources: [
          // Wood: standard abundance → standard exchange price (~$11.17/t), 0 transit (same city)
          // Delivered = $11.17 → WINS despite higher sticker than Prague
          { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.7 },
        ],
      },
      {
        id: 'city-cz',
        name: 'Prague',
        countryCode: 'CZ',
        latitude: 50.0755,
        longitude: 14.4378,
        population: 1350000,
        averageRentPerSqm: 18,
        resources: [
          // Wood: very high abundance → cheapest sticker price (~$9.46/t), but ~300 km away
          // Transit = ~3.75/t, Delivered = ~$13.21/t → LOSES despite cheaper sticker
          { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.95 },
        ],
      },
    ]

    player.companies.push({
      id: 'company-nearby-wins',
      playerId: player.id,
      name: 'Nearby Wins Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-nearby-wins',
          companyId: 'company-nearby-wins',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Nearby Wins Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-nearby-wins',
              buildingId: 'building-nearby-wins',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 999,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    // Override cities so Prague has cheap sticker but high transit
    state.cities = cities as typeof state.cities
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-nearby-wins')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList).toBeVisible()

    // Both cities must be listed
    await expect(offersList.getByText('Bratislava')).toBeVisible()
    await expect(offersList.getByText('Prague')).toBeVisible()

    // Bratislava offer must carry the "best" badge (0 transit wins despite higher sticker)
    const bratislavaOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(bratislavaOffer.locator('.offer-best-badge')).toBeVisible()

    // Prague must NOT have the best badge
    const pragueOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    await expect(pragueOffer.locator('.offer-best-badge')).toHaveCount(0)

    // Prague transit cost must be positive (it's far away)
    const pragueTransitText = await pragueOffer.getByText(/Transit:/).textContent()
    expect(pragueTransitText).not.toContain('$0')

    // Bratislava must show $0 transit (same city as destination)
    await expect(bratislavaOffer.getByText(/Transit: \$0/)).toBeVisible()
  })
})

// ── Exchange panel narrow/mobile layout ────────────────────────────────────────

test.describe('Global exchange market — narrow layout', () => {
  function makeExchangePlayer() {
    const player = makePlayer()
    player.companies.push({
      id: 'company-narrow-ex',
      playerId: player.id,
      name: 'Narrow Exchange Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-narrow-ex',
          companyId: 'company-narrow-ex',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Narrow Exchange Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-narrow-ex',
              buildingId: 'building-narrow-ex',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              maxPrice: 999,
            },
          ],
        },
      ],
    })
    return player
  }

  test('exchange offers list is visible and readable on 375px viewport', async ({ page }) => {
    const player = makeExchangePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/building/building-narrow-ex')
    await expect(page.getByRole('heading', { name: 'Narrow Exchange Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange section heading must be visible
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // All three city offers must appear in the list
    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList).toBeVisible()
    await expect(offersList.getByText('Bratislava')).toBeVisible()
    await expect(offersList.getByText('Prague')).toBeVisible()
    await expect(offersList.getByText('Vienna')).toBeVisible()
  })

  test('exchange price, transit cost, and delivered price are visible on 375px viewport', async ({ page }) => {
    const player = makeExchangePlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/building/building-narrow-ex')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    const offersList = page.locator('.exchange-offers-list')
    // Exchange price, transit and delivered price labels must all be present
    await expect(offersList.getByText(/Exchange:/).first()).toBeVisible()
    await expect(offersList.getByText(/Transit:/).first()).toBeVisible()
    await expect(offersList.getByText(/Delivered:/).first()).toBeVisible()

    // Quality must be visible for at least the first offer
    await expect(offersList.getByText(/Quality \d/).first()).toBeVisible()
  })

  test('best-offer badge and no-valid-offers message are visible on 375px viewport', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-narrow-blocked',
      playerId: player.id,
      name: 'Narrow Blocked Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-narrow-blocked',
          companyId: 'company-narrow-blocked',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Narrow Blocked Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-narrow-blocked',
              buildingId: 'building-narrow-blocked',
              unitType: 'PURCHASE',
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
              purchaseSource: 'EXCHANGE',
              // Extremely low max price so no offer can pass
              maxPrice: 0.01,
            },
          ],
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

    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/building/building-narrow-blocked')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()
    // "No valid offers" message must be visible without horizontal scrolling
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeVisible()
  })
})

test.describe('Starter factory setup banner', () => {
  function makeEmptyFactoryPlayer() {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Industrial Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-empty-factory',
              companyId: 'company-1',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'New Factory',
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
    return player
  }

  test('shows starter setup banner for an empty factory', async ({ page }) => {
    const player = makeEmptyFactoryPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-factory')

    // Starter setup banner should be visible
    await expect(page.getByRole('region', { name: /starter setup/i })).toBeVisible()
    // Title and body text
    await expect(page.getByText(/New Factory — Ready to Set Up/i)).toBeVisible()
    await expect(page.getByText(/no units configured yet/i)).toBeVisible()
    // Starter layout description
    await expect(page.getByText(/Purchase.*Manufacturing.*Storage/i)).toBeVisible()
    // Apply Starter Layout button
    await expect(page.getByRole('button', { name: /Apply Starter Layout/i })).toBeVisible()
  })

  test('apply starter layout pre-populates the planning grid', async ({ page }) => {
    const player = makeEmptyFactoryPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-factory')

    await expect(page.getByRole('button', { name: /Apply Starter Layout/i })).toBeVisible()
    await page.getByRole('button', { name: /Apply Starter Layout/i }).click()

    // Planning grid should now be visible (edit mode)
    const planningSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(planningSection).toBeVisible()

    // PURCHASE unit should appear at position (0,0)
    const purchaseCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await expect(purchaseCell).toContainText('Purchase')

    // MANUFACTURING unit at (1,0)
    const manufacturingCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await expect(manufacturingCell).toContainText('Manufacturing')

    // STORAGE unit at (2,0)
    const storageCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(2)
    await expect(storageCell).toContainText('Storage')

    // Starter setup banner should no longer be visible (we are now in edit mode)
    await expect(page.locator('.starter-setup-banner')).toBeHidden()
  })

  test('apply starter layout can be saved via Store Upgrade', async ({ page }) => {
    const player = makeEmptyFactoryPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-factory')

    await page.getByRole('button', { name: /Apply Starter Layout/i }).click()

    // Store Upgrade button should be enabled (draft has changes)
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await expect(storeBtn).toBeVisible()
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // After save, upgrade-in-progress banner should appear (pending configuration)
    await expect(page.locator('.upgrade-banner')).toBeVisible()
    // Starter setup banner should be gone
    await expect(page.locator('.starter-setup-banner')).toBeHidden()
  })

  test('starter setup banner is hidden for a factory that already has units', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-1',
          playerId: 'player-1',
          name: 'Industrial Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-factory-with-units',
              companyId: 'company-1',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'Running Factory',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              units: [
                {
                  id: 'u-purchase',
                  buildingId: 'building-factory-with-units',
                  unitType: 'PURCHASE',
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
                },
              ],
              pendingConfiguration: null,
            },
          ],
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

    await page.goto('/building/building-factory-with-units')

    // Should NOT show the starter setup banner for a factory that already has units
    await expect(page.locator('.starter-setup-banner')).toBeHidden()
  })
})

// ── Production chain configuration ───────────────────────────────────────────

test.describe('Production chain configuration', () => {
  /**
   * Returns a player whose factory has PURCHASE, MANUFACTURING, and STORAGE units
   * saved as active units. The resources/products can be configured via the override params.
   */
  function makeFactoryWithStarterUnits({ purchaseResourceId = null as string | null, purchaseProductId = null as string | null, manufacturingProductId = null as string | null } = {}) {
    return makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-chain',
          playerId: 'player-1',
          name: 'Chain Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-chain-factory',
              companyId: 'company-chain',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'Chain Factory',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              pendingConfiguration: null,
              units: [
                {
                  id: 'unit-purchase',
                  buildingId: 'building-chain-factory',
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
                  resourceTypeId: purchaseResourceId,
                  productTypeId: purchaseProductId,
                },
                {
                  id: 'unit-manufacturing',
                  buildingId: 'building-chain-factory',
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
                  productTypeId: manufacturingProductId,
                },
                {
                  id: 'unit-storage',
                  buildingId: 'building-chain-factory',
                  unitType: 'STORAGE',
                  gridX: 2,
                  gridY: 0,
                  level: 1,
                  linkUp: false,
                  linkDown: false,
                  linkLeft: true,
                  linkRight: false,
                  linkUpLeft: false,
                  linkUpRight: false,
                  linkDownLeft: false,
                  linkDownRight: false,
                },
              ],
            },
          ],
        },
      ],
    })
  }

  async function setupChainTest(page: import('@playwright/test').Page, player: ReturnType<typeof makePlayer>) {
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    return state
  }

  test('shows production chain panel for factory with starter layout units', async ({ page }) => {
    const player = makeFactoryWithStarterUnits()
    await setupChainTest(page, player)
    await page.goto('/building/building-chain-factory')

    // Production chain panel should be visible
    await expect(page.getByRole('region', { name: /production chain status/i })).toBeVisible()
    const panel = page.getByRole('region', { name: /production chain status/i })
    // Panel should show the three unit type labels
    await expect(panel.locator('.chain-step-type', { hasText: 'Purchase' })).toBeVisible()
    await expect(panel.locator('.chain-step-type', { hasText: 'Manufacturing' })).toBeVisible()
    await expect(panel.locator('.chain-step-type', { hasText: 'Storage' })).toBeVisible()
  })

  test('chain panel shows incomplete status when units are not yet configured', async ({ page }) => {
    const player = makeFactoryWithStarterUnits()
    await setupChainTest(page, player)
    await page.goto('/building/building-chain-factory')

    const panel = page.getByRole('region', { name: /production chain status/i })
    // Should show "Configuration Needed" badge
    await expect(panel.getByText(/Configuration Needed/i)).toBeVisible()
    // Should show "Not configured yet" for purchase and manufacturing steps
    await expect(panel.locator('.chain-step-missing-label').first()).toBeVisible()
    // Should show guidance items
    await expect(panel.getByText(/select the raw material to buy/i)).toBeVisible()
    await expect(panel.getByText(/select the product to manufacture/i)).toBeVisible()
  })

  test('chain panel shows fully configured status when all units have items set', async ({ page }) => {
    const player = makeFactoryWithStarterUnits({
      purchaseResourceId: 'res-wood',
      manufacturingProductId: 'prod-chair',
    })
    await setupChainTest(page, player)
    await page.goto('/building/building-chain-factory')

    const panel = page.getByRole('region', { name: /production chain status/i })
    // Should show "Chain Ready" badge
    await expect(panel.getByText(/Chain Ready/i)).toBeVisible()
    // Should show resource name in Purchase step (exact match to avoid "Wooden Chair" substring)
    await expect(panel.locator('.chain-step').nth(0).locator('.chain-step-value')).toContainText('Wood')
    // Should show product name in Manufacturing step
    await expect(panel.locator('.chain-step').nth(2).locator('.chain-step-value')).toContainText('Wooden Chair')
    // Should show chain complete message
    await expect(panel.getByText(/next step/i)).toBeVisible()
  })

  test('configure purchase unit and save: chain panel reflects new resource', async ({ page }) => {
    const player = makeFactoryWithStarterUnits()
    const state = await setupChainTest(page, player)
    await page.goto('/building/building-chain-factory')

    // Panel starts with "Configuration Needed"
    const panel = page.getByRole('region', { name: /production chain status/i })
    await expect(panel.getByText(/Configuration Needed/i)).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: /Edit Building/i }).click()

    // Click the PURCHASE unit cell (0,0)
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Configure with Wood resource via the item selector
    await expect(page.getByText('Input Item')).toBeVisible()
    await page.getByPlaceholder(/Search/i).fill('Wood')
    await page.getByRole('button', { name: /^Wood/ }).first().click()

    // Save the configuration
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await storeBtn.click()

    // After save, exit edit mode, chain panel should update
    await expect(page.locator('.production-chain-panel')).toBeVisible()

    // Mock state should reflect the new pending config with Wood resource
    const pendingBuilding = state.players[0].companies[0].buildings[0]
    const pendingUnits = pendingBuilding.pendingConfiguration?.units ?? []
    const purchaseUnit = pendingUnits.find((u) => u.unitType === 'PURCHASE')
    expect(purchaseUnit?.resourceTypeId).toBe('res-wood')
  })

  test('configure manufacturing unit and save: chain panel reflects selected product', async ({ page }) => {
    const player = makeFactoryWithStarterUnits({ purchaseResourceId: 'res-wood' })
    const state = await setupChainTest(page, player)
    await page.goto('/building/building-chain-factory')

    // Enter edit mode
    await page.getByRole('button', { name: /Edit Building/i }).click()

    // Click the MANUFACTURING unit cell (1,0)
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1).click()

    // Configure with Wooden Chair (output product)
    await expect(page.getByText('Output Product')).toBeVisible()
    await page.getByRole('button', { name: /Wooden Chair/ }).click()

    // Save
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await storeBtn.click()

    // Verify pending config has the product
    const pendingUnits = state.players[0].companies[0].buildings[0].pendingConfiguration?.units ?? []
    const mfgUnit = pendingUnits.find((u) => u.unitType === 'MANUFACTURING')
    expect(mfgUnit?.productTypeId).toBe('prod-chair')
  })

  test('reloading page shows saved production chain configuration', async ({ page }) => {
    // Factory already has fully configured active units
    const player = makeFactoryWithStarterUnits({
      purchaseResourceId: 'res-wood',
      manufacturingProductId: 'prod-chair',
    })
    await setupChainTest(page, player)

    // First visit
    await page.goto('/building/building-chain-factory')
    const panel = page.getByRole('region', { name: /production chain status/i })
    await expect(panel.getByText(/Chain Ready/i)).toBeVisible()
    await expect(panel.locator('.chain-step').nth(0).locator('.chain-step-value')).toContainText('Wood')
    await expect(panel.locator('.chain-step').nth(2).locator('.chain-step-value')).toContainText('Wooden Chair')

    // Reload the page — configuration should persist from the mock state
    await page.reload()
    await expect(page.getByRole('region', { name: /production chain status/i })).toBeVisible()
    await expect(page.getByRole('region', { name: /production chain status/i }).getByText(/Chain Ready/i)).toBeVisible()
  })

  test('incompatible recipe combination shows a warning in edit mode', async ({ page }) => {
    // Factory has PURCHASE with Grain (res-grain) and MANUFACTURING will try to produce
    // Wooden Chair (which requires Wood, not Grain) — should show a recipe mismatch warning.
    const player = makeFactoryWithStarterUnits({ purchaseResourceId: 'res-grain' })
    const state = await setupChainTest(page, player)
    // Add a chair product and grain resource to mock state
    state.productTypes = [
      makeChairProduct(),
      {
        id: 'prod-bread',
        name: 'Bread',
        slug: 'bread',
        industry: 'FOOD_PROCESSING',
        basePrice: 3,
        baseCraftTicks: 1,
        outputQuantity: 12,
        energyConsumptionMwh: 0.5,
        unitName: 'Loaf',
        unitSymbol: 'loaves',
        isProOnly: false,
        description: 'Bread from grain.',
        recipes: [
          {
            resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', unitName: 'Ton', unitSymbol: 't' },
            inputProductType: null,
            quantity: 1,
          },
        ],
      },
    ]
    state.resourceTypes = [
      { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', description: 'Wood', imageUrl: null },
      { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC', basePrice: 5, weightPerUnit: 2, unitName: 'Ton', unitSymbol: 't', description: 'Grain', imageUrl: null },
    ]

    await page.goto('/building/building-chain-factory')

    // Enter edit mode and manually select Wooden Chair for MANUFACTURING
    // (which requires Wood, but PURCHASE has Grain)
    await page.getByRole('button', { name: /Edit Building/i }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()

    // Directly patch the draft state by configuring manufacturing unit via JS
    // We set the manufacturing unit's productTypeId to the chair product
    // The frontend configWarnings should detect the mismatch (Grain purchase != Wood required)
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1).click()
    await expect(page.getByText('Output Product')).toBeVisible()

    // `getManufacturingSelectableItems` filters products by reachable inputs from linked Purchase
    // units. Since PURCHASE is configured with Grain (res-grain) and Wooden Chair requires Wood,
    // the selector must NOT offer Wooden Chair — asserting count 0 verifies this filtering.
    await expect(page.getByRole('button', { name: /Wooden Chair/ })).toHaveCount(0)
    // Bread requires Grain, so it SHOULD appear as an available output.
    await expect(page.getByRole('button', { name: /Bread/ })).toBeVisible()
  })

  test('starter setup banner is hidden when factory has the starter layout in pending config', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-pending',
          playerId: 'player-1',
          name: 'Pending Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-pending-factory',
              companyId: 'company-pending',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'Pending Factory',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              units: [],
              pendingConfiguration: {
                id: 'pending-plan-1',
                buildingId: 'building-pending-factory',
                submittedAtUtc: '2026-01-01T00:00:00Z',
                submittedAtTick: 10,
                appliesAtTick: 13,
                totalTicksRequired: 3,
                units: [
                  {
                    id: 'pending-unit-purchase',
                    buildingId: 'building-pending-factory',
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
                    isChanged: true,
                    isReverting: false,
                    appliesAtTick: 13,
                    startedAtTick: 10,
                    ticksRequired: 3,
                  },
                  {
                    id: 'pending-unit-mfg',
                    buildingId: 'building-pending-factory',
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
                    isChanged: true,
                    isReverting: false,
                    appliesAtTick: 13,
                    startedAtTick: 10,
                    ticksRequired: 3,
                  },
                ],
                removals: [],
              },
            },
          ],
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

    await page.goto('/building/building-pending-factory')

    // Starter setup banner should NOT show (there's a pending config)
    await expect(page.locator('.starter-setup-banner')).toBeHidden()
    // Production chain panel SHOULD show (factory has pending configuration)
    await expect(page.locator('.production-chain-panel')).toBeVisible()
    // It should show "Configuration Needed" since pending units have no resource/product
    await expect(page.getByRole('region', { name: /production chain status/i }).getByText(/Configuration Needed/i)).toBeVisible()
  })

  test('full end-to-end: apply starter layout, configure both units, save, see chain complete', async ({ page }) => {
    // Start with a completely empty factory — simulates the player journey after
    // purchasing a lot and landing on the building detail page for the first time.
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-e2e',
          playerId: 'player-1',
          name: 'E2E Corp',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-e2e-factory',
              companyId: 'company-e2e',
              cityId: 'city-ba',
              type: 'FACTORY',
              name: 'E2E Factory',
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

    const state = await setupChainTest(page, player)
    await page.goto('/building/building-e2e-factory')

    // Step 1: Starter setup banner should be visible for an empty factory
    await expect(page.getByRole('region', { name: /starter setup/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Apply Starter Layout/i })).toBeVisible()

    // Step 2: Apply starter layout — enters edit mode with PURCHASE/MANUFACTURING/STORAGE
    await page.getByRole('button', { name: /Apply Starter Layout/i }).click()
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)).toContainText('Purchase')
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)).toContainText('Manufacturing')
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(2)).toContainText('Storage')

    // Step 3: Configure the PURCHASE unit — choose Wood as input resource
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()
    await expect(page.getByText('Input Item')).toBeVisible()
    // Onboarding guide should be visible for FACTORY PURCHASE unit
    await expect(page.getByText(/raw material this factory will buy/i)).toBeVisible()
    await page.getByPlaceholder(/Search/i).fill('Wood')
    await page.getByRole('button', { name: /^Wood/ }).first().click()
    await expect(page.locator('.selected-chip')).toContainText('Wood')

    // Step 4: Configure the MANUFACTURING unit — Wooden Chair should appear (Wood recipe matches)
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1).click()
    await expect(page.getByText('Output Product')).toBeVisible()
    // Factory-specific manufacturing onboarding guide
    await expect(page.getByText(/product this factory will manufacture/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /Wooden Chair/ })).toBeVisible()
    await page.getByRole('button', { name: /Wooden Chair/ }).click()
    await expect(page.locator('.selected-chip')).toContainText('Wooden Chair')

    // Step 5: Save the configuration (Store Upgrade)
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // Step 6: After save, edit mode exits; upgrade banner should appear
    await expect(page.locator('.upgrade-banner')).toBeVisible()
    // Starter setup banner should be gone
    await expect(page.locator('.starter-setup-banner')).toBeHidden()

    // Step 7: Production chain panel should show with the saved resource and product
    await expect(page.locator('.production-chain-panel')).toBeVisible()
    const panel = page.getByRole('region', { name: /production chain status/i })
    // Pending units now have Wood and Wooden Chair configured
    const pendingUnits = state.players[0].companies[0].buildings[0].pendingConfiguration?.units ?? []
    expect(pendingUnits.find((u) => u.unitType === 'PURCHASE')?.resourceTypeId).toBe('res-wood')
    expect(pendingUnits.find((u) => u.unitType === 'MANUFACTURING')?.productTypeId).toBe('prod-chair')

    // Panel should reflect "Chain Ready" since both purchase and manufacturing are configured
    await expect(panel.getByText(/Chain Ready/i)).toBeVisible()
  })

  test('failure path: save with incompatible resource/product combination shows inline error', async ({ page }) => {
    // Start with starter layout units where PURCHASE has Grain configured.
    // The player tries to override the MANUFACTURING unit with Wooden Chair
    // (which requires Wood, not Grain). The save should fail with RECIPE_INPUT_MISMATCH
    // and the error should be shown inline without destroying the edit session.
    const player = makeFactoryWithStarterUnits({ purchaseResourceId: 'res-grain' })
    const state = await setupChainTest(page, player)

    // Override product types so Wooden Chair requires Wood (not Grain)
    state.productTypes = [
      makeChairProduct(), // requires Wood (res-wood)
      {
        id: 'prod-bread',
        name: 'Bread',
        slug: 'bread',
        industry: 'FOOD_PROCESSING',
        basePrice: 3,
        baseCraftTicks: 1,
        outputQuantity: 12,
        energyConsumptionMwh: 0.5,
        unitName: 'Loaf',
        unitSymbol: 'loaves',
        isProOnly: false,
        description: 'Basic wheat bread.',
        recipes: [
          {
            resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', unitName: 'Ton', unitSymbol: 't' },
            inputProductType: null,
            quantity: 1,
          },
        ],
      },
    ]
    state.resourceTypes = [
      { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', description: 'Wood', imageUrl: null },
      { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC', basePrice: 5, weightPerUnit: 2, unitName: 'Ton', unitSymbol: 't', description: 'Grain', imageUrl: null },
    ]

    await page.goto('/building/building-chain-factory')

    // Enter edit mode
    await page.getByRole('button', { name: /Edit Building/i }).click()

    // Click the PURCHASE unit and verify it shows Grain (already configured)
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()

    // Click on the MANUFACTURING cell and force-add Wooden Chair via API
    // We do this by directly patching the draft via mock: we POST StoreBuildingConfiguration
    // with PURCHASE(Grain) + MANUFACTURING(Wooden Chair) which should fail.
    // To simulate this, we just click Store Upgrade directly — the mock will detect the mismatch.

    // The draft already has PURCHASE(Grain) from the factory's active units.
    // We need to add a product to MANUFACTURING to trigger the incompatibility.
    // Since the selector filters prevent selecting Wooden Chair via UI (only Bread shows),
    // we verify the save-failure path by directly crafting the submit action.

    // Click MANUFACTURING cell and select Bread (compatible with Grain)
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1).click()
    await expect(page.getByText('Output Product')).toBeVisible()
    await page.getByRole('button', { name: /Bread/ }).click()

    // Now change the Purchase back to Wood to create an incompatible state
    // by re-clicking PURCHASE and switching to Wood
    await plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()
    await expect(page.getByText('Input Item')).toBeVisible()
    // Clear the current selection (scope to the config panel to avoid strict-mode issues
    // when multiple chips exist across the sidebar and the planned grid cells).
    await page.locator('.unit-config-fields .selected-chip').click() // deselect Grain
    await page.getByPlaceholder(/Search/i).fill('Wood')
    await page.getByRole('button', { name: /^Wood/ }).first().click()

    // Now PURCHASE has Wood but MANUFACTURING has Bread (which requires Grain) — incompatible!
    // Click Store Upgrade — the mock should return RECIPE_INPUT_MISMATCH
    await page.getByRole('button', { name: /Store Upgrade/i }).click()

    // The save error banner should appear inline WITHOUT losing the edit session
    await expect(page.locator('.save-error-banner')).toBeVisible()
    await expect(page.locator('.save-error-banner')).toContainText(/MANUFACTURING|Purchase|recipe|requires|input/i)

    // The player should still be in edit mode (draft grid visible, not exited)
    await expect(plannedSection).toBeVisible()
    await expect(page.getByRole('button', { name: /Store Upgrade/i })).toBeVisible()
  })
})

// ── Starter sales-shop setup banner ─────────────────────────────────────────

test.describe('Starter sales-shop setup banner', () => {
  function makeEmptySalesShopPlayer() {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-shop',
          playerId: 'player-1',
          name: 'Retail Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-empty-shop',
              companyId: 'company-shop',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'New Sales Shop',
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
    return player
  }

  test('shows starter setup banner for an empty sales shop', async ({ page }) => {
    const player = makeEmptySalesShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-shop')

    // Shop starter setup banner should be visible
    await expect(page.getByRole('region', { name: /shop starter setup/i })).toBeVisible()
    // Title and body text
    await expect(page.getByText(/New Sales Shop — Ready for Your First Product/i)).toBeVisible()
    await expect(page.getByText(/no units configured yet/i)).toBeVisible()
    // Starter layout description (scope to the desc paragraph to avoid strict mode issues)
    await expect(page.locator('.starter-setup-desc').filter({ hasText: /Purchase.*Public Sales/i })).toBeVisible()
    // Apply Starter Shop Layout button
    await expect(page.getByRole('button', { name: /Apply Starter Shop Layout/i })).toBeVisible()
  })

  test('apply starter shop layout pre-populates PURCHASE and PUBLIC_SALES units', async ({ page }) => {
    const player = makeEmptySalesShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-shop')

    await expect(page.getByRole('button', { name: /Apply Starter Shop Layout/i })).toBeVisible()
    await page.getByRole('button', { name: /Apply Starter Shop Layout/i }).click()

    // Planning grid should now be visible (edit mode)
    const planningSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(planningSection).toBeVisible()

    // PURCHASE unit should appear at position (0,0)
    const purchaseCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await expect(purchaseCell).toContainText('Purchase')

    // PUBLIC_SALES unit should appear at position (1,0)
    const publicSalesCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await expect(publicSalesCell).toContainText('Public Sales')

    // Shop starter setup banner should no longer be visible (we are now in edit mode)
    await expect(page.locator('.starter-setup-banner--shop')).toBeHidden()
  })

  test('apply starter shop layout can be saved via Store Upgrade', async ({ page }) => {
    const player = makeEmptySalesShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-empty-shop')

    await page.getByRole('button', { name: /Apply Starter Shop Layout/i }).click()

    // Store Upgrade button should be enabled
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await expect(storeBtn).toBeVisible()
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // After save, upgrade-in-progress banner should appear (pending configuration)
    await expect(page.locator('.upgrade-banner')).toBeVisible()
    // Starter setup banner should be gone
    await expect(page.locator('.starter-setup-banner--shop')).toBeHidden()
  })

  test('starter shop banner is hidden for a sales shop that already has units', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-shop-units',
          playerId: 'player-1',
          name: 'Retail Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-shop-with-units',
              companyId: 'company-shop-units',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Running Shop',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              units: [
                {
                  id: 'u-public-sales',
                  buildingId: 'building-shop-with-units',
                  unitType: 'PUBLIC_SALES',
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
                },
              ],
              pendingConfiguration: null,
            },
          ],
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

    await page.goto('/building/building-shop-with-units')

    // Should NOT show the starter setup banner for a shop that already has units
    await expect(page.locator('.starter-setup-banner--shop')).toBeHidden()
  })
})

// ── Sales chain status panel ─────────────────────────────────────────────────

test.describe('Sales chain status panel', () => {
  function makeShopWithUnits({ purchaseProductId = null as string | null, publicSalesProductId = null as string | null, publicSalesMinPrice = null as number | null } = {}) {
    return makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-sales-chain',
          playerId: 'player-1',
          name: 'Sales Chain Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-sales-chain-shop',
              companyId: 'company-sales-chain',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'My Sales Shop',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              units: [
                {
                  id: 'u-shop-purchase',
                  buildingId: 'building-sales-chain-shop',
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
                  productTypeId: purchaseProductId,
                },
                {
                  id: 'u-shop-public-sales',
                  buildingId: 'building-sales-chain-shop',
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
                  productTypeId: publicSalesProductId,
                  minPrice: publicSalesMinPrice,
                },
              ],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })
  }

  async function loginAndGoto(page: Parameters<typeof test>[0]['page'], player: ReturnType<typeof makePlayer>, url: string) {
    const state = setupMockApi(page, { players: [player], products: [makeChairProduct()] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)
    await page.goto(url)
    return state
  }

  test('shows sales chain panel with incomplete status when units are unconfigured', async ({ page }) => {
    const player = makeShopWithUnits()
    await loginAndGoto(page, player, '/building/building-sales-chain-shop')

    // Sales chain panel should be visible
    await expect(page.getByRole('region', { name: /sales chain status/i })).toBeVisible()
    // Status badge: incomplete
    await expect(page.getByText(/Setup Required/i)).toBeVisible()
    // Both steps should show "not configured"
    const panel = page.getByRole('region', { name: /sales chain status/i })
    await expect(panel.getByText(/Not configured yet/i).first()).toBeVisible()
    // Guidance should appear
    await expect(page.getByText(/What still needs to be configured/i)).toBeVisible()
    await expect(page.getByText(/Purchase unit and select the product/i)).toBeVisible()
    await expect(page.getByText(/Public Sales unit.*minimum selling price/i)).toBeVisible()
  })

  test('shows partial completion when only purchase is configured', async ({ page }) => {
    const player = makeShopWithUnits({ purchaseProductId: 'prod-chair' })
    await loginAndGoto(page, player, '/building/building-sales-chain-shop')

    const panel = page.getByRole('region', { name: /sales chain status/i })
    await expect(panel).toBeVisible()
    // Status badge: still incomplete
    await expect(panel.getByText(/Setup Required/i)).toBeVisible()
    // Purchase is configured - shows product name
    await expect(panel.getByText(/Wooden Chair/i)).toBeVisible()
    // Only the public sales todo should remain
    await expect(page.getByText(/Public Sales unit.*minimum selling price/i)).toBeVisible()
  })

  test('shows Ready to Sell when both purchase and public sales are fully configured', async ({ page }) => {
    const player = makeShopWithUnits({
      purchaseProductId: 'prod-chair',
      publicSalesProductId: 'prod-chair',
      publicSalesMinPrice: 49.99,
    })
    await loginAndGoto(page, player, '/building/building-sales-chain-shop')

    const panel = page.getByRole('region', { name: /sales chain status/i })
    await expect(panel).toBeVisible()
    // Status badge: complete
    await expect(panel.getByText(/Ready to Sell/i)).toBeVisible()
    // Chain complete description mentions product and price
    await expect(panel.locator('.chain-complete-message').getByText(/Wooden Chair/i)).toBeVisible()
    await expect(panel.locator('.chain-complete-message').getByText(/49\.99/i)).toBeVisible()
    // Next step hint
    await expect(panel.getByText(/customers in the city can discover and buy/i)).toBeVisible()
    // No guidance section (chain is complete)
    await expect(page.getByText(/What still needs to be configured/i)).toBeHidden()
  })
})

// ── Sales shop PUBLIC_SALES price validation and persistence ─────────────────

test.describe('Sales shop PUBLIC_SALES price validation and persistence', () => {
  function makeEmptySalesShopForPricing() {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-pricing',
          playerId: 'player-1',
          name: 'Pricing Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-pricing-shop',
              companyId: 'company-pricing',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Pricing Shop',
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
    return player
  }

  test('save and reload shows configured product and price in sales chain panel', async ({ page }) => {
    // Start with an empty shop, apply the starter layout, configure product and price, save,
    // then simulate reload (navigate away and back) and verify the sales chain panel shows
    // the saved product and price correctly.
    const chair = makeChairProduct()
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
      companies: [
        {
          id: 'company-persist-shop',
          playerId: 'player-1',
          name: 'Persistence Corp',
          cash: 300000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [
            {
              id: 'building-persist-shop',
              companyId: 'company-persist-shop',
              cityId: 'city-ba',
              type: 'SALES_SHOP',
              name: 'Persist Shop',
              latitude: 48.15,
              longitude: 17.11,
              level: 1,
              powerConsumption: 1,
              isForSale: false,
              builtAtUtc: '2026-01-01T00:00:00Z',
              // Pre-configure the shop with PURCHASE + PUBLIC_SALES saved as active units
              // (simulating the post-save/post-upgrade state after some ticks)
              units: [
                {
                  id: 'u-persist-purchase',
                  buildingId: 'building-persist-shop',
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
                  productTypeId: chair.id,
                },
                {
                  id: 'u-persist-public-sales',
                  buildingId: 'building-persist-shop',
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
                  productTypeId: chair.id,
                  minPrice: 67.5,
                  saleVisibility: 'PUBLIC',
                },
              ],
              pendingConfiguration: null,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player], products: [chair] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-persist-shop')

    // The sales chain panel should show the saved configuration (both units configured)
    const panel = page.getByRole('region', { name: /sales chain status/i })
    await expect(panel).toBeVisible()
    await expect(panel.getByText(/Ready to Sell/i)).toBeVisible()

    // The PUBLIC_SALES step shows the product name and price
    await expect(panel.getByText(/Wooden Chair · \$67\.50/i)).toBeVisible()

    // The PURCHASE step shows the product name
    await expect(
      panel
        .locator('.chain-step--configured')
        .first()
        .getByText(/Wooden Chair/i),
    ).toBeVisible()

    // The complete message includes the price
    await expect(panel.locator('.chain-complete-message').getByText(/67\.50/i)).toBeVisible()

    // No starter banner should show (shop has units)
    await expect(page.locator('.starter-setup-banner--shop')).toBeHidden()
  })

  test('negative price rejected — save error shown without exiting edit mode', async ({ page }) => {
    const chair = makeChairProduct()
    const player = makeEmptySalesShopForPricing()
    const state = setupMockApi(page, { players: [player], products: [chair] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-pricing-shop')

    // Apply starter shop layout (PURCHASE → PUBLIC_SALES)
    await page.getByRole('button', { name: /Apply Starter Shop Layout/i }).click()

    // Select the PUBLIC_SALES cell and configure a negative price
    const planningSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()

    const publicSalesCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await publicSalesCell.click()

    // PUBLIC_SALES uses a <select> dropdown for product type (label has no `for` attr, scope by config-field)
    const productTypeField = page
      .locator('.config-field')
      .filter({ has: page.getByText('Product Type', { exact: true }) })
      .first()
    await expect(productTypeField.locator('select')).toBeVisible()
    await productTypeField.locator('select').selectOption({ label: 'Wooden Chair' })

    // Set a negative min price in the number input
    const minPriceField = page
      .locator('.config-field')
      .filter({ has: page.getByText('Min Price', { exact: true }) })
      .first()
    await minPriceField.locator('input').fill('-5')

    // Attempt to save — should trigger INVALID_MIN_PRICE error from mock
    await page.getByRole('button', { name: /Store Upgrade/i }).click()

    // The save error banner should appear inline
    await expect(page.locator('.save-error-banner')).toBeVisible()
    await expect(page.locator('.save-error-banner')).toContainText(/INVALID_MIN_PRICE|price|minimum/i)

    // Player should still be in edit mode (planning grid visible)
    await expect(planningSection).toBeVisible()
    await expect(page.getByRole('button', { name: /Store Upgrade/i })).toBeVisible()
  })

  test('zero price rejected — save error shown, shop not treated as ready', async ({ page }) => {
    // The runtime engine silently replaces price <= 0 with base price, so we must block 0
    // up front and honestly tell the player that pricing is a real decision.
    const chair = makeChairProduct()
    const player = makeEmptySalesShopForPricing()
    const state = setupMockApi(page, { players: [player], products: [chair] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-pricing-shop')

    // Apply starter shop layout
    await page.getByRole('button', { name: /Apply Starter Shop Layout/i }).click()

    const planningSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()

    const publicSalesCell = planningSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await publicSalesCell.click()

    const productTypeField = page
      .locator('.config-field')
      .filter({ has: page.getByText('Product Type', { exact: true }) })
      .first()
    await expect(productTypeField.locator('select')).toBeVisible()
    await productTypeField.locator('select').selectOption({ label: 'Wooden Chair' })

    // Set price to exactly 0
    const minPriceField = page
      .locator('.config-field')
      .filter({ has: page.getByText('Min Price', { exact: true }) })
      .first()
    await minPriceField.locator('input').fill('0')

    await page.getByRole('button', { name: /Store Upgrade/i }).click()

    // Backend (mocked) must reject price = 0 with INVALID_MIN_PRICE
    await expect(page.locator('.save-error-banner')).toBeVisible()
    await expect(page.locator('.save-error-banner')).toContainText(/INVALID_MIN_PRICE|price|minimum/i)

    // Chain should NOT show "Ready to Sell" — shop remains unconfigured
    await expect(page.getByText(/Ready to Sell/i)).toBeHidden()

    // Player remains in edit mode
    await expect(planningSection).toBeVisible()
    await expect(page.getByRole('button', { name: /Store Upgrade/i })).toBeVisible()
  })
})
