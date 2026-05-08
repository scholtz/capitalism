<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import AppHeader from '@/components/layout/AppHeader.vue'
import AppFooter from '@/components/layout/AppFooter.vue'
import GameOverOverlay from '@/components/layout/GameOverOverlay.vue'
import { usePwa } from '@/composables/usePwa'
import { useAuthStore } from '@/stores/auth'
import { useGameStateStore } from '@/stores/gameState'
import { useNewsStore } from '@/stores/news'
import { useGameAdminStore } from '@/stores/gameAdmin'

const { t } = useI18n()
const { isOffline, updateAvailable, acceptUpdate } = usePwa()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const newsStore = useNewsStore()
const gameAdminStore = useGameAdminStore()
const { gameState } = storeToRefs(gameStateStore)
gameStateStore.start()

onMounted(() => {
  auth.initFromStorage()
  if (auth.token) {
    void auth.fetchMe()
    void newsStore.fetchUnreadCount()
    void gameAdminStore.fetchSession()
  }
})

watch(
  () => auth.token,
  (token, previousToken) => {
    if (!token) {
      newsStore.clear()
      gameAdminStore.clear()
      return
    }

    if (token !== previousToken) {
      void newsStore.fetchUnreadCount()
      void gameAdminStore.fetchSession()
    }
  },
)

</script>

<template>
  <div class="app-layout">
    <AppHeader />

    <!-- Offline banner: shown when the browser loses connectivity -->
    <div v-if="isOffline" role="status" aria-live="polite" class="offline-banner">
      <span class="offline-icon" aria-hidden="true">📡</span>
      {{ t('banners.offline') }}
    </div>

    <!-- Update prompt: shown when a new service-worker version is waiting -->
    <div v-if="updateAvailable" role="status" aria-live="polite" class="update-banner">
      <span>{{ t('banners.updateAvailable') }}</span>
      <button class="btn btn-primary update-btn" @click="acceptUpdate">{{ t('banners.refreshToUpdate') }}</button>
    </div>

    <main class="app-main">
      <RouterView />
    </main>
    <AppFooter />
    <GameOverOverlay :game-state="gameState" />
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.app-main {
  flex: 1;
}

/* Offline / update banners */
.offline-banner,
.update-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.6rem 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  /* Keep banners above content but below the fixed header */
  position: sticky;
  top: 0;
  z-index: 90;
}

.offline-banner {
  background: var(--color-surface-raised);
  color: var(--color-warning);
  border-bottom: 1px solid var(--color-border);
}

.offline-icon {
  font-size: 1rem;
}

.update-banner {
  background: var(--color-primary-light);
  color: var(--color-primary);
  border-bottom: 1px solid var(--color-primary);
  justify-content: space-between;
}

.update-btn {
  padding: 0.35rem 0.9rem;
  font-size: 0.8rem;
  flex-shrink: 0;
}
</style>
