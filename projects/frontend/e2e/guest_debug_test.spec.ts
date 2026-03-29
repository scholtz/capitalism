import { test, expect } from '@playwright/test'
import { setupMockApi } from './e2e/helpers/mock-api'

test('debug guest flow step 4', async ({ page }) => {
  setupMockApi(page)
  
  // Capture console errors
  page.on('console', msg => {
    if (msg.type() === 'error') {
      console.log('CONSOLE ERROR:', msg.text())
    }
  })
  
  await page.goto('/onboarding')
  
  // Step 1
  await expect(page.getByRole('heading', { name: 'Choose Your Industry' })).toBeVisible()
  await page.locator('.industry-card', { hasText: 'Furniture' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  
  // Step 2
  await expect(page.getByRole('heading', { name: 'Choose Your City' })).toBeVisible()
  await page.locator('.city-card', { hasText: 'Bratislava' }).click()
  await page.getByRole('button', { name: 'Next' }).click()
  
  // Step 3
  await expect(page.getByRole('heading', { name: 'Choose Your First Factory Lot' })).toBeVisible()
  await page.getByLabel('Company Name').fill('Guest Corp')
  await page.getByRole('button', { name: 'List View' }).click()
  await page.getByRole('button', { name: /Industrial Plot A1/i }).click()
  
  // Check canProceedStep3 state
  const purchaseBtn3 = page.getByRole('button', { name: 'Purchase First Factory' })
  const isDisabled = await purchaseBtn3.isDisabled()
  console.log('Purchase First Factory disabled:', isDisabled)
  
  await purchaseBtn3.click()
  
  // Step 4
  await expect(page.getByRole('heading', { name: 'Choose Product & First Shop Lot' })).toBeVisible({ timeout: 10000 })
  
  console.log('URL after step 3:', page.url())
  
  // Check what products are loaded
  const productCards = page.locator('.product-card')
  const productCount = await productCards.count()
  console.log('Product card count:', productCount)
  
  await page.locator('.product-card', { hasText: 'Wooden Chair' }).click()
  await page.getByRole('button', { name: 'List View' }).click()
  
  // Check shop lots
  const shopLotButtons = page.locator('.lot-list-item')
  const shopLotCount = await shopLotButtons.count()
  console.log('Shop lot count:', shopLotCount)
  
  await page.getByRole('button', { name: /High Street Retail Space/i }).click()
  
  // Check if Purchase First Sales Shop button is enabled
  const purchaseBtn4 = page.getByRole('button', { name: 'Purchase First Sales Shop' })
  const isDisabled4 = await purchaseBtn4.isDisabled()
  console.log('Purchase First Sales Shop disabled:', isDisabled4)
  
  await purchaseBtn4.click()
  
  // Wait a bit and check state
  await page.waitForTimeout(2000)
  
  // Take a snapshot of the current page state
  const mainContent = await page.locator('main').innerHTML()
  console.log('Main content after click:', mainContent.substring(0, 500))
  
  console.log('URL after click:', page.url())
  
  // Check if step 5 heading is visible
  const heading = page.getByRole('heading', { name: /Your Empire Preview is Ready/i })
  await expect(heading).toBeVisible({ timeout: 3000 })
})
