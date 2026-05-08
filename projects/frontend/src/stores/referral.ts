import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const REFERRAL_STORAGE_KEY = 'referral_code'

export const useReferralStore = defineStore('referral', () => {
  const code = ref<string | null>(null)
  const applied = ref(false)

  function init() {
    const stored = localStorage.getItem(REFERRAL_STORAGE_KEY)
    if (stored) {
      code.value = stored
    }
  }

  function setCode(newCode: string) {
    const trimmed = newCode.trim()
    if (!trimmed) return
    code.value = trimmed
    localStorage.setItem(REFERRAL_STORAGE_KEY, trimmed)
  }

  /** Capture referral code from URL search params (e.g. ?ref=CODE). */
  function captureFromUrl(search: string) {
    const params = new URLSearchParams(search)
    const ref = params.get('ref')
    if (ref) {
      setCode(ref)
    }
  }

  /** Mark the referral code as applied and remove it from local state. */
  function markApplied() {
    applied.value = true
    code.value = null
    localStorage.removeItem(REFERRAL_STORAGE_KEY)
  }

  /** Dismiss the post-login applied banner. */
  function dismissAppliedBanner() {
    applied.value = false
  }

  const hasCode = computed(() => !!code.value)

  return {
    code,
    applied,
    hasCode,
    init,
    setCode,
    captureFromUrl,
    markApplied,
    dismissAppliedBanner,
  }
})
