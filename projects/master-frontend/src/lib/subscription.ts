import type { SubscriptionInfo } from './masterApi'

/**
 * Returns a human-readable label for the subscription tier.
 */
export function formatTierLabel(tier: SubscriptionInfo['tier']): string {
  switch (tier) {
    case 'PRO':
      return 'Pro'
    default:
      return 'Free'
  }
}

/**
 * Returns a human-readable label for the subscription status.
 */
export function formatStatusLabel(sub: SubscriptionInfo): string {
  if (!sub.isActive && sub.status === 'NONE') return 'No active subscription'
  if (sub.status === 'EXPIRED') return 'Expired'
  if (sub.isActive) return 'Active'
  return 'Inactive'
}

/**
 * Returns a description of days remaining or when the subscription expires.
 */
export function formatRenewalNote(sub: SubscriptionInfo): string {
  if (!sub.isActive || sub.expiresAtUtc === null) return ''
  const days = sub.daysRemaining ?? 0
  if (days <= 0) return 'Expires today'
  if (days === 1) return 'Expires tomorrow'
  if (days <= 30) return `Expires in ${days} days`
  const expiry = new Date(sub.expiresAtUtc)
  const locale = typeof navigator !== 'undefined' ? navigator.language : 'en-US'
  return `Renews on ${expiry.toLocaleDateString(locale, { year: 'numeric', month: 'long', day: 'numeric' })}`
}

/**
 * Returns the primary CTA label for a subscription state.
 */
export function formatProlongLabel(sub: SubscriptionInfo): string {
  if (!sub.isActive || sub.status === 'EXPIRED') return 'Subscribe to Pro'
  const days = sub.daysRemaining ?? 0
  if (days <= 30) return 'Extend subscription'
  return 'Prolong subscription'
}
