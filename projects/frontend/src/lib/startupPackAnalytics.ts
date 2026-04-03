export type StartupPackAnalyticsEvent =
  | 'view'
  | 'countdown_active'
  | 'dismiss'
  | 'continue'
  | 'claim_click'
  | 'claim_success'
  | 'claim_error'
  | 'offer_expired'

/**
 * Lightweight analytics hook for startup-pack interactions.
 * Dispatches a browser event so future instrumentation can subscribe without
 * coupling this flow to a specific analytics vendor.
 */
export function trackStartupPackEvent(
  eventName: StartupPackAnalyticsEvent,
  detail: Record<string, unknown> = {},
): void {
  if (typeof window === 'undefined') {
    return
  }

  window.dispatchEvent(
    new CustomEvent('capitalism:startup-pack', {
      detail: {
        eventName,
        ...detail,
      },
    }),
  )
}

/** Active offer statuses that have a running countdown timer. */
const ACTIVE_STATUSES = ['ELIGIBLE', 'SHOWN', 'DISMISSED']

/**
 * Emits `view` followed by the appropriate secondary event for the current offer state:
 * - `countdown_active` when the offer is claimable and the countdown is running
 * - `offer_expired` when the offer window has closed
 *
 * Use this in place of a bare `trackStartupPackEvent('view', ...)` call whenever the
 * view and countdown/expiry side-effects should be emitted together (e.g. on mount or
 * after fetching the offer state).
 */
export function emitStartupPackViewEvents(
  offer: { status: string; expiresAtUtc: string },
  context: string,
): void {
  trackStartupPackEvent('view', { context, status: offer.status })
  if (ACTIVE_STATUSES.includes(offer.status)) {
    trackStartupPackEvent('countdown_active', { context, expiresAtUtc: offer.expiresAtUtc })
  } else if (offer.status === 'EXPIRED') {
    trackStartupPackEvent('offer_expired', { context })
  }
}
