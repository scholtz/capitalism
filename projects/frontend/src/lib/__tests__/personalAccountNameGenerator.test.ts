import { describe, expect, it } from 'vitest'
import { generatePersonalAccountName } from '@/lib/personalAccountNameGenerator'

describe('generatePersonalAccountName', () => {
  it('returns three words separated by spaces', () => {
    const value = generatePersonalAccountName()
    const parts = value.split(' ')

    expect(parts).toHaveLength(3)
    expect(parts.every((part) => part.length > 0)).toBe(true)
  })

  it('respects the 60 character limit', () => {
    const samples = Array.from({ length: 50 }, () => generatePersonalAccountName())
    expect(samples.every((sample) => sample.length <= 60)).toBe(true)
  })

  it('generates diverse names across calls', () => {
    const samples = Array.from({ length: 20 }, () => generatePersonalAccountName())
    expect(new Set(samples).size).toBeGreaterThanOrEqual(15)
  })
})
