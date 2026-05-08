<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { useTickRefresh } from '@/composables/useTickRefresh'
import type { PlayerRanking } from '@/types'

const { t } = useI18n()
const route = useRoute()

const rankings = ref<PlayerRanking[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

const RANKINGS_QUERY = `
  {
    rankings {
      playerId
      displayName
      personalAccountName
      totalWealth
      personalCash
      sharesValue
      companyCount
    }
  }
`

const profile = computed(() => rankings.value.find((entry) => entry.playerId === route.params.id) ?? null)
const profileRank = computed(() => {
  const index = rankings.value.findIndex((entry) => entry.playerId === route.params.id)
  return index >= 0 ? index + 1 : null
})
const profileName = computed(() => profile.value?.personalAccountName ?? profile.value?.displayName ?? '')

function formatWealth(value: number) {
  if (value >= 1_000_000) {
    return `$${(value / 1_000_000).toFixed(2)}M`
  }
  if (value >= 1_000) {
    return `$${(value / 1_000).toFixed(1)}K`
  }
  return `$${value.toLocaleString()}`
}

async function fetchProfile(isRefresh = false) {
  if (!isRefresh) {
    loading.value = true
  }
  error.value = null
  try {
    const data = await gqlRequest<{ rankings: PlayerRanking[] }>(RANKINGS_QUERY)
    rankings.value = data.rankings
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : t('playerProfile.loadFailed')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void fetchProfile()
})

useTickRefresh(async () => {
  await fetchProfile(true)
})
</script>

<template>
  <div class="container player-profile-view">
    <RouterLink class="back-link" to="/leaderboard">← {{ t('playerProfile.backToLeaderboard') }}</RouterLink>

    <div v-if="loading" class="state-box">
      <span class="state-icon">⏳</span>
      <p>{{ t('common.loading') }}</p>
    </div>

    <div v-else-if="error" class="state-box state-error">
      <span class="state-icon">⚠️</span>
      <p>{{ error }}</p>
    </div>

    <div v-else-if="!profile" class="state-box">
      <span class="state-icon">🕵️</span>
      <p class="state-title">{{ t('playerProfile.notFoundTitle') }}</p>
      <p class="state-desc">{{ t('playerProfile.notFoundBody') }}</p>
    </div>

    <section v-else class="profile-card" :aria-label="profileName">
      <p class="profile-kicker">{{ t('playerProfile.kicker') }}</p>
      <h1>{{ profileName }}</h1>
      <p class="profile-subtitle">{{ t('playerProfile.subtitle') }}</p>

      <div class="profile-grid">
        <div class="metric-card">
          <span class="metric-label">{{ t('playerProfile.rank') }}</span>
          <strong class="metric-value">#{{ profileRank }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">{{ t('playerProfile.totalWealth') }}</span>
          <strong class="metric-value">{{ formatWealth(profile.totalWealth) }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">{{ t('playerProfile.personalCash') }}</span>
          <strong class="metric-value">{{ formatWealth(profile.personalCash) }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">{{ t('playerProfile.sharesValue') }}</span>
          <strong class="metric-value">{{ formatWealth(profile.sharesValue) }}</strong>
        </div>
        <div class="metric-card">
          <span class="metric-label">{{ t('playerProfile.companyCount') }}</span>
          <strong class="metric-value">{{ profile.companyCount }}</strong>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.player-profile-view {
  padding-top: 2rem;
  padding-bottom: 2rem;
}

.back-link {
  display: inline-flex;
  margin-bottom: 1rem;
  color: var(--color-primary);
  font-weight: 600;
  text-decoration: none;
}

.profile-card {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  background: var(--color-surface);
  padding: 1.5rem;
}

.profile-kicker {
  margin: 0 0 0.35rem;
  color: var(--color-primary);
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.profile-subtitle {
  margin: 0.5rem 0 1.5rem;
  color: var(--color-text-muted);
}

.profile-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
}

.metric-card {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--color-surface) 92%, var(--color-primary) 8%);
  padding: 1rem;
}

.metric-label {
  display: block;
  color: var(--color-text-muted);
  font-size: 0.85rem;
  margin-bottom: 0.4rem;
}

.metric-value {
  font-size: 1.2rem;
}

.state-box {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  padding: 1.5rem;
  text-align: center;
}

.state-error {
  border-color: var(--color-danger);
}

.state-icon {
  display: block;
  font-size: 1.8rem;
  margin-bottom: 0.5rem;
}

.state-title {
  font-weight: 700;
}

.state-desc {
  color: var(--color-text-muted);
}
</style>
