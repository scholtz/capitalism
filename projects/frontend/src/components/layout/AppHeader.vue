<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import LanguageSwitcher from '@/components/layout/LanguageSwitcher.vue'

const { t } = useI18n()
const auth = useAuthStore()
</script>

<template>
  <header class="app-header">
    <div class="container header-inner">
      <RouterLink to="/" class="logo">
        <span class="logo-icon">🏭</span>
        <span class="logo-text">CAPITALISM V</span>
      </RouterLink>
      <nav class="nav-links">
        <RouterLink to="/">{{ t('nav.home') }}</RouterLink>
        <RouterLink v-if="auth.isAuthenticated" to="/dashboard">{{ t('nav.dashboard') }}</RouterLink>
        <RouterLink to="/leaderboard">{{ t('nav.leaderboard') }}</RouterLink>
        <RouterLink to="/encyclopedia">{{ t('nav.encyclopedia') }}</RouterLink>
      </nav>
      <div class="header-actions">
        <LanguageSwitcher />
        <template v-if="auth.isAuthenticated">
          <span class="player-name">{{ auth.player?.displayName }}</span>
          <button class="btn btn-secondary" @click="auth.logout()">{{ t('common.logout') }}</button>
        </template>
        <RouterLink v-else to="/login" class="btn btn-primary">{{ t('common.login') }}</RouterLink>
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

.logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.25rem;
  color: var(--color-text);
  text-decoration: none;
  letter-spacing: -0.02em;
}

.logo-icon {
  font-size: 1.5rem;
}

.logo-text {
  background: linear-gradient(135deg, var(--color-primary), var(--color-secondary));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
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

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
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

  .logo-text {
    display: none;
  }
}
</style>
