<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest } from '@/lib/graphql'
import { useTickRefresh } from '@/composables/useTickRefresh'
import { deepEqual } from '@/lib/utils'
import type { PlayerRanking, CompanyRanking } from '@/types'

const { t } = useI18n()
const auth = useAuthStore()

const rankings = ref<PlayerRanking[]>([])
const companyRankings = ref<CompanyRanking[]>([])
const playerLoading = ref(true)
const companyLoading = ref(false)
const playerError = ref<string | null>(null)
const companyError = ref<string | null>(null)
const companyRankingsLoaded = ref(false)
const activeTab = ref<'players' | 'companies'>('players')

const PLAYER_RANKINGS_QUERY = `
  {
    rankings {
      playerId
      displayName
      totalWealth
      cashTotal
      buildingValue
      inventoryValue
      companyCount
    }
  }
`

const COMPANY_RANKINGS_QUERY = `
  {
    companyRankings {
      companyId
      companyName
      playerId
      ownerDisplayName
      totalWealth
      cash
      buildingValue
      inventoryValue
      buildingCount
    }
  }
`

async function fetchPlayerRankings(isRefresh = false) {
  if (!isRefresh) {
    playerLoading.value = true
  }
  playerError.value = null
  try {
    const data = await gqlRequest<{
      rankings: PlayerRanking[]
    }>(PLAYER_RANKINGS_QUERY)
    if (!deepEqual(rankings.value, data.rankings)) {
      rankings.value = data.rankings
    }
  } catch (e) {
    playerError.value = e instanceof Error ? e.message : t('leaderboard.loadFailed')
  } finally {
    playerLoading.value = false
  }
}

async function fetchCompanyRankings(isRefresh = false) {
  if (!isRefresh) {
    companyLoading.value = true
  }
  companyError.value = null
  try {
    const data = await gqlRequest<{
      companyRankings: CompanyRanking[]
    }>(COMPANY_RANKINGS_QUERY)
    if (!deepEqual(companyRankings.value, data.companyRankings)) {
      companyRankings.value = data.companyRankings
    }
    companyRankingsLoaded.value = true
  } catch (e) {
    companyError.value = e instanceof Error ? e.message : t('leaderboard.loadFailed')
  } finally {
    companyLoading.value = false
  }
}

onMounted(async () => {
  auth.initFromStorage()
  if (auth.isAuthenticated) {
    void auth.fetchMe()
  }
  await Promise.allSettled([fetchPlayerRankings(), fetchCompanyRankings()])
})

useTickRefresh(() => {
  void fetchPlayerRankings(true)
  if (companyRankingsLoaded.value || activeTab.value === 'companies') {
    void fetchCompanyRankings(true)
  }
})

watch(
  activeTab,
  (tab: 'players' | 'companies') => {
    if (tab === 'companies' && !companyRankingsLoaded.value && !companyLoading.value) {
      void fetchCompanyRankings()
    }
  },
)

function retryActiveTab() {
  if (activeTab.value === 'companies') {
    void fetchCompanyRankings()
    return
  }
  void fetchPlayerRankings()
}

function formatWealth(value: number): string {
  if (value >= 1_000_000) {
    return `$${(value / 1_000_000).toFixed(2)}M`
  }
  if (value >= 1_000) {
    return `$${(value / 1_000).toFixed(1)}K`
  }
  return `$${value.toLocaleString()}`
}

function rankBadge(index: number): string {
  if (index === 0) return '🥇'
  if (index === 1) return '🥈'
  if (index === 2) return '🥉'
  return `${index + 1}`
}

const currentPlayerId = computed(() => auth.player?.id ?? null)
</script>

<template>
  <div class="leaderboard-view">
    <div class="leaderboard-hero">
      <div class="container">
        <p class="leaderboard-eyebrow">{{ t('leaderboard.eyebrow') }}</p>
        <h1 class="leaderboard-title">{{ t('leaderboard.title') }}</h1>
        <p class="leaderboard-subtitle">{{ t('leaderboard.subtitle') }}</p>
      </div>
    </div>

    <div class="container leaderboard-content">
      <!-- Tab switcher -->
      <div class="tab-switcher" role="tablist">
        <button
          role="tab"
          :aria-selected="activeTab === 'players'"
          class="tab-btn"
          :class="{ active: activeTab === 'players' }"
          @click="activeTab = 'players'"
        >
          👤 {{ t('leaderboard.tabPlayers') }}
        </button>
        <button
          role="tab"
          :aria-selected="activeTab === 'companies'"
          class="tab-btn"
          :class="{ active: activeTab === 'companies' }"
          @click="activeTab = 'companies'"
        >
          🏢 {{ t('leaderboard.tabCompanies') }}
        </button>
      </div>

      <!-- Player rankings tab -->
      <template v-if="activeTab === 'players'">
        <div v-if="playerLoading" class="state-box">
          <span class="state-icon">⏳</span>
          <p>{{ t('common.loading') }}</p>
        </div>

        <div v-else-if="playerError" class="state-box state-error">
          <span class="state-icon">⚠️</span>
          <p>{{ playerError }}</p>
          <button class="btn btn-secondary" aria-label="Retry loading leaderboard" @click="retryActiveTab">
            {{ t('common.tryAgain') }}
          </button>
        </div>

        <div v-else-if="rankings.length === 0" class="state-box">
          <span class="state-icon">🏆</span>
          <p class="state-title">{{ t('leaderboard.emptyTitle') }}</p>
          <p class="state-desc">{{ t('leaderboard.emptyDesc') }}</p>
          <RouterLink to="/onboarding" class="btn btn-primary">{{ t('leaderboard.startEmpire') }}</RouterLink>
        </div>

        <div v-else class="rankings-list">
          <div
            v-for="(rank, index) in rankings"
            :key="rank.playerId"
            class="rank-card"
            :class="{
              'rank-top3': index < 3,
              'rank-gold': index === 0,
              'rank-silver': index === 1,
              'rank-bronze': index === 2,
              'rank-self': rank.playerId === currentPlayerId,
            }"
          >
            <div class="rank-badge">{{ rankBadge(index) }}</div>
            <div class="rank-info">
              <div class="rank-name">
                {{ rank.displayName }}
                <span v-if="rank.playerId === currentPlayerId" class="you-badge">{{ t('leaderboard.you') }}</span>
              </div>
              <div class="rank-companies">
                {{ t('leaderboard.companiesCount', { n: rank.companyCount }) }}
              </div>
            </div>
            <div class="rank-wealth">
              <div class="total-wealth">{{ formatWealth(rank.totalWealth) }}</div>
              <div class="wealth-breakdown">
                <span class="breakdown-item" :title="t('leaderboard.cashTooltip')"> 💵 {{ formatWealth(rank.cashTotal) }} </span>
                <span class="breakdown-sep">·</span>
                <span class="breakdown-item" :title="t('leaderboard.buildingsTooltip')"> 🏗️ {{ formatWealth(rank.buildingValue) }} </span>
                <span class="breakdown-sep">·</span>
                <span class="breakdown-item" :title="t('leaderboard.inventoryTooltip')"> 📦 {{ formatWealth(rank.inventoryValue) }} </span>
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Company rankings tab -->
      <template v-else-if="activeTab === 'companies'">
        <div v-if="companyLoading" class="state-box">
          <span class="state-icon">⏳</span>
          <p>{{ t('common.loading') }}</p>
        </div>

        <div v-else-if="companyError" class="state-box state-error">
          <span class="state-icon">⚠️</span>
          <p>{{ companyError }}</p>
          <button class="btn btn-secondary" aria-label="Retry loading leaderboard" @click="retryActiveTab">
            {{ t('common.tryAgain') }}
          </button>
        </div>

        <div v-else-if="companyRankings.length === 0" class="state-box">
          <span class="state-icon">🏢</span>
          <p class="state-title">{{ t('leaderboard.emptyCompanyTitle') }}</p>
          <p class="state-desc">{{ t('leaderboard.emptyCompanyDesc') }}</p>
          <RouterLink to="/onboarding" class="btn btn-primary">{{ t('leaderboard.startEmpire') }}</RouterLink>
        </div>

        <div v-else class="rankings-list">
          <div
            v-for="(rank, index) in companyRankings"
            :key="rank.companyId"
            class="rank-card"
            :class="{
              'rank-top3': index < 3,
              'rank-gold': index === 0,
              'rank-silver': index === 1,
              'rank-bronze': index === 2,
              'rank-self': rank.playerId === currentPlayerId,
            }"
          >
            <div class="rank-badge">{{ rankBadge(index) }}</div>
            <div class="rank-info">
              <div class="rank-name">
                {{ rank.companyName }}
                <span v-if="rank.playerId === currentPlayerId" class="you-badge">{{ t('leaderboard.you') }}</span>
              </div>
              <div class="rank-companies">
                {{ t('leaderboard.ownedBy', { name: rank.ownerDisplayName }) }} · {{ t('leaderboard.buildingsCount', { n: rank.buildingCount }) }}
              </div>
            </div>
            <div class="rank-wealth">
              <div class="total-wealth">{{ formatWealth(rank.totalWealth) }}</div>
              <div class="wealth-breakdown">
                <span class="breakdown-item" :title="t('leaderboard.cashTooltip')"> 💵 {{ formatWealth(rank.cash) }} </span>
                <span class="breakdown-sep">·</span>
                <span class="breakdown-item" :title="t('leaderboard.buildingsTooltip')"> 🏗️ {{ formatWealth(rank.buildingValue) }} </span>
                <span class="breakdown-sep">·</span>
                <span class="breakdown-item" :title="t('leaderboard.inventoryTooltip')"> 📦 {{ formatWealth(rank.inventoryValue) }} </span>
              </div>
            </div>
          </div>
        </div>
      </template>

      <div class="leaderboard-explainer">
        <h3>{{ t('leaderboard.howItWorksTitle') }}</h3>
        <p>{{ t('leaderboard.howItWorksBody') }}</p>
        <ul class="formula-list">
          <li>
            💵 <strong>{{ t('leaderboard.cashLabel') }}</strong> — {{ t('leaderboard.cashExplain') }}
          </li>
          <li>
            🏗️ <strong>{{ t('leaderboard.buildingsLabel') }}</strong> — {{ t('leaderboard.buildingsExplain') }}
          </li>
          <li>
            📦 <strong>{{ t('leaderboard.inventoryLabel') }}</strong> — {{ t('leaderboard.inventoryExplain') }}
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>

<style scoped>
.leaderboard-view {
  min-height: 100vh;
}

.leaderboard-hero {
  background: linear-gradient(160deg, #0d1117 0%, rgba(0, 71, 255, 0.14) 100%);
  border-bottom: 1px solid var(--color-border);
  padding: 3rem 0 2.5rem;
  text-align: center;
}

.leaderboard-eyebrow {
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--color-primary);
  margin-bottom: 0.5rem;
}

.leaderboard-title {
  font-size: 2.25rem;
  font-weight: 800;
  background: linear-gradient(135deg, var(--color-primary), var(--color-secondary));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin-bottom: 0.75rem;
}

.leaderboard-subtitle {
  font-size: 1rem;
  color: var(--color-text-secondary);
  max-width: 540px;
  margin: 0 auto;
}

.leaderboard-content {
  padding: 2.5rem 1rem 4rem;
}

/* ── Tab switcher ──────────────────────────────────────────────────────────── */
.tab-switcher {
  display: flex;
  gap: 0.5rem;
  max-width: 800px;
  margin: 0 auto 1.5rem;
}

.tab-btn {
  flex: 1;
  padding: 0.75rem 1rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  color: var(--color-text-secondary);
  font-size: 0.9375rem;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s,
    color 0.15s,
    border-color 0.15s;
}

.tab-btn:hover {
  border-color: var(--color-primary);
  color: var(--color-text);
}

.tab-btn.active {
  background: var(--color-primary);
  color: #fff;
  border-color: var(--color-primary);
}

/* ── States ─────────────────────────────────────────────────────────────────── */
.state-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem 1rem;
  text-align: center;
}

.state-icon {
  font-size: 2.5rem;
}

.state-title {
  font-size: 1.25rem;
  font-weight: 700;
}

.state-desc {
  color: var(--color-text-secondary);
  max-width: 400px;
}

.state-error {
  color: var(--color-danger, #f85149);
}

/* ── Rank cards ─────────────────────────────────────────────────────────────── */
.rankings-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 800px;
  margin: 0 auto 3rem;
}

.rank-card {
  display: flex;
  align-items: center;
  gap: 1rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1rem 1.25rem;
  transition: border-color 0.15s;
}

.rank-card:hover {
  border-color: var(--color-primary);
}

.rank-top3 {
  border-left: 3px solid var(--color-primary);
}

.rank-gold {
  border-left-color: #ffd700;
  background: linear-gradient(90deg, rgba(255, 215, 0, 0.06) 0%, var(--color-surface) 40%);
}

.rank-silver {
  border-left-color: #c0c0c0;
  background: linear-gradient(90deg, rgba(192, 192, 192, 0.06) 0%, var(--color-surface) 40%);
}

.rank-bronze {
  border-left-color: #cd7f32;
  background: linear-gradient(90deg, rgba(205, 127, 50, 0.06) 0%, var(--color-surface) 40%);
}

.rank-self {
  border-color: var(--color-secondary);
}

.rank-badge {
  font-size: 1.5rem;
  font-weight: 800;
  min-width: 2.5rem;
  text-align: center;
  color: var(--color-primary);
  line-height: 1;
}

.rank-info {
  flex: 1;
  min-width: 0;
}

.rank-name {
  font-size: 1rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.you-badge {
  font-size: 0.6875rem;
  font-weight: 700;
  background: var(--color-secondary);
  color: #000;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.rank-companies {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  margin-top: 0.125rem;
}

.rank-wealth {
  text-align: right;
}

.total-wealth {
  font-size: 1.25rem;
  font-weight: 800;
  color: var(--color-secondary);
}

.wealth-breakdown {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
  margin-top: 0.25rem;
  display: flex;
  gap: 0.25rem;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.breakdown-sep {
  opacity: 0.4;
}

/* ── How it works ────────────────────────────────────────────────────────────── */
.leaderboard-explainer {
  max-width: 800px;
  margin: 0 auto;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1.5rem;
}

.leaderboard-explainer h3 {
  font-size: 1rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}

.leaderboard-explainer p {
  font-size: 0.9rem;
  color: var(--color-text-secondary);
  margin-bottom: 0.75rem;
}

.formula-list {
  list-style: none;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  font-size: 0.875rem;
  color: var(--color-text-secondary);
}

.formula-list strong {
  color: var(--color-text);
}

/* ── Responsive ───────────────────────────────────────────────────────────────── */
@media (max-width: 600px) {
  .leaderboard-title {
    font-size: 1.75rem;
  }

  .rank-card {
    flex-wrap: wrap;
  }

  .rank-wealth {
    width: 100%;
    text-align: left;
    padding-left: calc(2.5rem + 1rem);
  }

  .wealth-breakdown {
    justify-content: flex-start;
  }
}
</style>
