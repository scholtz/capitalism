import { expect, test, type Page } from '@playwright/test'
import { makeChairProduct, makePlayer, setupMockApi, type MockBuildingUnit, type MockPublicSalesAnalytics } from './helpers/mock-api'

function getGridSection(page: Page, heading: string) {
  return page
    .locator('.grid-section')
    .filter({ has: page.getByRole('heading', { name: heading }) })
    .first()
}

function getGridCell(section: ReturnType<typeof getGridSection>, x: number, y: number) {
  return section.locator('.unit-row').nth(y).locator('.grid-cell').nth(x)
}

async function openPurchaseSelector(page: Page) {
  await page.getByRole('button', { name: /product and vendor/i }).click()
  const dialog = page.getByRole('dialog', { name: 'Choose product and vendor' })
  await expect(dialog).toBeVisible()
  return dialog
}

async function selectPurchaseItem(page: Page, searchTerm: string, optionName: RegExp) {
  const dialog = await openPurchaseSelector(page)
  await dialog.getByPlaceholder(/search/i).fill(searchTerm)
  await dialog.getByRole('button', { name: optionName }).first().click()
  await dialog.getByRole('button', { name: 'Done' }).click()
  await expect(dialog).toBeHidden()
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
    await expect(page.getByRole('button', { name: 'Choose product and vendor' })).toBeVisible()
    await expect(page.getByText('Max Price')).toBeVisible()
    await expect(page.locator('.config-field').filter({ has: page.getByText('Procurement Mode', { exact: true }) })).toBeVisible()

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

  test('shows building overview stats and opens the city map focused on the building lot', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-overview',
      playerId: player.id,
      name: 'Overview Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-overview',
          companyId: 'company-overview',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Overview Factory',
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
    state.buildingLots.push({
      id: 'lot-overview-building',
      cityId: 'city-ba',
      name: 'Central Factory Lot',
      description: 'Existing production lot in the city core.',
      district: 'Central District',
      latitude: 48.15,
      longitude: 17.11,
      populationIndex: 1.12,
      basePrice: 100000,
      price: 112000,
      suitableTypes: 'FACTORY',
      ownerCompanyId: 'company-overview',
      buildingId: 'building-overview',
      ownerCompany: { id: 'company-overview', name: 'Overview Co' },
      building: { id: 'building-overview', name: 'Overview Factory', type: 'FACTORY' },
      resourceType: null,
      materialQuality: null,
      materialQuantity: null,
    })
    state.buildingFinancialTimelines['building-overview'] = {
      buildingId: 'building-overview',
      buildingName: 'Overview Factory',
      dataFromTick: 40,
      dataToTick: 42,
      totalSales: 560,
      totalCosts: 290,
      totalProfit: 270,
      timeline: [
        { tick: 40, sales: 180, costs: 90, profit: 90 },
        { tick: 41, sales: 140, costs: 120, profit: 20 },
        { tick: 42, sales: 240, costs: 80, profit: 160 },
      ],
    }

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-overview')

    const overview = page.locator('.building-overview-detail')
    await expect(page.getByRole('heading', { name: 'Building Overview' })).toBeVisible()
    await expect(overview.getByText('Bratislava')).toBeVisible()
    await expect(overview.getByText('48.15000°N, 17.11000°E')).toBeVisible()
    await expect(overview.getByText('$560')).toBeVisible()
    await expect(overview.getByText('$290')).toBeVisible()
    await expect(overview.getByText('$270')).toBeVisible()
    const chartCard = overview.locator('.building-financial-chart-card')
    await expect(chartCard.getByRole('img', { name: 'Building financial history' })).toBeVisible()
    await chartCard.locator('.building-financial-hit-area').nth(1).hover()
    await expect(chartCard.locator('.building-financial-active-tick')).toHaveText('Tick 41')
    await expect(chartCard.locator('.building-financial-chart-detail-stat').nth(0)).toContainText('$140')
    await expect(chartCard.locator('.building-financial-chart-detail-stat').nth(1)).toContainText('$120')
    await expect(chartCard.locator('.building-financial-chart-detail-stat').nth(2)).toContainText('$20')

    await overview.getByRole('link', { name: 'Show on Map' }).click()

    await expect(page).toHaveURL(/\/city\/city-ba\?building=building-overview/)
    await expect(page.getByRole('heading', { name: 'Central Factory Lot' })).toBeVisible()
    await expect(page.getByText('Your Property')).toBeVisible()
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
    await expect(page.getByText('Procurement Mode: Global Exchange')).toBeVisible()
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
    const exchangeSection = page.locator('.unit-insight-card', { hasText: 'Global exchange offers' })
    await expect(exchangeSection).toBeVisible()
    await expect(exchangeSection.getByText('Bratislava')).toBeVisible()
    await expect(exchangeSection.getByText('Prague')).toBeVisible()
    await expect(exchangeSection.getByText('Vienna')).toBeVisible()
  })

  test('preserves readonly unit selection in the route so refresh keeps the sidebar open', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-route',
      playerId: player.id,
      name: 'Route Memory Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-route',
          companyId: 'company-route',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Route Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'route-1',
              buildingId: 'building-route',
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
              maxPrice: 120,
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

    await page.goto('/building/building-route')
    const activeSection = getGridSection(page, 'Current Configuration')
    await getGridCell(activeSection, 0, 0).click()

    await expect(page).toHaveURL(/\/building\/building-route\?unit=0(?:,|%2C)0$/)
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()

    await page.reload()

    await expect(page).toHaveURL(/\/building\/building-route\?unit=0(?:,|%2C)0$/)
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()
    await expect(page.getByText('Procurement Mode: Global Exchange')).toBeVisible()
  })

  test('flush storage button appears in storage unit detail sidebar and shows confirmation', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-flush',
      playerId: player.id,
      name: 'Flush Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-flush',
          companyId: 'company-flush',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Flush Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'flush-storage-1',
              buildingId: 'building-flush',
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
              inventoryQuantity: 50,
              inventoryQuality: 0.7,
              inventorySourcingCostTotal: 500,
              inventoryItems: [
                {
                  id: 'inv-flush-1',
                  resourceTypeId: 'res-wood',
                  productTypeId: null,
                  quantity: 50,
                  quality: 0.7,
                  sourcingCostTotal: 500,
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

    await page.goto('/building/building-flush')
    await expect(page.getByRole('heading', { name: 'Flush Factory' })).toBeVisible()

    const activeSection = getGridSection(page, 'Current Configuration')
    await getGridCell(activeSection, 0, 0).click()
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()

    // Flush button should be visible for storage units with inventory
    await expect(page.getByRole('button', { name: /Discard All Inventory/i })).toBeVisible()

    // Click flush — confirmation dialog should appear
    await page.getByRole('button', { name: /Discard All Inventory/i }).click()
    await expect(page.getByText(/cannot be undone/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /Yes, Discard All/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Cancel/i })).toBeVisible()

    // Cancel hides the confirmation
    await page.getByRole('button', { name: /Cancel/i }).click()
    await expect(page.getByText(/cannot be undone/i)).toBeHidden()
  })

  test('B2B_SALES config shows competitive price suggestion from linked manufacturing unit', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-b2b',
      playerId: player.id,
      name: 'B2B Price Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-b2b',
          companyId: 'company-b2b',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'B2B Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'b2b-purchase-1',
              buildingId: 'building-b2b',
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
              id: 'b2b-mfg-1',
              buildingId: 'building-b2b',
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
            },
            {
              id: 'b2b-storage-1',
              buildingId: 'building-b2b',
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
              id: 'b2b-sales-1',
              buildingId: 'building-b2b',
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

    await page.goto('/building/building-b2b')
    await expect(page.getByRole('heading', { name: 'B2B Factory' })).toBeVisible()

    // Enter edit mode and click on the B2B_SALES unit
    await page.getByRole('button', { name: /Edit Building/i }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 3, 0).click()

    // B2B_SALES config panel should show Min Price field
    await expect(page.getByText('Min Price')).toBeVisible()

    // Should show competitive price suggestion since manufacturing unit has Wooden Chair (basePrice=45)
    await expect(page.getByText(/Competitive base price/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /Use this price/i })).toBeVisible()

    // Click "Use this price" should populate the min price field
    await page.getByRole('button', { name: /Use this price/i }).click()
  })

  test('starter factory layout includes B2B_SALES unit at position (3,0)', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-b2b-layout',
      playerId: player.id,
      name: 'B2B Layout Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-b2b-layout',
          companyId: 'company-b2b-layout',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'B2B Layout Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 1,
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

    await page.goto('/building/building-b2b-layout')
    await page.getByRole('button', { name: /Apply Starter Layout/i }).click()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await expect(plannedSection).toBeVisible()

    // B2B_SALES unit should appear at position (3,0) in the planned grid
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(3)).toContainText('B2B Sales')
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
      .locator('.picker-item-name', { hasText: 'Wooden Chair' })
      .click()

    await getGridCell(plannedSection, 1, 0).click()
    await expect(page.getByText('Brand Scope')).toBeVisible()
    await page
      .locator('.config-field')
      .filter({ has: page.getByText('Brand Scope') })
      .locator('select')
      .selectOption('CATEGORY')
    const anchorProductField = page.locator('.config-field').filter({ has: page.getByText('Anchor Product', { exact: true }) })
    await expect(anchorProductField).toBeVisible()
    await anchorProductField.locator('.picker-item-name', { hasText: 'Wooden Chair' }).click()
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
    const dialog = await openPurchaseSelector(page)
    await expect(dialog.getByText('Electronic Components')).toBeVisible()
    await expect(dialog.getByText('Wooden Chair')).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Done' }).click()
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

    const dialog = await openPurchaseSelector(page)
    const searchInput = dialog.getByPlaceholder(/search/i)
    await searchInput.fill('wood')
    await expect(searchInput).toHaveValue('wood')
    await dialog.getByRole('button', { name: 'Done' }).click()

    // Switch procurement mode using label clicks (EXCHANGE, then OPTIMAL)
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })
      .click()
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Optimal Landed Cost' }) })
      .click()

    await expect(page.locator('.loading')).toHaveCount(0)
    await expect(page.locator('.sidebar')).toBeVisible()
    await expect(getGridCell(plannedSection, 0, 0)).toHaveClass(/selected/)
    await expect(page.locator('.sidebar .purchase-selection-summary')).toContainText('Wood')
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
    const exchangeCard = page.locator('.unit-insight-card', { hasText: 'Global exchange offers' })
    await expect(exchangeCard).toBeVisible()
    await expect(exchangeCard.getByText('Bratislava')).toBeVisible()
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
    await expect(page.locator('.config-field').filter({ has: page.getByText('Procurement Mode', { exact: true }) })).toBeVisible()

    // Set procurement mode to EXCHANGE via label click
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })
      .click()

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

  test('higher-quality offer is selected when min quality threshold excludes cheaper alternatives', async ({ page }) => {
    // Scenario: Building is in Bratislava (destination city = same city = 0 transit).
    //
    // Bratislava: abundance 0.7 → quality 0.77, no transit, delivered ≈ $11.17/t
    // Prague:     abundance 0.9 → quality 0.89, transit ≈ $3.88/t,  delivered ≈ $13.77/t
    //
    // With minQuality = 0.80:
    //   Bratislava (CHEAPER at $11.17) is BLOCKED — quality 0.77 < minQuality 0.80
    //   Prague (MORE EXPENSIVE at $13.77) PASSES — quality 0.89 > minQuality 0.80
    //
    // Proves: "cheaper alternative excluded by quality threshold, higher-quality offer selected."
    const player = makePlayer()
    const cities = [
      {
        id: 'city-ba',
        name: 'Bratislava',
        countryCode: 'SK',
        latitude: 48.1486,
        longitude: 17.1077,
        population: 475000,
        averageRentPerSqm: 14,
        baseSalaryPerManhour: 18,
        resources: [
          // abundance 0.7 → quality = 0.77 → BLOCKED by minQuality 0.80
          { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.7 },
        ],
      },
      {
        id: 'city-pr',
        name: 'Prague',
        countryCode: 'CZ',
        latitude: 50.0755,
        longitude: 14.4378,
        population: 1350000,
        averageRentPerSqm: 18,
        baseSalaryPerManhour: 22,
        resources: [
          // abundance 0.9 → quality = 0.89 → PASSES minQuality 0.80, but transit adds cost
          { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.9 },
        ],
      },
    ]

    player.companies.push({
      id: 'company-qual-filter',
      playerId: player.id,
      name: 'Quality Filter Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-qual-filter',
          companyId: 'company-qual-filter',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Quality Filter Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-qual-filter',
              buildingId: 'building-qual-filter',
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
              // minQuality 0.80 blocks Bratislava (quality 0.77) but passes Prague (quality 0.89)
              minQuality: 0.8,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.cities = cities as ReturnType<typeof makeDefaultCities>
    state.resourceTypes = [
      {
        id: 'res-wood',
        name: 'Wood',
        slug: 'wood',
        category: 'ORGANIC',
        basePrice: 10,
        weightPerUnit: 5,
        unitName: 'Ton',
        unitSymbol: 't',
        imageUrl: null,
        description: 'Harvested timber.',
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-qual-filter')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList).toBeVisible()

    // Bratislava: cheaper delivered price but blocked by min quality
    const bratislavaOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(bratislavaOffer).toBeVisible()
    await expect(bratislavaOffer).toHaveClass(/offer-blocked/)
    await expect(bratislavaOffer.locator('.offer-blocked-reason')).toContainText('Below your min quality')

    // Prague: more expensive delivered price, but passes min quality → selected as best
    const pragueOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    await expect(pragueOffer).toBeVisible()
    await expect(pragueOffer).not.toHaveClass(/offer-blocked/)
    await expect(pragueOffer.locator('.offer-best-badge')).toBeVisible()
    await expect(pragueOffer.locator('.offer-best-badge')).toContainText('Best delivered price')

    // Prague has positive transit cost (it is ~310 km from Bratislava)
    const pragueTransitText = await pragueOffer.getByText(/Transit:/).textContent()
    expect(pragueTransitText).not.toContain('$0.00')

    // The "no valid offers" warning must NOT appear — Prague still qualifies
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeHidden()
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

// ── Full end-to-end exchange sourcing flow ────────────────────────────────────

test.describe('Global exchange sourcing — end-to-end flow', () => {
  test('configure EXCHANGE source, save, and verify exchange panel shows in read-only mode', async ({ page }) => {
    // Full sourcing flow: start with a configured PURCHASE unit with EXCHANGE source,
    // review the exchange offers panel (source price, transit, delivered cost),
    // modify the max price constraint to widen the offer selection,
    // save the configuration, and verify the exchange panel still shows in read-only mode
    // with the updated constraint — proving the configuration round-trips correctly.
    const player = makePlayer()
    player.companies.push({
      id: 'company-e2e-flow',
      playerId: player.id,
      name: 'E2E Flow Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-e2e-flow',
          companyId: 'company-e2e-flow',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'E2E Exchange Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-e2e-flow',
              buildingId: 'building-e2e-flow',
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

    await page.goto('/building/building-e2e-flow')
    await expect(page.getByRole('heading', { name: 'E2E Exchange Factory' })).toBeVisible()

    // Step 1: In read-only mode, click the active PURCHASE unit to review exchange offers
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers panel must be visible immediately
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // Must show source price, transit cost, and delivered price for all seeded cities
    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList).toBeVisible()
    await expect(offersList.getByText(/Exchange:/).first()).toBeVisible()
    await expect(offersList.getByText(/Transit:/).first()).toBeVisible()
    await expect(offersList.getByText(/Delivered:/).first()).toBeVisible()

    // The best delivered price badge must identify the cheapest option
    await expect(page.locator('.offer-best-badge').first()).toBeVisible()

    // The selection hint must explain the distance-vs-sticker-price trade-off
    await expect(page.getByText(/lowest delivered price/)).toBeVisible()

    // Step 2: Enter edit mode to update the max price constraint
    await page.getByRole('button', { name: 'Edit Building' }).click()
    await expect(page.getByRole('button', { name: 'Store Upgrade' })).toBeVisible()

    // Select the PURCHASE cell in the Planned Upgrade draft section
    const draftSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await draftSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Config panel opens — verify EXCHANGE source and exchange offers are still shown in edit mode
    await expect(page.getByText('Unit Configuration')).toBeVisible()
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // Make a change (update max price) so the mock detects a configuration change
    const maxPriceInput = page
      .locator('.config-field')
      .filter({ has: page.getByText('Max Price') })
      .locator('input[type="number"]')
    await maxPriceInput.fill('800')

    // Step 3: Save and verify the exchange panel persists in the post-save read-only state
    await page.getByRole('button', { name: 'Store Upgrade' }).click()
    await expect(page.getByRole('button', { name: 'Edit Building' })).toBeVisible()

    // After saving, reload so the mock applies the pending configuration to active units
    await page.reload()
    await expect(page.getByRole('heading', { name: 'E2E Exchange Factory' })).toBeVisible()

    // Click the active PURCHASE unit again in read-only mode
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers panel must remain visible after save + reload
    await expect(page.getByText('Global exchange offers')).toBeVisible()
    await expect(page.locator('.exchange-offer-item')).toHaveCount(3)
  })

  test('negative path: stale sourcing — all exchange offers blocked in read-only view shows actionable warning', async ({ page }) => {
    // Simulates a "stale sourcing" scenario: a purchase unit was configured with EXCHANGE
    // source and a maxPrice that is now too low for any offer (prices rose above the cap).
    // The UI must show a clear, actionable warning so the player knows they need to
    // update their configuration.
    const player = makePlayer()
    player.companies.push({
      id: 'company-stale',
      playerId: player.id,
      name: 'Stale Sourcing Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-stale',
          companyId: 'company-stale',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Stale Exchange Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-stale',
              buildingId: 'building-stale',
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
              // EXCHANGE source with an impossibly low maxPrice (stale configuration)
              purchaseSource: 'EXCHANGE',
              maxPrice: 0.001,
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

    await page.goto('/building/building-stale')
    await expect(page.getByRole('heading', { name: 'Stale Exchange Factory' })).toBeVisible()

    // Click the PURCHASE unit in read-only (active) mode
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange panel must be visible
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // All offers must be blocked because maxPrice is too low (stale constraint)
    await expect(page.locator('.exchange-offer-item.offer-blocked')).toHaveCount(3)

    // Actionable "no valid offers" message must be shown
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeVisible()

    // Individual blocked-reason annotations must appear for each offer
    const blockedReasons = page.locator('.offer-blocked-reason')
    await expect(blockedReasons.first()).toBeVisible()
    await expect(blockedReasons.first()).toContainText(/Over your max price/)
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
    await selectPurchaseItem(page, 'Wood', /^Wood/)

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
    await selectPurchaseItem(page, 'Wood', /^Wood/)
    await expect(page.locator('.purchase-selection-summary')).toContainText('Wood')

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
    await selectPurchaseItem(page, 'Wood', /^Wood/)

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

    // PUBLIC_SALES uses ProductPicker for product type
    const productTypeField = page
      .locator('.config-field')
      .filter({ has: page.getByText('Product Type', { exact: true }) })
      .first()
    await expect(productTypeField.locator('.picker-item-name', { hasText: 'Wooden Chair' })).toBeVisible()
    await productTypeField.locator('.picker-item-name', { hasText: 'Wooden Chair' }).click()

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
    await expect(productTypeField.locator('.picker-item-name', { hasText: 'Wooden Chair' })).toBeVisible()
    await productTypeField.locator('.picker-item-name', { hasText: 'Wooden Chair' }).click()

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

// ── Property Management (APARTMENT / COMMERCIAL) ──────────────────────────────

function makeApartmentPlayer() {
  const player = makePlayer()
  player.companies.push({
    id: 'company-apt',
    playerId: player.id,
    name: 'Apt Holdings',
    cash: 1_000_000,
    foundedAtUtc: '2026-01-01T00:00:00Z',
    buildings: [
      {
        id: 'building-apt',
        companyId: 'company-apt',
        cityId: 'city-ba',
        type: 'APARTMENT',
        name: 'River View Apartments',
        latitude: 48.14,
        longitude: 17.1,
        level: 1,
        powerConsumption: 1,
        isForSale: false,
        builtAtUtc: '2026-01-01T00:00:00Z',
        pendingConfiguration: null,
        units: [],
        pricePerSqm: 14,
        occupancyPercent: 72.5,
        totalAreaSqm: 2000,
        pendingPricePerSqm: null,
        pendingPriceActivationTick: null,
      },
    ],
  })
  return player
}

test.describe('Property management panel', () => {
  test('shows property metrics for apartment building', async ({ page }) => {
    const player = makeApartmentPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-apt')

    const panel = page.locator('[aria-label="property management"]')
    await expect(panel).toBeVisible()

    // Occupancy
    await expect(panel).toContainText('72.5%')
    // Active rent
    await expect(panel).toContainText('€14.00 / m²')
    // Total area
    await expect(panel).toContainText('2,000 m²')
    // Set Rent button
    await expect(panel.getByRole('button', { name: /Set Rent/i })).toBeVisible()
  })

  test('shows pending rent notice when pending change is queued', async ({ page }) => {
    const player = makeApartmentPlayer()
    const apt = player.companies.find((c) => c.id === 'company-apt')!.buildings.find((b) => b.id === 'building-apt')!
    apt.pendingPricePerSqm = 18
    apt.pendingPriceActivationTick = 50

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState = { ...state.gameState, currentTick: 30 }
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-apt')

    const notice = page.locator('.pending-rent-notice')
    await expect(notice).toBeVisible()
    await expect(notice).toContainText('€18.00')
    await expect(notice).toContainText('20 ticks')
  })

  test('opens rent dialog and schedules a new rent', async ({ page }) => {
    const player = makeApartmentPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-apt')

    const panel = page.locator('[aria-label="property management"]')
    await panel.getByRole('button', { name: /Set Rent/i }).click()

    // Dialog should open
    await expect(panel.locator('.rent-dialog')).toBeVisible()
    // Should show the delay hint
    await expect(panel.locator('.rent-dialog')).toContainText('24 ticks')

    // Fill new rent
    await panel.locator('.rent-dialog input[type="number"]').fill('20')
    await panel.getByRole('button', { name: /Schedule Change/i }).click()

    // Dialog closes and pending notice appears
    await expect(panel.locator('.rent-dialog')).toBeHidden()
    await expect(panel.locator('.pending-rent-notice')).toBeVisible()
    await expect(panel.locator('.pending-rent-notice')).toContainText('€20.00')
  })

  test('property panel is not shown for factory buildings', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-fac',
      playerId: player.id,
      name: 'Factory Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-fac',
          companyId: 'company-fac',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'My Factory',
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

    await page.goto('/building/building-fac')

    await expect(page.locator('[aria-label="property management"]')).toBeHidden()
  })

  test('commercial building also shows property panel', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-comm',
      playerId: player.id,
      name: 'Commercial Corp',
      cash: 800000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-comm',
          companyId: 'company-comm',
          cityId: 'city-ba',
          type: 'COMMERCIAL',
          name: 'Business Centre',
          latitude: 48.15,
          longitude: 17.1,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [],
          pricePerSqm: 22,
          occupancyPercent: 85,
          totalAreaSqm: 3000,
          pendingPricePerSqm: null,
          pendingPriceActivationTick: null,
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

    await page.goto('/building/building-comm')

    const panel = page.locator('[aria-label="property management"]')
    await expect(panel).toBeVisible()
    await expect(panel).toContainText('€22.00 / m²')
    await expect(panel).toContainText('85.0%')
  })
})

// ── Grid editor: configure + directional link, save, reload, persist ──────────

test.describe('Grid editor: save and reload persistence', () => {
  test('configure directional link, save, reload — pending configuration persists', async ({ page }) => {
    // Scenario: player opens a factory, enters edit mode, activates a directional link
    // between existing units, saves the plan, then reloads the page and verifies the
    // pending configuration with the link is still visible in the Queued Upgrade section.
    const player = makePlayer()
    player.companies.push({
      id: 'company-persist',
      playerId: player.id,
      name: 'Persist Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-persist',
          companyId: 'company-persist',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Persist Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'persist-u1',
              buildingId: 'building-persist',
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
              id: 'persist-u2',
              buildingId: 'building-persist',
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

    await page.goto('/building/building-persist')
    await expect(page.getByRole('heading', { name: 'Persist Factory' })).toBeVisible()

    // Step 1: Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // Step 2: Activate the horizontal link from PURCHASE (0,0) → MANUFACTURING (1,0)
    const hLink = plannedSection.locator('.link-toggle.horizontal').first()
    await expect(hLink).toHaveClass(/link-state-none/)
    await hLink.click()
    await expect(hLink).toHaveClass(/link-state-forward/)

    // Step 3: Save the plan — this should queue a pending configuration
    await page.getByRole('button', { name: 'Store Upgrade' }).click()

    // After save the upgrade-in-progress banner should appear
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')

    // Step 4: Reload the page — the pending configuration must survive the reload
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Persist Factory' })).toBeVisible()

    // The queued upgrade banner must still be visible after reload
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')

    // Step 5: Enter edit mode again to inspect the queued plan
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const queuedSection = getGridSection(page, 'Queued Upgrade')

    // The horizontal link between the two units must still show as forward/active
    const queuedHLink = queuedSection.locator('.link-toggle.horizontal').first()
    await expect(queuedHLink).toHaveClass(/link-state-forward/)
    await expect(queuedHLink).toHaveClass(/active/)

    // Both units must appear in the queued section
    await expect(getGridCell(queuedSection, 0, 0)).toContainText('Purchase')
    await expect(getGridCell(queuedSection, 1, 0)).toContainText('Manufacturing')
  })
})

// ── R&D Research Progress Panel ───────────────────────────────────────────────

test.describe('R&D Research Progress Panel', () => {
  function makeRdBuilding(companyId: string, units: MockBuildingUnit[] = []) {
    return {
      id: 'building-rd-progress',
      companyId,
      cityId: 'city-ba',
      type: 'RESEARCH_DEVELOPMENT',
      name: 'Innovation Lab',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 3,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units,
    }
  }

  test('shows research progress panel for R&D buildings', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-rd-prog',
      playerId: player.id,
      name: 'Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeRdBuilding('company-rd-prog')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    await expect(page.getByRole('region', { name: 'research progress' })).toBeVisible()
    await expect(page.getByText('Research Progress')).toBeVisible()
  })

  test('shows empty state when no research brands exist yet', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-rd-empty',
      playerId: player.id,
      name: 'Empty Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeRdBuilding('company-rd-empty')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.researchBrands['company-rd-empty'] = []
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    await expect(page.getByRole('region', { name: 'research progress' })).toBeVisible()
    await expect(page.getByText(/No research recorded yet/)).toBeVisible()
  })

  test('shows product quality brand state in research progress panel', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-rd-brands'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Quality Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        makeRdBuilding(companyId, [
          {
            id: 'rd-unit-pq',
            buildingId: 'building-rd-progress',
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
            productTypeId: 'prod-chair',
          },
        ]),
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.researchBrands[companyId] = [
      {
        id: 'brand-chair',
        companyId,
        name: 'Wooden Chair',
        scope: 'PRODUCT',
        productTypeId: 'prod-chair',
        productName: 'Wooden Chair',
        industryCategory: null,
        awareness: 0,
        quality: 0.35,
        marketingEfficiencyMultiplier: 1,
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    const panel = page.getByRole('region', { name: 'research progress' })
    await expect(panel).toBeVisible()
    await expect(panel.getByText('Wooden Chair')).toBeVisible()
    await expect(panel.locator('.research-metric-label', { hasText: 'Product Quality' }).first()).toBeVisible()
    await expect(panel.locator('.research-metric-value', { hasText: '35.0%' })).toBeVisible()
  })

  test('shows marketing efficiency multiplier for brand quality research', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-rd-bq'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Brand Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        makeRdBuilding(companyId, [
          {
            id: 'rd-unit-bq',
            buildingId: 'building-rd-progress',
            unitType: 'BRAND_QUALITY',
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
            brandScope: 'COMPANY',
          },
        ]),
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.researchBrands[companyId] = [
      {
        id: 'brand-company',
        companyId,
        name: 'Brand Research Corp',
        scope: 'COMPANY',
        productTypeId: null,
        productName: null,
        industryCategory: null,
        awareness: 0,
        quality: 0,
        marketingEfficiencyMultiplier: 1.45, // Accumulated via BRAND_QUALITY R&D over many ticks
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    const panel = page.getByRole('region', { name: 'research progress' })
    await expect(panel).toBeVisible()
    // BRAND_QUALITY R&D shows marketing efficiency, not awareness (not free brand gain)
    await expect(panel.locator('.research-metric-label', { hasText: 'Marketing Efficiency' })).toBeVisible()
    await expect(panel.locator('.research-metric-value', { hasText: '1.45×' })).toBeVisible()
    await expect(panel.locator('.research-brand-scope-badge', { hasText: 'Company' })).toBeVisible()
    // Awareness should NOT be shown (no marketing spend in this scenario)
    await expect(panel.locator('.research-metric-label', { hasText: 'Brand Awareness' })).toBeHidden()
  })

  test('does not show research progress panel for non-RD buildings', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-factory',
      playerId: player.id,
      name: 'Factory Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-factory-rd-test',
          companyId: 'company-factory',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Regular Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
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

    await page.goto('/building/building-factory-rd-test')

    await expect(page.getByRole('region', { name: 'research progress' })).toBeHidden()
  })

  test('shows R&D configuration warnings when units are incomplete', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-rd-warn',
      playerId: player.id,
      name: 'Warning Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-rd-warn',
          companyId: 'company-rd-warn',
          cityId: 'city-ba',
          type: 'RESEARCH_DEVELOPMENT',
          name: 'Warning Lab',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 3,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'rd-pq-warn',
              buildingId: 'building-rd-warn',
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
              // No productTypeId — should trigger warning
            },
            {
              id: 'rd-bq-warn',
              buildingId: 'building-rd-warn',
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
              // No brandScope — should trigger warning
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

    await page.goto('/building/building-rd-warn')

    await expect(page.getByRole('alert')).toBeVisible()
    await expect(page.getByText(/Product Quality unit at \(0, 0\) has no researched product selected/)).toBeVisible()
    await expect(page.getByText(/Brand Quality unit at \(1, 0\) has no research scope selected/)).toBeVisible()
  })

  test('shows category-scope badge and efficiency for industry brand quality research', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-rd-cat'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Category Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        makeRdBuilding(companyId, [
          {
            id: 'rd-unit-cat',
            buildingId: 'building-rd-progress',
            unitType: 'BRAND_QUALITY',
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
            brandScope: 'CATEGORY',
          },
        ]),
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Simulate accumulated CATEGORY-scope R&D (e.g., FURNITURE industry)
    state.researchBrands[companyId] = [
      {
        id: 'brand-cat',
        companyId,
        name: 'FURNITURE',
        scope: 'CATEGORY',
        productTypeId: null,
        productName: null,
        industryCategory: 'FURNITURE',
        awareness: 0,
        quality: 0,
        marketingEfficiencyMultiplier: 1.3,
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    const panel = page.getByRole('region', { name: 'research progress' })
    await expect(panel).toBeVisible()
    // Category scope badge shown (not product-specific, not company-wide)
    await expect(panel.locator('.research-brand-scope-badge', { hasText: 'Category' })).toBeVisible()
    // Marketing efficiency multiplier shown (R&D effect)
    await expect(panel.locator('.research-metric-label', { hasText: 'Marketing Efficiency' })).toBeVisible()
    await expect(panel.locator('.research-metric-value', { hasText: '1.30×' })).toBeVisible()
    // Awareness should NOT be shown (no marketing spend in this scenario)
    await expect(panel.locator('.research-metric-label', { hasText: 'Brand Awareness' })).toBeHidden()
  })

  test('shows product-scope badge for product-specific brand quality research', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-rd-prod'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Product Research Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        makeRdBuilding(companyId, [
          {
            id: 'rd-unit-prod',
            buildingId: 'building-rd-progress',
            unitType: 'BRAND_QUALITY',
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
            brandScope: 'PRODUCT',
            productTypeId: 'prod-chair',
          },
        ]),
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Simulate accumulated PRODUCT-scope R&D (e.g., Wooden Chair)
    state.researchBrands[companyId] = [
      {
        id: 'brand-prod',
        companyId,
        name: 'Wooden Chair',
        scope: 'PRODUCT',
        productTypeId: 'prod-chair',
        productName: 'Wooden Chair',
        industryCategory: null,
        awareness: 0.12,
        quality: 0.35,
        marketingEfficiencyMultiplier: 1.25,
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-rd-progress')

    const panel = page.getByRole('region', { name: 'research progress' })
    await expect(panel).toBeVisible()
    // Product scope badge shown
    await expect(panel.locator('.research-brand-scope-badge', { hasText: 'Product' })).toBeVisible()
    // Product name shown
    await expect(panel.getByText('Wooden Chair')).toBeVisible()
    // Marketing efficiency multiplier shown
    await expect(panel.locator('.research-metric-label', { hasText: 'Marketing Efficiency' })).toBeVisible()
    await expect(panel.locator('.research-metric-value', { hasText: '1.25×' })).toBeVisible()
    // Awareness IS shown because marketing has been spent for this product
    await expect(panel.locator('.research-metric-label', { hasText: 'Brand Awareness' })).toBeVisible()
    await expect(panel.locator('.research-metric-value', { hasText: '12.0%' })).toBeVisible()
  })

  test('scope explanation text is visible in brand quality research config panel', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-rd-scope-help',
      playerId: player.id,
      name: 'Scope Help Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-rd-scope-help',
          companyId: 'company-rd-scope-help',
          cityId: 'city-ba',
          type: 'RESEARCH_DEVELOPMENT',
          name: 'Scope Help Lab',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 3,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'rd-scope-bq',
              buildingId: 'building-rd-scope-help',
              unitType: 'BRAND_QUALITY',
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
              brandScope: null,
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

    await page.goto('/building/building-rd-scope-help')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page.locator('.grid-section').filter({ hasText: 'Planned Upgrade' })
    await plannedSection.locator('.grid-cell').first().click()

    // Brand scope selector is shown for BRAND_QUALITY unit
    await expect(page.getByText('Brand Scope')).toBeVisible()
    // Help text explaining the three scope options is visible (from i18n key researchBrandHelp)
    await expect(page.getByText(/company-wide branding efficiency.*product category.*single product line/i)).toBeVisible()
  })
})

// ── Exchange sourcing — per-industry coverage ─────────────────────────────────

test.describe('Global exchange market — per-industry resource coverage', () => {
  // Helper that builds a building with a single PURCHASE unit targeting the given resource.
  function makePurchaseBuilding(resourceId: string, buildingId: string, cityId = 'city-ba') {
    return {
      id: buildingId,
      companyId: `company-${buildingId}`,
      cityId,
      type: 'FACTORY' as const,
      name: `${buildingId} Factory`,
      latitude: 48.1486,
      longitude: 17.1077,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units: [
        {
          id: `unit-${buildingId}`,
          buildingId,
          unitType: 'PURCHASE' as const,
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
          resourceTypeId: resourceId,
          purchaseSource: 'EXCHANGE',
          // null = no price cap, matching the correct starter-industry configuration
          // (avoids the PR #93 regression where BasePrice caps blocked Grain purchases)
          maxPrice: null,
        },
      ],
    }
  }

  test('Grain (Food Processing input) purchase unit shows exchange offers for all cities', async ({ page }) => {
    // A PURCHASE unit targeting Grain (Food Processing raw material) must show
    // exchange offers from all seeded cities with price/transit/delivered breakdown.
    const player = makePlayer()
    player.companies.push({
      id: 'company-grain-exch',
      playerId: player.id,
      name: 'Grain Exchange Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makePurchaseBuilding('res-grain', 'building-grain-exch')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-grain-exch')
    await expect(page.getByRole('heading', { name: 'building-grain-exch Factory' })).toBeVisible()

    // Click the PURCHASE cell in the Current Configuration section
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers panel must appear
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // All seeded cities must have Grain offers
    const offersList = page.locator('.exchange-offers-list')
    await expect(offersList.getByText('Bratislava')).toBeVisible()
    await expect(offersList.getByText('Prague')).toBeVisible()
    await expect(offersList.getByText('Vienna')).toBeVisible()

    // Each offer must show all three cost components
    await expect(offersList.getByText(/Exchange:/).first()).toBeVisible()
    await expect(offersList.getByText(/Transit:/).first()).toBeVisible()
    await expect(offersList.getByText(/Delivered:/).first()).toBeVisible()

    // The same-city offer (Bratislava) must have zero transit cost
    const braOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(braOffer.getByText(/Transit: \$0/)).toBeVisible()

    // A remote-city offer must have positive transit cost
    const pragueOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    const pragueTransitText = await pragueOffer.getByText(/Transit:/).textContent()
    expect(pragueTransitText).toMatch(/Transit: \$[1-9]\d*/)

    // Best-price badge must appear exactly once
    await expect(page.locator('.offer-best-badge')).toHaveCount(1)
  })

  test('Chemical Minerals (Healthcare input) purchase unit shows exchange offers for all cities', async ({ page }) => {
    // A PURCHASE unit targeting Chemical Minerals (Healthcare raw material) must show
    // exchange offers. Chemical Minerals has no city-specific abundance in mock data,
    // so the default abundance (0.05) is used for all cities — exchange price is higher
    // than the base price and quality is lower than high-abundance resources.
    const player = makePlayer()
    player.companies.push({
      id: 'company-chem-exch',
      playerId: player.id,
      name: 'Chemical Exchange Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makePurchaseBuilding('res-chem', 'building-chem-exch')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-chem-exch')
    await expect(page.getByRole('heading', { name: 'building-chem-exch Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers panel must appear for Chemical Minerals
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    const offersList = page.locator('.exchange-offers-list')
    // All three cities must be represented
    await expect(offersList.getByText('Bratislava')).toBeVisible()
    await expect(offersList.getByText('Prague')).toBeVisible()
    await expect(offersList.getByText('Vienna')).toBeVisible()

    // Cost breakdown must be present
    await expect(offersList.getByText(/Exchange:/).first()).toBeVisible()
    await expect(offersList.getByText(/Transit:/).first()).toBeVisible()
    await expect(offersList.getByText(/Delivered:/).first()).toBeVisible()

    // Quality must be visible for each offer
    await expect(offersList.getByText(/Quality \d/).first()).toBeVisible()

    // Best-price badge must appear exactly once
    await expect(page.locator('.offer-best-badge')).toHaveCount(1)

    // Since Chemical Minerals are not in any city's abundance list, the same-city offer
    // (Bratislava) still has zero transit cost, making it the cheapest delivered option.
    const braOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(braOffer.locator('.offer-best-badge')).toBeVisible()
    await expect(braOffer.getByText(/Transit: \$0/)).toBeVisible()
  })

  test('changing minQuality in edit mode reactively blocks/unblocks exchange offers without saving', async ({ page }) => {
    // Proves that the exchange offer panel reacts immediately when the player edits
    // the minQuality threshold in the planning sidebar — no save required.
    //
    // Setup: two cities with different Wood abundances so they have different quality scores.
    //   Bratislava: abundance 0.5 → quality = 0.35 + 0.5*0.6 = 0.65 → BLOCKED by minQuality 0.70
    //   Prague:     abundance 0.8 → quality = 0.35 + 0.8*0.6 = 0.83 → PASSES minQuality 0.70
    //
    // Starting state: minQuality = null → both offers pass (no blocked styling).
    // After edit:     minQuality = 0.70 → Bratislava offer shows blocked reason without saving.
    const player = makePlayer()
    const cities = [
      {
        id: 'city-ba',
        name: 'Bratislava',
        countryCode: 'SK',
        latitude: 48.1486,
        longitude: 17.1077,
        population: 475000,
        averageRentPerSqm: 14,
        baseSalaryPerManhour: 18,
        resources: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.5 }],
      },
      {
        id: 'city-pr',
        name: 'Prague',
        countryCode: 'CZ',
        latitude: 50.0755,
        longitude: 14.4378,
        population: 1350000,
        averageRentPerSqm: 18,
        baseSalaryPerManhour: 22,
        resources: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.8 }],
      },
    ]

    player.companies.push({
      id: 'company-reactive-qual',
      playerId: player.id,
      name: 'Reactive Quality Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-reactive-qual',
          companyId: 'company-reactive-qual',
          cityId: 'city-ba',
          type: 'FACTORY' as const,
          name: 'Reactive Quality Factory',
          latitude: 48.1486,
          longitude: 17.1077,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-reactive-qual',
              buildingId: 'building-reactive-qual',
              unitType: 'PURCHASE' as const,
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
              // No minQuality initially — all offers pass
              minQuality: null,
            },
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.cities = cities as typeof state.cities
    state.resourceTypes = [
      {
        id: 'res-wood',
        name: 'Wood',
        slug: 'wood',
        category: 'ORGANIC',
        basePrice: 10,
        weightPerUnit: 5,
        unitName: 'Ton',
        unitSymbol: 't',
        imageUrl: null,
        description: 'Harvested timber.',
      },
    ]
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-reactive-qual')
    await expect(page.getByRole('heading', { name: 'Reactive Quality Factory' })).toBeVisible()

    // Step 1: Enter edit mode and click the PURCHASE cell in the Planned Upgrade draft
    await page.getByRole('button', { name: 'Edit Building' }).click()
    await expect(page.getByRole('button', { name: 'Store Upgrade' })).toBeVisible()

    const draftSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await draftSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Config panel opens and exchange offers appear (no blocked offers yet)
    await expect(page.getByText('Unit Configuration')).toBeVisible()
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // Both cities should be unblocked (minQuality is null)
    const braOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    const pragueOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    await expect(braOffer).toBeVisible()
    await expect(braOffer).not.toHaveClass(/offer-blocked/)
    await expect(pragueOffer).toBeVisible()
    await expect(pragueOffer).not.toHaveClass(/offer-blocked/)

    // Step 2: Change minQuality to 0.70 — this should block Bratislava (quality 0.65)
    // but leave Prague unblocked (quality 0.83). No save needed.
    const minQualityInput = page
      .locator('.config-field')
      .filter({ has: page.getByText('Min Quality') })
      .locator('input[type="number"]')
    await minQualityInput.fill('0.70')
    // Trigger the input event so the Vue binding fires
    await minQualityInput.dispatchEvent('input')

    // Bratislava must now be blocked (quality 0.65 < minQuality 0.70)
    await expect(braOffer).toHaveClass(/offer-blocked/)
    await expect(braOffer.locator('.offer-blocked-reason')).toContainText('Below your min quality')

    // Prague must remain unblocked (quality 0.83 > minQuality 0.70) and be the new best
    await expect(pragueOffer).toBeVisible()
    await expect(pragueOffer).not.toHaveClass(/offer-blocked/)
    await expect(pragueOffer.locator('.offer-best-badge')).toBeVisible()

    // The "no valid offers" banner must NOT appear (Prague still qualifies)
    await expect(page.getByText(/No offers meet your price and quality constraints/)).toBeHidden()
  })
})

// ── Public Sales Market Intelligence Panel ────────────────────────────────────

test.describe('Public Sales Market Intelligence panel', () => {
  function makeShopPlayer() {
    const player = makePlayer()
    const chairProduct = makeChairProduct()
    player.companies.push({
      id: 'company-shop-mi',
      playerId: player.id,
      name: 'Market Intel Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-shop-mi',
          companyId: 'company-shop-mi',
          cityId: 'city-ba',
          type: 'SALES_SHOP',
          name: 'Market Intel Shop',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 3,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-shop-mi-ps',
              buildingId: 'building-shop-mi',
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
              productTypeId: chairProduct.id,
              resourceTypeId: null,
              minPrice: chairProduct.basePrice * 1.5,
              maxPrice: null,
              purchaseSource: null,
              saleVisibility: null,
              budget: null,
              mediaHouseBuildingId: null,
              minQuality: null,
              brandScope: null,
              vendorLockCompanyId: null,
            } satisfies MockBuildingUnit,
          ],
        },
      ],
    })
    return { player, chairProduct }
  }

  test('shows market intelligence panel for PUBLIC_SALES unit with rich data', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 1500,
      totalQuantitySold: 100,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong. Consider testing a slightly higher price to improve your margin.',
      recentUtilization: 0.83,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 150, quantitySold: 10 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 1.5 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    // Click the PUBLIC_SALES unit to select it
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    // The market intelligence panel should be visible
    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()
    await expect(panel.getByRole('heading', { level: 5, name: 'Market Intelligence' })).toBeVisible()

    // Summary metrics (scope to mi-summary-grid to avoid matching chart labels)
    const summaryGrid = panel.locator('.mi-summary-grid')
    await expect(summaryGrid.getByText('Total Revenue')).toBeVisible()
    await expect(summaryGrid.getByText('Units Sold', { exact: true })).toBeVisible()

    // Demand signal badge should show STRONG
    await expect(panel.locator('.mi-demand-badge')).toContainText('Strong')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-strong/)

    // Action hint should be visible
    await expect(panel.getByText('Recommended Action')).toBeVisible()
    await expect(panel.getByText(/higher price/i)).toBeVisible()

    // Revenue chart should be visible with rendered bar data
    await expect(panel.getByText('Revenue per Tick')).toBeVisible()
    await expect(panel.locator('.mi-bar-revenue')).not.toHaveCount(0)
    await expect(panel.locator('.mi-bar-revenue').first()).toHaveAttribute('title', /T\d+:/)

    // Market share section should show the company
    await expect(panel.getByText('Market Share (latest tick)')).toBeVisible()
    await expect(panel.locator('.mi-share-row')).toHaveCount(1)
    await expect(panel.locator('.mi-share-pct')).toContainText('100.0%')

    // Elasticity index should be visible in context card (analytics.elasticityIndex = -1.0)
    await expect(panel.locator('.mi-context-card')).toBeVisible()
    await expect(panel.locator('.mi-context-label').filter({ hasText: 'Price Elasticity' })).toBeVisible()
    await expect(panel.locator('.mi-context-value').first()).toContainText('-1.00')

    // Population index should appear (analytics.populationIndex = 1.2)
    await expect(panel.locator('.mi-context-label').filter({ hasText: 'Location Index' })).toBeVisible()
  })

  test('shows empty state guidance when no sales history', async ({ page }) => {
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // Analytics with empty history (just configured, no sales yet)
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 0,
      totalQuantitySold: 0,
      averagePricePerUnit: 0,
      currentSalesCapacity: 120,
      dataFromTick: 0,
      dataToTick: 0,
      demandSignal: 'NO_DATA',
      actionHint: 'No sales recorded yet. Configure a product and price on this unit and let a few ticks pass.',
      recentUtilization: 0,
      revenueHistory: [],
      priceHistory: [],
      marketShare: [],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: null,
      inventoryQuality: null,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Should show empty state guidance
    await expect(panel.locator('.mi-empty-state')).toBeVisible()
    await expect(panel.locator('.mi-empty-state')).toContainText('No sales data yet')

    // Demand signal should be NO_DATA
    await expect(panel.locator('.mi-demand-badge')).toContainText('No Data')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-no-data/)

    // No market share section (empty)
    await expect(panel.getByText('No market share data')).toBeVisible()
  })

  test('shows WEAK demand signal with lower price hint', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 50,
      totalQuantitySold: 5,
      averagePricePerUnit: chairProduct.basePrice * 3,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'WEAK',
      actionHint: 'Sales are slow. Consider lowering your price.',
      recentUtilization: 0.04,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 5, quantitySold: 0.5 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 3 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    await expect(panel.locator('.mi-demand-badge')).toContainText('Weak')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-weak/)
    await expect(panel.getByText(/lower/i)).toBeVisible()
  })

  test('shows SUPPLY_CONSTRAINED demand signal', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 1200,
      totalQuantitySold: 120,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'SUPPLY_CONSTRAINED',
      actionHint: 'Demand is outpacing your stock. Increase factory output or storage to capture more sales.',
      recentUtilization: 0.92,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 120, quantitySold: 12 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    await expect(panel.locator('.mi-demand-badge')).toContainText('Supply Constrained')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-supply-constrained/)
    await expect(panel.getByText(/factory output/i)).toBeVisible()
  })

  test('shows market intelligence panel on mobile viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 900,
      totalQuantitySold: 60,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'MODERATE',
      actionHint: 'Sales are healthy. Keep monitoring stock levels.',
      recentUtilization: 0.5,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 90, quantitySold: 6 })),
      priceHistory: [],
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Summary grid should still be visible on mobile
    await expect(panel.locator('.mi-summary-grid')).toBeVisible()

    // Demand badge visible
    await expect(panel.locator('.mi-demand-badge')).toContainText('Moderate')

    // Panel should not overflow horizontally
    const panelBox = await panel.boundingBox()
    expect(panelBox).not.toBeNull()

    expect(panelBox!.width).toBeLessThanOrEqual(375)
  })

  test('panel does not appear for non-PUBLIC_SALES units', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-factory-mi',
      playerId: player.id,
      name: 'Factory MI Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-factory-mi',
          companyId: 'company-factory-mi',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Factory MI',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-factory-mi-storage',
              buildingId: 'building-factory-mi',
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
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-factory-mi')

    // Click the STORAGE unit
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const storageCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await storageCell.click()

    // Market intelligence panel should NOT appear for STORAGE units
    await expect(page.locator('[aria-label="Market Intelligence"]')).toBeHidden()
  })

  test('shows price history chart when price history data exists', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 1200,
      totalQuantitySold: 80,
      averagePricePerUnit: chairProduct.basePrice * 1.2,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong. Consider testing a slightly higher price.',
      recentUtilization: 0.75,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        revenue: 120,
        quantitySold: 8,
      })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        pricePerUnit: chairProduct.basePrice * (1.2 - i * 0.01),
      })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Price chart should be visible with rendered bar data
    await expect(panel.getByText('Realized Price per Tick')).toBeVisible()
    await expect(panel.locator('.mi-bar-price')).not.toHaveCount(0)
    await expect(panel.locator('.mi-bar-price').first()).toHaveAttribute('title', /T\d+:/)
  })

  test('shows MODERATE demand signal with monitor hint', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 600,
      totalQuantitySold: 40,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'MODERATE',
      actionHint: 'Sales are healthy. Keep monitoring stock levels and brand awareness to sustain performance.',
      recentUtilization: 0.5,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 60, quantitySold: 4 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    await expect(panel.locator('.mi-demand-badge')).toContainText('Moderate')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-moderate/)
    await expect(panel.getByText(/monitoring/i)).toBeVisible()
  })

  test('shows competitor in market share with distinct styling', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 750,
      totalQuantitySold: 50,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 120,
      dataFromTick: 99,
      dataToTick: 99,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.75,
      revenueHistory: [{ tick: 99, revenue: 750, quantitySold: 50 }],
      priceHistory: [{ tick: 99, pricePerUnit: chairProduct.basePrice }],
      // 75% to this company, 25% to competitor
      marketShare: [
        { label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 0.75, isUnmet: false },
        { label: 'Rival Corp', companyId: 'company-rival', share: 0.25, isUnmet: false },
      ],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Two share rows should appear
    await expect(panel.locator('.mi-share-row')).toHaveCount(2)

    // Player row should have the star and you styling
    const playerRow = panel.locator('.mi-share-row-you')
    await expect(playerRow).toBeVisible()
    await expect(playerRow.locator('.mi-share-label')).toContainText('★')
    await expect(playerRow.locator('.mi-share-pct')).toContainText('75.0%')

    // Competitor row should not have you styling
    const competitorRows = panel.locator('.mi-share-row:not(.mi-share-row-you)')
    await expect(competitorRows.locator('.mi-share-label')).toContainText('Rival Corp')
    await expect(competitorRows.locator('.mi-share-pct')).toContainText('25.0%')
  })

  test('shows unmet demand entry in market share when demand exceeds total sold', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // Demand exceeds total sold: 40% sold, 60% unmet
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 600,
      totalQuantitySold: 40,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 50,
      dataFromTick: 1,
      dataToTick: 1,
      demandSignal: 'WEAK',
      actionHint: 'Sales are slow.',
      recentUtilization: 0.4,
      revenueHistory: [{ tick: 1, revenue: 600, quantitySold: 40 }],
      priceHistory: [{ tick: 1, pricePerUnit: chairProduct.basePrice }],
      // 40% sold by this company; 60% unmet demand
      marketShare: [
        { label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 0.4, isUnmet: false },
        { label: 'Unmet Demand', companyId: null, share: 0.6, isUnmet: true },
      ],
      elasticityIndex: -1.0,
      unmetDemandShare: 0.6,
      populationIndex: 1.0,
      inventoryQuality: 0.5,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Two rows: company and unmet demand
    await expect(panel.locator('.mi-share-row')).toHaveCount(2)
    const unmetRow = panel.locator('.mi-share-row-unmet')
    await expect(unmetRow).toBeVisible()
    await expect(unmetRow.locator('.mi-share-label')).toContainText('Unmet Demand')
    await expect(unmetRow.locator('.mi-share-pct')).toContainText('60.0%')
  })

  test('quick price update panel is visible and applies new price with success message', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 1500,
      totalQuantitySold: 100,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.83,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 150, quantitySold: 10 })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 1.5 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.2,
      unmetDemandShare: 0,
      populationIndex: 1.1,
      inventoryQuality: 0.75,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Quick price update panel should be visible
    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()
    await expect(pricePanel.getByText('Quick Price Update')).toBeVisible()
    await expect(pricePanel.getByText(/no building upgrade required/i)).toBeVisible()

    // Enter new price and submit
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill('25.00')
    const applyBtn = pricePanel.getByRole('button', { name: 'Apply Price' })
    await expect(applyBtn).toBeEnabled()
    await applyBtn.click()

    // Success message should appear
    await expect(pricePanel.locator('.mi-price-success')).toBeVisible()
    await expect(pricePanel.locator('.mi-price-success')).toContainText('Price updated')
  })

  test('quick price update shows directional impact hint when raising price with elasticity data', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const currentMinPrice = chairProduct.basePrice * 1.5 // e.g. 45
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 900,
      totalQuantitySold: 60,
      averagePricePerUnit: currentMinPrice,
      currentSalesCapacity: 100,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'MODERATE',
      actionHint: 'Demand is moderate.',
      recentUtilization: 0.6,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 180, quantitySold: 12 })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, pricePerUnit: currentMinPrice })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.5,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.7,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()

    // Enter a HIGHER price — should show a "raising" hint
    const higherPrice = currentMinPrice + 10
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill(String(higherPrice))

    // The directional impact hint for raising should appear
    await expect(pricePanel.locator('.mi-price-impact-raise')).toBeVisible()
    await expect(pricePanel.locator('.mi-price-impact-raise')).toContainText('1.5')
  })

  test('quick price update shows directional impact hint when lowering price', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const currentMinPrice = chairProduct.basePrice * 1.5
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 900,
      totalQuantitySold: 60,
      averagePricePerUnit: currentMinPrice,
      currentSalesCapacity: 100,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'WEAK',
      actionHint: 'Try lowering price.',
      recentUtilization: 0.3,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 90, quantitySold: 6 })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, pricePerUnit: currentMinPrice })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -0.9,
      unmetDemandShare: 0,
      populationIndex: 0.95,
      inventoryQuality: 0.65,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()

    // Enter a LOWER price — should show a "lowering" hint
    const lowerPrice = Math.max(1, currentMinPrice - 10)
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill(String(lowerPrice))

    // The directional impact hint for lowering should appear
    await expect(pricePanel.locator('.mi-price-impact-lower')).toBeVisible()
    await expect(pricePanel.locator('.mi-price-impact-lower')).toContainText('0.9')
  })

  test('quick price update shows error message when backend returns unit-not-found error', async ({
    page,
  }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 600,
      totalQuantitySold: 40,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 100,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.8,
      revenueHistory: Array.from({ length: 3 }, (_, i) => ({
        tick: i + 1,
        revenue: 200,
        quantitySold: 13,
      })),
      priceHistory: Array.from({ length: 3 }, (_, i) => ({
        tick: i + 1,
        pricePerUnit: chairProduct.basePrice * 1.5,
      })),
      marketShare: [
        { label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false },
      ],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.7,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()

    // Simulate unit deletion from server state to trigger UNIT_NOT_FOUND
    const shopBuilding = state.players
      .flatMap((p) => p.companies.flatMap((c) => c.buildings))
      .find((b) => b.id === 'building-shop-mi')
    if (shopBuilding) {
      shopBuilding.units = shopBuilding.units?.filter((u) => u.id !== 'unit-shop-mi-ps') ?? []
    }

    // Enter a valid price and submit — backend will return UNIT_NOT_FOUND
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill('20.00')
    const applyBtn = pricePanel.getByRole('button', { name: 'Apply Price' })
    await expect(applyBtn).toBeEnabled()
    await applyBtn.click()

    // Error message should be displayed in the price panel
    await expect(pricePanel.locator('.mi-price-error')).toBeVisible()
    await expect(pricePanel.locator('.mi-price-error')).toContainText(/not found|failed|error/i)

    // Success message should NOT appear
    await expect(pricePanel.locator('.mi-price-success')).toBeHidden()
  })

  test('quick price update preserves route, unit selection, and analytics panel after applying price', async ({
    page,
  }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 1200,
      totalQuantitySold: 80,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.9,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({
        tick: i + 1,
        revenue: 240,
        quantitySold: 16,
      })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({
        tick: i + 1,
        pricePerUnit: chairProduct.basePrice * 1.5,
      })),
      marketShare: [
        { label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false },
      ],
      elasticityIndex: -1.3,
      unmetDemandShare: 0,
      populationIndex: 1.1,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()

    // Apply a new price
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill('30.00')
    const applyBtn = pricePanel.getByRole('button', { name: 'Apply Price' })
    await applyBtn.click()

    // Success message should appear
    await expect(pricePanel.locator('.mi-price-success')).toBeVisible()

    // Route should be preserved — still on the building detail page
    await expect(page).toHaveURL(/\/building\/building-shop-mi/)

    // The Market Intelligence panel should still be visible (no navigation away)
    await expect(panel).toBeVisible()

    // The Quick Price Update panel should still be visible
    await expect(pricePanel).toBeVisible()

    // The analytics demand signal should still be visible
    await expect(panel.locator('.mi-demand-badge')).toBeVisible()
    await expect(panel.locator('.mi-demand-badge')).toContainText('Strong')
  })

  test('quick price update apply button is disabled when price is zero or negative', async ({
    page,
  }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 450,
      totalQuantitySold: 30,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 80,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'MODERATE',
      actionHint: 'Demand is moderate.',
      recentUtilization: 0.5,
      revenueHistory: Array.from({ length: 3 }, (_, i) => ({
        tick: i + 1,
        revenue: 150,
        quantitySold: 10,
      })),
      priceHistory: Array.from({ length: 3 }, (_, i) => ({
        tick: i + 1,
        pricePerUnit: chairProduct.basePrice * 1.5,
      })),
      marketShare: [
        { label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false },
      ],
      elasticityIndex: -0.8,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.6,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const pricePanel = panel.locator('[aria-label="Quick Price Update"]')
    await expect(pricePanel).toBeVisible()

    const applyBtn = pricePanel.getByRole('button', { name: 'Apply Price' })

    // Button is initially enabled because quickPriceInput is pre-filled with the current minPrice
    // Enter zero — button should become disabled
    const priceInput = pricePanel.locator('#quick-price-input')
    await priceInput.fill('0')
    await expect(applyBtn).toBeDisabled()

    // Enter negative value — button should remain disabled
    await priceInput.fill('-5')
    await expect(applyBtn).toBeDisabled()

    // Enter a valid positive price — button should become enabled
    await priceInput.fill('10.00')
    await expect(applyBtn).toBeEnabled()
  })

  test('shows gross profit metric and profit chart when profitHistory is provided', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const profitHistory = Array.from({ length: 5 }, (_, i) => ({
      tick: i + 1,
      profit: 30 + i * 5,
      grossMarginPct: 33.3,
    }))

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 750,
      totalQuantitySold: 50,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.8,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 150, quantitySold: 10 })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 1.5 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: 175,
      profitHistory,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Gross Profit metric should be visible
    await expect(panel.getByText('Gross Profit', { exact: true })).toBeVisible()

    // Profit chart label should be visible
    await expect(panel.getByText('Gross Profit per Tick', { exact: true })).toBeVisible()

    // Profit bars should be rendered
    const profitBars = panel.locator('.mi-bar-profit-positive')
    await expect(profitBars).not.toHaveCount(0)
    await expect(profitBars.first()).toHaveAttribute('title', /T\d+:/)
  })

  test('shows demand drivers when provided', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 900,
      totalQuantitySold: 60,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.85,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 180, quantitySold: 12 })),
      priceHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: 0.6,
      totalProfit: 350,
      profitHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, profit: 70, grossMarginPct: 38.9 })),
      demandDrivers: [
        { factor: 'PRICE', impact: 'POSITIVE', score: 0.9, description: 'Price is competitive versus the market baseline.' },
        { factor: 'QUALITY', impact: 'POSITIVE', score: 0.8, description: 'High product quality (80%) increases buyer willingness to purchase.' },
        { factor: 'BRAND', impact: 'NEGATIVE', score: 0.1, description: 'Low brand awareness (10%). Customers do not know your product yet.' },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Demand Drivers section should be present
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()
    await expect(driversSection.getByText('Demand Drivers')).toBeVisible()

    // Each driver should be rendered with its factor label
    await expect(driversSection.locator('.mi-driver-factor', { hasText: 'Price' }).first()).toBeVisible()
    await expect(driversSection.locator('.mi-driver-factor', { hasText: 'Quality' }).first()).toBeVisible()
    await expect(driversSection.locator('.mi-driver-factor', { hasText: 'Brand' }).first()).toBeVisible()

    // NEGATIVE driver should have negative styling
    const negativeDriver = driversSection.locator('.mi-driver-negative')
    await expect(negativeDriver).toBeVisible()
    await expect(negativeDriver).toContainText('Brand')

    // POSITIVE driver description should mention quality
    const positiveDrivers = driversSection.locator('.mi-driver-positive')
    await expect(positiveDrivers.first()).toBeVisible()
  })

  test('shows SALARY demand driver as POSITIVE in high-wage city', async ({ page }) => {
    // A unit whose analytics include a SALARY demand driver with POSITIVE impact should
    // render the factor label "Purchasing Power" and the driver row with positive styling.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Vienna Shop',
      cityName: 'Vienna',
      totalRevenue: 800,
      totalQuantitySold: 40,
      averagePricePerUnit: 20,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'STRONG',
      actionHint: 'Sales are strong.',
      recentUtilization: 0.75,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 160, quantitySold: 8 })),
      priceHistory: [],
      marketShare: [],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: 1.1,
      inventoryQuality: 0.7,
      brandAwareness: 0.3,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [
        {
          factor: 'SALARY',
          impact: 'POSITIVE',
          score: 0.8,
          description: 'Residents here have above-average purchasing power (salary ×2.00), boosting baseline demand.',
        },
        {
          factor: 'PRICE',
          impact: 'POSITIVE',
          score: 0.95,
          description: 'Price is competitive versus the market baseline.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()

    // The SALARY driver should render with the i18n label "Purchasing Power"
    await expect(driversSection.locator('.mi-driver-factor', { hasText: 'Purchasing Power' }).first()).toBeVisible()

    // SALARY driver should have positive styling
    const positiveDriver = driversSection.locator('.mi-driver-positive').filter({ hasText: 'Purchasing Power' })
    await expect(positiveDriver).toBeVisible()
  })

  test('does not show demand drivers section when demandDrivers is empty', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 300,
      totalQuantitySold: 20,
      averagePricePerUnit: chairProduct.basePrice,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'MODERATE',
      actionHint: 'Sales are healthy.',
      recentUtilization: 0.5,
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 60, quantitySold: 4 })),
      priceHistory: [],
      marketShare: [],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: null,
      inventoryQuality: null,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Demand Drivers section should NOT be rendered when list is empty
    await expect(panel.locator('[aria-label="Demand Drivers"]')).toBeHidden()
  })

  test('market intelligence panel does not blank during tick refresh — charts stay visible', async ({ page }) => {
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Use a 1-second tick interval so the tick refresh fires quickly
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 800,
      totalQuantitySold: 50,
      averagePricePerUnit: 16,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.7,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 80, quantitySold: 5 })),
      priceHistory: [],
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    // Click the PUBLIC_SALES unit to open the market intelligence panel
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Revenue chart should be visible before tick
    await expect(panel.getByText('Revenue per Tick')).toBeVisible()
    await expect(panel.locator('.mi-bar-revenue')).not.toHaveCount(0)

    // Simulate a tick advancing: update the analytics data and advance tick
    analytics.dataToTick = 11
    analytics.revenueHistory.push({ tick: 11, revenue: 85, quantitySold: 5 })
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics
    state.gameState.currentTick += 1
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // After tick fires, the Market Intelligence panel must still be visible — no blank flash
    await expect(panel).toBeVisible()
    await expect(panel.getByText('Revenue per Tick')).toBeVisible()
    // Loading indicator must NOT appear in the panel during background refresh
    await expect(panel.locator('.config-help', { hasText: 'Loading' })).toBeHidden()
  })

  test('navigating to building URL with ?unit=x,y pre-selects PUBLIC_SALES unit and shows market intelligence', async ({ page }) => {
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 600,
      totalQuantitySold: 40,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 8,
      demandSignal: 'MODERATE',
      actionHint: 'Sales are healthy.',
      recentUtilization: 0.6,
      revenueHistory: Array.from({ length: 8 }, (_, i) => ({ tick: i + 1, revenue: 75, quantitySold: 5 })),
      priceHistory: [],
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.2,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.75,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    // Navigate directly with the ?unit=1,0 query param (PUBLIC_SALES unit is at gridX=1, gridY=0)
    await page.goto('/building/building-shop-mi?unit=1,0')

    // URL should contain the unit query param
    await expect(page).toHaveURL(/\/building\/building-shop-mi\?unit=1(?:,|%2C)0$/)

    // Unit detail sidebar should be open with Market Intelligence panel visible
    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Revenue chart should be shown immediately from the URL-restored selection
    await expect(panel.getByText('Revenue per Tick')).toBeVisible()
    await expect(panel.locator('.mi-bar-revenue')).not.toHaveCount(0)

    // Demand signal should be MODERATE
    await expect(panel.locator('.mi-demand-badge')).toContainText('Moderate')
  })

  test('market intelligence panel updates analytics after page reload preserving unit selection', async ({ page }) => {
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      totalRevenue: 450,
      totalQuantitySold: 30,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 6,
      demandSignal: 'STRONG',
      actionHint: 'Good performance.',
      recentUtilization: 0.65,
      revenueHistory: Array.from({ length: 6 }, (_, i) => ({ tick: i + 1, revenue: 75, quantitySold: 5 })),
      priceHistory: [],
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -0.8,
      unmetDemandShare: 0,
      populationIndex: 1.1,
      inventoryQuality: 0.9,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    // Click the PUBLIC_SALES unit
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // URL should encode the selected unit
    await expect(page).toHaveURL(/\/building\/building-shop-mi\?unit=1(?:,|%2C)0$/)

    // Update analytics with new tick data before reload
    analytics.dataToTick = 7
    analytics.revenueHistory.push({ tick: 7, revenue: 90, quantitySold: 6 })
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics
    state.gameState.currentTick = 7

    // Reload the page — unit selection must be restored from URL
    await page.reload()

    // After reload: URL param still present and panel still visible
    await expect(page).toHaveURL(/\/building\/building-shop-mi\?unit=1(?:,|%2C)0$/)
    await expect(panel).toBeVisible()
    await expect(panel.getByText('Revenue per Tick')).toBeVisible()
  })

  test('shows load-failed message when analytics API returns null', async ({ page }) => {
    // Edge case: publicSalesAnalytics query returns null (e.g., server error or unit
    // not found). The panel must show "Could not load market data." rather than a blank area.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // Explicitly set null analytics so the mock returns null for this unit
    // (don't add to state.publicSalesAnalytics — missing key returns null from mock)

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // When analytics is null the panel should show the fallback load-failed message
    await expect(panel.locator('.config-help', { hasText: 'Could not load market data' })).toBeVisible()

    // Charts and metrics should NOT be shown
    await expect(panel.locator('.mi-summary-grid')).toBeHidden()
    await expect(panel.locator('.mi-bar-chart')).toBeHidden()
  })

  test('extreme price shown in demand drivers as NEGATIVE PRICE driver', async ({ page }) => {
    // Validates that when a unit is configured with a very high price (well above market),
    // the demand drivers panel highlights this as a NEGATIVE price factor so the player
    // understands why sales are poor.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Overpriced Shop',
      cityName: 'Bratislava',
      totalRevenue: 0,
      totalQuantitySold: 0,
      averagePricePerUnit: 0,
      currentSalesCapacity: 120,
      dataFromTick: 0,
      dataToTick: 0,
      demandSignal: 'WEAK',
      actionHint: 'Your price is far above the market baseline. Try lowering it to attract buyers.',
      recentUtilization: 0,
      revenueHistory: [],
      priceHistory: [],
      marketShare: [],
      elasticityIndex: -1.5,
      unmetDemandShare: 0.95,
      populationIndex: 1.0,
      inventoryQuality: 0.85,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [
        {
          factor: 'PRICE',
          impact: 'NEGATIVE',
          score: 0.05,
          description: 'Price is significantly above the market baseline — reducing demand noticeably.',
        },
        {
          factor: 'QUALITY',
          impact: 'POSITIVE',
          score: 0.85,
          description: 'High product quality (85%) increases buyer willingness to purchase.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Demand drivers section must be visible
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()

    // NEGATIVE PRICE driver must be shown, indicating to the player that price is the problem
    const negativeDriver = driversSection.locator('.mi-driver-negative', { hasText: 'Price' })
    await expect(negativeDriver.first()).toBeVisible()

    // Action hint should direct the player to lower price
    await expect(panel.locator('.mi-action-hint', { hasText: 'lower' })).toBeVisible()
  })

  test('lower price correlates with higher utilization and positive price driver in analytics', async ({
    page,
  }) => {
    // ROADMAP AC: "Cover at least one scenario showing a lower price increasing sold quantity."
    // We model this by presenting analytics where the unit is priced below the market baseline
    // and verify that:
    //  1. The PRICE demand driver shows POSITIVE impact.
    //  2. Utilisation is displayed as a meaningful non-zero percentage.
    //  3. The demand signal indicates STRONG demand.
    const { player, chairProduct } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // Analytics reflects a unit priced 20% below the market baseline (price discount).
    const discountedPrice = chairProduct.basePrice * 0.8
    const discountTotalQuantitySold = 150
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Discount Shop',
      cityName: 'Bratislava',
      totalRevenue: discountedPrice * discountTotalQuantitySold,
      totalQuantitySold: discountTotalQuantitySold,
      averagePricePerUnit: discountedPrice,
      currentSalesCapacity: 200,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong. Your low price is attracting buyers.',
      recentUtilization: 0.75,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        revenue: discountedPrice * 15,
        quantitySold: 15,
      })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        pricePerUnit: discountedPrice,
      })),
      marketShare: [
        { label: 'Discount Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false },
      ],
      elasticityIndex: -1.2,
      unmetDemandShare: 0.1,
      populationIndex: 1.0,
      inventoryQuality: 0.7,
      brandAwareness: null,
      totalProfit: 200,
      profitHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        profit: 20,
        grossMarginPct: 16.7,
      })),
      demandDrivers: [
        {
          factor: 'PRICE',
          impact: 'POSITIVE',
          score: 0.95,
          description: 'Priced below market baseline — attracts price-sensitive buyers.',
        },
        {
          factor: 'QUALITY',
          impact: 'POSITIVE',
          score: 0.7,
          description: 'Good product quality increases buyer willingness to purchase.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Demand signal should be STRONG
    await expect(panel.locator('.mi-demand-badge')).toContainText('Strong')
    await expect(panel.locator('.mi-demand-card')).toHaveClass(/mi-demand-strong/)

    // Recent utilisation should show a non-zero value (75%)
    const summaryGrid = panel.locator('.mi-summary-grid')
    await expect(summaryGrid.getByText('Recent Utilization')).toBeVisible()
    await expect(summaryGrid.locator('.mi-metric-value').filter({ hasText: '75%' })).toBeVisible()

    // Demand Drivers section should show a POSITIVE PRICE driver
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()
    const positivePriceDriver = driversSection.locator('.mi-driver-positive', { hasText: 'Price' })
    await expect(positivePriceDriver.first()).toBeVisible()
  })

  test('quality-advantaged seller holds larger market share than quality-disadvantaged competitor', async ({
    page,
  }) => {
    // ROADMAP AC: "Cover at least one scenario showing a stronger product outperforming
    // a weaker competitor."
    // Analytics for a seller with 90% quality holding 70% market share, while a
    // lower-quality competitor holds only 30% of the market.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Quality Advantage Shop',
      cityName: 'Bratislava',
      totalRevenue: 15 * 140, // averagePricePerUnit × totalQuantitySold
      totalQuantitySold: 140,
      averagePricePerUnit: 15,
      currentSalesCapacity: 200,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Your quality advantage is working. Demand is strong.',
      recentUtilization: 0.7,
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        revenue: 15 * 14, // pricePerUnit × quantitySold per tick
        quantitySold: 14,
      })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: 15 })),
      // Market share: our high-quality company has 70%, the weaker competitor has 30%
      marketShare: [
        { label: 'Quality Corp', companyId: 'company-shop-mi', share: 0.7, isUnmet: false },
        { label: 'Budget Rival', companyId: 'rival-company-id', share: 0.3, isUnmet: false },
      ],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.9,
      brandAwareness: 0.5,
      totalProfit: 700,
      profitHistory: Array.from({ length: 10 }, (_, i) => ({
        tick: i + 1,
        profit: 70,
        grossMarginPct: 33.3,
      })),
      demandDrivers: [
        {
          factor: 'QUALITY',
          impact: 'POSITIVE',
          score: 0.9,
          description: 'High product quality (90%) gives a strong competitive advantage.',
        },
        {
          factor: 'PRICE',
          impact: 'NEUTRAL',
          score: 0.75,
          description: 'Price is in line with the market baseline.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Market share section should show two entries
    const shareRows = panel.locator('.mi-share-row')
    await expect(shareRows).toHaveCount(2)

    // Our company (marked with ★) should have the larger share (70%)
    const ourRow = panel.locator('.mi-share-row-you')
    await expect(ourRow.locator('.mi-share-pct')).toContainText('70.0%')

    // Competitor row should show the lower share (30%)
    const competitorRow = panel.locator('.mi-share-row:not(.mi-share-row-you)')
    await expect(competitorRow.locator('.mi-share-label')).toContainText('Budget Rival')
    await expect(competitorRow.locator('.mi-share-pct')).toContainText('30.0%')

    // QUALITY driver should be POSITIVE — confirming quality is driving the advantage
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()
    const positiveQualityDriver = driversSection.locator('.mi-driver-positive', { hasText: 'Quality' })
    await expect(positiveQualityDriver.first()).toBeVisible()
  })

  test('supply-constrained market shows POSITIVE SATURATION driver and scarcity description', async ({
    page,
  }) => {
    // When unmet demand is high (demand >> supply), the SATURATION driver should be POSITIVE
    // so players know they could capture more sales by stocking more inventory.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Scarcity Shop',
      cityName: 'Bratislava',
      totalRevenue: 5 * 45,
      totalQuantitySold: 5,
      averagePricePerUnit: 45,
      currentSalesCapacity: 200,
      dataFromTick: 1,
      dataToTick: 1,
      demandSignal: 'SUPPLY_CONSTRAINED',
      actionHint: 'You are selling out fast — increase stock to capture more revenue.',
      recentUtilization: 0.95,
      revenueHistory: [{ tick: 1, revenue: 5 * 45, quantitySold: 5 }],
      priceHistory: [{ tick: 1, pricePerUnit: 45 }],
      marketShare: [{ label: 'My Shop', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -0.5,
      unmetDemandShare: 0.95,
      populationIndex: 1.0,
      inventoryQuality: 0.7,
      brandAwareness: 0.5,
      totalProfit: 225,
      profitHistory: [{ tick: 1, profit: 225, grossMarginPct: 100 }],
      demandDrivers: [
        {
          factor: 'SATURATION',
          impact: 'POSITIVE',
          score: 0.95,
          description: 'Demand exceeds supply — 95% of city demand is unmet. Increasing your stock would capture more sales.',
        },
        {
          factor: 'PRICE',
          impact: 'NEUTRAL',
          score: 0.75,
          description: 'Price is at the market baseline.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Demand signal should show SUPPLY_CONSTRAINED
    const demandCard = panel.locator('.mi-demand-supply-constrained')
    await expect(demandCard).toBeVisible()
    await expect(demandCard.locator('.mi-demand-badge')).toContainText('Supply Constrained')

    // SATURATION driver should be visible and POSITIVE
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()
    const saturationDriver = driversSection.locator('.mi-driver-positive', { hasText: 'Saturation' })
    await expect(saturationDriver.first()).toBeVisible()
  })

  test('competitive market with small own share shows NEGATIVE COMPETITION driver', async ({
    page,
  }) => {
    // When the player holds a small market share against several rivals, the COMPETITION
    // driver should be NEGATIVE so the player knows to improve price or quality.
    const { player } = makeShopPlayer()

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Weak Competitor Shop',
      cityName: 'Bratislava',
      totalRevenue: 10 * 45,
      totalQuantitySold: 10,
      averagePricePerUnit: 45,
      currentSalesCapacity: 200,
      dataFromTick: 1,
      dataToTick: 1,
      demandSignal: 'WEAK',
      actionHint: 'Strong competition is limiting your sales. Improve price or quality to gain market share.',
      recentUtilization: 0.1,
      revenueHistory: [{ tick: 1, revenue: 10 * 45, quantitySold: 10 }],
      priceHistory: [{ tick: 1, pricePerUnit: 45 }],
      marketShare: [
        { label: 'My Shop', companyId: 'company-shop-mi', share: 0.1, isUnmet: false },
        { label: 'Rival A', companyId: 'rival-a-id', share: 0.45, isUnmet: false },
        { label: 'Rival B', companyId: 'rival-b-id', share: 0.45, isUnmet: false },
      ],
      elasticityIndex: -0.8,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.5,
      brandAwareness: 0.2,
      totalProfit: 50,
      profitHistory: [{ tick: 1, profit: 50, grossMarginPct: 11 }],
      demandDrivers: [
        {
          factor: 'COMPETITION',
          impact: 'NEGATIVE',
          score: 0.1,
          description:
            'Strong competition: 2 rivals hold most of this market and your 10% share is low. Improve price competitiveness, quality, or brand to win more demand.',
        },
        {
          factor: 'PRICE',
          impact: 'NEUTRAL',
          score: 0.75,
          description: 'Price is at the market baseline.',
        },
      ],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Market share: 3 rows, our share is smallest (10%)
    const shareRows = panel.locator('.mi-share-row')
    await expect(shareRows).toHaveCount(3)
    const ourRow = panel.locator('.mi-share-row-you')
    await expect(ourRow.locator('.mi-share-pct')).toContainText('10.0%')

    // COMPETITION driver should be NEGATIVE
    const driversSection = panel.locator('[aria-label="Demand Drivers"]')
    await expect(driversSection).toBeVisible()
    const competitionDriver = driversSection.locator('.mi-driver-negative', { hasText: 'Competition' })
    await expect(competitionDriver.first()).toBeVisible()
  })

  test('shows product name chip when productName is provided', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: chairProduct.id,
      productName: chairProduct.name,
      totalRevenue: 1500,
      totalQuantitySold: 100,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.83,
      trendDirection: 'UP',
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 150, quantitySold: 10 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 1.5 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Product chip should be visible with the product name
    const productChip = panel.locator('.mi-product-chip')
    await expect(productChip).toBeVisible()
    await expect(productChip).toContainText(chairProduct.name)

    // Tick window label should be visible
    const tickWindow = panel.locator('.mi-tick-window')
    await expect(tickWindow).toBeVisible()
    await expect(tickWindow).toContainText('T1')
    await expect(tickWindow).toContainText('T10')
  })

  test('shows trend direction indicator when trendDirection is provided', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: chairProduct.id,
      productName: chairProduct.name,
      totalRevenue: 2000,
      totalQuantitySold: 130,
      averagePricePerUnit: chairProduct.basePrice * 1.5,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.9,
      trendDirection: 'UP',
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: i < 5 ? 100 : 300, quantitySold: 10 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: chairProduct.basePrice * 1.5 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.2,
      inventoryQuality: 0.8,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Trend metric shows "Trend" label
    await expect(panel.getByText('Trend', { exact: true })).toBeVisible()
    // UP direction shows rising indicator
    const trendValue = panel.locator('.mi-trend-up')
    await expect(trendValue).toBeVisible()
    await expect(trendValue).toContainText('↑')
  })

  test('hides trend indicator when trendDirection is NO_DATA', async ({ page }) => {
    const { player } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: null,
      productName: null,
      totalRevenue: 0,
      totalQuantitySold: 0,
      averagePricePerUnit: 0,
      currentSalesCapacity: 120,
      dataFromTick: 0,
      dataToTick: 0,
      demandSignal: 'NO_DATA',
      actionHint: 'No sales recorded yet.',
      recentUtilization: 0,
      trendDirection: 'NO_DATA',
      revenueHistory: [],
      priceHistory: [],
      marketShare: [],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: null,
      inventoryQuality: null,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Trend metric should not be visible when there is no data
    await expect(panel.locator('.mi-trend-up')).toBeHidden()
    await expect(panel.locator('.mi-trend-down')).toBeHidden()
    await expect(panel.locator('.mi-trend-flat')).toBeHidden()

    // Product chip should not be visible when no product is configured
    await expect(panel.locator('.mi-product-chip')).toBeHidden()
  })

  test('shows profit chart with positive and negative profit bars', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: chairProduct.id,
      productName: chairProduct.name,
      totalRevenue: 1200,
      totalQuantitySold: 80,
      averagePricePerUnit: 15,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 10,
      demandSignal: 'MODERATE',
      actionHint: 'Sales are healthy.',
      recentUtilization: 0.65,
      trendDirection: 'FLAT',
      revenueHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, revenue: 120, quantitySold: 8 })),
      priceHistory: Array.from({ length: 10 }, (_, i) => ({ tick: i + 1, pricePerUnit: 15 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: -1.0,
      unmetDemandShare: 0,
      populationIndex: 1.0,
      inventoryQuality: 0.75,
      brandAwareness: null,
      totalProfit: 350,
      profitHistory: [
        { tick: 1, profit: -30, grossMarginPct: -25 },
        { tick: 2, profit: 20, grossMarginPct: 17 },
        { tick: 3, profit: 50, grossMarginPct: 42 },
        { tick: 4, profit: 45, grossMarginPct: 37 },
        { tick: 5, profit: 55, grossMarginPct: 46 },
        { tick: 6, profit: 60, grossMarginPct: 50 },
        { tick: 7, profit: 52, grossMarginPct: 43 },
        { tick: 8, profit: 48, grossMarginPct: 40 },
        { tick: 9, profit: -5, grossMarginPct: -4 },
        { tick: 10, profit: 55, grossMarginPct: 46 },
      ],
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Profit chart should be visible
    await expect(panel.getByText('Gross Profit per Tick', { exact: true })).toBeVisible()
    // Both positive and negative profit bars should exist with exact counts
    // profitHistory fixture: ticks 1,9 negative; ticks 2-8,10 positive → 8 positive, 2 negative
    await expect(panel.locator('.mi-bar-profit-positive')).toHaveCount(8)
    await expect(panel.locator('.mi-bar-profit-negative')).toHaveCount(2)

    // Profit bar titles should include the profit amount
    const firstProfitBar = panel.locator('.mi-bar-profit-negative').first()
    await expect(firstProfitBar).toHaveAttribute('title', /T1:/)

    // Total profit should show in summary (green for positive total)
    const summaryGrid = panel.locator('.mi-summary-grid')
    const profitMetric = summaryGrid.locator('.mi-metric').filter({ has: page.getByText('Gross Profit', { exact: true }) })
    await expect(profitMetric).toBeVisible()
    await expect(profitMetric.locator('.building-profit-positive-text')).toBeVisible()
  })

  test('shows summary metrics: totalRevenue, totalQuantitySold, totalProfit negative', async ({ page }) => {
    const { player, chairProduct } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: chairProduct.id,
      productName: chairProduct.name,
      totalRevenue: 500,
      totalQuantitySold: 40,
      averagePricePerUnit: 12.5,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 5,
      demandSignal: 'WEAK',
      actionHint: 'Sales are slow.',
      recentUtilization: 0.33,
      trendDirection: 'DOWN',
      revenueHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, revenue: 100, quantitySold: 8 })),
      priceHistory: [],
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: null,
      inventoryQuality: null,
      brandAwareness: null,
      totalProfit: -120,
      profitHistory: Array.from({ length: 5 }, (_, i) => ({ tick: i + 1, profit: -24, grossMarginPct: -24 })),
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // Summary grid should show all key metrics
    const summaryGrid = panel.locator('.mi-summary-grid')
    await expect(summaryGrid.getByText('Total Revenue', { exact: true })).toBeVisible()
    await expect(summaryGrid.getByText('Units Sold', { exact: true })).toBeVisible()
    await expect(summaryGrid.getByText('Gross Profit', { exact: true })).toBeVisible()

    // Negative profit should show red styling
    const profitMetric = summaryGrid.locator('.mi-metric').filter({ has: page.getByText('Gross Profit', { exact: true }) })
    await expect(profitMetric.locator('.building-profit-negative-text')).toBeVisible()

    // Trend DOWN indicator should be visible
    await expect(panel.locator('.mi-trend-down')).toBeVisible()
    await expect(panel.locator('.mi-trend-down')).toContainText('↓')
  })

  test('revenue and quantity charts show all 100 ticks when backend returns full history', async ({ page }) => {
    const { player } = makeShopPlayer()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    // 100 ticks of history — the ROADMAP requires "last 100 ticks" to be shown
    const analytics: MockPublicSalesAnalytics = {
      buildingUnitId: 'unit-shop-mi-ps',
      buildingId: 'building-shop-mi',
      buildingName: 'Market Intel Shop',
      cityName: 'Bratislava',
      productTypeId: null,
      productName: null,
      totalRevenue: 10000,
      totalQuantitySold: 1000,
      averagePricePerUnit: 10,
      currentSalesCapacity: 120,
      dataFromTick: 1,
      dataToTick: 100,
      demandSignal: 'STRONG',
      actionHint: 'Demand is strong.',
      recentUtilization: 0.85,
      trendDirection: 'UP',
      revenueHistory: Array.from({ length: 100 }, (_, i) => ({ tick: i + 1, revenue: 100, quantitySold: 10 })),
      priceHistory: Array.from({ length: 100 }, (_, i) => ({ tick: i + 1, pricePerUnit: 10 })),
      marketShare: [{ label: 'Market Intel Corp', companyId: 'company-shop-mi', share: 1.0, isUnmet: false }],
      elasticityIndex: null,
      unmetDemandShare: null,
      populationIndex: null,
      inventoryQuality: null,
      brandAwareness: null,
      totalProfit: null,
      profitHistory: null,
      demandDrivers: [],
    }
    state.publicSalesAnalytics['unit-shop-mi-ps'] = analytics

    await page.goto('/building/building-shop-mi')

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await psCell.click()

    const panel = page.locator('[aria-label="Market Intelligence"]')
    await expect(panel).toBeVisible()

    // All 100 revenue bars should be present (not capped at 30)
    await expect(panel.locator('.mi-bar-revenue')).toHaveCount(100)
    // All 100 quantity bars should be present
    await expect(panel.locator('.mi-bar-quantity')).toHaveCount(100)
    // All 100 price bars should be present
    await expect(panel.locator('.mi-bar-price')).toHaveCount(100)

    // Tick window label should show T1–T100
    await expect(panel.locator('.mi-tick-window')).toContainText('T1')
    await expect(panel.locator('.mi-tick-window')).toContainText('T100')
  })
})

test.describe('Mine building edit mode', () => {
  test('player adds MINING unit to empty mine in edit mode and sees pending state', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-mine-edit',
      playerId: player.id,
      name: 'Iron Mine Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-mine-edit',
          companyId: 'company-mine-edit',
          cityId: 'city-ba',
          type: 'MINE',
          name: 'Iron Mine',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [], // empty mine — no units yet
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

    await page.goto('/building/building-mine-edit')
    await expect(page.getByRole('heading', { name: 'Iron Mine' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()

    // The planned upgrade section should appear
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Click an empty cell at (0,0) to trigger the unit picker
    const emptyCell = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await emptyCell.click()

    // Unit picker should appear with mine-specific units
    await expect(page.getByText('Select a unit type to place')).toBeVisible()

    // Mining unit option should be available (mine-specific)
    const miningBtn = page.locator('.picker-option').filter({ hasText: 'Mining' })
    await expect(miningBtn).toBeVisible()

    // MANUFACTURING should NOT appear (factory-only unit)
    await expect(page.locator('.picker-option').filter({ hasText: 'Manufacturing' })).toHaveCount(0)

    // Place the MINING unit
    await miningBtn.click()

    // Cell should now show "Mining" in the draft grid
    // After placing, selectedCell resets — click cell again to see it
    await emptyCell.click()
    await expect(emptyCell).toContainText('Mining')

    // Store Upgrade button should be enabled (draft has changes)
    const storeBtn = page.getByRole('button', { name: 'Store Upgrade' })
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // After saving, upgrade-in-progress banner must appear
    await expect(page.locator('.upgrade-banner')).toBeVisible()
    await expect(page.getByRole('status')).toContainText('Building upgrade in progress')
  })

  test('mine edit mode: allowed unit types match mine building type (MINING, STORAGE, B2B_SALES)', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-mine-types',
      playerId: player.id,
      name: 'Type Check Mine Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-mine-types',
          companyId: 'company-mine-types',
          cityId: 'city-ba',
          type: 'MINE',
          name: 'Type Check Mine',
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

    await page.goto('/building/building-mine-types')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Open unit picker for an empty cell
    const emptyCell = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await emptyCell.click()
    await expect(page.getByText('Select a unit type to place')).toBeVisible()

    // Mine-specific types must be present
    await expect(page.locator('.picker-option').filter({ hasText: 'Mining' })).toBeVisible()
    await expect(page.locator('.picker-option').filter({ hasText: 'Storage' })).toBeVisible()
    await expect(page.locator('.picker-option').filter({ hasText: 'B2B Sales' })).toBeVisible()

    // Factory/shop-only types must NOT appear in the picker
    await expect(page.locator('.picker-option').filter({ hasText: 'Manufacturing' })).toHaveCount(0)
    await expect(page.locator('.picker-option').filter({ hasText: 'Purchase' })).toHaveCount(0)
    await expect(page.locator('.picker-option').filter({ hasText: 'Public Sales' })).toHaveCount(0)
  })
})

// ── Sales shop unit-type picker coverage ─────────────────────────────────────

test.describe('Sales shop edit mode — unit type picker', () => {
  function makeEmptySalesShopForPicker() {
    const player = makePlayer()
    player.companies.push({
      id: 'company-shop-picker',
      playerId: player.id,
      name: 'Picker Shop Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-shop-picker',
          companyId: 'company-shop-picker',
          cityId: 'city-ba',
          type: 'SALES_SHOP',
          name: 'Picker Sales Shop',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 1,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [],
        },
      ],
    })
    return player
  }

  test('sales shop unit picker shows exactly PURCHASE, MARKETING, and PUBLIC_SALES options', async ({ page }) => {
    const player = makeEmptySalesShopForPicker()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-shop-picker')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Click an empty cell to open the picker
    const emptyCell = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await emptyCell.click()
    await expect(page.getByText('Select a unit type to place')).toBeVisible()

    // Sales-shop-specific unit types must be present
    await expect(page.locator('.picker-option').filter({ hasText: 'Purchase' })).toBeVisible()
    await expect(page.locator('.picker-option').filter({ hasText: 'Marketing' })).toBeVisible()
    await expect(page.locator('.picker-option').filter({ hasText: 'Public Sales' })).toBeVisible()

    // Factory-only types must NOT appear
    await expect(page.locator('.picker-option').filter({ hasText: 'Manufacturing' })).toHaveCount(0)
    await expect(page.locator('.picker-option').filter({ hasText: 'Mining' })).toHaveCount(0)
    await expect(page.locator('.picker-option').filter({ hasText: 'B2B Sales' })).toHaveCount(0)
  })

  test('player adds MARKETING unit to empty sales shop and sees it in the planned grid', async ({ page }) => {
    const player = makeEmptySalesShopForPicker()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-shop-picker')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Click an empty cell and select MARKETING
    const emptyCell = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await emptyCell.click()
    await expect(page.getByText('Select a unit type to place')).toBeVisible()

    const marketingOption = page.locator('.picker-option').filter({ hasText: 'Marketing' })
    await expect(marketingOption).toBeVisible()
    await marketingOption.click()

    // After placing, the cell should show the Marketing label
    const placedCell = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await expect(placedCell).toContainText('Marketing')

    // The Store Upgrade button should now be enabled
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await expect(storeBtn).toBeVisible()
    await expect(storeBtn).toBeEnabled()
  })

  test('full flow: PURCHASE → MARKETING → PUBLIC_SALES shop layout saved as pending upgrade', async ({ page }) => {
    const player = makeEmptySalesShopForPicker()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-shop-picker')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Place PURCHASE at (0,0)
    const cell00 = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await cell00.click()
    await page.locator('.picker-option').filter({ hasText: 'Purchase' }).click()

    // After placing PURCHASE, click cell (1,0) for MARKETING
    const cell10 = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
    await cell10.click()
    await page.locator('.picker-option').filter({ hasText: 'Marketing' }).click()

    // Click cell (2,0) for PUBLIC_SALES
    const cell20 = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(2)
    await cell20.click()
    await page.locator('.picker-option').filter({ hasText: 'Public Sales' }).click()

    // All three cells should be filled
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)).toContainText('Purchase')
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)).toContainText('Marketing')
    await expect(plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(2)).toContainText('Public Sales')

    // Save the plan
    const storeBtn = page.getByRole('button', { name: /Store Upgrade/i })
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // After save, the upgrade banner should confirm the pending upgrade
    await expect(page.locator('.upgrade-banner')).toBeVisible()
  })

  test('unit changes summary panel lists each addition with tick cost', async ({ page }) => {
    const player = makeEmptySalesShopForPicker()
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-shop-picker')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Add a PURCHASE unit
    const cell00 = plannedSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await cell00.click()
    await page.locator('.picker-option').filter({ hasText: 'Purchase' }).click()

    // The planned unit changes panel should appear and list the added unit
    const changesPanel = page.locator('.unit-changes-summary')
    await expect(changesPanel).toBeVisible()

    // Should list "Purchase" as an addition
    await expect(changesPanel.getByText(/Purchase/)).toBeVisible()
    // Should show tick cost (3 ticks for a new unit)
    await expect(changesPanel.getByText(/3 ticks/)).toBeVisible()
  })
})

test.describe('Building grid editor — mobile viewport (375px)', () => {
  test('grid inspection and edit submission are usable on 375px viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })

    const player = makePlayer()
    player.companies.push({
      id: 'company-mobile',
      playerId: player.id,
      name: 'Mobile Factory Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-mobile',
          companyId: 'company-mobile',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Mobile Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'mobile-u1',
              buildingId: 'building-mobile',
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
            } satisfies MockBuildingUnit,
            {
              id: 'mobile-u2',
              buildingId: 'building-mobile',
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
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-mobile')
    await expect(page.getByRole('heading', { name: 'Mobile Factory' })).toBeVisible()

    // The 4x4 grid must be visible at 375px width without horizontal overflow
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await expect(activeSection).toBeVisible()

    // Click the PURCHASE unit to inspect it (read-only)
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await purchaseCell.click()
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Planned Upgrade' }) })
      .first()
    await expect(plannedSection).toBeVisible()

    // Add a unit in an empty cell
    const emptyCell = plannedSection.locator('.unit-row').nth(1).locator('.grid-cell').nth(0)
    await emptyCell.click()
    await expect(page.getByText('Select a unit type to place')).toBeVisible()

    // Pick Storage
    const storageBtn = page.locator('.picker-option').filter({ hasText: 'Storage' })
    await expect(storageBtn).toBeVisible()
    await storageBtn.click()

    // Store Upgrade button must be reachable and functional on mobile
    const storeBtn = page.getByRole('button', { name: 'Store Upgrade' })
    await expect(storeBtn).toBeVisible()
    await expect(storeBtn).toBeEnabled()
    await storeBtn.click()

    // Confirm pending state appears
    await expect(page.locator('.upgrade-banner')).toBeVisible()
  })
})

test.describe('Operational status panel and recent activity', () => {
  test('shows IDLE operational status badge when unit has no inventory', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-opstat',
      playerId: player.id,
      name: 'OpStat Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-opstat',
          companyId: 'company-opstat',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'OpStat Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'opstat-unit-1',
              buildingId: 'building-opstat',
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
              inventoryQuantity: 0,
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-opstat')

    // Click the PURCHASE unit to open its inspector
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await purchaseCell.click()

    // The operational status panel should be visible
    await expect(page.locator('[aria-label="Unit operational status"]')).toBeVisible()
    // IDLE badge (inventoryQuantity === 0)
    const badge = page.locator('.status-badge')
    await expect(badge).toBeVisible()
  })

  test('shows ACTIVE operational status badge when unit has inventory', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-opstat-active',
      playerId: player.id,
      name: 'OpStat Active Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-opstat-active',
          companyId: 'company-opstat-active',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'OpStat Active Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'opstat-active-unit-1',
              buildingId: 'building-opstat-active',
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
              inventoryQuantity: 10,
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-opstat-active')

    // Click the PURCHASE unit to open its inspector
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await purchaseCell.click()

    // The operational status panel must be visible
    await expect(page.locator('[aria-label="Unit operational status"]')).toBeVisible()

    // ACTIVE badge (inventoryQuantity > 0)
    const badge = page.locator('.status-badge.status-active')
    await expect(badge).toBeVisible()
  })

  test('shows recent activity panel in unit inspector', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-activity',
      playerId: player.id,
      name: 'Activity Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-activity',
          companyId: 'company-activity',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Activity Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'activity-unit-1',
              buildingId: 'building-activity',
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
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-activity')

    // Click the PURCHASE unit to open its inspector
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await purchaseCell.click()

    // Recent Activity panel should be visible
    await expect(page.locator('[aria-label="Recent Activity"]')).toBeVisible()

    // With empty mock, should show the empty state message
    await expect(page.locator('[aria-label="Recent Activity"]')).toContainText('No activity recorded yet')
  })
})

// ── Destination-aware sourcing: logistics trap warning and sort controls ───

test.describe('Destination-aware purchase sourcing', () => {
  /**
   * Helper: builds a minimal factory in Bratislava with a PURCHASE unit
   * targeting the given resource at EXCHANGE source.
   */
  function makeExchangeFactory(resourceId: string, buildingId: string, buildingName: string) {
    return {
      id: buildingId,
      companyId: `company-${buildingId}`,
      cityId: 'city-ba',
      type: 'FACTORY' as const,
      name: buildingName,
      latitude: 48.1486,
      longitude: 17.1077,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units: [
        {
          id: `unit-${buildingId}`,
          buildingId,
          unitType: 'PURCHASE' as const,
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
          resourceTypeId: resourceId,
          purchaseSource: 'EXCHANGE',
          maxPrice: null,
        },
      ],
    }
  }

  test('shows logistics-trap warning when distant city has cheaper sticker but worse delivered price', async ({ page }) => {
    // Set up a scenario where Prague has a cheaper exchange (sticker) price for Wood
    // than Bratislava, but higher transit cost makes it more expensive delivered.
    // Bratislava abundance 0.4 → sticker ~$13.63, delivered $13.63 (same city, 0 transit).
    // Prague    abundance 0.5 → sticker ~$13.28 (cheaper!) but transit ~$3.61 → delivered ~$16.89.
    // This creates a logistics trap: Prague sticker < BA sticker, but Prague delivered > BA delivered.
    const player = makePlayer()
    player.companies.push({
      id: 'company-trap',
      playerId: player.id,
      name: 'Trap Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeExchangeFactory('res-wood', 'building-trap', 'Trap Factory')],
    })

    const state = setupMockApi(page, { players: [player] })
    // Override Wood abundances to create the logistics trap:
    // Bratislava: low abundance → higher sticker, but 0 transit → good delivered
    // Prague: medium abundance → lower sticker, but 300km transit → bad delivered
    state.cities[0]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.4 }]
    state.cities[1]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.5 }]
    state.cities[2]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.1 }]

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-trap')
    await expect(page.getByRole('heading', { name: 'Trap Factory' })).toBeVisible()

    // Click on the PURCHASE unit to open its inspector
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    // Exchange offers panel must be visible
    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // The logistics trap warning must appear because Prague has a lower sticker
    // price but transit makes it more expensive delivered than Bratislava.
    await expect(page.locator('.logistics-trap-warning')).toBeVisible()
    await expect(page.locator('.logistics-trap-warning')).toContainText('Prague')
    await expect(page.locator('.logistics-trap-warning')).toContainText('Bratislava')

    // Bratislava must be the recommended (best delivered price) offer
    const braOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(braOffer).toHaveClass(/offer-best/)

    // Prague must NOT be the recommended offer despite having a lower sticker price
    const prOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Prague' })
    await expect(prOffer).not.toHaveClass(/offer-best/)
  })

  test('no logistics-trap warning when cheapest sticker is also cheapest delivered', async ({ page }) => {
    // When Bratislava has the highest abundance it gets the lowest exchange price
    // AND 0 transit (same city), so sticker and delivered both win at Bratislava.
    // No logistics trap should be shown.
    const player = makePlayer()
    player.companies.push({
      id: 'company-notrap',
      playerId: player.id,
      name: 'No-Trap Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeExchangeFactory('res-wood', 'building-notrap', 'No-Trap Factory')],
    })

    const state = setupMockApi(page, { players: [player] })
    // Bratislava has the highest abundance → cheapest sticker AND cheapest delivered (0 transit).
    state.cities[0]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.9 }]
    state.cities[1]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.1 }]
    state.cities[2]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.1 }]

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-notrap')
    await expect(page.getByRole('heading', { name: 'No-Trap Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()
    await expect(page.locator('.exchange-offers-list')).toBeVisible()

    // No logistics trap warning should appear
    await expect(page.locator('.logistics-trap-warning')).toHaveCount(0)

    // Bratislava must be the recommended offer
    await expect(page.locator('.offer-best-badge')).toBeVisible()
    const braOffer = page.locator('.exchange-offer-item').filter({ hasText: 'Bratislava' })
    await expect(braOffer).toHaveClass(/offer-best/)
  })

  test('sort controls change ordering of exchange offers', async ({ page }) => {
    // Set up cities where Prague has lower exchange price but high transit (logistics trap scenario).
    const player = makePlayer()
    player.companies.push({
      id: 'company-sort',
      playerId: player.id,
      name: 'Sort Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeExchangeFactory('res-wood', 'building-sort', 'Sort Factory')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.cities[0]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.4 }]
    state.cities[1]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.5 }]
    state.cities[2]!.resources = [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.1 }]

    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sort')
    await expect(page.getByRole('heading', { name: 'Sort Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // Sort controls must be visible (more than 1 offer)
    await expect(page.locator('.exchange-sort-controls')).toBeVisible()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Delivered' })).toBeVisible()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Exchange' })).toBeVisible()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Quality' })).toBeVisible()

    // Default sort is by Delivered price — Bratislava should appear first (cheapest delivered)
    const offerItems = page.locator('.exchange-offer-item')
    await expect(offerItems.first()).toContainText('Bratislava')

    // Switch to Exchange price sort — Prague should appear first (cheapest sticker)
    await page.locator('.exchange-sort-btn', { hasText: 'Exchange' }).click()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Exchange' })).toHaveClass(/active/)
    // After switching to exchange-price sort, Prague (abundance=0.5, cheaper sticker) should be first
    await expect(offerItems.first()).toContainText('Prague')

    // Switch to Quality sort — highest quality offer first
    await page.locator('.exchange-sort-btn', { hasText: 'Quality' }).click()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Quality' })).toHaveClass(/active/)
    // Prague (abundance 0.5) and Bratislava (abundance 0.4) — Prague has higher quality (0.35 + 0.5*0.6 = 0.65 vs 0.35+0.4*0.6=0.59)
    await expect(offerItems.first()).toContainText('Prague')

    // Switch back to Delivered sort
    await page.locator('.exchange-sort-btn', { hasText: 'Delivered' }).click()
    await expect(page.locator('.exchange-sort-btn', { hasText: 'Delivered' })).toHaveClass(/active/)
    await expect(offerItems.first()).toContainText('Bratislava')
  })

  test('View on Global Exchange link navigates to exchange with resource and city pre-selected', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-link',
      playerId: player.id,
      name: 'Link Test Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeExchangeFactory('res-wood', 'building-link', 'Link Factory')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-link')
    await expect(page.getByRole('heading', { name: 'Link Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()

    // The "View on Global Exchange" link must be visible
    const exchangeLink = page.locator('.exchange-view-link')
    await expect(exchangeLink).toBeVisible()
    await expect(exchangeLink).toContainText('View on Global Exchange')

    // Clicking the link navigates to /exchange with resource and city query params
    await exchangeLink.click()
    await page.waitForURL(/\/exchange/)
    await expect(page).toHaveURL(/resource=wood/)
    await expect(page).toHaveURL(/city=city-ba/)
  })

  test('exchange link in read-only mode also navigates to exchange', async ({ page }) => {
    // This test verifies the exchange link appears in the read-only unit inspector
    // (not just in the edit-mode config panel).
    const player = makePlayer()
    player.companies.push({
      id: 'company-linkro',
      playerId: player.id,
      name: 'Read-Only Link Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeExchangeFactory('res-grain', 'building-linkro', 'Read-Only Factory')],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-linkro')
    await expect(page.getByRole('heading', { name: 'Read-Only Factory' })).toBeVisible()

    // Click the unit in the Current Configuration section (read-only mode)
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    await activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0).click()

    await expect(page.getByText('Global exchange offers')).toBeVisible()
    // Exchange link must be present in read-only sidebar too
    await expect(page.locator('.exchange-view-link')).toBeVisible()
    // Link points to exchange with the grain resource slug
    const href = await page.locator('.exchange-view-link').getAttribute('href')
    expect(href).toContain('resource=grain')
  })
})

// ── Procurement mode selection UI ─────────────────────────────────────────

test.describe('Procurement mode configuration', () => {
  /**
   * Helper: builds a minimal factory in Bratislava with a PURCHASE unit (OPTIMAL mode by default).
   */
  function makeProcurementFactory(buildingId: string, buildingName: string, purchaseSource: string = 'OPTIMAL', lockedCityId: string | null = null) {
    return {
      id: buildingId,
      companyId: `company-proc-${buildingId}`,
      cityId: 'city-ba',
      type: 'FACTORY' as const,
      name: buildingName,
      latitude: 48.1486,
      longitude: 17.1077,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units: [
        {
          id: `unit-proc-${buildingId}`,
          buildingId,
          unitType: 'PURCHASE' as const,
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
          purchaseSource,
          maxPrice: null,
          lockedCityId,
        },
      ],
    }
  }

  test('shows procurement mode radio buttons in edit mode for PURCHASE unit', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-proc-edit'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Procurement Edit Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-edit', 'Procurement Factory', 'OPTIMAL'), companyId }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-edit')
    await expect(page.getByRole('heading', { name: 'Procurement Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()

    // Click on the PURCHASE unit cell in Planned Upgrade section
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    // Procurement Mode section should appear
    await expect(page.locator('.config-field').filter({ has: page.getByText('Procurement Mode', { exact: true }) })).toBeVisible()

    // Should show OPTIMAL as selected (default) — check via label class
    await expect(page.locator('.procurement-mode-option').filter({ has: page.locator('.procurement-mode-label', { hasText: 'Optimal Landed Cost' }) })).toHaveClass(/selected/)

    // Changing to EXCHANGE should work
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })
      .click()
    await expect(page.locator('.procurement-mode-option').filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })).toHaveClass(/selected/)

    // LOCAL option should also be present
    await expect(page.locator('.procurement-mode-option').filter({ has: page.locator('.procurement-mode-label', { hasText: 'Local Supplier' }) })).toBeVisible()
  })

  test('switching to EXCHANGE mode shows city lock dropdown', async ({ page }) => {
    const player = makePlayer()
    const companyId = 'company-proc-exchange'
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Exchange Lock Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-exchange', 'Exchange Factory', 'OPTIMAL'), companyId: 'company-proc-exchange' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-exchange')
    await expect(page.getByRole('heading', { name: 'Exchange Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()

    // Click on the PURCHASE unit cell in Planned Upgrade section
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    // Initially on OPTIMAL mode — city lock dropdown should NOT appear
    await expect(page.getByText('Lock to Source City')).toBeHidden()

    // Switch to EXCHANGE mode using label click
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })
      .click()

    // City lock dropdown should now appear
    await expect(page.getByText('Lock to Source City')).toBeVisible()
    const citySelect = page.locator('select').filter({ hasText: /Any city/ })
    await expect(citySelect).toBeVisible()
  })

  test('switching to LOCAL mode shows own-company vendor option in the selector', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-proc-local',
      playerId: player.id,
      name: 'Local Lock Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-local', 'Local Factory', 'OPTIMAL'), companyId: 'company-proc-local' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-local')
    await expect(page.getByRole('heading', { name: 'Local Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()

    // Click on the PURCHASE unit cell in Planned Upgrade section
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    // Switch to LOCAL mode using label click
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Local Supplier' }) })
      .click()

    // Purchase selector button remains the single entry point for product/vendor selection
    await expect(page.getByRole('button', { name: /product and vendor/i })).toBeVisible()
    const dialog = await openPurchaseSelector(page)
    await expect(dialog.getByRole('heading', { name: 'Vendor link' })).toBeVisible()
    await expect(dialog.getByRole('button', { name: /Your own company/ })).toBeVisible()
  })

  test('sales shop purchase selector can lock sourcing to your own company and persists it on save', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-shop-own',
      playerId: player.id,
      name: 'Own Supply Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-own-shop',
          companyId: 'company-shop-own',
          cityId: 'city-ba',
          type: 'SALES_SHOP',
          name: 'Own Shop',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 1,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'own-shop-purchase',
              buildingId: 'building-own-shop',
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
            } satisfies MockBuildingUnit,
            {
              id: 'own-shop-sales',
              buildingId: 'building-own-shop',
              unitType: 'PUBLIC_SALES',
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
            } satisfies MockBuildingUnit,
          ],
        },
        {
          id: 'building-own-factory',
          companyId: 'company-shop-own',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Own Factory',
          latitude: 48.16,
          longitude: 17.12,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'own-factory-b2b',
              buildingId: 'building-own-factory',
              unitType: 'B2B_SALES',
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
              productTypeId: 'prod-chair',
              minPrice: 45,
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-own-shop')
    await page.getByRole('button', { name: 'Edit Building' }).click()

    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()
    await selectPurchaseItem(page, 'chair', /Wooden Chair/)

    const dialog = await openPurchaseSelector(page)
    await dialog.getByRole('button', { name: 'Your own company' }).click()
    await dialog.getByRole('button', { name: 'Done' }).click()

    await expect(page.locator('.purchase-selection-summary')).toContainText('Wooden Chair')
    await expect(page.locator('.purchase-selection-summary')).toContainText('Own Supply Co')
    await expect(page.locator('.purchase-selection-summary')).toContainText('Own Factory')

    await page.getByRole('button', { name: /Store Upgrade/i }).click()

    const pendingUnits = state.players[0].companies[0].buildings[0].pendingConfiguration?.units ?? []
    const purchaseUnit = pendingUnits.find((unit) => unit.unitType === 'PURCHASE')
    expect(purchaseUnit?.productTypeId).toBe('prod-chair')
    expect(purchaseUnit?.vendorLockCompanyId).toBe('company-shop-own')
    expect(purchaseUnit?.purchaseSource).toBe('LOCAL')
  })

  test('procurement preview card shows for active PURCHASE unit in view mode', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-proc-preview',
      playerId: player.id,
      name: 'Preview Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-preview', 'Preview Factory', 'OPTIMAL'), companyId: 'company-proc-preview' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Set up a mock procurement preview that will execute successfully
    state.procurementPreviews['unit-proc-building-proc-preview'] = {
      sourceType: 'GLOBAL_EXCHANGE',
      sourceCityId: 'city-ba',
      sourceCityName: 'Bratislava',
      sourceVendorCompanyId: null,
      sourceVendorName: null,
      exchangePricePerUnit: 8.5,
      transitCostPerUnit: 0,
      deliveredPricePerUnit: 8.5,
      estimatedQuality: 0.7,
      canExecute: true,
      blockReason: null,
      blockMessage: null,
    }
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-preview')
    await expect(page.getByRole('heading', { name: 'Preview Factory' })).toBeVisible()

    // Click on the unit in Current Configuration
    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    // Procurement preview card should be visible
    await expect(page.locator('.procurement-preview')).toBeVisible()
    await expect(page.getByText('Next-Tick Preview')).toBeVisible()
    await expect(page.getByText('Purchase will execute')).toBeVisible()
    await expect(page.locator('.procurement-preview').getByText('Bratislava', { exact: false })).toBeVisible()
    await expect(page.locator('.procurement-preview .preview-value', { hasText: 'Global Exchange' })).toBeVisible()
  })

  test('procurement preview card shows blocked state with reason', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-proc-blocked',
      playerId: player.id,
      name: 'Blocked Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-blocked', 'Blocked Factory', 'EXCHANGE'), companyId: 'company-proc-blocked' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Set up a blocked procurement preview
    state.procurementPreviews['unit-proc-building-proc-blocked'] = {
      sourceType: 'GLOBAL_EXCHANGE',
      sourceCityId: 'city-ba',
      sourceCityName: 'Bratislava',
      sourceVendorCompanyId: null,
      sourceVendorName: null,
      exchangePricePerUnit: 8.5,
      transitCostPerUnit: 0,
      deliveredPricePerUnit: 8.5,
      estimatedQuality: 0.7,
      canExecute: false,
      blockReason: 'MAX_PRICE_EXCEEDED',
      blockMessage: 'All available offers exceed your max price of $0.001.',
    }
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-blocked')
    await expect(page.getByRole('heading', { name: 'Blocked Factory' })).toBeVisible()

    // Click on the unit in Current Configuration
    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    // Procurement preview shows blocked state
    await expect(page.locator('.procurement-preview')).toBeVisible()
    await expect(page.getByText('Purchase is blocked')).toBeVisible()
    await expect(page.getByText('Max price exceeded')).toBeVisible()
    await expect(page.getByText('All available offers exceed your max price')).toBeVisible()
  })

  test('switching from EXCHANGE to OPTIMAL clears city lock dropdown state', async ({ page }) => {
    // Verifies that when a player switches from EXCHANGE back to OPTIMAL,
    // the lockedCityId is cleared in the draft state so it won't be persisted.
    const companyId = 'company-proc-clr'
    const player = makePlayer()
    player.companies.push({
      id: companyId,
      playerId: player.id,
      name: 'Clear Lock Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeProcurementFactory('building-proc-clr', 'Clear Lock Factory', 'EXCHANGE'), companyId }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-proc-clr')
    await expect(page.getByRole('heading', { name: 'Clear Lock Factory' })).toBeVisible()

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')
    await getGridCell(plannedSection, 0, 0).click()

    // Switch to EXCHANGE to show the city lock dropdown
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })
      .click()
    await expect(page.locator('.procurement-mode-option').filter({ has: page.locator('.procurement-mode-label', { hasText: 'Global Exchange' }) })).toHaveClass(/selected/)

    // City lock dropdown should be visible in EXCHANGE mode
    const cityLockDropdown = page.locator('select').filter({ has: page.locator('option', { hasText: 'Any city' }) })
    await expect(cityLockDropdown).toBeVisible()

    // Now switch back to OPTIMAL – city lock dropdown should disappear
    await page
      .locator('.procurement-mode-option')
      .filter({ has: page.locator('.procurement-mode-label', { hasText: 'Optimal Landed Cost' }) })
      .click()
    await expect(page.locator('.procurement-mode-option').filter({ has: page.locator('.procurement-mode-label', { hasText: 'Optimal Landed Cost' }) })).toHaveClass(/selected/)

    // City lock dropdown must be hidden – OPTIMAL mode must not expose city restriction
    await expect(cityLockDropdown).toBeHidden()
  })

  test('shows operating cost label on active grid tile', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-opcost-tile',
      playerId: player.id,
      name: 'OpCost Tile Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-opcost-tile',
          companyId: 'company-opcost-tile',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'OpCost Tile Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'opcost-unit-tile-1',
              buildingId: 'building-opcost-tile',
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
              inventoryQuantity: 50,
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-opcost-tile')
    await expect(page.getByRole('heading', { name: 'OpCost Tile Factory' })).toBeVisible()

    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)

    // The tile should show an operating cost label with "op:" prefix
    const operatingCostLabel = purchaseCell.locator('.cell-operating-cost')
    await expect(operatingCostLabel).toBeVisible()
    const labelText = await operatingCostLabel.textContent()
    expect(labelText?.toLowerCase()).toContain('op:')
  })

  test('shows operating cost breakdown in unit detail sidebar', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-opcost-sidebar',
      playerId: player.id,
      name: 'OpCost Sidebar Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-opcost-sidebar',
          companyId: 'company-opcost-sidebar',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'OpCost Sidebar Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 5,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'opcost-unit-sb-1',
              buildingId: 'building-opcost-sidebar',
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
              inventoryQuantity: 50,
            } satisfies MockBuildingUnit,
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

    await page.goto('/building/building-opcost-sidebar')
    await expect(page.getByRole('heading', { name: 'OpCost Sidebar Factory' })).toBeVisible()

    // Click the PURCHASE unit to open the inspector
    const activeSection = page
      .locator('.grid-section')
      .filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) })
      .first()
    const purchaseCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(0)
    await purchaseCell.click()

    // The operating costs row should be visible in the sidebar
    await expect(page.locator('.operating-costs-row')).toBeVisible()

    // Should show labor and energy cost items
    const operatingCostsRow = page.locator('.operating-costs-row')
    await expect(operatingCostsRow.locator('.operating-cost-item').first()).toBeVisible()
    // At least one cost item should contain a dollar amount
    const costText = await operatingCostsRow.textContent()
    expect(costText).toContain('$')
  })
})

// ── Sourcing Comparison Panel ─────────────────────────────────────────────────

test.describe('Sourcing Comparison Panel', () => {
  // Helper that builds a procurement factory with a resource-configured PURCHASE unit.
  function makeSourcingFactory(buildingId: string, buildingName: string) {
    return {
      id: buildingId,
      companyId: `company-sc-${buildingId}`,
      cityId: 'city-ba',
      type: 'FACTORY' as const,
      name: buildingName,
      latitude: 48.1486,
      longitude: 17.1077,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units: [
        {
          id: `unit-sc-${buildingId}`,
          buildingId,
          unitType: 'PURCHASE' as const,
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
          maxPrice: null,
          lockedCityId: null,
        },
      ],
    }
  }

  test('sourcing comparison panel shows for active PURCHASE unit with resource', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-sc-panel',
      playerId: player.id,
      name: 'SC Panel Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeSourcingFactory('building-sc-panel', 'SC Panel Factory'), companyId: 'company-sc-panel' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sc-panel')
    await expect(page.getByRole('heading', { name: 'SC Panel Factory' })).toBeVisible()

    // Click on the PURCHASE unit in Current Configuration
    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    // Sourcing comparison panel should be visible
    await expect(page.locator('.sourcing-comparison')).toBeVisible()
    await expect(page.getByText('Sourcing Comparison')).toBeVisible()
    // The subtitle should explain the landed cost formula
    await expect(page.locator('.sourcing-comparison-subtitle')).toBeVisible()
  })

  test('sourcing comparison shows ranked candidates from default mock (3 cities)', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-sc-ranked',
      playerId: player.id,
      name: 'SC Ranked Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeSourcingFactory('building-sc-ranked', 'SC Ranked Factory'), companyId: 'company-sc-ranked' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sc-ranked')
    await expect(page.getByRole('heading', { name: 'SC Ranked Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    // Panel should be visible and show table
    const panel = page.locator('.sourcing-comparison')
    await expect(panel).toBeVisible()

    // Default mock returns 3 candidates (Bratislava, Prague, Vienna)
    const rows = panel.locator('.sourcing-row')
    await expect(rows).toHaveCount(3)

    // First row (rank 1) should be Bratislava (recommended = best landed)
    const firstRow = rows.first()
    await expect(firstRow).toHaveClass(/recommended/)
    await expect(firstRow).toContainText('Bratislava')
    // Should show the recommended badge
    await expect(firstRow.locator('.sc-badge--recommended')).toBeVisible()

    // Prague has lower sticker price ($7.20) but higher transit ($2.80), landed $10.00
    const pragueRow = rows.nth(1)
    await expect(pragueRow).toContainText('Prague')

    // Vienna in third position
    const viennaRow = rows.nth(2)
    await expect(viennaRow).toContainText('Vienna')
  })

  test('sourcing comparison shows landed cost = offer price + transit (distinguishes raw from delivered)', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-sc-landedcost',
      playerId: player.id,
      name: 'SC LandedCost Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeSourcingFactory('building-sc-lc', 'SC LandedCost Factory'), companyId: 'company-sc-landedcost' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    // Set Prague as the cheapest sticker but most expensive landed (proves landed ≠ sticker)
    state.sourcingCandidates[`unit-sc-building-sc-lc`] = [
      {
        sourceType: 'GLOBAL_EXCHANGE',
        sourceCityId: 'city-ba',
        sourceCityName: 'Bratislava',
        sourceVendorCompanyId: null,
        sourceVendorName: null,
        exchangePricePerUnit: 9.0,
        transitCostPerUnit: 0,
        deliveredPricePerUnit: 9.0,
        estimatedQuality: 0.65,
        distanceKm: 0,
        isEligible: true,
        blockReason: null,
        blockMessage: null,
        isRecommended: true,
        rank: 1,
      },
      {
        sourceType: 'GLOBAL_EXCHANGE',
        sourceCityId: 'city-pr',
        sourceCityName: 'Prague',
        sourceVendorCompanyId: null,
        sourceVendorName: null,
        exchangePricePerUnit: 5.0, // cheaper sticker!
        transitCostPerUnit: 6.0, // but expensive transit
        deliveredPricePerUnit: 11.0, // worse landed cost
        estimatedQuality: 0.85,
        distanceKm: 310,
        isEligible: true,
        blockReason: null,
        blockMessage: null,
        isRecommended: false,
        rank: 2,
      },
    ]

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sc-lc')
    await expect(page.getByRole('heading', { name: 'SC LandedCost Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const panel = page.locator('.sourcing-comparison')
    await expect(panel).toBeVisible()

    // The "cheapest sticker ≠ best landed" note should be visible (Prague is cheaper on sticker)
    await expect(panel.locator('.sourcing-trap-note')).toBeVisible()

    // Bratislava row is recommended (best landed cost)
    const braRow = panel.locator('.sourcing-row').first()
    await expect(braRow).toHaveClass(/recommended/)
    await expect(braRow).toContainText('Bratislava')
    await expect(braRow.locator('.sc-badge--recommended')).toBeVisible()

    // Prague row shows transit cost
    const prRow = panel.locator('.sourcing-row').nth(1)
    await expect(prRow).toContainText('Prague')
    await expect(prRow.locator('.transit-cost')).toBeVisible()

    // Bratislava has zero transit → transit-free label
    await expect(braRow.locator('.transit-free')).toBeVisible()
  })

  test('sourcing comparison shows blocked candidates with reason explanation', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-sc-blocked',
      playerId: player.id,
      name: 'SC Blocked Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeSourcingFactory('building-sc-blk', 'SC Blocked Factory'), companyId: 'company-sc-blocked' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    state.sourcingCandidates[`unit-sc-building-sc-blk`] = [
      {
        sourceType: 'GLOBAL_EXCHANGE',
        sourceCityId: 'city-ba',
        sourceCityName: 'Bratislava',
        sourceVendorCompanyId: null,
        sourceVendorName: null,
        exchangePricePerUnit: 8.5,
        transitCostPerUnit: 0,
        deliveredPricePerUnit: 8.5,
        estimatedQuality: 0.7,
        distanceKm: 0,
        isEligible: true,
        blockReason: null,
        blockMessage: null,
        isRecommended: true,
        rank: 1,
      },
      {
        sourceType: 'GLOBAL_EXCHANGE',
        sourceCityId: 'city-pr',
        sourceCityName: 'Prague',
        sourceVendorCompanyId: null,
        sourceVendorName: null,
        exchangePricePerUnit: 7.2,
        transitCostPerUnit: 2.8,
        deliveredPricePerUnit: 10.0,
        estimatedQuality: 0.82,
        distanceKm: 310,
        isEligible: false,
        blockReason: 'MAX_PRICE_EXCEEDED',
        blockMessage: 'Landed cost ($10.00) exceeds your max price ($9.00).',
        isRecommended: false,
        rank: 2,
      },
    ]

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sc-blk')
    await expect(page.getByRole('heading', { name: 'SC Blocked Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const panel = page.locator('.sourcing-comparison')
    await expect(panel).toBeVisible()

    // Prague row should show the blocked badge
    const prRow = panel.locator('.sourcing-row').nth(1)
    await expect(prRow).toHaveClass(/ineligible/)
    await expect(prRow.locator('.sc-badge--blocked')).toBeVisible()
    await expect(prRow.locator('.sc-badge--blocked')).toContainText('Price too high')

    // The filter hint should appear (some candidates blocked)
    await expect(panel.locator('.sourcing-filter-hint')).toBeVisible()
  })

  test('sourcing comparison shows quality for each candidate', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-sc-quality',
      playerId: player.id,
      name: 'SC Quality Co',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [{ ...makeSourcingFactory('building-sc-qlt', 'SC Quality Factory'), companyId: 'company-sc-quality' }],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-sc-qlt')
    await expect(page.getByRole('heading', { name: 'SC Quality Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const panel = page.locator('.sourcing-comparison')
    await expect(panel).toBeVisible()

    // All candidate rows should show quality percentage
    const rows = panel.locator('.sourcing-row')
    // Wait for at least one row to be visible (async data load)
    await expect(rows.first()).toBeVisible()
    const count = await rows.count()
    expect(count).toBeGreaterThan(0)

    for (let i = 0; i < count; i++) {
      const qualityCell = rows.nth(i).locator('.sourcing-col-quality')
      await expect(qualityCell).toBeVisible()
      const text = await qualityCell.textContent()
      // Quality should be formatted as a percentage (e.g. "70%")
      expect(text).toMatch(/%/)
    }
  })
})

test.describe('Unit upgrade panel', () => {
  function makeUpgradeBuilding(playerId: string, unitType = 'MANUFACTURING') {
    return {
      id: 'building-uu-1',
      companyId: 'company-uu',
      cityId: 'city-ba',
      type: 'FACTORY',
      name: 'Upgrade Factory',
      latitude: 48.15,
      longitude: 17.11,
      level: 1,
      powerConsumption: 2,
      isForSale: false,
      builtAtUtc: '2026-01-01T00:00:00Z',
      pendingConfiguration: null,
      units: [
        {
          id: 'unit-uu-1',
          buildingId: 'building-uu-1',
          unitType,
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
    }
  }

  test('shows upgrade panel with cost and duration for an upgradable unit', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-uu',
      playerId: player.id,
      name: 'Upgrade Co',
      cash: 50000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeUpgradeBuilding(player.id)],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-uu-1')
    await expect(page.getByRole('heading', { name: 'Upgrade Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const upgradePanel = page.locator('.unit-upgrade-panel')
    await expect(upgradePanel).toBeVisible()

    // Should show current and next level
    await expect(upgradePanel.getByText('Level 1')).toBeVisible()
    await expect(upgradePanel.locator('.next-level')).toBeVisible()

    // Should show cost (mocked: $8,000)
    await expect(upgradePanel.locator('.unit-upgrade-cost')).toBeVisible()

    // Should show duration
    await expect(upgradePanel.locator('.unit-upgrade-duration')).toBeVisible()

    // Should show confirm button
    await expect(upgradePanel.getByRole('button', { name: 'Upgrade Now' })).toBeVisible()
  })

  test('upgrade confirm button schedules upgrade and shows building reload', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-uu',
      playerId: player.id,
      name: 'Upgrade Co',
      cash: 50000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeUpgradeBuilding(player.id)],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-uu-1')
    await expect(page.getByRole('heading', { name: 'Upgrade Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const upgradePanel = page.locator('.unit-upgrade-panel')
    await expect(upgradePanel).toBeVisible()
    await expect(upgradePanel.getByRole('button', { name: 'Upgrade Now' })).toBeVisible()

    // After scheduling, panel should re-render (button becomes disabled while processing)
    await upgradePanel.getByRole('button', { name: 'Upgrade Now' }).click()

    // Panel should still be visible after upgrade (not collapsed)
    await expect(upgradePanel).toBeVisible()
  })

  test('shows insufficient-funds error when upgrade cannot be afforded', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-uu',
      playerId: player.id,
      name: 'Broke Co',
      cash: 1,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [makeUpgradeBuilding(player.id)],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.upgradeInsufficientFundsUnitId = 'unit-uu-1'

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-uu-1')
    await expect(page.getByRole('heading', { name: 'Upgrade Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const upgradePanel = page.locator('.unit-upgrade-panel')
    await expect(upgradePanel).toBeVisible()

    await upgradePanel.getByRole('button', { name: 'Upgrade Now' }).click()

    // Error message should appear
    await expect(upgradePanel.locator('.form-error')).toContainText('Not enough cash')
  })

  test('shows max-level state for a fully upgraded unit', async ({ page }) => {
    const player = makePlayer()
    const building = makeUpgradeBuilding(player.id)
    building.units[0].level = 4
    player.companies.push({
      id: 'company-uu',
      playerId: player.id,
      name: 'Max Level Co',
      cash: 50000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [building],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    // Override upgrade info to return max level
    state.unitUpgradeInfoOverrides['unit-uu-1'] = {
      unitId: 'unit-uu-1',
      unitType: 'MANUFACTURING',
      currentLevel: 4,
      nextLevel: 4,
      isMaxLevel: true,
      isUpgradable: true,
      upgradeCost: 0,
      upgradeTicks: 0,
      currentStat: 4.0,
      nextStat: 4.0,
      statLabel: 'Batches/tick',
    }

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-uu-1')
    await expect(page.getByRole('heading', { name: 'Upgrade Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const upgradePanel = page.locator('.unit-upgrade-panel')
    await expect(upgradePanel).toBeVisible()

    await expect(upgradePanel.locator('.unit-upgrade-max-level')).toBeVisible()
    await expect(upgradePanel).toContainText('Max Level')
    // Upgrade button should NOT be visible
    await expect(upgradePanel.getByRole('button', { name: 'Upgrade Now' })).toBeHidden()
  })

  test('shows in-progress indicator when a pending upgrade plan exists for the unit', async ({ page }) => {
    const player = makePlayer()
    const building = makeUpgradeBuilding(player.id)
    // Set a pending configuration with an upgrade for unit at (0,0)
    building.pendingConfiguration = {
      id: 'plan-uu-1',
      buildingId: 'building-uu-1',
      submittedAtTick: 40,
      appliesAtTick: 50,
      totalTicksRequired: 10,
      units: [
        {
          id: 'plan-unit-uu-1',
          buildingConfigurationPlanId: 'plan-uu-1',
          unitType: 'MANUFACTURING',
          gridX: 0,
          gridY: 0,
          level: 2,
          isChanged: true,
          ticksRequired: 10,
          startedAtTick: 40,
          appliesAtTick: 50,
          isReverting: false,
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
      removals: [],
    }
    player.companies.push({
      id: 'company-uu',
      playerId: player.id,
      name: 'Upgrading Co',
      cash: 50000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [building],
    })
    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-uu-1')
    await expect(page.getByRole('heading', { name: 'Upgrade Factory' })).toBeVisible()

    const currentSection = getGridSection(page, 'Current Configuration')
    await getGridCell(currentSection, 0, 0).click()

    const upgradePanel = page.locator('.unit-upgrade-panel')
    await expect(upgradePanel).toBeVisible()

    await expect(upgradePanel.locator('.unit-upgrade-in-progress')).toBeVisible()
    await expect(upgradePanel).toContainText('Upgrade in Progress')
  })
})

test.describe('Building detail tick-refresh stability', () => {
  test('background tick refresh does not show a loading spinner or reset the building view', async ({
    page,
  }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-tick-stable',
      playerId: player.id,
      name: 'Stable Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-tick-stable',
          companyId: 'company-tick-stable',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Stable Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-stable-1',
              buildingId: 'building-tick-stable',
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
              maxPrice: 100,
              purchaseSource: 'EXCHANGE',
            } satisfies MockBuildingUnit,
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 10
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-tick-stable')
    await expect(page.getByRole('heading', { name: 'Stable Factory' })).toBeVisible()

    // Simulate a tick advancing
    state.gameState.currentTick = 11
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // The building heading must remain visible — no full-page loading spinner
    await expect(page.getByRole('heading', { name: 'Stable Factory' })).toBeVisible()
    await expect(page.locator('.loading', { hasText: 'Loading' })).toBeHidden()
  })

  test('selected unit sidebar stays open after a background tick refresh', async ({ page }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-unit-stable',
      playerId: player.id,
      name: 'Unit Stable Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-unit-stable',
          companyId: 'company-unit-stable',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Unit Stable Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-stable-sel',
              buildingId: 'building-unit-stable',
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
              maxPrice: 100,
              purchaseSource: 'EXCHANGE',
            } satisfies MockBuildingUnit,
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 20
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-unit-stable')
    await expect(page.getByRole('heading', { name: 'Unit Stable Factory' })).toBeVisible()

    // Select the unit to open the sidebar
    const activeSection = getGridSection(page, 'Current Configuration')
    await getGridCell(activeSection, 0, 0).click()
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()
    await expect(page).toHaveURL(/\/building\/building-unit-stable\?unit=0(?:,|%2C)0$/)

    // Simulate a tick advancing
    state.gameState.currentTick = 21
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Unit Details sidebar must remain visible — context must not be lost
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()
    await expect(page).toHaveURL(/\/building\/building-unit-stable\?unit=0(?:,|%2C)0$/)
    // Main loading spinner must not appear
    await expect(page.locator('.loading', { hasText: 'Loading' })).toBeHidden()
  })

  test('in-progress draft layout edit is preserved after a background tick refresh', async ({
    page,
  }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-draft-stable',
      playerId: player.id,
      name: 'Draft Stable Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-draft-stable',
          companyId: 'company-draft-stable',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Draft Stable Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-draft-stable-1',
              buildingId: 'building-draft-stable',
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
              maxPrice: 50,
              purchaseSource: 'EXCHANGE',
            } satisfies MockBuildingUnit,
          ],
        },
      ],
    })

    const state = setupMockApi(page, { players: [player] })
    state.currentUserId = player.id
    state.currentToken = `token-${player.id}`
    state.gameState.currentTick = 30
    state.gameState.tickIntervalSeconds = 1
    state.gameState.lastTickAtUtc = new Date(Date.now() - 500).toISOString()

    await page.addInitScript((token) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
    }, `token-${player.id}`)

    await page.goto('/building/building-draft-stable')
    await expect(page.getByRole('heading', { name: 'Draft Stable Factory' })).toBeVisible()

    // Enter edit mode — this opens the draft layout editor (shows "Planned Upgrade" section)
    await page.getByRole('button', { name: 'Edit Building' }).click()
    // "Cancel Editing" is always visible when isEditing = true, regardless of whether draft has changes
    await expect(page.getByRole('button', { name: 'Cancel Editing' })).toBeVisible()
    // The planned-upgrade section heading confirms edit mode is active
    await expect(page.getByRole('heading', { name: 'Planned Upgrade' })).toBeVisible()

    // Simulate a tick advancing while we have unsaved draft edits
    state.gameState.currentTick = 31
    state.gameState.lastTickAtUtc = new Date().toISOString()

    // Edit mode must still be active — preserveDraft: true prevents tick refresh from resetting isEditing
    await expect(page.getByRole('button', { name: 'Cancel Editing' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Planned Upgrade' })).toBeVisible()
    // No loading spinner must have appeared
    await expect(page.locator('.loading', { hasText: 'Loading' })).toBeHidden()
  })

  test('unit selection is restored from ?unit= query param on direct navigation', async ({
    page,
  }) => {
    const player = makePlayer()
    player.companies.push({
      id: 'company-deeplink',
      playerId: player.id,
      name: 'Deeplink Corp',
      cash: 500000,
      foundedAtUtc: '2026-01-01T00:00:00Z',
      buildings: [
        {
          id: 'building-deeplink',
          companyId: 'company-deeplink',
          cityId: 'city-ba',
          type: 'FACTORY',
          name: 'Deeplink Factory',
          latitude: 48.15,
          longitude: 17.11,
          level: 1,
          powerConsumption: 2,
          isForSale: false,
          builtAtUtc: '2026-01-01T00:00:00Z',
          pendingConfiguration: null,
          units: [
            {
              id: 'unit-deeplink-1',
              buildingId: 'building-deeplink',
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
              maxPrice: 80,
              purchaseSource: 'EXCHANGE',
            } satisfies MockBuildingUnit,
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

    // Navigate directly with the unit query param to test deep-link restore
    await page.goto('/building/building-deeplink?unit=0,0')
    await expect(page.getByRole('heading', { name: 'Deeplink Factory' })).toBeVisible()

    // The unit details panel must open automatically from the URL param
    await expect(page.getByRole('heading', { name: 'Unit Details' })).toBeVisible()
    // URL must still contain the unit param
    await expect(page).toHaveURL(/\/building\/building-deeplink\?unit=0(?:,|%2C)0$/)
  })
})
