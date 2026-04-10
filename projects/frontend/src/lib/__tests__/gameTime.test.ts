import { describe, expect, it } from 'vitest'
import {
  computeGameYearFromTick,
  computeInGameTimeUtcFromTick,
  computeNextTaxTick,
  formatGameTickTime,
  formatInGameTime,
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
})
