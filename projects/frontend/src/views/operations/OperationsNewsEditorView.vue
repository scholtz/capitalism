<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

import RichTextEditor from '@/components/admin/RichTextEditor.vue'
import { createEmptyNewsDraft, NEWS_EDITOR_LOCALES, upsertNewsLocalization } from '@/lib/news'
import { gqlRequest } from '@/lib/graphql'
import { useGameAdminStore } from '@/stores/gameAdmin'
import type { GameNewsFeed } from '@/types'

const { t } = useI18n()
const route = useRoute()
const adminStore = useGameAdminStore()
const newsEditor = ref(createEmptyNewsDraft())
const activeLocale = ref<(typeof NEWS_EDITOR_LOCALES)[number]>('en')
const loading = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)

const activeLocalization = computed(() => newsEditor.value.localizations.find((localization) => localization.locale === activeLocale.value))

function updateLocalization<K extends 'title' | 'summary' | 'htmlContent'>(key: K, value: string) {
  newsEditor.value = {
    ...newsEditor.value,
    localizations: upsertNewsLocalization(newsEditor.value.localizations, activeLocale.value, { [key]: value }),
  }
}

async function loadForEdit() {
  const editId = typeof route.query.edit === 'string' ? route.query.edit : null
  if (!editId) {
    return
  }

  loading.value = true
  error.value = null
  try {
    const data = await gqlRequest<{ gameNewsFeed: GameNewsFeed }>(
      `query OperationsNewsEditorFeed {
        gameNewsFeed(includeDrafts: true) {
          unreadCount
          items {
            id
            entryType
            status
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
    const entry = data.gameNewsFeed.items.find((item) => item.id === editId)
    if (!entry) {
      return
    }
    newsEditor.value = {
      entryId: entry.id,
      entryType: entry.entryType,
      status: entry.status,
      localizations: entry.localizations.map((localization) => ({ ...localization })),
    }
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.newsLoadFailed')
  } finally {
    loading.value = false
  }
}

async function saveEntry() {
  error.value = null
  message.value = null

  try {
    await adminStore.upsertGameNewsEntry(newsEditor.value)
    message.value = t('admin.newsSaved')
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.newsSaveFailed')
  }
}

onMounted(loadForEdit)
</script>

<template>
  <section class="card page-card">
    <div class="header">
      <div>
        <h2>{{ t('admin.newsComposerTitle') }}</h2>
        <p>{{ t('admin.newsComposerBody') }}</p>
      </div>
      <RouterLink to="/operations/news" class="btn btn-secondary">{{ t('common.back') }}</RouterLink>
    </div>

    <p v-if="loading" class="state">{{ t('common.loading') }}</p>
    <p v-if="error" class="state">{{ error }}</p>
    <p v-if="message" class="state success">{{ message }}</p>

    <div class="form-grid">
      <div class="inline-fields">
        <label class="form-label">
          {{ t('admin.entryType') }}
          <select v-model="newsEditor.entryType" class="form-select">
            <option value="NEWS">{{ t('news.filterNews') }}</option>
            <option value="CHANGELOG">{{ t('news.filterChangelog') }}</option>
          </select>
        </label>
        <label class="form-label">
          {{ t('admin.entryStatus') }}
          <select v-model="newsEditor.status" class="form-select">
            <option value="DRAFT">{{ t('admin.statusDraft') }}</option>
            <option value="PUBLISHED">{{ t('admin.statusPublished') }}</option>
          </select>
        </label>
      </div>

      <div class="locale-tabs">
        <button
          v-for="editorLocale in NEWS_EDITOR_LOCALES"
          :key="editorLocale"
          type="button"
          class="locale-tab"
          :class="{ active: activeLocale === editorLocale }"
          @click="activeLocale = editorLocale"
        >
          {{ editorLocale.toUpperCase() }}
        </button>
      </div>

      <label class="form-label">
        {{ t('admin.entryTitle') }}
        <input class="form-input" :value="activeLocalization?.title ?? ''" @input="updateLocalization('title', ($event.target as HTMLInputElement).value)" />
      </label>

      <label class="form-label">
        {{ t('admin.entrySummary') }}
        <textarea class="form-textarea" :value="activeLocalization?.summary ?? ''" @input="updateLocalization('summary', ($event.target as HTMLTextAreaElement).value)"></textarea>
      </label>

      <label class="form-label">
        {{ t('admin.entryContent') }}
        <RichTextEditor :model-value="activeLocalization?.htmlContent ?? ''" @update:model-value="updateLocalization('htmlContent', $event)" />
      </label>

      <div class="actions">
        <button type="button" class="btn btn-primary" @click="saveEntry">{{ t('admin.saveEntry') }}</button>
      </div>
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

.form-grid {
  display: grid;
  gap: 1rem;
}

.inline-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.locale-tabs {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}

.locale-tab {
  padding: 0.45rem 0.8rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-text-secondary);
}

.locale-tab.active {
  background: rgba(0, 71, 255, 0.2);
  border-color: rgba(0, 71, 255, 0.45);
  color: white;
}

.actions {
  display: flex;
  justify-content: flex-end;
}

.state {
  margin-bottom: 0.7rem;
  color: var(--color-text-secondary);
}

.state.success {
  color: #4ade80;
}

@media (max-width: 720px) {
  .header {
    flex-direction: column;
  }

  .inline-fields {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
