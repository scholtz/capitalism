import { test, expect } from '@playwright/test'
import { setupMockApi, makePlayer } from './helpers/mock-api'

test('debug resume step 5', async ({ page }) => {
  const shopBuildingId = 'building-shop-debug'
  const player = makePlayer({
    onboardingCompletedAtUtc: new Date().toISOString(),
    onboardingShopBuildingId: shopBuildingId,
    onboardingFirstSaleCompletedAtUtc: null,
    companies: [{
      id: 'comp-debug',
      playerId: 'player-1',
      name: 'Debug Corp',
      cash: 350000,
      foundedAtUtc: new Date().toISOString(),
      buildings: [],
    }],
  })
  const state = setupMockApi(page, { players: [player] })
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`

  await page.addInitScript((token) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, `token-${player.id}`)

  const logs: string[] = []
  page.on('console', (msg) => logs.push(`[${msg.type()}] ${msg.text()}`))

  await page.goto('/onboarding')
  await page.waitForTimeout(3000)
  
  const url = page.url()
  const headings = await page.locator('h2,h3').allTextContents()
  const buttons = await page.locator('button').allTextContents()
  console.log('URL:', url)
  console.log('Headings:', JSON.stringify(headings))
  console.log('Buttons:', JSON.stringify(buttons))
  console.log('LOGS:', logs.slice(0,10).join('\n'))
  
  expect(url).toBeTruthy() // always pass
})
