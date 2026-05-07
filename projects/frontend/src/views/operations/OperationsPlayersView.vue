<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'

import { useGameAdminStore } from '@/stores/gameAdmin'

type SortField = 'displayName' | 'role' | 'equity' | 'lastSeen'

const { t, locale } = useI18n()
const adminStore = useGameAdminStore()
const loading = ref(false)
const error = ref<string | null>(null)
const searchTerm = ref('')
const sortField = ref<SortField>('displayName')
const sortDirection = ref<'asc' | 'desc'>('asc')
const globalAdminEmail = ref('')

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

const rows = computed(() => {
  const query = searchTerm.value.trim().toLowerCase()
  const filtered = (adminStore.dashboard?.players ?? []).filter((player) => {
    if (!query) {
      return true
    }

    return (
      player.displayName.toLowerCase().includes(query) ||
      player.email.toLowerCase().includes(query) ||
      player.role.toLowerCase().includes(query)
    )
  })

  const multiplier = sortDirection.value === 'asc' ? 1 : -1
  return [...filtered].sort((left, right) => {
    if (sortField.value === 'equity') {
      const leftEquity = left.personalCash + left.totalCompanyCash
      const rightEquity = right.personalCash + right.totalCompanyCash
      return (leftEquity - rightEquity) * multiplier
    }

    if (sortField.value === 'lastSeen') {
      return ((left.lastLoginAtUtc ?? '').localeCompare(right.lastLoginAtUtc ?? '') || left.displayName.localeCompare(right.displayName)) * multiplier
    }

    return (left[sortField.value] as string).localeCompare(right[sortField.value] as string) * multiplier
  })
})

async function load() {
  loading.value = true
  error.value = null
  try {
    await adminStore.fetchDashboard()
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.loadFailed')
  } finally {
    loading.value = false
  }
}

async function assignGlobalAdmin() {
  if (!globalAdminEmail.value.trim()) {
    return
  }

  try {
    await adminStore.assignGlobalGameAdminRole(globalAdminEmail.value.trim())
    globalAdminEmail.value = ''
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.globalAdminFailed')
  }
}

async function removeGlobalAdmin(email: string) {
  try {
    await adminStore.removeGlobalGameAdminRole(email)
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.globalAdminFailed')
  }
}

onMounted(load)
</script>

<template>
  <section class="card page-card">
    <div class="header">
      <div>
        <h2>{{ t('admin.menuPlayers') }}</h2>
        <p>{{ t('admin.playersBody') }}</p>
      </div>
    </div>

    <div class="controls">
      <input v-model="searchTerm" class="form-input" :placeholder="t('admin.playerSearchPlaceholder')" />
      <select v-model="sortField" class="form-select">
        <option value="displayName">{{ t('admin.playerSortName') }}</option>
        <option value="role">{{ t('admin.playerSortRank') }}</option>
        <option value="equity">{{ t('admin.playerSortEquity') }}</option>
        <option value="lastSeen">{{ t('admin.playerSortJoinDate') }}</option>
      </select>
      <button type="button" class="btn btn-secondary" @click="sortDirection = sortDirection === 'asc' ? 'desc' : 'asc'">
        {{ sortDirection === 'asc' ? '↑' : '↓' }}
      </button>
    </div>

    <div v-if="loading" class="state">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="state">{{ error }}</div>
    <table v-else class="players-table">
      <thead>
        <tr>
          <th>{{ t('admin.playerColumnUsername') }}</th>
          <th>{{ t('admin.playerColumnRank') }}</th>
          <th>{{ t('admin.playerColumnEquity') }}</th>
          <th>{{ t('admin.playerColumnJoinDate') }}</th>
          <th>{{ t('admin.playerColumnCity') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="player in rows" :key="player.id">
          <td>
            <RouterLink :to="`/operations/players/${player.id}`">{{ player.displayName }}</RouterLink>
          </td>
          <td>{{ player.role }}</td>
          <td>{{ formatCurrency(player.personalCash + player.totalCompanyCash) }}</td>
          <td>{{ formatDate(player.lastLoginAtUtc) }}</td>
          <td>{{ t('common.notAvailable') }}</td>
        </tr>
      </tbody>
    </table>

    <section class="extra-grid">
      <article class="card block">
        <h3>{{ t('admin.alertsTitle') }}</h3>
        <div v-if="(adminStore.dashboard?.multiAccountAlerts.length ?? 0) === 0" class="state">{{ t('admin.alertsEmpty') }}</div>
        <div v-else class="stack">
          <div v-for="alert in adminStore.dashboard?.multiAccountAlerts ?? []" :key="`${alert.reason}-${alert.supportingEntityName}`" class="item">
            <strong>{{ alert.reason }}</strong>
            <p>{{ alert.primaryPlayer.displayName }} {{ t('admin.alertsLinkedTo') }} {{ alert.relatedPlayer.displayName }}</p>
          </div>
        </div>
      </article>

      <article v-if="adminStore.session?.isRootAdministrator" class="card block">
        <h3>{{ t('admin.globalAdminsTitle') }}</h3>
        <div class="controls">
          <input v-model="globalAdminEmail" class="form-input" :placeholder="t('admin.globalAdminPlaceholder')" />
          <button type="button" class="btn btn-primary" @click="assignGlobalAdmin">{{ t('admin.grantGlobalAdmin') }}</button>
        </div>
        <div class="stack">
          <div v-for="grant in adminStore.dashboard?.globalGameAdminGrants ?? []" :key="grant.id" class="item item-row">
            <span>{{ grant.email }}</span>
            <button type="button" class="btn btn-ghost" @click="removeGlobalAdmin(grant.email)">{{ t('admin.removeGlobalAdmin') }}</button>
          </div>
        </div>
      </article>

      <article class="card block">
        <h3>{{ t('admin.auditTitle') }}</h3>
        <div class="stack">
          <div v-for="log in adminStore.dashboard?.recentAuditLogs ?? []" :key="log.id" class="item">
            <strong>{{ log.adminActorDisplayName }}</strong>
            <p>{{ log.effectivePlayerDisplayName }} · {{ log.graphQlOperationName || log.mutationSummary }}</p>
          </div>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.page-card {
  padding: 1.1rem;
}

.header p {
  color: var(--color-text-secondary);
  margin-top: 0.3rem;
}

.controls {
  margin: 1rem 0;
  display: flex;
  gap: 0.7rem;
  flex-wrap: wrap;
}

.players-table {
  width: 100%;
  border-collapse: collapse;
}

.players-table th,
.players-table td {
  padding: 0.65rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
}

.state {
  color: var(--color-text-secondary);
}

.extra-grid {
  margin-top: 1rem;
  display: grid;
  gap: 0.8rem;
}

.block {
  padding: 0.9rem;
}

.stack {
  margin-top: 0.6rem;
  display: grid;
  gap: 0.5rem;
}

.item {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.6rem;
}

.item p {
  color: var(--color-text-secondary);
  margin-top: 0.2rem;
}

.item-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.8rem;
}

@media (max-width: 820px) {
  .players-table {
    display: block;
    overflow-x: auto;
  }
}
</style>
