import { test, expect } from '@playwright/test'
import { setupMockApi } from './helpers/mock-api'

test('debug guest flow step 4', async ({ page }) => {
  setupMockApi(page)
  
  page.on('console', msg => {
    if (msg.type() === 'error') {
      console.log('CONSOLE ERROR:', msg.text())
    }
  })
  
  await page.goto('/onboarding')
  
  await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
  await page.locator('.industry-card', { hasText: 'Furniture' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  
  await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  
  await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
  await page.getByLabel('Company Name').fill('Guest Corp')
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  
  const purchaseBtn3 = page.getByRole('button', { name: 'Purchase First Factory' })
  console.log('Purchase First Factory disabled:', await purchaseBtn3.isDisabled())
  await purchaseBtn3.click()
  
  await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible({ timeout: 10000 })
  console.log('URL after step 3:', page.url())
  
  const productCards = page.locator('.product-card')
  console.log('Product card count:', await productCards.count())
  
  await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
  await page.getByRole('button', { name: 'List View' }).click()
  
  console.log('Shop lot count:', await page.locator('.lot-list-item').count())
  
  await page.getByRole('button', { name: /High Street Retail Space/i }).click()
  
  const purchaseBtn4 = page.getByRole('button', { name: 'Purchase First Sales Shop' })
  console.log('Purchase First Sales Shop disabled:', await purchaseBtn4.isDisabled())
  
  await purchaseBtn4.click()
  await page.waitForTimeout(2000)
  
  console.log('URL after click:', page.url())
  const main = page.locator('main')
  console.log('Main HTML:', (await main.innerHTML()).substring(0, 1000))
})
