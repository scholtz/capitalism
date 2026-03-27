import { describe, expect, it } from 'vitest'
import { getProductAccessState, isProductLocked } from '../productAccess'

describe('productAccess', () => {
  it('treats free catalog products as free', () => {
    expect(getProductAccessState({ isProOnly: false, isUnlockedForCurrentPlayer: true })).toBe('free')
    expect(isProductLocked({ isProOnly: false, isUnlockedForCurrentPlayer: false })).toBe(false)
  })

  it('marks locked pro products as unavailable', () => {
    expect(getProductAccessState({ isProOnly: true, isUnlockedForCurrentPlayer: false })).toBe('pro-locked')
    expect(isProductLocked({ isProOnly: true, isUnlockedForCurrentPlayer: false })).toBe(true)
  })

  it('marks unlocked pro products as available', () => {
    expect(getProductAccessState({ isProOnly: true, isUnlockedForCurrentPlayer: true })).toBe('pro-unlocked')
    expect(isProductLocked({ isProOnly: true, isUnlockedForCurrentPlayer: true })).toBe(false)
  })
})
