import { ref, onUnmounted } from 'vue'
import type { Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { GameState } from '@/types'

/**
 * Provides a reactive next-tick countdown derived from backend-authoritative GameState data.
 * Extracts the interval logic from individual views so it can be shared without duplication.
 *
 * Usage:
 *   const { tickCountdown, startTickCountdown, stopTickCountdown } = useTickCountdown(gameState)
 *   // call startTickCountdown() once gameState.value is populated
 */
export function useTickCountdown(gameStateRef: Ref<GameState | null>) {
  const { t } = useI18n()
  const tickCountdown = ref<string | null>(null)
  let intervalId: ReturnType<typeof setInterval> | null = null

  function updateTickCountdown() {
    const gs = gameStateRef.value
    if (!gs) {
      tickCountdown.value = null
      return
    }

    const lastTickMs = new Date(gs.lastTickAtUtc).getTime()
    const nextTickMs = lastTickMs + gs.tickIntervalSeconds * 1000
    const remainingMs = nextTickMs - Date.now()

    if (remainingMs <= 0) {
      tickCountdown.value = t('tickClock.tickSoon')
      return
    }

    const totalSeconds = Math.ceil(remainingMs / 1000)
    const minutes = Math.floor(totalSeconds / 60)
    const seconds = totalSeconds % 60
    const paddedSeconds = String(seconds).padStart(2, '0')
    const timeStr = minutes > 0 ? `${minutes}m ${paddedSeconds}s` : `${seconds}s`
    tickCountdown.value = t('tickClock.nextTick', { time: timeStr })
  }

  function startTickCountdown() {
    stopTickCountdown()
    updateTickCountdown()
    intervalId = setInterval(updateTickCountdown, 1000)
  }

  function stopTickCountdown() {
    if (intervalId !== null) {
      clearInterval(intervalId)
      intervalId = null
    }
  }

  onUnmounted(stopTickCountdown)

  return { tickCountdown, startTickCountdown, stopTickCountdown }
}
