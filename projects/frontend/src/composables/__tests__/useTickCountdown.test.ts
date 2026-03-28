/**
 * Unit tests for the computeCountdownTimeStr pure helper exported by useTickCountdown.
 *
 * The composable wraps this helper with Vue reactivity and vue-i18n.
 * These tests cover the time-formatting math directly, without needing
 * a Vue app instance or i18n context.
 */

import { describe, expect, it } from 'vitest'
import { computeCountdownTimeStr } from '../useTickCountdown'

describe('computeCountdownTimeStr', () => {
  describe('returns null (tick-soon) cases', () => {
    it('returns null when remainingMs is exactly 0', () => {
      expect(computeCountdownTimeStr(0)).toBeNull()
    })

    it('returns null when remainingMs is negative (tick already fired)', () => {
      expect(computeCountdownTimeStr(-1)).toBeNull()
      expect(computeCountdownTimeStr(-5000)).toBeNull()
    })
  })

  describe('sub-minute countdown (seconds only)', () => {
    it('formats 1 ms as "1s"', () => {
      expect(computeCountdownTimeStr(1)).toBe('1s')
    })

    it('formats exactly 1 second', () => {
      expect(computeCountdownTimeStr(1000)).toBe('1s')
    })

    it('formats 9 seconds', () => {
      expect(computeCountdownTimeStr(9000)).toBe('9s')
    })

    it('formats 30 seconds', () => {
      expect(computeCountdownTimeStr(30000)).toBe('30s')
    })

    it('formats 59 seconds', () => {
      expect(computeCountdownTimeStr(59000)).toBe('59s')
    })

    it('rounds up a partial second (501 ms → "1s")', () => {
      expect(computeCountdownTimeStr(501)).toBe('1s')
    })

    it('rounds up exactly half a second (500 ms → "1s")', () => {
      expect(computeCountdownTimeStr(500)).toBe('1s')
    })

    it('1 ms over zero rounds to "1s"', () => {
      expect(computeCountdownTimeStr(1)).toBe('1s')
    })
  })

  describe('multi-minute countdown', () => {
    it('formats exactly 1 minute as "1m 00s"', () => {
      expect(computeCountdownTimeStr(60000)).toBe('1m 00s')
    })

    it('formats 1 minute 5 seconds as "1m 05s" (zero-padded)', () => {
      expect(computeCountdownTimeStr(65000)).toBe('1m 05s')
    })

    it('formats 1 minute 30 seconds as "1m 30s"', () => {
      expect(computeCountdownTimeStr(90000)).toBe('1m 30s')
    })

    it('formats 2 minutes exactly as "2m 00s"', () => {
      expect(computeCountdownTimeStr(120000)).toBe('2m 00s')
    })

    it('formats 5 minutes 59 seconds as "5m 59s"', () => {
      expect(computeCountdownTimeStr(359000)).toBe('5m 59s')
    })

    it('formats 10 minutes as "10m 00s"', () => {
      expect(computeCountdownTimeStr(600000)).toBe('10m 00s')
    })

    it('zero-pads single-digit seconds in multi-minute format', () => {
      // 2 min 3 sec → "2m 03s"
      expect(computeCountdownTimeStr(123000)).toBe('2m 03s')
    })
  })

  describe('boundary between formats', () => {
    it('59 999 ms rounds up to 60s and displays as "1m 00s"', () => {
      // Math.ceil(59999 / 1000) = 60 → 1 minute, 0 seconds
      expect(computeCountdownTimeStr(59999)).toBe('1m 00s')
    })

    it('59 001 ms rounds up to 60s and displays as "1m 00s"', () => {
      expect(computeCountdownTimeStr(59001)).toBe('1m 00s')
    })

    it('59 000 ms (exactly 59 s) displays as "59s"', () => {
      expect(computeCountdownTimeStr(59000)).toBe('59s')
    })
  })
})
