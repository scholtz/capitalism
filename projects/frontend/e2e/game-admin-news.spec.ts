import { expect, test, type Page } from '@playwright/test'

import { makeAdminPlayer, makePlayer, setupMockApi, type MockGameNewsEntry } from './helpers/mock-api'

async function authenticate(page: Page, token: string) {
  await page.addInitScript((value) => {
    localStorage.setItem('auth_token', value)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7_200_000).toISOString())
  }, token)
}

const makeNewsEntry = (overrides: Partial<MockGameNewsEntry> = {}): MockGameNewsEntry => ({
  id: `news-${Date.now()}-${Math.random()}`,
  entryType: 'NEWS',
  status: 'PUBLISHED',
  targetServerKey: null,
  createdByEmail: 'admin@test.com',
  updatedByEmail: 'admin@test.com',
  createdAtUtc: '2026-01-10T08:00:00Z',
  updatedAtUtc: '2026-01-10T08:00:00Z',
  publishedAtUtc: '2026-01-10T08:00:00Z',
  readByPlayerIds: [],
  localizations: [
    {
      locale: 'en',
      title: 'Default News Title',
      summary: 'Default summary text.',
      htmlContent: '<p>Default HTML content.</p>',
    },
  ],
  ...overrides,
})

const makeChangelogEntry = (overrides: Partial<MockGameNewsEntry> = {}): MockGameNewsEntry =>
  makeNewsEntry({ entryType: 'CHANGELOG', ...overrides })

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

  test('unauthenticated visitor can browse the news page and see changelog entries', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeChangelogEntry({
          id: 'cl-1',
          localizations: [
            {
              locale: 'en',
              title: 'Version 1.0 released',
              summary: 'Initial public release of the game.',
              htmlContent: '<p>The game is now publicly available.</p>',
            },
          ],
        }),
      ],
    })

    await page.goto('/news')

    await expect(page.getByRole('heading', { name: 'Newsroom & Changelog' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Version 1.0 released' })).toBeVisible()
    await expect(page.getByText('Initial public release of the game.')).toBeVisible()
    // Unauthenticated: no badge rendered
    await expect(page.locator('.news-badge')).toHaveCount(0)
  })

  test('empty state is displayed with informative text when no entries exist', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [],
    })

    await page.goto('/news')

    await expect(page.locator('.state-card')).toBeVisible()
    await expect(page.getByText('No published entries yet')).toBeVisible()
    await expect(page.getByText('When administrators publish news or changelog notes, they will appear here.')).toBeVisible()
  })

  test('error state is displayed with retry button when the feed fails to load', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [],
    })

    // Override the gameNewsFeed handler to return a server error response.
    // Must be registered AFTER setupMockApi so it takes priority (LIFO ordering).
    await page.route('**/graphql', (route) => {
      const postData = route.request().postDataJSON() as { query?: string } | null
      const query = postData?.query ?? ''
      if (query.includes('gameNewsFeed')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'News feed is temporarily unavailable' }] }),
        })
      }
      return route.continue()
    })

    await page.goto('/news')

    await expect(page.locator('.state-card-error')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Try again' })).toBeVisible()
  })

  test('changelog filter shows only changelog entries', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeNewsEntry({
          id: 'news-only',
          localizations: [
            {
              locale: 'en',
              title: 'Market News Headline',
              summary: 'News summary',
              htmlContent: '<p>News body</p>',
            },
          ],
        }),
        makeChangelogEntry({
          id: 'cl-only',
          localizations: [
            {
              locale: 'en',
              title: 'Changelog Update',
              summary: 'Changelog summary',
              htmlContent: '<p>Changelog body</p>',
            },
          ],
        }),
      ],
    })

    await page.goto('/news')

    // All entries visible initially
    await expect(page.getByRole('heading', { name: 'Market News Headline' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Changelog Update' })).toBeVisible()

    // Click Changelog filter tab
    await page.getByRole('button', { name: 'Changelog' }).click()

    await expect(page.getByRole('heading', { name: 'Changelog Update' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Market News Headline' })).toHaveCount(0)
  })

  test('newspaper filter shows only news entries', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeNewsEntry({
          id: 'news-only-2',
          localizations: [
            {
              locale: 'en',
              title: 'Breaking Economic News',
              summary: 'News summary',
              htmlContent: '<p>News body</p>',
            },
          ],
        }),
        makeChangelogEntry({
          id: 'cl-only-2',
          localizations: [
            {
              locale: 'en',
              title: 'Patch Notes Entry',
              summary: 'Changelog summary',
              htmlContent: '<p>Changelog body</p>',
            },
          ],
        }),
      ],
    })

    await page.goto('/news')

    // Click Newspaper filter tab
    await page.getByRole('button', { name: 'Newspaper' }).click()

    await expect(page.getByRole('heading', { name: 'Breaking Economic News' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Patch Notes Entry' })).toHaveCount(0)
  })

  test('renders multiple entries with publication dates and html content', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeChangelogEntry({
          id: 'cl-multi-1',
          publishedAtUtc: '2026-03-15T10:00:00Z',
          localizations: [
            {
              locale: 'en',
              title: 'March Changelog Entry',
              summary: 'New buildings added to the city.',
              htmlContent: '<p>Three new industrial buildings are now available in Prague.</p>',
            },
          ],
        }),
        makeChangelogEntry({
          id: 'cl-multi-2',
          publishedAtUtc: '2026-04-01T09:00:00Z',
          localizations: [
            {
              locale: 'en',
              title: 'April Changelog Entry',
              summary: 'Tax system updated.',
              htmlContent: '<p>The tax rate now applies to net profit rather than gross revenue.</p>',
            },
          ],
        }),
      ],
    })

    await page.goto('/news')

    await expect(page.getByRole('heading', { name: 'March Changelog Entry' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'April Changelog Entry' })).toBeVisible()
    await expect(page.getByText('New buildings added to the city.')).toBeVisible()
    await expect(page.getByText('Tax system updated.')).toBeVisible()
    // HTML content is rendered
    await expect(page.getByText('Three new industrial buildings are now available in Prague.')).toBeVisible()
    await expect(page.getByText('The tax rate now applies to net profit rather than gross revenue.')).toBeVisible()
  })

  test('global news entries are visible on all servers (null targetServerKey)', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeChangelogEntry({
          id: 'global-cl',
          targetServerKey: null,
          localizations: [
            {
              locale: 'en',
              title: 'Global Announcement',
              summary: 'This appears everywhere.',
              htmlContent: '<p>Visible across all game servers.</p>',
            },
          ],
        }),
        makeNewsEntry({
          id: 'other-server-news',
          targetServerKey: 'other-server',
          localizations: [
            {
              locale: 'en',
              title: 'Hidden Server News',
              summary: 'Only for other-server players.',
              htmlContent: '<p>Server-specific news.</p>',
            },
          ],
        }),
      ],
    })

    await page.goto('/news')

    await expect(page.getByRole('heading', { name: 'Global Announcement' })).toBeVisible()
    // Entry for another server key should not be visible
    await expect(page.getByRole('heading', { name: 'Hidden Server News' })).toHaveCount(0)
  })

  test('news entry badge pill is shown with correct label', async ({ page }) => {
    setupMockApi(page, {
      players: [],
      gameNewsEntries: [
        makeChangelogEntry({
          id: 'pill-cl',
          localizations: [
            { locale: 'en', title: 'Changelog Pill Test', summary: '', htmlContent: '' },
          ],
        }),
        makeNewsEntry({
          id: 'pill-news',
          localizations: [
            { locale: 'en', title: 'News Pill Test', summary: '', htmlContent: '' },
          ],
        }),
      ],
    })

    await page.goto('/news')

    const changelogCard = page.locator('.news-card', { hasText: 'Changelog Pill Test' })
    await expect(changelogCard.locator('.news-pill-changelog')).toContainText('Changelog')

    const newsCard = page.locator('.news-card', { hasText: 'News Pill Test' })
    await expect(newsCard.locator('.news-pill-news')).toContainText('Newspaper')
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
