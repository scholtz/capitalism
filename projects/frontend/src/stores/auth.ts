import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { gqlRequest } from '@/lib/graphql'
import type { Player, AuthPayload } from '@/types'

export const useAuthStore = defineStore('auth', () => {
  const player = ref<Player | null>(null)
  const token = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => !!token.value)
  const isAdmin = computed(() => player.value?.role === 'ADMIN')

  function initFromStorage() {
    const stored = localStorage.getItem('auth_token')
    const expires = localStorage.getItem('auth_expires')
    if (stored && expires && new Date(expires) > new Date()) {
      token.value = stored
    } else {
      localStorage.removeItem('auth_token')
      localStorage.removeItem('auth_expires')
    }
  }

  function setSession(auth: AuthPayload) {
    token.value = auth.token
    player.value = auth.player
    localStorage.setItem('auth_token', auth.token)
    localStorage.setItem('auth_expires', auth.expiresAtUtc)
  }

  async function register(email: string, displayName: string, password: string) {
    loading.value = true
    error.value = null
    try {
      const data = await gqlRequest<{ register: AuthPayload }>(
        `mutation Register($input: RegisterInput!) {
          register(input: $input) {
            token
            expiresAtUtc
            player { id displayName email role createdAtUtc onboardingCompletedAtUtc companies { id name cash } }
          }
        }`,
        { input: { email, displayName, password } },
      )
      setSession(data.register)
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
      const data = await gqlRequest<{ login: AuthPayload }>(
        `mutation Login($input: LoginInput!) {
          login(input: $input) {
            token
            expiresAtUtc
            player { id displayName email role createdAtUtc onboardingCompletedAtUtc companies { id name cash } }
          }
        }`,
        { input: { email, password } },
      )
      setSession(data.login)
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : 'Login failed'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function fetchMe() {
    if (!token.value) return
    loading.value = true
    try {
      const data = await gqlRequest<{ me: Player }>(
        `{ me { id displayName email role createdAtUtc onboardingCompletedAtUtc companies { id name cash } } }`,
      )
      player.value = data.me
    } catch {
      logout()
    } finally {
      loading.value = false
    }
  }

  function logout() {
    token.value = null
    player.value = null
    localStorage.removeItem('auth_token')
    localStorage.removeItem('auth_expires')
  }

  return {
    player,
    token,
    loading,
    error,
    isAuthenticated,
    isAdmin,
    initFromStorage,
    register,
    login,
    fetchMe,
    logout,
  }
})
