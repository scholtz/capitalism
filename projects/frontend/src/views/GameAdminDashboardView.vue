<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'

import RichTextEditor from '@/components/admin/RichTextEditor.vue'
import { createEmptyNewsDraft, NEWS_EDITOR_LOCALES, pickGameNewsLocalization, upsertNewsLocalization } from '@/lib/news'
import { gqlRequest } from '@/lib/graphql'
import { useAuthStore } from '@/stores/auth'
import { useGameAdminStore } from '@/stores/gameAdmin'
import { useNewsStore } from '@/stores/news'
import type { AccountContextType, GameAdminPlayer, GameNewsEntry, GameNewsFeed } from '@/types'

const { t, locale } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const adminStore = useGameAdminStore()
const newsStore = useNewsStore()

const newsEditor = ref(createEmptyNewsDraft())
const activeLocale = ref<(typeof NEWS_EDITOR_LOCALES)[number]>('en')
const adminFeed = ref<GameNewsFeed | null>(null)
const adminFeedLoading = ref(false)
const adminFeedError = ref<string | null>(null)
const globalAdminEmail = ref('')
const actionError = ref<string | null>(null)
const actionMessage = ref<string | null>(null)
const showShippingCosts = ref(false)

const canAccessDashboard = computed(() => adminStore.session?.canAccessAdminDashboard ?? false)
const canManageRootFeatures = computed(() => adminStore.session?.isRootAdministrator ?? false)
const activeLocalization = computed(() => newsEditor.value.localizations.find((localization) => localization.locale === activeLocale.value))

const latestEntries = computed(() => adminFeed.value?.items ?? [])

async function loadAdminFeed() {
  adminFeedLoading.value = true
  adminFeedError.value = null

  try {
    const data = await gqlRequest<{ gameNewsFeed: GameNewsFeed }>(
      `query AdminNewsFeed {
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

    adminFeed.value = data.gameNewsFeed
  } catch (caughtError) {
    adminFeedError.value = caughtError instanceof Error ? caughtError.message : t('admin.newsLoadFailed')
  } finally {
    adminFeedLoading.value = false
  }
}

async function loadDashboard() {
  actionError.value = null

  try {
    const session = await adminStore.fetchSession()
    if (!session.canAccessAdminDashboard) {
      return
    }

    await Promise.all([adminStore.fetchDashboard(), loadAdminFeed()])
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.loadFailed')
  }
}

function resetComposer() {
  newsEditor.value = createEmptyNewsDraft()
  activeLocale.value = 'en'
}

function editEntry(entry: GameNewsEntry) {
  newsEditor.value = {
    entryId: entry.id,
    entryType: entry.entryType,
    status: entry.status,
    localizations: entry.localizations.map((localization) => ({ ...localization })),
  }
  activeLocale.value = 'en'
}

function updateLocalization<K extends 'title' | 'summary' | 'htmlContent'>(key: K, value: string) {
  newsEditor.value = {
    ...newsEditor.value,
    localizations: upsertNewsLocalization(newsEditor.value.localizations, activeLocale.value, { [key]: value }),
  }
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value)
}

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

function canEditEntry(entry: GameNewsEntry) {
  return entry.targetServerKey !== null || canManageRootFeatures.value || adminStore.session?.hasGlobalAdminRole
}

async function saveEntry() {
  actionError.value = null
  actionMessage.value = null

  try {
    await adminStore.upsertGameNewsEntry(newsEditor.value)
    await Promise.all([loadAdminFeed(), newsStore.fetchUnreadCount()])
    actionMessage.value = t('admin.newsSaved')
    resetComposer()
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.newsSaveFailed')
  }
}

async function startImpersonation(playerId: string, accountType: AccountContextType, companyId?: string | null) {
  actionError.value = null
  actionMessage.value = null

  try {
    const authPayload = await adminStore.startImpersonation(playerId, accountType, companyId)
    auth.applyAuthPayload(authPayload)
    await Promise.all([adminStore.fetchSession(), newsStore.fetchUnreadCount()])
    actionMessage.value = t('admin.impersonationStarted')
    await router.push('/dashboard')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.impersonationFailed')
  }
}

async function stopImpersonation() {
  actionError.value = null
  actionMessage.value = null

  try {
    const authPayload = await adminStore.stopImpersonation()
    auth.applyAuthPayload(authPayload)
    await Promise.all([adminStore.fetchSession(), adminStore.fetchDashboard(), newsStore.fetchUnreadCount()])
    actionMessage.value = t('admin.impersonationStopped')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.impersonationFailed')
  }
}

async function toggleInvisible(player: GameAdminPlayer) {
  actionError.value = null

  try {
    await adminStore.setPlayerInvisibleInChat(player.id, !player.isInvisibleInChat)
    actionMessage.value = player.isInvisibleInChat ? t('admin.playerVisible') : t('admin.playerInvisible')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.playerVisibilityFailed')
  }
}

async function toggleLocalAdmin(player: GameAdminPlayer) {
  actionError.value = null

  try {
    await adminStore.setLocalGameAdminRole(player.id, player.role !== 'ADMIN')
    actionMessage.value = player.role === 'ADMIN' ? t('admin.localAdminRemoved') : t('admin.localAdminGranted')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.localAdminFailed')
  }
}

async function assignGlobalAdmin() {
  actionError.value = null

  try {
    await adminStore.assignGlobalGameAdminRole(globalAdminEmail.value)
    globalAdminEmail.value = ''
    actionMessage.value = t('admin.globalAdminGranted')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.globalAdminFailed')
  }
}

async function removeGlobalAdmin(email: string) {
  actionError.value = null

  try {
    await adminStore.removeGlobalGameAdminRole(email)
    actionMessage.value = t('admin.globalAdminRemoved')
  } catch (caughtError) {
    actionError.value = caughtError instanceof Error ? caughtError.message : t('admin.globalAdminFailed')
  }
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    await router.replace('/login')
    return
  }

  await loadDashboard()
})
</script>

<template>
  <div class="admin-view container">
    <div class="page-header admin-header">
      <div>
        <p class="admin-eyebrow">{{ t('admin.eyebrow') }}</p>
        <h1>{{ t('admin.title') }}</h1>
        <p>{{ t('admin.subtitle') }}</p>
      </div>
      <button v-if="adminStore.session?.isImpersonating" type="button" class="btn btn-secondary" @click="stopImpersonation">
        {{ t('admin.stopImpersonation') }}
      </button>
    </div>

    <div v-if="!canAccessDashboard && !adminStore.loadingSession" class="admin-locked card">
      <h2>{{ t('admin.accessDeniedTitle') }}</h2>
      <p>{{ t('admin.accessDeniedBody') }}</p>
    </div>

    <template v-else>
      <div v-if="actionError" class="admin-banner admin-banner-error">{{ actionError }}</div>
      <div v-else-if="actionMessage" class="admin-banner">{{ actionMessage }}</div>

      <section v-if="adminStore.dashboard" class="admin-metrics">
        <article class="admin-metric-card">
          <span>{{ t('admin.moneySupply') }}</span>
          <strong>{{ formatCurrency(adminStore.dashboard.moneySupply) }}</strong>
        </article>
        <article class="admin-metric-card">
          <span>{{ t('admin.personalCash') }}</span>
          <strong>{{ formatCurrency(adminStore.dashboard.totalPersonalCash) }}</strong>
        </article>
        <article class="admin-metric-card">
          <span>{{ t('admin.companyCash') }}</span>
          <strong>{{ formatCurrency(adminStore.dashboard.totalCompanyCash) }}</strong>
        </article>
        <article class="admin-metric-card admin-metric-highlight">
          <span>{{ t('admin.externalInflow') }}</span>
          <strong>{{ formatCurrency(adminStore.dashboard.externalMoneyInflowLast100Ticks) }}</strong>
        </article>
        <button type="button" class="admin-metric-card admin-metric-button" @click="showShippingCosts = !showShippingCosts">
          <span>{{ t('admin.shippingCosts') }}</span>
          <strong>{{ formatCurrency(adminStore.dashboard.totalShippingCostsLast100Ticks) }}</strong>
        </button>
      </section>

      <section class="admin-grid">
        <article class="card admin-panel">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.inflowTitle') }}</h2>
              <p>{{ t('admin.inflowBody') }}</p>
            </div>
          </div>
          <div class="admin-list">
            <div v-for="summary in adminStore.dashboard?.inflowSummaries ?? []" :key="summary.category" class="admin-list-item">
              <div>
                <strong>{{ summary.category }}</strong>
                <p>{{ summary.description }}</p>
              </div>
              <span>{{ formatCurrency(summary.amount) }}</span>
            </div>
          </div>
        </article>

        <article class="card admin-panel">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.shippingTitle') }}</h2>
              <p>{{ t('admin.shippingBody') }}</p>
            </div>
            <button type="button" class="btn btn-secondary" @click="showShippingCosts = !showShippingCosts">
              {{ showShippingCosts ? t('common.close') : t('admin.viewShippingCosts') }}
            </button>
          </div>
          <div v-if="!showShippingCosts" class="admin-empty-state">
            {{ t('admin.shippingClosed') }}
          </div>
          <div v-else-if="(adminStore.dashboard?.shippingCostSummaries.length ?? 0) === 0" class="admin-empty-state">
            {{ t('admin.shippingEmpty') }}
          </div>
          <div v-else class="admin-list">
            <div v-for="summary in adminStore.dashboard?.shippingCostSummaries ?? []" :key="summary.companyId" class="admin-list-item">
              <div>
                <strong>{{ summary.companyName }}</strong>
                <p>{{ t('admin.shippingEntryCount', { count: summary.entryCount }) }}</p>
              </div>
              <span>{{ formatCurrency(summary.amount) }}</span>
            </div>
          </div>
        </article>

        <article class="card admin-panel">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.alertsTitle') }}</h2>
              <p>{{ t('admin.alertsBody') }}</p>
            </div>
          </div>
          <div v-if="(adminStore.dashboard?.multiAccountAlerts.length ?? 0) === 0" class="admin-empty-state">
            {{ t('admin.alertsEmpty') }}
          </div>
          <div v-else class="admin-alert-list">
            <div v-for="alert in adminStore.dashboard?.multiAccountAlerts ?? []" :key="`${alert.reason}-${alert.supportingEntityName}`" class="admin-alert-card">
              <div class="admin-alert-topline">
                <span class="badge badge-warning">{{ alert.reason }}</span>
                <span>{{ alert.confidenceScore.toFixed(2) }}</span>
              </div>
              <p class="admin-alert-body">
                <strong>{{ alert.primaryPlayer.displayName }}</strong>
                {{ t('admin.alertsLinkedTo') }}
                <strong>{{ alert.relatedPlayer.displayName }}</strong>
              </p>
              <p class="admin-alert-meta">{{ alert.supportingEntityType }} · {{ alert.supportingEntityName }}</p>
              <p class="admin-alert-amount">{{ formatCurrency(alert.exposureAmount) }}</p>
            </div>
          </div>
        </article>
      </section>

      <section class="admin-grid admin-grid-wide">
        <article class="card admin-panel admin-panel-wide">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.playersTitle') }}</h2>
              <p>{{ t('admin.playersBody') }}</p>
            </div>
          </div>
          <div class="admin-player-list">
            <article v-for="player in adminStore.dashboard?.players ?? []" :key="player.id" class="admin-player-card">
              <div class="admin-player-topline">
                <div>
                  <h3>{{ player.displayName }}</h3>
                  <p>{{ player.email }}</p>
                </div>
                <div class="admin-player-badges">
                  <span class="badge" :class="player.role === 'ADMIN' ? 'badge-primary' : 'badge-success'">{{ player.role }}</span>
                  <span v-if="player.isInvisibleInChat" class="badge badge-warning">{{ t('admin.invisibleLabel') }}</span>
                </div>
              </div>

              <div class="admin-player-stats">
                <span>{{ t('admin.personalCash') }}: {{ formatCurrency(player.personalCash) }}</span>
                <span>{{ t('admin.companyCash') }}: {{ formatCurrency(player.totalCompanyCash) }}</span>
                <span>{{ t('admin.lastSeen') }}: {{ formatDate(player.lastLoginAtUtc) }}</span>
              </div>

              <div class="admin-player-actions">
                <button type="button" class="btn btn-secondary" @click="startImpersonation(player.id, 'PERSON')">
                  {{ t('admin.impersonatePerson') }}
                </button>
                <button type="button" class="btn btn-secondary" @click="toggleInvisible(player)">
                  {{ player.isInvisibleInChat ? t('admin.makeVisible') : t('admin.makeInvisible') }}
                </button>
                <button v-if="canManageRootFeatures" type="button" class="btn btn-ghost" @click="toggleLocalAdmin(player)">
                  {{ player.role === 'ADMIN' ? t('admin.removeLocalAdmin') : t('admin.grantLocalAdmin') }}
                </button>
              </div>

              <div v-if="player.companies.length > 0" class="admin-company-list">
                <button
                  v-for="company in player.companies"
                  :key="company.id"
                  type="button"
                  class="admin-company-pill"
                  @click="startImpersonation(player.id, 'COMPANY', company.id)"
                >
                  {{ company.name }} · {{ formatCurrency(company.cash) }}
                </button>
              </div>
            </article>
          </div>
        </article>

        <article class="card admin-panel admin-panel-wide">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.newsComposerTitle') }}</h2>
              <p>{{ t('admin.newsComposerBody') }}</p>
            </div>
            <button type="button" class="btn btn-ghost" @click="resetComposer">{{ t('admin.newEntry') }}</button>
          </div>

          <div class="admin-composer-grid">
            <div class="admin-composer-form">
              <div class="admin-inline-fields">
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
                <input
                  class="form-input"
                  :value="activeLocalization?.title ?? ''"
                  @input="updateLocalization('title', ($event.target as HTMLInputElement).value)"
                />
              </label>

              <label class="form-label">
                {{ t('admin.entrySummary') }}
                <textarea
                  class="form-textarea"
                  :value="activeLocalization?.summary ?? ''"
                  @input="updateLocalization('summary', ($event.target as HTMLTextAreaElement).value)"
                ></textarea>
              </label>

              <label class="form-label">
                {{ t('admin.entryContent') }}
                <RichTextEditor :model-value="activeLocalization?.htmlContent ?? ''" @update:model-value="updateLocalization('htmlContent', $event)" />
              </label>

              <div class="admin-composer-actions">
                <button type="button" class="btn btn-primary" @click="saveEntry">{{ t('admin.saveEntry') }}</button>
              </div>
            </div>

            <div class="admin-feed-list">
              <div v-if="adminFeedLoading" class="admin-empty-state">{{ t('common.loading') }}</div>
              <div v-else-if="adminFeedError" class="admin-empty-state">{{ adminFeedError }}</div>
              <article v-for="entry in latestEntries" :key="entry.id" class="admin-feed-card">
                <div class="admin-feed-topline">
                  <span class="badge" :class="entry.status === 'PUBLISHED' ? 'badge-success' : 'badge-warning'">{{ entry.status }}</span>
                  <span class="badge badge-primary">{{ entry.entryType }}</span>
                </div>
                <h3>{{ getLocalizedEntry(entry)?.title ?? t('news.untitled') }}</h3>
                <p>{{ getLocalizedEntry(entry)?.summary }}</p>
                <div class="admin-feed-meta">
                  <span>{{ formatDate(entry.updatedAtUtc) }}</span>
                  <span>{{ entry.targetServerKey ?? t('admin.globalScope') }}</span>
                </div>
                <button type="button" class="btn btn-secondary" :disabled="!canEditEntry(entry)" @click="editEntry(entry)">
                  {{ t('admin.editEntry') }}
                </button>
              </article>
            </div>
          </div>
        </article>
      </section>

      <section v-if="canManageRootFeatures" class="admin-grid admin-grid-wide">
        <article class="card admin-panel admin-panel-wide">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.globalAdminsTitle') }}</h2>
              <p>{{ t('admin.globalAdminsBody') }}</p>
            </div>
          </div>

          <div class="admin-global-admins">
            <div class="admin-inline-fields">
              <input v-model="globalAdminEmail" class="form-input" :placeholder="t('admin.globalAdminPlaceholder')" />
              <button type="button" class="btn btn-primary" @click="assignGlobalAdmin">{{ t('admin.grantGlobalAdmin') }}</button>
            </div>

            <div class="admin-list">
              <div v-for="grant in adminStore.dashboard?.globalGameAdminGrants ?? []" :key="grant.id" class="admin-list-item">
                <div>
                  <strong>{{ grant.email }}</strong>
                  <p>{{ grant.grantedByEmail }} · {{ formatDate(grant.updatedAtUtc) }}</p>
                </div>
                <button type="button" class="btn btn-ghost" @click="removeGlobalAdmin(grant.email)">
                  {{ t('admin.removeGlobalAdmin') }}
                </button>
              </div>
            </div>
          </div>
        </article>
      </section>

      <section class="admin-grid admin-grid-wide">
        <article class="card admin-panel admin-panel-wide">
          <div class="admin-panel-header">
            <div>
              <h2>{{ t('admin.auditTitle') }}</h2>
              <p>{{ t('admin.auditBody') }}</p>
            </div>
          </div>
          <div class="admin-audit-list">
            <div v-for="log in adminStore.dashboard?.recentAuditLogs ?? []" :key="log.id" class="admin-list-item">
              <div>
                <strong>{{ log.adminActorDisplayName }}</strong>
                <p>
                  {{ log.effectivePlayerDisplayName }} · {{ log.effectiveAccountType }}
                  <span v-if="log.effectiveCompanyName">· {{ log.effectiveCompanyName }}</span>
                </p>
                <p>{{ log.graphQlOperationName || log.mutationSummary }}</p>
              </div>
              <span>{{ formatDate(log.recordedAtUtc) }}</span>
            </div>
          </div>
        </article>
      </section>
    </template>
  </div>
</template>

<style scoped>
.admin-view {
  padding-top: 2rem;
  padding-bottom: 4rem;
}

.admin-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1.5rem;
}

.admin-eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.16em;
  font-size: 0.72rem;
  color: #ffc07a;
  margin-bottom: 0.35rem;
}

.admin-locked {
  padding: 1.5rem;
}

.admin-banner {
  margin-bottom: 1rem;
  padding: 0.85rem 1rem;
  border-radius: var(--radius-md);
  border: 1px solid rgba(34, 197, 94, 0.4);
  background: rgba(34, 197, 94, 0.14);
}

.admin-banner-error {
  border-color: rgba(248, 113, 113, 0.45);
  background: rgba(248, 113, 113, 0.12);
}

.admin-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.admin-metric-card {
  padding: 1.2rem;
  border-radius: var(--radius-lg);
  border: 1px solid var(--color-border);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.03), rgba(255, 255, 255, 0));
  display: grid;
  gap: 0.45rem;
}

.admin-metric-button {
  width: 100%;
  text-align: left;
  cursor: pointer;
}

.admin-metric-card span {
  color: var(--color-text-secondary);
}

.admin-metric-card strong {
  font-size: 1.55rem;
}

.admin-metric-highlight {
  border-color: rgba(255, 138, 0, 0.5);
}

.admin-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.admin-grid-wide {
  grid-template-columns: minmax(0, 1fr);
}

.admin-panel {
  padding: 1.25rem;
}

.admin-panel-wide {
  padding: 1.4rem;
}

.admin-panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.admin-panel-header p {
  color: var(--color-text-secondary);
  margin-top: 0.3rem;
}

.admin-list,
.admin-audit-list {
  display: grid;
  gap: 0.75rem;
}

.admin-list-item {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: center;
  padding: 0.95rem 1rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  background: rgba(255, 255, 255, 0.02);
}

.admin-list-item p {
  color: var(--color-text-secondary);
  margin-top: 0.2rem;
}

.admin-empty-state {
  padding: 1rem;
  border-radius: var(--radius-md);
  border: 1px dashed var(--color-border);
  color: var(--color-text-secondary);
}

.admin-alert-list {
  display: grid;
  gap: 0.75rem;
}

.admin-alert-card {
  padding: 1rem;
  border-radius: var(--radius-md);
  border: 1px solid rgba(255, 138, 0, 0.35);
  background: rgba(255, 138, 0, 0.08);
}

.admin-alert-topline {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.7rem;
}

.admin-alert-body {
  margin-bottom: 0.35rem;
}

.admin-alert-meta {
  color: var(--color-text-secondary);
  font-size: 0.85rem;
}

.admin-alert-amount {
  margin-top: 0.45rem;
  font-weight: 700;
}

.admin-player-list {
  display: grid;
  gap: 0.9rem;
}

.admin-player-card {
  padding: 1rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  background: rgba(255, 255, 255, 0.02);
}

.admin-player-topline {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.admin-player-topline p,
.admin-player-stats {
  color: var(--color-text-secondary);
}

.admin-player-badges {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.admin-player-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  margin: 0.8rem 0;
  font-size: 0.88rem;
}

.admin-player-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.admin-company-list {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
  margin-top: 0.85rem;
}

.admin-company-pill {
  padding: 0.55rem 0.8rem;
  border-radius: 999px;
  border: 1px solid rgba(0, 71, 255, 0.38);
  background: rgba(0, 71, 255, 0.12);
  color: #cddcff;
}

.admin-composer-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(18rem, 0.75fr);
  gap: 1rem;
}

.admin-composer-form {
  display: grid;
  gap: 1rem;
}

.admin-inline-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.locale-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
}

.locale-tab {
  padding: 0.55rem 0.8rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-text-secondary);
}

.locale-tab.active {
  background: rgba(0, 71, 255, 0.18);
  border-color: rgba(0, 71, 255, 0.45);
  color: white;
}

.admin-composer-actions {
  display: flex;
  justify-content: flex-end;
}

.admin-feed-list {
  display: grid;
  gap: 0.8rem;
}

.admin-feed-card {
  padding: 1rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  background: rgba(255, 255, 255, 0.02);
  display: grid;
  gap: 0.6rem;
}

.admin-feed-topline,
.admin-feed-meta {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
  color: var(--color-text-secondary);
  font-size: 0.82rem;
}

.admin-global-admins {
  display: grid;
  gap: 1rem;
}

@media (max-width: 1080px) {
  .admin-metrics,
  .admin-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .admin-composer-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 720px) {
  .admin-header,
  .admin-panel-header,
  .admin-player-topline,
  .admin-list-item {
    flex-direction: column;
    align-items: stretch;
  }

  .admin-metrics,
  .admin-grid,
  .admin-inline-fields {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
