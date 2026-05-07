<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'

import { useAuthStore } from '@/stores/auth'
import { useGameAdminStore } from '@/stores/gameAdmin'

const { t, locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const adminStore = useGameAdminStore()
const loading = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)

const player = computed(() => adminStore.dashboard?.players.find((item) => item.id === route.params.playerId))

function formatCurrency(value: number) {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value)
}

async function load() {
  if (adminStore.dashboard) {
    return
  }

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

async function toggleInvisible() {
  if (!player.value) {
    return
  }

  try {
    await adminStore.setPlayerInvisibleInChat(player.value.id, !player.value.isInvisibleInChat)
    message.value = player.value.isInvisibleInChat ? t('admin.playerVisible') : t('admin.playerInvisible')
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.playerVisibilityFailed')
  }
}

async function toggleLocalAdmin() {
  if (!player.value) {
    return
  }

  try {
    await adminStore.setLocalGameAdminRole(player.value.id, player.value.role !== 'ADMIN')
    message.value = player.value.role === 'ADMIN' ? t('admin.localAdminRemoved') : t('admin.localAdminGranted')
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.localAdminFailed')
  }
}

async function startImpersonation() {
  if (!player.value) {
    return
  }

  try {
    const authPayload = await adminStore.startImpersonation(player.value.id, 'PERSON')
    auth.applyAuthPayload(authPayload)
    message.value = t('admin.impersonationStarted')
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.impersonationFailed')
  }
}

onMounted(load)
</script>

<template>
  <section class="card page-card">
    <div class="header">
      <div>
        <h2>{{ t('admin.playerDetailTitle') }}</h2>
      </div>
      <RouterLink to="/operations/players" class="btn btn-secondary">{{ t('common.back') }}</RouterLink>
    </div>

    <p v-if="loading" class="state">{{ t('common.loading') }}</p>
    <p v-else-if="error" class="state">{{ error }}</p>
    <template v-else-if="player">
      <div class="stats-grid">
        <article class="stat-card">
          <span>{{ t('admin.playerColumnUsername') }}</span>
          <strong>{{ player.displayName }}</strong>
        </article>
        <article class="stat-card">
          <span>{{ t('admin.playerColumnRank') }}</span>
          <strong>{{ player.role }}</strong>
        </article>
        <article class="stat-card">
          <span>{{ t('admin.playerColumnEquity') }}</span>
          <strong>{{ formatCurrency(player.personalCash + player.totalCompanyCash) }}</strong>
        </article>
      </div>

      <div class="actions">
        <button type="button" class="btn btn-secondary" @click="startImpersonation">
          {{ t('admin.impersonatePerson') }}
        </button>
        <button type="button" class="btn btn-secondary" @click="toggleInvisible">
          {{ player.isInvisibleInChat ? t('admin.makeVisible') : t('admin.makeInvisible') }}
        </button>
        <button type="button" class="btn btn-secondary" @click="toggleLocalAdmin">
          {{ player.role === 'ADMIN' ? t('admin.removeLocalAdmin') : t('admin.grantLocalAdmin') }}
        </button>
        <button type="button" class="btn btn-ghost" disabled>{{ t('admin.playerActionAddGold') }}</button>
        <button type="button" class="btn btn-ghost" disabled>{{ t('admin.playerActionSendMessage') }}</button>
        <button type="button" class="btn btn-ghost" disabled>{{ t('admin.playerActionFlagAccount') }}</button>
      </div>
      <p v-if="message" class="state success">{{ message }}</p>
    </template>
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

.stats-grid {
  display: grid;
  gap: 0.7rem;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.stat-card {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.7rem;
  display: grid;
  gap: 0.35rem;
}

.stat-card span {
  color: var(--color-text-secondary);
}

.actions {
  margin-top: 1rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.7rem;
}

.state {
  margin-top: 0.8rem;
  color: var(--color-text-secondary);
}

.state.success {
  color: #4ade80;
}

@media (max-width: 820px) {
  .stats-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .header {
    flex-direction: column;
  }
}
</style>
