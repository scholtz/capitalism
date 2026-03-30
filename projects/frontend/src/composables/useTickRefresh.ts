import { storeToRefs } from 'pinia'
import { onUnmounted, watch } from 'vue'
import { useGameStateStore } from '@/stores/gameState'

type TickRefreshOptions = {
  enabled?: () => boolean
}

export function useTickRefresh(refresh: () => Promise<void> | void, options: TickRefreshOptions = {}) {
  const gameStateStore = useGameStateStore()
  const { gameState } = storeToRefs(gameStateStore)
  let lastSeenTick: number | null = null
  let refreshInFlight: Promise<void> | null = null
  let queuedRefresh = false

  const runRefresh = async () => {
    if (refreshInFlight) {
      queuedRefresh = true
      return refreshInFlight
    }

    refreshInFlight = (async () => {
      do {
        queuedRefresh = false
        await refresh()
      } while (queuedRefresh && (!options.enabled || options.enabled()))
    })().finally(() => {
      refreshInFlight = null
    })

    return refreshInFlight
  }

  const stop = watch(
    () => gameState.value?.currentTick ?? null,
    (tick) => {
      if (tick === null) {
        return
      }

      if (lastSeenTick === null) {
        lastSeenTick = tick
        return
      }

      if (tick === lastSeenTick) {
        return
      }

      lastSeenTick = tick
      if (options.enabled && !options.enabled()) {
        return
      }

      void runRefresh()
    },
  )

  onUnmounted(stop)
}
