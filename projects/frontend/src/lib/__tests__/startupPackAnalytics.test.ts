import { describe, it, expect } from 'vitest'
import { trackStartupPackEvent, emitStartupPackViewEvents } from '../startupPackAnalytics'

describe('trackStartupPackEvent', () => {
  it('returns without throwing when window is not defined (SSR/node)', () => {
    // In the node test environment window is undefined; the function must be a no-op.
    expect(() => trackStartupPackEvent('view')).not.toThrow()
    expect(() => trackStartupPackEvent('countdown_active')).not.toThrow()
    expect(() => trackStartupPackEvent('dismiss')).not.toThrow()
    expect(() => trackStartupPackEvent('continue')).not.toThrow()
    expect(() => trackStartupPackEvent('claim_click', { context: 'onboarding' })).not.toThrow()
    expect(() => trackStartupPackEvent('claim_success', { context: 'dashboard' })).not.toThrow()
    expect(() => trackStartupPackEvent('claim_error', { context: 'onboarding' })).not.toThrow()
    expect(() => trackStartupPackEvent('offer_expired')).not.toThrow()
  })

  it('accepts all defined event names without throwing', () => {
    const events: Parameters<typeof trackStartupPackEvent>[0][] = [
      'view',
      'countdown_active',
      'dismiss',
      'continue',
      'claim_click',
      'claim_success',
      'claim_error',
      'offer_expired',
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

  it('countdown_active accepts expiresAtUtc detail field', () => {
    expect(() =>
      trackStartupPackEvent('countdown_active', {
        context: 'onboarding',
        expiresAtUtc: new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString(),
      }),
    ).not.toThrow()
  })

  it('offer_expired accepts context detail field', () => {
    expect(() => trackStartupPackEvent('offer_expired', { context: 'dashboard' })).not.toThrow()
    expect(() => trackStartupPackEvent('offer_expired', { context: 'onboarding' })).not.toThrow()
  })

  it('continue accepts context detail field', () => {
    expect(() => trackStartupPackEvent('continue', { context: 'onboarding' })).not.toThrow()
  })
})

describe('emitStartupPackViewEvents', () => {
  const futureExpiry = new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString()
  const pastExpiry = new Date(Date.now() - 1000).toISOString()

  it('does not throw for ELIGIBLE status', () => {
    expect(() =>
      emitStartupPackViewEvents({ status: 'ELIGIBLE', expiresAtUtc: futureExpiry }, 'onboarding'),
    ).not.toThrow()
  })

  it('does not throw for SHOWN status', () => {
    expect(() =>
      emitStartupPackViewEvents({ status: 'SHOWN', expiresAtUtc: futureExpiry }, 'dashboard'),
    ).not.toThrow()
  })

  it('does not throw for DISMISSED status', () => {
    expect(() =>
      emitStartupPackViewEvents({ status: 'DISMISSED', expiresAtUtc: futureExpiry }, 'dashboard'),
    ).not.toThrow()
  })

  it('does not throw for EXPIRED status', () => {
    expect(() =>
      emitStartupPackViewEvents({ status: 'EXPIRED', expiresAtUtc: pastExpiry }, 'dashboard'),
    ).not.toThrow()
  })

  it('does not throw for CLAIMED status', () => {
    expect(() =>
      emitStartupPackViewEvents({ status: 'CLAIMED', expiresAtUtc: pastExpiry }, 'onboarding'),
    ).not.toThrow()
  })
})
