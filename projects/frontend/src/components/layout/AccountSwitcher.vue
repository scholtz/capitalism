<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { buildAccountOptions, getActiveAccountName } from '@/lib/accountContext'

const emit = defineEmits<{
  switched: []
}>()

const { t, locale } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const root = ref<HTMLElement | null>(null)
const isOpen = ref(false)
const switchingKey = ref<string | null>(null)

const accountOptions = computed(() => buildAccountOptions(auth.player, auth.player?.companies ?? []))
const activeAccountName = computed(() => getActiveAccountName(auth.player, auth.player?.companies ?? []) ?? auth.player?.personalAccountName ?? auth.player?.displayName ?? '')
const activeAccountBadge = computed(() => (auth.player?.activeAccountType === 'COMPANY' ? t('accountSwitcher.companyBadge') : t('accountSwitcher.personBadge')))

function formatCurrency(value: number): string {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value)
}

function closeMenu() {
  isOpen.value = false
}

function toggleMenu() {
  if (!auth.player) {
    return
  }

  isOpen.value = !isOpen.value
}

function handlePointerDown(event: MouseEvent) {
  if (!root.value || !(event.target instanceof Node) || root.value.contains(event.target)) {
    return
  }

  closeMenu()
}

function handleEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    closeMenu()
  }
}

function getRouteTarget(accountType: 'PERSON' | 'COMPANY', companyId: string | null) {
  const routeName = typeof route.name === 'string' ? route.name : null

  if (routeName === 'ledger' || routeName === 'company-settings' || routeName === 'buy-building') {
    if (accountType !== 'COMPANY' || !companyId) {
      return { name: 'dashboard' as const }
    }

    return {
      name: routeName,
      params: {
        ...route.params,
        companyId,
      },
    }
  }

  if (routeName === 'building-detail' || routeName === 'bank-management') {
    return { name: 'dashboard' as const }
  }

  return null
}

async function switchAccount(accountType: 'PERSON' | 'COMPANY', companyId: string | null, key: string) {
  if (!auth.player) {
    return
  }

  if (auth.player.activeAccountType === accountType && (accountType !== 'COMPANY' || auth.player.activeCompanyId === companyId)) {
    closeMenu()
    emit('switched')
    return
  }

  switchingKey.value = key

  try {
    await auth.switchAccountContext(accountType, companyId)
    closeMenu()
    emit('switched')

    const routeTarget = getRouteTarget(accountType, companyId)
    if (routeTarget) {
      await router.replace(routeTarget)
    }
  } finally {
    switchingKey.value = null
  }
}

onMounted(() => {
  document.addEventListener('mousedown', handlePointerDown)
  document.addEventListener('keydown', handleEscape)
})

onUnmounted(() => {
  document.removeEventListener('mousedown', handlePointerDown)
  document.removeEventListener('keydown', handleEscape)
})
</script>

<template>
  <div v-if="auth.player" ref="root" class="account-switcher">
    <button class="account-trigger" type="button" :aria-expanded="isOpen" aria-haspopup="menu" :aria-label="t('accountSwitcher.openMenu', { account: activeAccountName })" @click="toggleMenu">
      <span class="account-trigger-name">{{ activeAccountName }}</span>
      <span class="account-trigger-meta">{{ activeAccountBadge }}</span>
      <span class="account-trigger-caret" aria-hidden="true">v</span>
    </button>

    <div v-if="isOpen" class="account-menu" role="menu" :aria-label="t('accountSwitcher.menuLabel')">
      <button
        v-for="option in accountOptions"
        :key="option.key"
        class="account-option"
        :class="{ active: option.isActive }"
        type="button"
        role="menuitemradio"
        :aria-checked="option.isActive"
        :disabled="switchingKey === option.key"
        @click="switchAccount(option.accountType, option.companyId, option.key)"
      >
        <span class="account-option-main">
          <span class="account-option-name">{{ option.name }}</span>
          <span class="account-option-type">
            {{ option.accountType === 'PERSON' ? t('accountSwitcher.personalAccountHint') : t('accountSwitcher.companyAccountHint') }}
          </span>
        </span>
        <span class="account-option-meta">
          <span v-if="option.accountType === 'PERSON' && auth.player?.personalCash != null" class="account-option-cash">
            {{ formatCurrency(auth.player.personalCash) }}
          </span>
          <span v-else-if="option.cash != null" class="account-option-cash">
            {{ formatCurrency(option.cash) }}
          </span>
          <span v-if="option.isActive" class="account-option-active">{{ t('accountSwitcher.active') }}</span>
        </span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.account-switcher {
  position: relative;
}

.account-trigger {
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  min-width: 11rem;
  padding: 0.45rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface-hover);
  color: var(--color-text);
  cursor: pointer;
  transition:
    border-color 0.15s ease,
    background 0.15s ease;
}

.account-trigger:hover,
.account-trigger:focus-visible {
  border-color: var(--color-primary);
  background: var(--color-surface-raised);
}

.account-trigger-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.875rem;
  font-weight: 600;
  text-align: left;
}

.account-trigger-meta {
  font-size: 0.6875rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.account-trigger-caret {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.account-menu {
  position: absolute;
  top: calc(100% + 0.4rem);
  right: 0;
  width: min(22rem, calc(100vw - 2rem));
  padding: 0.4rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.22);
  z-index: 120;
}

.account-option {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-text);
  cursor: pointer;
  text-align: left;
}

.account-option:hover,
.account-option:focus-visible,
.account-option.active {
  background: var(--color-surface-hover);
}

.account-option:disabled {
  opacity: 0.65;
  cursor: wait;
}

.account-option-main,
.account-option-meta {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.account-option-name {
  font-size: 0.875rem;
  font-weight: 600;
}

.account-option-type,
.account-option-cash,
.account-option-active {
  font-size: 0.75rem;
  color: var(--color-text-secondary);
}

.account-option-active {
  font-weight: 700;
  color: var(--color-primary);
}

@media (max-width: 768px) {
  .account-trigger {
    min-width: 9.5rem;
    max-width: 11rem;
  }

  .account-menu {
    right: -2.8rem;
  }
}
</style>
