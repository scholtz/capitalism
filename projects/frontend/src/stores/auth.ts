import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { gqlRequest, GraphQLError } from '@/lib/graphql'
import { gqlRequest as gqlMasterRequest } from '@/lib/graphqlMasterServer'
import { deepEqual } from '@/lib/utils'
import type { AccountContextResult, AccountContextType, Player, AuthPayload } from '@/types'

const PLAYER_SELECTION = `
  id
  displayName
  email
  role
  createdAtUtc
  lastLoginAtUtc
  personalCash
  activeAccountType
  activeCompanyId
  onboardingCompletedAtUtc
  onboardingCurrentStep
  onboardingIndustry
  onboardingCityId
  onboardingCompanyId
  onboardingFactoryLotId
  onboardingShopBuildingId
  onboardingFirstSaleCompletedAtUtc
  proSubscriptionEndsAtUtc
  companies {
    id
    name
    cash
    foundedAtUtc
    foundedAtTick
  }
`

interface MasterSessionPayload {
  token: string
  expiresAtUtc: string
}

const MASTER_REGISTER_MUTATION = `
  mutation Register($input: RegisterInput!) {
    register(input: $input) {
      token
      expiresAtUtc
    }
  }
`

const MASTER_LOGIN_MUTATION = `
  mutation Login($input: LoginInput!) {
    login(input: $input) {
      token
      expiresAtUtc
    }
  }
`

export const useAuthStore = defineStore('auth', () => {
  const player = ref<Player | null>(null)
  const token = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  function getCookieValue(name: string) {
    if (typeof document === 'undefined') {
      return null
    }

    const prefix = `${name}=`
    const match = document.cookie.split('; ').find((entry) => entry.startsWith(prefix))

    return match ? decodeURIComponent(match.slice(prefix.length)) : null
  }

  function clearStoredSession() {
    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem('auth_token')
      localStorage.removeItem('auth_expires')
    }

    if (typeof document !== 'undefined') {
      document.cookie = 'auth_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/'
      document.cookie = 'auth_expires=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/'
    }
  }

  function getStoredToken() {
    if (typeof localStorage === 'undefined') {
      return null
    }

    const stored = localStorage.getItem('auth_token')
    const expires = localStorage.getItem('auth_expires')
    if (stored && expires && new Date(expires) > new Date()) {
      return stored
    }

    const cookieToken = getCookieValue('auth_token')
    const cookieExpires = getCookieValue('auth_expires')
    if (cookieToken && cookieExpires && new Date(cookieExpires) > new Date()) {
      localStorage.setItem('auth_token', cookieToken)
      localStorage.setItem('auth_expires', cookieExpires)
      return cookieToken
    }

    clearStoredSession()
    return null
  }

  const isAuthenticated = computed(() => !!token.value || !!getStoredToken())
  const isAdmin = computed(() => player.value?.role === 'ADMIN')
  const isProSubscriber = computed(() => !!player.value?.proSubscriptionEndsAtUtc && new Date(player.value.proSubscriptionEndsAtUtc).getTime() > Date.now())
  const effectiveProSubscriptionEndsAtUtc = computed(() => player.value?.proSubscriptionEndsAtUtc ?? null)

  function initFromStorage() {
    token.value = getStoredToken()
  }

  function setSession(auth: AuthPayload) {
    applyStoredSession(auth.token, auth.expiresAtUtc)
    player.value = auth.player
  }

  function applyStoredSession(tokenValue: string, expiresAtUtc: string) {
    token.value = tokenValue
    localStorage.setItem('auth_token', tokenValue)
    localStorage.setItem('auth_expires', expiresAtUtc)
    if (typeof document !== 'undefined') {
      document.cookie = `auth_token=${encodeURIComponent(tokenValue)}; path=/`
      document.cookie = `auth_expires=${encodeURIComponent(expiresAtUtc)}; path=/`
    }
  }

  async function fetchCurrentPlayer() {
    const data = await gqlRequest<{ me: Player }>(`{ me {${PLAYER_SELECTION}} }`)
    if (!deepEqual(player.value, data.me)) {
      player.value = data.me
    }
  }

  async function register(email: string, displayName: string, password: string) {
    loading.value = true
    error.value = null
    try {
      const data = await gqlMasterRequest<{ register: MasterSessionPayload }>(
        MASTER_REGISTER_MUTATION,
        { input: { email, displayName, password } },
      )
      player.value = null
      applyStoredSession(data.register.token, data.register.expiresAtUtc)
      await fetchCurrentPlayer()
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : 'Registration failed'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function login(email: string, password: string) {
    loading.value = true
    error.value = null
    try {
      const data = await gqlMasterRequest<{ login: MasterSessionPayload }>(
        MASTER_LOGIN_MUTATION,
        { input: { email, password } },
      )
      player.value = null
      applyStoredSession(data.login.token, data.login.expiresAtUtc)
      await fetchCurrentPlayer()
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : 'Login failed'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchMe() {
    if (!token.value) {
      initFromStorage()
    }
    if (!token.value) return
    loading.value = true
    try {
      await fetchCurrentPlayer()
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : 'Failed to load account'

      if (e instanceof GraphQLError && /not authenticated/i.test(e.message)) {
        logout()
      }
    } finally {
      loading.value = false
    }
  }

  async function switchAccountContext(accountType: AccountContextType, companyId?: string | null) {
    loading.value = true
    error.value = null
    try {
      const data = await gqlRequest<{ switchAccountContext: AccountContextResult }>(
        `mutation SwitchAccountContext($input: SwitchAccountContextInput!) {
          switchAccountContext(input: $input) {
            activeAccountType
            activeCompanyId
            activeAccountName
          }
        }`,
        {
          input: {
            accountType,
            companyId: accountType === 'COMPANY' ? companyId : null,
          },
        },
      )

      await fetchMe()
      return data.switchAccountContext
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : 'Failed to switch account context'
      throw e
    } finally {
      loading.value = false
    }
  }

  function logout() {
    token.value = null
    player.value = null
    clearStoredSession()
  }

  return {
    player,
    token,
    loading,
    error,
    isAuthenticated,
    isAdmin,
    isProSubscriber,
    effectiveProSubscriptionEndsAtUtc,
    initFromStorage,
    register,
    login,
    applyAuthPayload: setSession,
    fetchMe,
    switchAccountContext,
    logout,
  }
})
