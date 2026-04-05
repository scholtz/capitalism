import { test } from '@playwright/test'
import { setupMockApi, makePlayer, makeChairProduct } from './e2e/helpers/mock-api'
import type { MockPublicSalesAnalytics } from './e2e/helpers/mock-api'

test('screenshot market intelligence', async ({ page }) => {
  const player = makePlayer()
  const chairProduct = makeChairProduct()
  player.companies.push({
    id: 'company-ss',
    playerId: player.id,
    name: 'Premier Furniture Shop',
    cash: 500000,
    foundedAtUtc: '2026-01-01T00:00:00Z',
    buildings: [
      {
        id: 'building-ss',
        companyId: 'company-ss',
        cityId: 'city-ba',
        type: 'SALES_SHOP',
        name: 'Downtown Furniture Store',
        latitude: 48.15,
        longitude: 17.11,
        level: 1,
        powerConsumption: 3,
        isForSale: false,
        builtAtUtc: '2026-01-01T00:00:00Z',
        pendingConfiguration: null,
        units: [
          {
            id: 'unit-ss-ps',
            buildingId: 'building-ss',
            unitType: 'PUBLIC_SALES',
            gridX: 1,
            gridY: 0,
            level: 1,
            linkUp: false, linkDown: false, linkLeft: false, linkRight: false,
            linkUpLeft: false, linkUpRight: false, linkDownLeft: false, linkDownRight: false,
            productTypeId: chairProduct.id,
            resourceTypeId: null,
            minPrice: 45,
            maxPrice: null,
            purchaseSource: null,
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
        ],
      },
    ],
  })

  const state = setupMockApi(page, { players: [player] })
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`
  await page.addInitScript((token: string) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, `token-${player.id}`)

  const analytics: MockPublicSalesAnalytics = {
    buildingUnitId: 'unit-ss-ps',
    buildingId: 'building-ss',
    buildingName: 'Downtown Furniture Store',
    cityName: 'Bratislava',
    totalRevenue: 4500,
    totalQuantitySold: 300,
    averagePricePerUnit: 15,
    currentSalesCapacity: 120,
    dataFromTick: 1,
    dataToTick: 30,
    demandSignal: 'STRONG',
    actionHint: 'Demand is strong — your location and product quality are attracting steady customers. Consider raising price slightly to improve margin.',
    recentUtilization: 0.88,
    revenueHistory: Array.from({ length: 30 }, (_, i) => ({ tick: i + 1, revenue: 140 + Math.floor(Math.random() * 40), quantitySold: 9 + Math.floor(Math.random() * 5) })),
    priceHistory: Array.from({ length: 30 }, (_, i) => ({ tick: i + 1, pricePerUnit: 45 + (i > 15 ? 2 : 0) })),
    marketShare: [
      { label: 'Premier Furniture Shop', companyId: 'company-ss', share: 0.62, isUnmet: false },
      { label: 'Rival Furniture Co', companyId: 'other-co', share: 0.23, isUnmet: false },
      { label: 'Unmet Demand', companyId: null, share: 0.15, isUnmet: true },
    ],
    elasticityIndex: -1.3,
    unmetDemandShare: 0.15,
    populationIndex: 1.35,
    inventoryQuality: 0.82,
    brandAwareness: 0.45,
  }
  state.publicSalesAnalytics['unit-ss-ps'] = analytics

  await page.goto('http://localhost:5173/building/building-ss')

  const activeSection = page.locator('.grid-section').filter({ has: page.getByRole('heading', { name: 'Current Configuration' }) }).first()
  const psCell = activeSection.locator('.unit-row').nth(0).locator('.grid-cell').nth(1)
  await psCell.click()

  const panel = page.locator('[aria-label="Market Intelligence"]')
  await panel.waitFor({ state: 'visible' })
  await page.waitForTimeout(500)

  await page.screenshot({ path: '/tmp/market-intelligence.png', fullPage: false })
})
