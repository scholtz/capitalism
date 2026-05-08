import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useReferralStore } from '@/stores/referral'

const localStorageMock = (() => {
  let store: Record<string, string> = {}
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value
    },
    removeItem: (key: string) => {
      delete store[key]
    },
    clear: () => {
      store = {}
    },
  }
})()

vi.stubGlobal('localStorage', localStorageMock)

describe('useReferralStore', () => {
  beforeEach(() => {
    localStorageMock.clear()
    setActivePinia(createPinia())
  })

  it('starts with no code', () => {
    const store = useReferralStore()
    expect(store.code).toBeNull()
    expect(store.hasCode).toBe(false)
  })

  it('init loads code from localStorage', () => {
    localStorageMock.setItem('referral_code', 'MYREF123')
    const store = useReferralStore()
    store.init()
    expect(store.code).toBe('MYREF123')
    expect(store.hasCode).toBe(true)
  })

  it('captureFromUrl picks up ?ref= query param', () => {
    const store = useReferralStore()
    store.captureFromUrl('?ref=WELCOMEREF')
    expect(store.code).toBe('WELCOMEREF')
    expect(localStorageMock.getItem('referral_code')).toBe('WELCOMEREF')
  })

  it('captureFromUrl ignores empty ref param', () => {
    const store = useReferralStore()
    store.captureFromUrl('?ref=')
    expect(store.code).toBeNull()
  })

  it('captureFromUrl ignores URLs without ref param', () => {
    const store = useReferralStore()
    store.captureFromUrl('?utm_source=email')
    expect(store.code).toBeNull()
  })

  it('setCode stores the code', () => {
    const store = useReferralStore()
    store.setCode('REF456')
    expect(store.code).toBe('REF456')
    expect(localStorageMock.getItem('referral_code')).toBe('REF456')
  })

  it('setCode trims whitespace', () => {
    const store = useReferralStore()
    store.setCode('  REF789  ')
    expect(store.code).toBe('REF789')
  })

  it('markApplied sets applied=true and clears code', () => {
    const store = useReferralStore()
    store.setCode('APPCODE')
    store.markApplied()
    expect(store.applied).toBe(true)
    expect(store.code).toBeNull()
    expect(store.hasCode).toBe(false)
    expect(localStorageMock.getItem('referral_code')).toBeNull()
  })
})
