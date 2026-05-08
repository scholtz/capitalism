<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import type { GameState, PlayerRanking } from '@/types'

const props = defineProps<{
  gameState: GameState | null
}>()

const { t, locale } = useI18n()

const loading = ref(false)
const rankings = ref<PlayerRanking[]>([])
const shareMessage = ref<string | null>(null)

const isVisible = computed(() => props.gameState?.isEnded === true)
const winnerName = computed(() => props.gameState?.winnerDisplayName ?? t('dashboard.gameEndedUnknownWinner'))
const winnerWealth = computed(() => props.gameState?.winnerWealth ?? 0)
const targetName = computed(() => props.gameState?.winningTargetName ?? t('dashboard.gameEndedTarget'))

watch(
  isVisible,
  async (visible) => {
    if (!visible) return
    loading.value = true
    try {
      const data = await gqlRequest<{ rankings: PlayerRanking[] }>(
        `{ rankings { playerId displayName personalAccountName totalWealth personalCash sharesValue companyCount } }`,
      )
      rankings.value = data.rankings
    } catch {
      rankings.value = []
    } finally {
      loading.value = false
    }
  },
  { immediate: true },
)

function formatCurrency(value: number): string {
  return value.toLocaleString(locale.value)
}

async function shareWin() {
  const text = t('dashboard.gameOverOverlayShareText', {
    winner: winnerName.value,
    wealth: formatCurrency(winnerWealth.value),
    target: targetName.value,
  })
  shareMessage.value = null

  try {
    if (typeof navigator !== 'undefined' && navigator.share) {
      await navigator.share({
        title: t('dashboard.gameOverOverlayTitle'),
        text,
      })
      return
    }

    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      shareMessage.value = t('dashboard.gameOverOverlayShareCopied')
      return
    }

    shareMessage.value = text
  } catch {
    shareMessage.value = t('dashboard.gameOverOverlayShareFailed')
  }
}
</script>

<template>
  <div
    v-if="isVisible"
    class="game-over-overlay"
    role="dialog"
    aria-modal="true"
    :aria-label="t('dashboard.gameOverOverlayTitle')"
  >
    <div class="game-over-card">
      <p class="game-over-eyebrow">🎉 {{ t('dashboard.gameEndedTitle') }}</p>
      <h2>{{ t('dashboard.gameOverOverlayTitle') }}</h2>
      <p>
        {{ t('dashboard.gameEndedBanner', { winner: winnerName, target: targetName }) }}
      </p>
      <p class="winner-wealth">
        {{ t('dashboard.gameEndedWinnerWealth', { wealth: formatCurrency(winnerWealth) }) }}
      </p>

      <button class="btn btn-primary" type="button" @click="shareWin">
        {{ t('dashboard.gameOverOverlayShare') }}
      </button>
      <p v-if="shareMessage" class="share-message" role="status">{{ shareMessage }}</p>

      <h3>{{ t('dashboard.gameOverOverlayRankingTitle') }}</h3>
      <p v-if="loading">{{ t('common.loading') }}</p>
      <ol v-else-if="rankings.length > 0" class="game-over-ranking">
        <li v-for="(entry, index) in rankings" :key="entry.playerId">
          #{{ index + 1 }} {{ entry.displayName }} — ${{ formatCurrency(entry.totalWealth) }}
        </li>
      </ol>
    </div>
  </div>
</template>

<style scoped>
.game-over-overlay {
  position: fixed;
  inset: 0;
  z-index: 1200;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgba(8, 12, 24, 0.78);
  backdrop-filter: blur(4px);
}

.game-over-card {
  width: min(48rem, 100%);
  max-height: calc(100vh - 2rem);
  overflow: auto;
  border-radius: var(--radius-lg);
  border: 1px solid rgba(255, 215, 0, 0.35);
  background: linear-gradient(180deg, rgba(23, 33, 56, 0.97), rgba(10, 14, 24, 0.97));
  padding: 1.5rem;
  color: var(--color-text);
}

.game-over-eyebrow {
  margin: 0 0 0.5rem;
  color: #ffd27a;
  font-weight: 700;
}

h2 {
  margin: 0 0 0.75rem;
}

.winner-wealth {
  margin: 0.5rem 0 1rem;
  font-weight: 700;
}

.game-over-ranking {
  margin: 0.75rem 0 0;
  padding-left: 1.25rem;
  display: grid;
  gap: 0.35rem;
}

.share-message {
  margin: 0.5rem 0 0;
  color: var(--color-text-secondary);
}
</style>
