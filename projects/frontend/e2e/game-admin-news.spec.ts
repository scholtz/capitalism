import { expect, test, type Page } from '@playwright/test'

import { makeAdminPlayer, makePlayer, setupMockApi } from './helpers/mock-api'

async function authenticate(page: Page, token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
  }, token)
}

test.describe('Game news and administration', () => {
  test('shows unread news badge and clears it after the news page is opened', async ({ page }) => {
    const player = makePlayer({
      onboardingCompletedAtUtc: '2026-01-02T00:00:00Z',
    })

    setupMockApi(page, {
      players: [player],
      currentUserId: player.id,
      currentToken: `token-${player.id}`,
      gameNewsEntries: [
        {
          id: 'news-1',
          entryType: 'NEWS',
          status: 'PUBLISHED',
          targetServerKey: 'test-server',
          createdByEmail: 'admin@test.com',
          updatedByEmail: 'admin@test.com',
          createdAtUtc: '2026-01-10T08:00:00Z',
          updatedAtUtc: '2026-01-10T08:00:00Z',
          publishedAtUtc: '2026-01-10T08:00:00Z',
          readByPlayerIds: [],
          localizations: [
            {
              locale: 'en',
              title: 'Server Gazette',
              summary: 'A fresh issue is waiting for every founder.',
              htmlContent: '<p>New production dashboards are now live.</p>',
            },
          ],
        },
      ],
    })

    await authenticate(page, `token-${player.id}`)
    await page.goto('/dashboard')

    await expect(page.locator('.news-badge')).toContainText('1')

    await page.getByRole('link', { name: 'News' }).click()

    await expect(page).toHaveURL('/news')
    await expect(page.getByRole('heading', { name: 'Server Gazette' })).toBeVisible()
    await expect(page.locator('.news-badge')).toHaveCount(0)
  })

  test('local admin can inspect the dashboard and impersonate a player', async ({ page }) => {
    const admin = makeAdminPlayer({
      id: 'local-admin',
      email: 'local-admin@test.com',
      displayName: 'Local Admin',
    })
    const player = makePlayer({
      id: 'target-player',
      email: 'target@test.com',
      displayName: 'Target Tycoon',
      onboardingCompletedAtUtc: '2026-01-03T00:00:00Z',
      companies: [
        {
          id: 'target-company',
          playerId: 'target-player',
          name: 'Target Holdings',
          cash: 420000,
          foundedAtUtc: '2026-01-01T00:00:00Z',
          buildings: [],
        },
      ],
    })

    setupMockApi(page, {
      players: [admin, player],
      currentUserId: admin.id,
      currentToken: `token-${admin.id}`,
      adminMoneyInflowSummaries: [
        {
          category: 'SUBSIDY_PAYOUT',
          amount: 120000,
          description: 'Government subsidies generated a sudden money injection.',
        },
      ],
      adminMultiAccountAlerts: [
        {
          reason: 'SHARED_LOAN',
          exposureAmount: 85000,
          confidenceScore: 0.81,
          supportingEntityType: 'LOAN',
          supportingEntityName: 'Bridge Credit Facility',
          primaryPlayerId: admin.id,
          relatedPlayerId: player.id,
        },
      ],
      adminAuditLogs: [
        {
          id: 'audit-1',
          adminActorPlayerId: admin.id,
          adminActorEmail: admin.email,
          adminActorDisplayName: admin.displayName,
          effectivePlayerId: player.id,
          effectivePlayerEmail: player.email,
          effectivePlayerDisplayName: player.displayName,
          effectiveAccountType: 'PERSON',
          effectiveCompanyId: null,
          effectiveCompanyName: null,
          graphQlOperationName: 'finishOnboarding',
          mutationSummary: 'finishOnboarding on behalf of Target Tycoon',
          responseStatusCode: 200,
          recordedAtUtc: '2026-01-12T12:00:00Z',
        },
      ],
    })

    await authenticate(page, `token-${admin.id}`)
    await page.goto('/admin')

    await expect(page.getByRole('heading', { name: 'Operations Dashboard' })).toBeVisible()
    await expect(page.getByText('SUBSIDY_PAYOUT')).toBeVisible()
    await expect(page.getByText('SHARED_LOAN')).toBeVisible()

    const targetCard = page.locator('.admin-player-card', { hasText: 'Target Tycoon' })
    await targetCard.getByRole('button', { name: 'Impersonate person' }).click()

    await page.waitForURL('/dashboard')
    await expect(page.locator('.impersonation-chip')).toContainText('Impersonating Target Tycoon via Target Tycoon')
  })

  test('admin dashboard shows aggregated shipping costs and expands per-company detail', async ({ page }) => {
    const admin = makeAdminPlayer({
      id: 'local-admin-shipping',
      email: 'local-admin-shipping@test.com',
      displayName: 'Local Admin Shipping',
    })
    const player = makePlayer({
      id: 'shipping-player',
      email: 'shipping@test.com',
      displayName: 'Shipping Tycoon',
    })

    setupMockApi(page, {
      players: [admin, player],
      currentUserId: admin.id,
      currentToken: `token-${admin.id}`,
      adminShippingCostSummaries: [
        {
          companyId: 'shipping-company',
          companyName: 'Shipping Holdings',
          amount: 2450,
          entryCount: 7,
        },
      ],
    })

    await authenticate(page, `token-${admin.id}`)
    await page.goto('/admin')

    await expect(page.getByRole('button', { name: /Shipping costs \(100 ticks\)/i })).toContainText('$2,450')
    await page.getByRole('button', { name: /Shipping costs \(100 ticks\)/i }).click()
    await expect(page.getByText('Shipping Holdings')).toBeVisible()
    await expect(page.getByText('7 shipping ledger entries')).toBeVisible()
  })

  test('root admin can publish a news entry from the dashboard into the public feed', async ({ page }) => {
    const rootAdmin = makePlayer({
      id: 'root-admin',
      email: 'root@example.com',
      displayName: 'Root Operator',
    })

    setupMockApi(page, {
      players: [rootAdmin],
      currentUserId: rootAdmin.id,
      currentToken: `token-${rootAdmin.id}`,
    })

    await authenticate(page, `token-${rootAdmin.id}`)
    await page.goto('/admin')

    await expect(page.getByRole('heading', { name: 'Global administrators' })).toBeVisible()

    await page.getByLabel('Status').selectOption('PUBLISHED')
    await page.getByLabel('Headline').fill('Weekend patch deployed')
    await page.getByLabel('Summary').fill('Core admin tools and the shared newspaper are now online.')
    await page.locator('.admin-composer-form .editor-surface').fill('Patch notes for the weekend are now live in every game.')
    await page.getByRole('button', { name: 'Save entry' }).click()

    await expect(page.getByText('News entry saved.')).toBeVisible()

    await page.goto('/news')

    await expect(page.getByRole('heading', { name: 'Weekend patch deployed' })).toBeVisible()
    await expect(page.getByText('Patch notes for the weekend are now live in every game.')).toBeVisible()
  })
})
