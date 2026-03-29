import { expect, test } from '@playwright/test'
import { makePlayer, setupMockApi } from './helpers/mock-api'

function getGridSection(page: Parameters<typeof test>[0]['page'], heading: string) {
  return page.locator('.grid-section').filter({ has: page.getByRole('heading', { name: heading }) }).first()
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
    await page.locator('.config-field').filter({ has: page.getByText('Research Product') }).locator('select').selectOption('prod-chair')

    await getGridCell(plannedSection, 1, 0).click()
    await expect(page.getByText('Brand Scope')).toBeVisible()
    await page.locator('.config-field').filter({ has: page.getByText('Brand Scope') }).locator('select').selectOption('CATEGORY')
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
      resourceTypes: [
        { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', description: 'Wood', imageUrl: null },
      ],
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

  test('directional link editing: full user journey — cycle states, see changes summary, submit, confirm pending', async ({
    page,
  }) => {
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

  test('directional link editing: configuration warnings shown when purchase unit is not linked', async ({
    page,
  }) => {
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
    await expect(page.locator('.config-warnings')).not.toContainText(
      'Purchase unit at (0, 0) is not linked to a consumer unit.',
    )
    await expect(page.locator('.config-warnings')).toContainText(
      'Manufacturing unit at (1, 0) is not linked to a storage or sales output.',
    )
  })

  test('diagonal link editing: full journey — create diagonal connections, review summary, submit plan', async ({
    page,
  }) => {
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

  test('diagonal link editing: flow diagnostics — warnings shown for disconnected layout, resolved after linking', async ({
    page,
  }) => {
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
    await expect(page.locator('.config-warnings')).toContainText(
      'Purchase unit at (0, 0) is not linked to a consumer unit.',
    )
    await expect(page.locator('.config-warnings')).toContainText(
      'Manufacturing unit at (1, 1) is not linked to a storage or sales output.',
    )

    // Enter edit mode
    await page.getByRole('button', { name: 'Edit Building' }).click()
    const plannedSection = getGridSection(page, 'Planned Upgrade')

    // The diagonal button covering the 2×2 block (0,0)-(1,1) is enabled (all 4 cells occupied)
    const diagButton = plannedSection.locator('.link-toggle.diagonal').first()
    await expect(diagButton).not.toBeDisabled()
    await diagButton.click()
    // tl-br: PURCHASE(0,0).linkDownRight → MANUFACTURING(1,1)
    await expect(diagButton).toHaveClass(/state-tl-br/)

    // Purchase warning should be gone — PURCHASE now links diagonally to MANUFACTURING
    await expect(page.locator('.config-warnings')).not.toContainText(
      'Purchase unit at (0, 0) is not linked to a consumer unit.',
    )

    // Manufacturing still has no output link — warning remains
    await expect(page.locator('.config-warnings')).toContainText(
      'Manufacturing unit at (1, 1) is not linked to a storage or sales output.',
    )
  })
})
