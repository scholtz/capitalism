export const PERSONAL_ACCOUNT_NAME_MIN_LENGTH = 3
export const PERSONAL_ACCOUNT_NAME_MAX_LENGTH = 60

export type PersonalAccountNameValidationCode =
  | 'required'
  | 'tooShort'
  | 'tooLong'
  | 'invalidCharacters'

export function normalizePersonalAccountName(personalAccountName: string) {
  return personalAccountName.trim().replace(/\s+/g, ' ')
}

export function validatePersonalAccountName(
  personalAccountName: string,
): PersonalAccountNameValidationCode | null {
  const normalized = normalizePersonalAccountName(personalAccountName)

  if (!normalized) {
    return 'required'
  }

  if (normalized.length < PERSONAL_ACCOUNT_NAME_MIN_LENGTH) {
    return 'tooShort'
  }

  if (normalized.length > PERSONAL_ACCOUNT_NAME_MAX_LENGTH) {
    return 'tooLong'
  }

  if (!/^[\p{L}\s'-]+$/u.test(normalized)) {
    return 'invalidCharacters'
  }

  return null
}
