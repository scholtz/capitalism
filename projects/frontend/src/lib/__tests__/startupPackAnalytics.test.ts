import { describe, it, expect } from 'vitest'
import { trackStartupPackEvent } from '../startupPackAnalytics'

describe('trackStartupPackEvent', () => {
  it('returns without throwing when window is not defined (SSR/node)', () => {
    // In the node test environment window is undefined; the function must be a no-op.
    expect(() => trackStartupPackEvent('view')).not.toThrow()
    expect(() => trackStartupPackEvent('dismiss')).not.toThrow()
    expect(() => trackStartupPackEvent('claim_click', { context: 'onboarding' })).not.toThrow()
    expect(() => trackStartupPackEvent('claim_success', { context: 'dashboard' })).not.toThrow()
    expect(() => trackStartupPackEvent('claim_error', { context: 'onboarding' })).not.toThrow()
  })

  it('accepts all defined event names without throwing', () => {
    const events: Parameters<typeof trackStartupPackEvent>[0][] = [
      'view',
      'dismiss',
      'claim_click',
      'claim_success',
      'claim_error',
    ]
    for (const eventName of events) {
      expect(() => trackStartupPackEvent(eventName)).not.toThrow()
    }
  })

  it('accepts an empty detail object without throwing', () => {
    expect(() => trackStartupPackEvent('view', {})).not.toThrow()
  })

  it('accepts extra detail fields without throwing', () => {
    expect(() =>
      trackStartupPackEvent('view', {
        context: 'onboarding',
        status: 'ELIGIBLE',
        offerKey: 'STARTUP_PACK_V1',
      }),
    ).not.toThrow()
  })
})
