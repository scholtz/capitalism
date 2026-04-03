import { describe, expect, it } from 'vitest'

import { formatProlongLabel, formatRenewalNote, formatStatusLabel, formatTierLabel } from '../subscription'
import type { SubscriptionInfo } from '../masterApi'

const makeActiveSub = (daysRemaining: number, expiresAtUtc: string): SubscriptionInfo => ({
  tier: 'PRO',
  status: 'ACTIVE',
  isActive: true,
  daysRemaining,
  canProlong: true,
  expiresAtUtc,
  startsAtUtc: '2026-01-01T00:00:00.000Z',
})

const noSub: SubscriptionInfo = {
  tier: 'FREE',
  status: 'NONE',
  isActive: false,
  daysRemaining: null,
  canProlong: true,
  expiresAtUtc: null,
  startsAtUtc: null,
}

const expiredSub: SubscriptionInfo = {
  tier: 'PRO',
  status: 'EXPIRED',
  isActive: false,
  daysRemaining: 0,
  canProlong: true,
  expiresAtUtc: '2026-01-01T00:00:00.000Z',
  startsAtUtc: '2025-01-01T00:00:00.000Z',
}

describe('formatTierLabel', () => {
  it('returns Pro for PRO tier', () => {
    expect(formatTierLabel('PRO')).toBe('Pro')
  })

  it('returns Free for FREE tier', () => {
    expect(formatTierLabel('FREE')).toBe('Free')
  })
})

describe('formatStatusLabel', () => {
  it('returns no subscription for NONE status', () => {
    expect(formatStatusLabel(noSub)).toBe('No active subscription')
  })

  it('returns Active for active subscription', () => {
    expect(formatStatusLabel(makeActiveSub(30, '2026-05-01T00:00:00.000Z'))).toBe('Active')
  })

  it('returns Expired for expired subscription', () => {
    expect(formatStatusLabel(expiredSub)).toBe('Expired')
  })
})

describe('formatRenewalNote', () => {
  it('returns empty string for no subscription', () => {
    expect(formatRenewalNote(noSub)).toBe('')
  })

  it('returns "Expires today" when daysRemaining is 0', () => {
    expect(formatRenewalNote({ ...makeActiveSub(0, '2026-04-03T23:59:59.000Z') })).toBe(
      'Expires today',
    )
  })

  it('returns "Expires tomorrow" when daysRemaining is 1', () => {
    expect(formatRenewalNote(makeActiveSub(1, '2026-04-04T00:00:00.000Z'))).toBe(
      'Expires tomorrow',
    )
  })

  it('returns "Expires in N days" for ≤30 days', () => {
    expect(formatRenewalNote(makeActiveSub(15, '2026-04-18T00:00:00.000Z'))).toBe(
      'Expires in 15 days',
    )
  })

  it('returns formatted date for >30 days', () => {
    const note = formatRenewalNote(makeActiveSub(90, '2026-07-01T00:00:00.000Z'))
    expect(note).toContain('July')
    expect(note).toContain('2026')
  })
})

describe('formatProlongLabel', () => {
  it('returns Subscribe to Pro when no subscription', () => {
    expect(formatProlongLabel(noSub)).toBe('Subscribe to Pro')
  })

  it('returns Subscribe to Pro when expired', () => {
    expect(formatProlongLabel(expiredSub)).toBe('Subscribe to Pro')
  })

  it('returns Extend subscription when expiring soon (≤30 days)', () => {
    expect(formatProlongLabel(makeActiveSub(20, '2026-04-23T00:00:00.000Z'))).toBe(
      'Extend subscription',
    )
  })

  it('returns Prolong subscription when more than 30 days remaining', () => {
    expect(formatProlongLabel(makeActiveSub(60, '2026-06-02T00:00:00.000Z'))).toBe(
      'Prolong subscription',
    )
  })
})
