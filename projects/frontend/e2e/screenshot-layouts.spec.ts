import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

test('screenshot building layouts panel', async ({ page }) => {
  const player = makePlayer({
    onboardingCompletedAtUtc: '2026-01-01T00:00:00Z',
    companies: [
      {
        id: 'company-lt',
        playerId: 'player-1',
        name: 'Layout Test Co',
        cash: 500000,
        foundedAtUtc: '2026-01-01T00:00:00Z',
        buildings: [
          {
            id: 'building-lt',
            companyId: 'company-lt',
            cityId: 'city-ba',
            type: 'FACTORY',
            name: 'Layout Test Factory',
            latitude: 48.15,
            longitude: 17.11,
            level: 1,
            powerConsumption: 0,
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

  state.buildingLayouts = [
    {
      id: 'layout-1',
      buildingType: 'FACTORY',
      name: 'My Factory Blueprint',
      description: 'Standard furniture factory setup',
      unitsJson: JSON.stringify([
        { unitType: 'PURCHASE', gridX: 0, gridY: 0, linkRight: true, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
        { unitType: 'MANUFACTURING', gridX: 1, gridY: 0, linkRight: true, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
        { unitType: 'STORAGE', gridX: 2, gridY: 0, linkRight: true, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
        { unitType: 'B2B_SALES', gridX: 3, gridY: 0, linkRight: false, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
      ]),
      updatedAtUtc: '2026-04-10T10:00:00Z',
      isLocal: false,
    },
    {
      id: 'layout-2',
      buildingType: 'FACTORY',
      name: 'Quick Start Layout',
      description: null,
      unitsJson: JSON.stringify([
        { unitType: 'PURCHASE', gridX: 0, gridY: 0, linkRight: true, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
        { unitType: 'PUBLIC_SALES', gridX: 1, gridY: 0, linkRight: false, linkDown: false, linkLeft: false, linkUp: false, linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false, resourceTypeId: null, productTypeId: null, minPrice: null, maxPrice: null, purchaseSource: null, saleVisibility: null, budget: null, mediaHouseBuildingId: null, minQuality: null, brandScope: null, vendorLockCompanyId: null },
      ]),
      updatedAtUtc: '2026-04-11T08:30:00Z',
      isLocal: false,
    },
  ]

  await page.addInitScript((token) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, `token-${player.id}`)

  await page.goto('/building/building-lt')

  // Enter edit mode via starter layout
  await page.getByRole('button', { name: /Apply Starter Layout/i }).click()

  const panel = page.locator('[aria-label="Building Layouts"]')
  await expect(panel).toBeVisible()

  await page.screenshot({ path: '/tmp/building-layouts-screenshot.png', fullPage: false })
  console.log('Screenshot saved to /tmp/building-layouts-screenshot.png')
})
