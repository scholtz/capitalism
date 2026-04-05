<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { computed, ref } from 'vue'
import { formatInGameTime } from '@/lib/gameTime'
import { useGameStateStore } from '@/stores/gameState'

const { t, locale } = useI18n()
const auth = useAuthStore()
const gameStateStore = useGameStateStore()
const { gameState } = storeToRefs(gameStateStore)
const isMenuOpen = ref(false)

const formattedGameTime = computed(() => {
  if (!gameState.value?.currentGameTimeUtc) {
    return null
  }

  return formatInGameTime(gameState.value.currentGameTimeUtc, locale.value)
})

const toggleMenu = () => {
  isMenuOpen.value = !isMenuOpen.value
}

const closeMenu = () => {
  isMenuOpen.value = false
}
</script>

<template>
  <header class="app-header">
    <div class="container header-inner">
      <RouterLink to="/" class="logo" @click="closeMenu">
        <span class="logo-text">CAPITALISM V</span>
      </RouterLink>
      <button class="menu-toggle" @click="toggleMenu" :aria-expanded="isMenuOpen" aria-label="Toggle navigation menu">
        <font-awesome-icon :icon="['fas', 'bars']" />
      </button>
      <nav class="nav-links" :class="{ 'nav-open': isMenuOpen }">
        <RouterLink to="/" :title="t('nav.home')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'home']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.home') }}</span>
        </RouterLink>
        <RouterLink v-if="auth.isAuthenticated" to="/dashboard" :title="t('nav.dashboard')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'tachometer-alt']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.dashboard') }}</span>
        </RouterLink>
        <RouterLink to="/leaderboard" :title="t('nav.leaderboard')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'trophy']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.leaderboard') }}</span>
        </RouterLink>
        <RouterLink to="/encyclopedia" :title="t('nav.encyclopedia')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'book']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.encyclopedia') }}</span>
        </RouterLink>
        <RouterLink to="/exchange" :title="t('nav.exchange')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'chart-bar']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.exchange') }}</span>
        </RouterLink>
        <RouterLink to="/loans" :title="t('nav.loans')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'landmark']" class="mr-2" /> <span class="inline-block md:hidden">{{ t('nav.loans') }}</span>
        </RouterLink>
      </nav>
      <div class="header-actions">
        <div v-if="gameState && formattedGameTime" class="game-time-chip" :title="t('nav.gameTime')">
          <span class="game-time-label">{{ t('nav.gameTime') }}</span>
          <span class="game-time-value">{{ formattedGameTime }}</span>
        </div>
        <template v-if="auth.isAuthenticated">
          <span class="player-name">{{ auth.player?.displayName }}</span>
          <button
            class="btn btn-secondary"
            @click="
              () => {
                auth.logout()
                closeMenu()
              }
            "
            :title="t('common.logout')"
          >
            <font-awesome-icon :icon="['fas', 'sign-out-alt']" />
          </button>
        </template>
        <RouterLink v-else to="/login" class="btn btn-primary" :title="t('common.login')" @click="closeMenu">
          <font-awesome-icon :icon="['fas', 'sign-in-alt']" />
        </RouterLink>
      </div>
    </div>
  </header>
</template>

<style scoped>
.app-header {
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(8px);
}

.header-inner {
  display: flex;
  align-items: center;
  gap: 2rem;
  height: 64px;
}

.menu-toggle {
  display: none;
  background: none;
  border: none;
  color: var(--color-text-secondary);
  font-size: 1.25rem;
  cursor: pointer;
  padding: 0.5rem;
  border-radius: var(--radius-sm);
  transition: background-color 0.15s;
}

.menu-toggle:hover {
  background: var(--color-surface-hover);
  color: var(--color-text);
}

.logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.25rem;
  color: var(--color-text);
  text-decoration: none;
  letter-spacing: -0.02em;
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

.logo-icon {
  font-size: 1.5rem;
}

.logo-text {
  background: linear-gradient(135deg, gold, orange);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  border-top: 1px solid gold;
  border-bottom: 1px solid gold;
  white-space: nowrap;
}

.nav-links {
  display: flex;
  gap: 1.5rem;
  flex: 1;
}

.nav-links a {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--color-text-secondary);
  text-decoration: none;
  transition: color 0.15s;
  position: relative;
  padding-bottom: 2px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.nav-links a svg {
  font-size: 1.25rem;
}

.nav-links a::after {
  content: '';
  position: absolute;
  bottom: -2px;
  left: 0;
  right: 0;
  height: 2px;
  background: var(--color-primary);
  border-radius: 1px;
  transform: scaleX(0);
  transition: transform 0.15s;
}

.nav-links a:hover,
.nav-links a.router-link-active {
  color: var(--color-text);
  text-decoration: none;
}

.nav-links a.router-link-active::after,
.nav-links a:hover::after {
  transform: scaleX(1);
}

/* Mobile responsive styles */
@media (max-width: 768px) {
  .header-inner {
    gap: 1rem;
  }

  .menu-toggle {
    display: block;
    order: 2;
  }

  .nav-links {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    background: var(--color-surface);
    border-bottom: 1px solid var(--color-border);
    flex-direction: column;
    gap: 0;
    padding: 1rem 0;
    transform: translateY(-100%);
    opacity: 0;
    visibility: hidden;
    transition: all 0.3s ease;
    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
  }

  .nav-links.nav-open {
    transform: translateY(0);
    opacity: 1;
    visibility: visible;
  }

  .nav-links a {
    padding: 1rem 2rem;
    justify-content: flex-start;
    gap: 0.75rem;
    font-size: 1rem;
  }

  .nav-links a svg {
    font-size: 1.5rem;
  }

  .nav-links a::after {
    display: none;
  }

  .header-actions {
    order: 3;
  }
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.game-time-chip {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.1rem;
  padding: 0.45rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface-hover);
}

.game-time-label {
  font-size: 0.6875rem;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.game-time-value {
  font-size: 0.8125rem;
  color: var(--color-text);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.user-name {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--color-text-secondary);
}

.btn-ghost {
  background: transparent;
  color: var(--color-text-secondary);
  border: 1px solid var(--color-border);
  padding: 0.5rem 1rem;
  border-radius: var(--radius-sm);
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-ghost:hover {
  background: var(--color-surface-raised);
  color: var(--color-text);
  text-decoration: none;
}

@media (max-width: 640px) {
  .header-inner {
    gap: 1rem;
  }

  .nav-links {
    gap: 0.75rem;
  }

  .game-time-chip {
    display: none;
  }
}
</style>
