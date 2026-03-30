import { defineStore } from 'pinia'
import { ref } from 'vue'
import { gqlRequest } from '@/lib/graphql'
import { deepEqual } from '@/lib/utils'
import type { GameState } from '@/types'

const GAME_STATE_QUERY = `
  {
    gameState {
      currentTick
      lastTickAtUtc
      tickIntervalSeconds
      taxCycleTicks
      taxRate
      currentGameYear
      currentGameTimeUtc
      ticksPerDay
      ticksPerYear
      nextTaxTick
      nextTaxGameTimeUtc
      nextTaxGameYear
    }
  }
`

export const useGameStateStore = defineStore('gameState', () => {
  const gameState = ref<GameState | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const started = ref(false)

  let refreshTimer: ReturnType<typeof setTimeout> | null = null
  let inFlight: Promise<GameState | null> | null = null
  let listenersBound = false

  const clearRefreshTimer = () => {
    if (refreshTimer !== null) {
      clearTimeout(refreshTimer)
      refreshTimer = null
    }
  }

  const scheduleNextRefresh = () => {
    clearRefreshTimer()

    if (typeof window === 'undefined' || document.visibilityState === 'hidden') {
      return
    }

    const state = gameState.value
    const nextRefreshInMs = !state ? 5000 : Math.max(new Date(state.lastTickAtUtc).getTime() + state.tickIntervalSeconds * 1000 + 250 - Date.now(), 250)

    refreshTimer = setTimeout(() => {
      void refreshGameState()
    }, nextRefreshInMs)
  }

  async function refreshGameState(force = false) {
    if (inFlight && !force) {
      return inFlight
    }

    loading.value = true
    error.value = null

    inFlight = (async () => {
      try {
        const data = await gqlRequest<{ gameState: GameState | null }>(GAME_STATE_QUERY)
        if (!deepEqual(gameState.value, data.gameState)) {
          gameState.value = data.gameState
        }
        return gameState.value
      } catch (reason: unknown) {
        error.value = reason instanceof Error ? reason.message : 'Failed to load game state'
        return gameState.value
      } finally {
        loading.value = false
        inFlight = null
        scheduleNextRefresh()
      }
    })()

    return inFlight
  }

  const handleVisibilityChange = () => {
    if (document.visibilityState === 'visible') {
      void refreshGameState(true)
      return
    }

    clearRefreshTimer()
  }

  const handleWindowFocus = () => {
    void refreshGameState(true)
  }

  function start() {
    if (started.value) {
      scheduleNextRefresh()
      return
    }

    started.value = true

    if (typeof window !== 'undefined' && !listenersBound) {
      document.addEventListener('visibilitychange', handleVisibilityChange)
      window.addEventListener('focus', handleWindowFocus)
      listenersBound = true
    }

    void refreshGameState(true)
  }

  return {
    gameState,
    loading,
    error,
    started,
    start,
    refreshGameState,
  }
})
