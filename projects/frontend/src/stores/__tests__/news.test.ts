import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

import { useNewsStore } from '@/stores/news'

const gqlRequestMock = vi.fn()

vi.mock('@/lib/graphql', () => ({
  gqlRequest: (...args: unknown[]) => gqlRequestMock(...args),
}))

describe('useNewsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    gqlRequestMock.mockReset()
  })

  it('fetchFeed passes includeDrafts to the local game API query', async () => {
    gqlRequestMock.mockResolvedValue({
      gameNewsFeed: {
        unreadCount: 2,
        items: [],
      },
    })

    const store = useNewsStore()
    await store.fetchFeed(true)

    expect(gqlRequestMock).toHaveBeenCalledWith(
      expect.stringContaining('gameNewsFeed(includeDrafts: $includeDrafts)'),
      { includeDrafts: true },
    )
    expect(store.unreadCount).toBe(2)
  })

  it('fetchUnreadCount always queries the published feed only', async () => {
    gqlRequestMock.mockResolvedValue({
      gameNewsFeed: {
        unreadCount: 5,
      },
    })

    const store = useNewsStore()
    const unreadCount = await store.fetchUnreadCount()

    expect(gqlRequestMock).toHaveBeenCalledWith(
      expect.stringContaining('query GameNewsUnreadCount($includeDrafts: Boolean!)'),
      { includeDrafts: false },
    )
    expect(unreadCount).toBe(5)
    expect(store.unreadCount).toBe(5)
  })

  it('markRead updates matching entries locally after a successful mutation', async () => {
    gqlRequestMock.mockResolvedValueOnce({
      markGameNewsRead: true,
    })

    const store = useNewsStore()
    store.feed = {
      unreadCount: 2,
      items: [
        {
          id: 'entry-1',
          entryType: 'NEWS',
          status: 'PUBLISHED',
          targetServerKey: null,
          createdByEmail: null,
          updatedByEmail: null,
          createdAtUtc: '2026-01-01T00:00:00Z',
          updatedAtUtc: '2026-01-01T00:00:00Z',
          publishedAtUtc: '2026-01-01T00:00:00Z',
          isRead: false,
          localizations: [],
        },
        {
          id: 'entry-2',
          entryType: 'CHANGELOG',
          status: 'PUBLISHED',
          targetServerKey: null,
          createdByEmail: null,
          updatedByEmail: null,
          createdAtUtc: '2026-01-01T00:00:00Z',
          updatedAtUtc: '2026-01-01T00:00:00Z',
          publishedAtUtc: '2026-01-01T00:00:00Z',
          isRead: false,
          localizations: [],
        },
      ],
    }
    store.unreadCount = 2

    await store.markRead(['entry-1'])

    expect(gqlRequestMock).toHaveBeenCalledWith(
      expect.stringContaining('mutation MarkGameNewsRead($input: MarkGameNewsReadInput!)'),
      { input: { entryIds: ['entry-1'] } },
    )
    expect(store.feed?.items[0]?.isRead).toBe(true)
    expect(store.feed?.items[1]?.isRead).toBe(false)
    expect(store.unreadCount).toBe(1)
  })
})
