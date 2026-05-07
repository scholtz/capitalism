<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'

import { gqlRequest } from '@/lib/graphql'
import { pickGameNewsLocalization } from '@/lib/news'
import type { GameNewsEntry, GameNewsFeed } from '@/types'

const { t, locale } = useI18n()
const feed = ref<GameNewsFeed | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const entries = computed(() => feed.value?.items ?? [])

function formatDate(value: string | null) {
  if (!value) {
    return t('common.notAvailable')
  }

  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function getLocalizedEntry(entry: GameNewsEntry) {
  return pickGameNewsLocalization(entry.localizations, locale.value)
}

async function load() {
  loading.value = true
  error.value = null
  try {
    const data = await gqlRequest<{ gameNewsFeed: GameNewsFeed }>(
      `query OperationsNewsFeed {
        gameNewsFeed(includeDrafts: true) {
          unreadCount
          items {
            id
            entryType
            status
            targetServerKey
            createdByEmail
            updatedByEmail
            createdAtUtc
            updatedAtUtc
            publishedAtUtc
            isRead
            localizations {
              locale
              title
              summary
              htmlContent
            }
          }
        }
      }`,
    )
    feed.value = data.gameNewsFeed
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.newsLoadFailed')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="card page-card">
    <div class="header">
      <div>
        <h2>{{ t('admin.menuNews') }}</h2>
        <p>{{ t('admin.newsListBody') }}</p>
      </div>
      <RouterLink to="/operations/news/new" class="btn btn-primary">{{ t('admin.newEntry') }}</RouterLink>
    </div>

    <div v-if="loading" class="state">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="state">{{ error }}</div>
    <div v-else-if="entries.length === 0" class="state">{{ t('news.emptyTitle') }}</div>
    <div v-else class="news-list">
      <article v-for="entry in entries" :key="entry.id" class="news-card">
        <div class="topline">
          <span class="badge" :class="entry.status === 'PUBLISHED' ? 'badge-success' : 'badge-warning'">{{ entry.status }}</span>
          <span class="badge badge-primary">{{ entry.entryType }}</span>
        </div>
        <h3>{{ getLocalizedEntry(entry)?.title ?? t('news.untitled') }}</h3>
        <p>{{ getLocalizedEntry(entry)?.summary }}</p>
        <div class="meta">
          <span>{{ formatDate(entry.updatedAtUtc) }}</span>
          <span>{{ entry.targetServerKey ?? t('admin.globalScope') }}</span>
        </div>
        <RouterLink :to="`/operations/news/new?edit=${entry.id}`" class="btn btn-secondary">{{ t('admin.editEntry') }}</RouterLink>
      </article>
    </div>
  </section>
</template>

<style scoped>
.page-card {
  padding: 1.1rem;
}

.header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.header p {
  color: var(--color-text-secondary);
  margin-top: 0.3rem;
}

.news-list {
  display: grid;
  gap: 0.75rem;
}

.news-card {
  padding: 0.9rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  display: grid;
  gap: 0.55rem;
}

.topline,
.meta {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  color: var(--color-text-secondary);
  font-size: 0.85rem;
}

.state {
  color: var(--color-text-secondary);
}

@media (max-width: 720px) {
  .header {
    flex-direction: column;
  }
}
</style>
