<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'

import { useAuthStore } from '@/stores/auth'
import { useGameAdminStore } from '@/stores/gameAdmin'

const { t } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const adminStore = useGameAdminStore()
const loading = ref(true)
const error = ref<string | null>(null)

const canAccessDashboard = computed(() => adminStore.session?.canAccessAdminDashboard ?? false)

async function stopImpersonation() {
  try {
    const authPayload = await adminStore.stopImpersonation()
    auth.applyAuthPayload(authPayload)
    await adminStore.fetchSession()
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.impersonationFailed')
  }
}

onMounted(async () => {
  if (!auth.isAuthenticated) {
    await router.replace('/login')
    return
  }

  try {
    await adminStore.fetchSession()
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.loadFailed')
  } finally {
    loading.value = false
  }
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

    <div v-if="loading" class="card state-card">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="card state-card">{{ error }}</div>
    <div v-else-if="!canAccessDashboard" class="card state-card">
      <h2>{{ t('admin.accessDeniedTitle') }}</h2>
      <p>{{ t('admin.accessDeniedBody') }}</p>
    </div>
    <template v-else>
      <nav class="operations-nav card" aria-label="Operations sections">
        <RouterLink to="/operations/statistics">{{ t('admin.menuStatistics') }}</RouterLink>
        <RouterLink to="/operations/news">{{ t('admin.menuNews') }}</RouterLink>
        <RouterLink to="/operations/players">{{ t('admin.menuPlayers') }}</RouterLink>
        <RouterLink to="/operations/products">{{ t('admin.menuProducts') }}</RouterLink>
      </nav>
      <RouterView />
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
  gap: 1rem;
  margin-bottom: 1rem;
}

.admin-eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.16em;
  font-size: 0.72rem;
  color: #ffc07a;
  margin-bottom: 0.35rem;
}

.operations-nav {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  padding: 0.8rem;
  margin-bottom: 1rem;
}

.operations-nav a {
  padding: 0.45rem 0.8rem;
  border-radius: 999px;
  border: 1px solid var(--color-border);
  color: var(--color-text-secondary);
  text-decoration: none;
}

.operations-nav a.router-link-active {
  color: white;
  border-color: rgba(0, 71, 255, 0.5);
  background: rgba(0, 71, 255, 0.2);
}

.state-card {
  padding: 1rem;
}
</style>
