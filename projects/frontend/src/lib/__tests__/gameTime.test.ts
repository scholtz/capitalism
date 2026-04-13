import { describe, expect, it } from 'vitest'
import {
  computeGameYearFromTick,
  computeInGameTimeUtcFromTick,
  computeNextTaxTick,
  formatGameTickTime,
  formatInGameTime,
  formatTickDuration,
} from '../gameTime'

describe('gameTime helpers', () => {
  it('formats backend in-game timestamps in UTC', () => {
    expect(formatInGameTime('2000-01-01T13:00:00.000Z', 'en')).toContain('2000')
    expect(formatInGameTime('2000-01-01T13:00:00.000Z', 'en')).toContain('13:00')
  })

  it('returns empty string for invalid timestamps', () => {
    expect(formatInGameTime('not-a-date', 'en')).toBe('')
  })

  it('computes the game year from ticks', () => {
    expect(computeGameYearFromTick(0)).toBe(2000)
    expect(computeGameYearFromTick(8759)).toBe(2000)
    expect(computeGameYearFromTick(8760)).toBe(2001)
  })

  it('computes the next tax tick on cycle boundaries', () => {
    expect(computeNextTaxTick(0, 8760)).toBe(8760)
    expect(computeNextTaxTick(8759, 8760)).toBe(8760)
    expect(computeNextTaxTick(8760, 8760)).toBe(17520)
  })

  it('converts ticks into in-game UTC timestamps', () => {
    expect(computeInGameTimeUtcFromTick(0)).toBe('2000-01-01T00:00:00.000Z')
    expect(computeInGameTimeUtcFromTick(24)).toBe('2000-01-02T00:00:00.000Z')
  })

  it('formats a game tick as in-game time', () => {
    expect(formatGameTickTime(24, 'en')).toContain('2000')
    expect(formatGameTickTime(24, 'en')).toContain('02')
  })

  describe('formatTickDuration', () => {
    it('returns "0" for zero ticks', () => {
      expect(formatTickDuration(0, 'en')).toBe('0')
    })

    it('formats hours-only durations (< 24 ticks)', () => {
      const result = formatTickDuration(10, 'en')
      expect(result).toContain('10')
      expect(result.toLowerCase()).toContain('hour')
    })

    it('formats single hour correctly', () => {
      const result = formatTickDuration(1, 'en')
      expect(result).toContain('1')
      expect(result.toLowerCase()).toContain('hour')
    })

    it('formats exact-day durations (multiple of 24)', () => {
      const result = formatTickDuration(48, 'en') // 2 days
      expect(result).toContain('2')
      expect(result.toLowerCase()).toContain('day')
      expect(result.toLowerCase()).not.toContain('hour')
    })

    it('formats days + hours durations', () => {
      const result = formatTickDuration(100, 'en') // 4 days 4 hours
      expect(result).toContain('4')
      expect(result.toLowerCase()).toContain('day')
      expect(result.toLowerCase()).toContain('hour')
    })

    it('formats large upgrade durations (1000 ticks = 41 days 16 hours)', () => {
      const result = formatTickDuration(1000, 'en')
      expect(result).toContain('41')
      expect(result.toLowerCase()).toContain('day')
    })

    it('handles negative ticks gracefully (clamps to 0)', () => {
      expect(formatTickDuration(-5, 'en')).toBe('0')
    })

    it('works with non-English locales', () => {
      const deDuration = formatTickDuration(10, 'de')
      expect(deDuration).toContain('10')
      // German should contain "Stunde" (singular) or "Stunden" (plural)
      expect(deDuration.toLowerCase()).toMatch(/stunde/)
    })
  })
})

