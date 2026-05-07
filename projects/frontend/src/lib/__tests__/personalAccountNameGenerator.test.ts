import { describe, expect, it } from 'vitest'
import { generatePersonalAccountName } from '@/lib/personalAccountNameGenerator'

describe('generatePersonalAccountName', () => {
  it('returns three words separated by spaces', () => {
    const value = generatePersonalAccountName()
    const parts = value.split(' ')

    expect(parts).toHaveLength(3)
    expect(parts.every((part) => part.length > 0)).toBe(true)
  })
})
