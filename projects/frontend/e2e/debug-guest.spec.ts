import { test, expect } from '@playwright/test'
import { setupMockApi } from './helpers/mock-api'

test('debug - step 5 should now render for guests', async ({ page }) => {
  const errors: string[] = []
  page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text().substring(0,100)) })
  setupMockApi(page)
  await page.addInitScript(() => {
    localStorage.setItem('onboarding_progress', JSON.stringify({
      step: 5, industry: 'FURNITURE', cityId: 'city-ba', productId: 'product-chair-1',
      companyName: 'Guest Corp', factoryLotId: 'lot-industrial-1', shopLotId: 'lot-commercial-1', guestCash: 420000
    }))
  })
  await page.goto('/onboarding?step=complete')
  await page.waitForLoadState('networkidle')
  const h2 = await page.locator('h2, h3').allTextContents()
  console.log('HEADINGS:', JSON.stringify(h2))
  console.log('ERRORS:', JSON.stringify(errors))
})
