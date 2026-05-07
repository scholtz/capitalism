import { expect, test, type Page } from '@playwright/test'

import { makeAdminPlayer, makePlayer, setupMockApi } from './helpers/mock-api'

async function authenticate(page: Page, token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, token)
}

test.describe('Operations dashboard', () => {
  test('navigates between operations sub-sections from level-2 menu', async ({ page }) => {
    const admin = makeAdminPlayer()
    setupMockApi(page, {
      players: [admin],
      currentUserId: admin.id,
      currentToken: `token-${admin.id}`,
      adminMoneyInflowSummaries: [{ category: 'Public sales', amount: 1000, description: 'Revenue inflow' }],
      adminShippingCostSummaries: [{ companyId: 'c1', companyName: 'Taxes', amount: 250, entryCount: 3 }],
      adminProductAnalyticsRows: [
        {
          productTypeId: 'prod-1',
          productName: 'Wooden Chair',
          materialCost: 100,
          energyCost: 25,
          laborCost: 40,
          unitsProduced: 120,
          unitsSold: 95,
          marketSize: 140,
          marketSaturationPercent: 67.86,
          currentMarketingSpend: 60,
          researchQualityLevel: 0.71,
        },
      ],
    })

    await authenticate(page, `token-${admin.id}`)
    await page.goto('/operations/statistics')
    await expect(page.getByRole('heading', { name: 'Overview / Statistics' })).toBeVisible()

    await page.getByRole('link', { name: 'News & Changelog' }).click()
    await expect(page).toHaveURL('/operations/news')

    await page.getByRole('link', { name: 'Players & Intervention' }).click()
    await expect(page).toHaveURL('/operations/players')

    await page.getByRole('link', { name: 'Product Analytics' }).click()
    await expect(page).toHaveURL('/operations/products')
    await expect(page.getByText('Wooden Chair')).toBeVisible()
  })

  test('supports players table and player detail navigation', async ({ page }) => {
    const admin = makeAdminPlayer()
    const target = makePlayer({ id: 'target-1', displayName: 'Target Tycoon', email: 'target@example.com' })
    setupMockApi(page, {
      players: [admin, target],
      currentUserId: admin.id,
      currentToken: `token-${admin.id}`,
    })

    await authenticate(page, `token-${admin.id}`)
    await page.goto('/operations/players')

    await page.getByPlaceholder('Search by player, email, or role').fill('Target')
    await page.getByRole('link', { name: 'Target Tycoon' }).click()
    await expect(page).toHaveURL('/operations/players/target-1')
    await expect(page.getByRole('heading', { name: 'Player detail & intervention' })).toBeVisible()
  })

  test('submits news from dedicated publisher route', async ({ page }) => {
    const rootAdmin = makePlayer({
      id: 'root-admin',
      email: 'root@example.com',
      displayName: 'Root Admin',
      role: 'ADMIN',
    })

    setupMockApi(page, {
      players: [rootAdmin],
      currentUserId: rootAdmin.id,
      currentToken: `token-${rootAdmin.id}`,
      rootAdminEmails: [rootAdmin.email],
    })

    await authenticate(page, `token-${rootAdmin.id}`)
    await page.goto('/operations/news/new')

    await page.getByLabel('Headline').fill('Operations news item')
    await page.getByLabel('Summary').fill('Summary for operators')
    await page.getByRole('button', { name: 'Save entry' }).click()

    await expect(page.getByText('News entry saved.')).toBeVisible()
  })

  test('exports product analytics CSV', async ({ page }) => {
    const admin = makeAdminPlayer()
    setupMockApi(page, {
      players: [admin],
      currentUserId: admin.id,
      currentToken: `token-${admin.id}`,
      adminProductAnalyticsRows: [
        {
          productTypeId: 'prod-2',
          productName: 'Bread',
          materialCost: 80,
          energyCost: 20,
          laborCost: 30,
          unitsProduced: 90,
          unitsSold: 75,
          marketSize: 100,
          marketSaturationPercent: 75,
          currentMarketingSpend: 50,
          researchQualityLevel: 0.8,
        },
      ],
    })

    await authenticate(page, `token-${admin.id}`)
    await page.goto('/operations/products')
    await expect(page.getByText('Bread')).toBeVisible()

    const downloadPromise = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Export CSV' }).click()
    const download = await downloadPromise
    expect(download.suggestedFilename()).toContain('.csv')
  })
})
