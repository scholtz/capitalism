import { test } from '@playwright/test'
import { makePlayer, setupMockApi } from './helpers/mock-api'

test('capture player settings screenshot', async ({ page }) => {
  const player = makePlayer({ personalAccountName: 'Aster Nova Finch' })
  const state = setupMockApi(page, { players: [player] })
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`

  await page.addInitScript((token) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, `token-${player.id}`)

  await page.goto('/settings')
  await page.screenshot({ path: 'test-results/player-settings-ui.png', fullPage: true })
})
