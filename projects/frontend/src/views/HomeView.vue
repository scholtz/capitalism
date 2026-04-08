<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest } from '@/lib/graphql'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { useGameStateStore } from '@/stores/gameState'
import { deepEqual } from '@/lib/utils'
import type { PlayerRanking, GameState } from '@/types'

const { t } = useI18n()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const { gameState } = storeToRefs(gameStateStore)

const rankings = ref<PlayerRanking[]>([])
const loading = ref(true)

async function loadHomeData(isRefresh = false) {
  if (!isRefresh) {
    loading.value = true
  }
  try {
    const [rankData, stateData] = await Promise.all([
      gqlRequest<{ rankings: PlayerRanking[] }>('{ rankings { playerId displayName totalWealth cashTotal buildingValue inventoryValue companyCount } }'),
      gqlRequest<{ gameState: GameState }>(
        '{ gameState { currentTick lastTickAtUtc tickIntervalSeconds taxCycleTicks taxRate currentGameYear currentGameTimeUtc ticksPerDay ticksPerYear nextTaxTick nextTaxGameTimeUtc nextTaxGameYear } }',
      ),
    ])
    if (!deepEqual(rankings.value, rankData.rankings)) {
      rankings.value = rankData.rankings
    }
    if (!deepEqual(gameState.value, stateData.gameState)) {
      gameState.value = stateData.gameState
    }
  } catch {
    // Silently fail on home page
  } finally {
    if (!isRefresh) {
      loading.value = false
    }
  }
}

onMounted(async () => {
  auth.initFromStorage()
  if (auth.isAuthenticated) {
    void auth.fetchMe()
  }

  await loadHomeData()
})

useTickRefresh(() => loadHomeData(true))
</script>

<template>
  <div class="home-view">
    <section class="hero">
      <div class="hero-video-wrapper">
        <div class="hero-video-overlay">
          <div class="hero-video-uplayer"></div>
          <video autoplay muted playsinline class="hero-video">
            <source src="../assets/hero-video.webm" type="video/webm" />
          </video>
        </div>
      </div>
      <div class="container hero-content">
        <h1 class="hero-title">{{ t('home.heroTitle') }}</h1>
        <p class="hero-description">{{ t('home.heroDescription') }}</p>
        <div class="hero-actions">
          <RouterLink v-if="!auth.isAuthenticated" to="/onboarding" class="btn btn-primary">
            {{ t('home.getStarted') }}
          </RouterLink>
          <RouterLink v-else-if="auth.player && !auth.player.onboardingCompletedAtUtc" to="/onboarding" class="btn btn-primary">
            {{ t('home.startOnboarding') }}
          </RouterLink>
          <RouterLink v-else to="/dashboard" class="btn btn-primary">
            {{ t('home.goToDashboard') }}
          </RouterLink>
        </div>
      </div>
    </section>

    <section v-if="gameState" class="game-status container">
      <div class="status-cards">
        <div class="status-card">
          <span class="status-label">{{ t('home.currentTick') }}</span>
          <span class="status-value">{{ gameState.currentTick }}</span>
        </div>
        <div class="status-card">
          <span class="status-label">{{ t('home.taxRate') }}</span>
          <span class="status-value">{{ gameState.taxRate }}%</span>
        </div>
        <div class="status-card">
          <span class="status-label">{{ t('home.activePlayers') }}</span>
          <span class="status-value">{{ rankings.length }}</span>
        </div>
      </div>
    </section>

    <section class="leaderboard container">
      <div class="leaderboard-header">
        <h2>{{ t('home.leaderboard') }}</h2>
        <RouterLink to="/leaderboard" class="btn btn-secondary leaderboard-link">
          {{ t('home.viewFullLeaderboard') }}
        </RouterLink>
      </div>
      <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
      <div v-else-if="rankings.length === 0" class="empty">{{ t('home.noPlayers') }}</div>
      <table v-else class="ranking-table">
        <thead>
          <tr>
            <th>#</th>
            <th>{{ t('home.playerName') }}</th>
            <th>{{ t('home.wealth') }}</th>
            <th>{{ t('home.companies') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(rank, index) in rankings.slice(0, 5)" :key="rank.playerId">
            <td class="rank-num">{{ index + 1 }}</td>
            <td>{{ rank.displayName }}</td>
            <td class="wealth">${{ rank.totalWealth.toLocaleString() }}</td>
            <td>{{ rank.companyCount }}</td>
          </tr>
        </tbody>
      </table>
    </section>
  </div>
</template>

<style scoped>
section {
  position: relative;
}
.hero {
  background: linear-gradient(160deg, #0d1117 0%, rgba(0, 71, 255, 0.12) 100%);
  border-bottom: 1px solid var(--color-border);
  padding: 4rem 0 3rem;
}

.hero-content {
  text-align: center;
}

.hero-video {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  z-index: -2;
}

.hero-video-uplayer {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(255, 246, 67, 0.2);
  z-index: -1;
}
.hero-title {
  font-size: 3rem;
  background: linear-gradient(135deg, rgba(246, 235, 17), rgb(246, 189, 17));
  border-top: 1px solid rgba(246, 235, 17);
  border-bottom: 1px solid rgba(246, 235, 17);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin-bottom: 1rem;
  font-style: normal;
  text-transform: uppercase;
  font-family:
    system-ui,
    -apple-system,
    BlinkMacSystemFont,
    'Segoe UI',
    Roboto,
    Oxygen,
    Ubuntu,
    Cantarell,
    'Open Sans',
    'Helvetica Neue',
    sans-serif;
}

.hero-description {
  font-size: 1.125rem;
  color: rgb(254, 226, 141);
  max-width: 600px;
  margin: 0 auto 2rem;
}

.hero-actions {
  display: flex;
  justify-content: center;
  gap: 1rem;
}

.game-status {
  padding: 2rem 1rem;
}

.status-cards {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
}

.status-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1.25rem;
  text-align: center;
}

.status-label {
  display: block;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.5rem;
}

.status-value {
  font-size: 1.5rem;
  font-weight: 700;
}

.leaderboard {
  padding: 2rem 1rem 4rem;
}

.leaderboard-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
  gap: 1rem;
}

.leaderboard-header h2 {
  font-size: 1.5rem;
  margin: 0;
}

.leaderboard-link {
  font-size: 0.875rem;
  white-space: nowrap;
}

.ranking-table {
  width: 100%;
  border-collapse: collapse;
}

.ranking-table th,
.ranking-table td {
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--color-border);
}

.ranking-table th {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  font-weight: 600;
  text-transform: uppercase;
}

.rank-num {
  font-weight: 700;
  color: var(--color-primary);
}

.wealth {
  color: var(--color-success);
  font-weight: 600;
}

.loading,
.empty {
  text-align: center;
  padding: 2rem;
  color: var(--color-text-secondary);
}
</style>
