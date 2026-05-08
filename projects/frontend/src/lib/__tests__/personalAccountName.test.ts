import { describe, expect, it } from 'vitest'
import {
  normalizePersonalAccountName,
  PERSONAL_ACCOUNT_NAME_MAX_LENGTH,
  validatePersonalAccountName,
} from '@/lib/personalAccountName'

describe('personalAccountName helpers', () => {
  it('normalizes repeated whitespace into single spaces', () => {
    expect(normalizePersonalAccountName('  Aster   Nova   Finch  ')).toBe('Aster Nova Finch')
  })

  it('returns tooShort for names under 3 characters', () => {
    expect(validatePersonalAccountName('Al')).toBe('tooShort')
  })

  it('returns tooLong for names above 60 characters', () => {
    expect(validatePersonalAccountName(`Aster Nova ${'F'.repeat(PERSONAL_ACCOUNT_NAME_MAX_LENGTH)}`)).toBe('tooLong')
  })

  it('returns invalidCharacters for non-letter symbols', () => {
    expect(validatePersonalAccountName('Aster Nova 123')).toBe('invalidCharacters')
  })

  it('accepts valid multi-part names', () => {
    expect(validatePersonalAccountName("Aster Nova O'Connell")).toBeNull()
  })
})
