import { expect, test } from '@playwright/test'
import { makePlayer, makeStartupPackOffer, setupMockApi } from './helpers/mock-api'

test.describe('Manufacturing encyclopedia', () => {
  test('shows raw material cards and product recipes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [
        {
          id: 'res-wood',
          name: 'Wood',
          slug: 'wood',
          category: 'ORGANIC',
          basePrice: 10,
          weightPerUnit: 5,
          unitName: 'Ton',
          unitSymbol: 't',
          imageUrl: 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="brown"/></svg>',
          description: 'Timber for furniture.',
        },
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
      ],
    })

    await page.goto('/encyclopedia')

    await expect(page.getByRole('heading', { name: 'Manufacturing Encyclopedia' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Wood' })).toBeVisible()
    await page.getByRole('button', { name: /Electronic Table/ }).click()
    await expect(page.getByRole('heading', { name: 'Electronic Table' }).first()).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Requirement composition' })).toBeVisible()
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('1 t')
    await expect(page.locator('.composition-node.ingredient').first()).toContainText('Wood')
    await expect(page.locator('.composition-node.ingredient').nth(1)).toContainText('10 packs')
    await expect(page.locator('.composition-node.ingredient').nth(1)).toContainText('Electronic Components')
    await expect(page.locator('.composition-node.ingredient')).toHaveCount(2)
  })

  test('localizes encyclopedia product names when language changes', async ({ page }) => {
    setupMockApi(page, {
      resourceTypes: [
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
          description: 'Timber for furniture.',
        },
      ],
      productTypes: [
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
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC', basePrice: 10, weightPerUnit: 5, unitName: 'Ton', unitSymbol: 't', imageUrl: null, description: 'Wood' }, inputProductType: null, quantity: 1 }],
        },
      ],
    })

    await page.goto('/encyclopedia')
    await page.getByLabel('Language').selectOption('sk')

    await expect(page.getByRole('heading', { name: 'Výrobná encyklopédia' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Drevo' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Elektronický Stôl' }).first()).toBeVisible()
  })

  test('updates locked pro products after claiming startup pack', async ({ page }) => {
    const player = makePlayer({
      startupPackOffer: makeStartupPackOffer(),
      companies: [
        {
          id: 'company-pro',
          playerId: 'player-1',
          name: 'Premium Works',
          cash: 500000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
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
          description: 'A basic wooden chair.',
          recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
        },
        {
          id: 'prod-premium-desk',
          name: 'Premium Desk',
          slug: 'premium-desk',
          industry: 'FURNITURE',
          basePrice: 180,
          baseCraftTicks: 4,
          outputQuantity: 4,
          energyConsumptionMwh: 1.6,
          unitName: 'Desk',
          unitSymbol: 'desks',
          isProOnly: true,
          description: 'Advanced office furniture line.',
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

    await page.goto('/encyclopedia')
    const premiumDeskCard = page.getByRole('button', { name: /Premium Desk/ })
    await expect(premiumDeskCard).toContainText('Requires Pro')
    await premiumDeskCard.click()
    await expect(page.locator('.selected-product')).toContainText('Pro unlocks this product and other advanced goods')

    await page.goto('/dashboard')
    await page.getByRole('button', { name: 'Claim startup pack' }).click()
    await expect(page.getByText('Your Pro access is active until')).toBeVisible()

    await page.goto('/encyclopedia')
    await expect(page.getByRole('button', { name: /Premium Desk/ })).toContainText('Pro unlocked')
    await page.getByRole('button', { name: /Premium Desk/ }).click()
    await expect(page.locator('.selected-product')).toContainText('Your active Pro access unlocks this product immediately.')
  })
})
