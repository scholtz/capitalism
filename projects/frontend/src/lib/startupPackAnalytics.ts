export type StartupPackAnalyticsEvent =
  | 'view'
  | 'dismiss'
  | 'claim_click'
  | 'claim_success'
  | 'claim_error'

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
