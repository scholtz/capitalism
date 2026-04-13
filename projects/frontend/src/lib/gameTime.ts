export const GAME_START_YEAR = 2000
export const TICKS_PER_DAY = 24
export const DAYS_PER_YEAR = 365
export const TICKS_PER_YEAR = TICKS_PER_DAY * DAYS_PER_YEAR

export function formatInGameTime(currentGameTimeUtc: string, locale: string): string {
  const gameTime = new Date(currentGameTimeUtc)

  if (Number.isNaN(gameTime.getTime())) {
    return ''
  }

  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'UTC',
  }).format(gameTime)
}

export function computeGameYearFromTick(currentTick: number): number {
  return GAME_START_YEAR + Math.floor(Math.max(currentTick, 0) / TICKS_PER_YEAR)
}

export function computeNextTaxTick(currentTick: number, taxCycleTicks: number): number {
  const cycleTicks = taxCycleTicks > 0 ? taxCycleTicks : TICKS_PER_YEAR
  const safeTick = Math.max(currentTick, 0)
  const cyclesCompleted = Math.floor(safeTick / cycleTicks)
  const currentCycleStart = cyclesCompleted * cycleTicks

  return safeTick === currentCycleStart ? currentCycleStart + cycleTicks : (cyclesCompleted + 1) * cycleTicks
}

export function computeInGameTimeUtcFromTick(currentTick: number): string {
  const gameStart = new Date(Date.UTC(GAME_START_YEAR, 0, 1, 0, 0, 0))
  gameStart.setUTCHours(gameStart.getUTCHours() + Math.max(currentTick, 0))
  return gameStart.toISOString()
}

export function formatGameTickTime(tick: number, locale: string): string {
  return formatInGameTime(computeInGameTimeUtcFromTick(tick), locale)
}

/**
 * Converts a tick count (duration) to a human-readable in-game time span.
 *
 * Since 1 tick = 1 in-game hour and TICKS_PER_DAY = 24:
 *   - 1–23 ticks → "X hours"
 *   - 24+ ticks  → "X days" or "X days Y hours"
 *
 * The raw tick count is intentionally NOT included here; callers should
 * place it in a `title` attribute for debugging.
 */
export function formatTickDuration(ticks: number, locale: string): string {
  const safeTicks = Math.max(0, Math.round(ticks))
  if (safeTicks === 0) return '0'

  const days = Math.floor(safeTicks / TICKS_PER_DAY)
  const hours = safeTicks % TICKS_PER_DAY

  const rtf = new Intl.RelativeTimeFormat(locale, { style: 'long', numeric: 'always' })

  /**
   * Formats a single duration unit (e.g. "5 hours") by extracting everything
   * from the first integer part onward — this reliably strips direction prefixes
   * like "in " (en/de) or "o " (sk) regardless of locale.
   */
  function formatUnit(count: number, unit: Intl.RelativeTimeFormatUnit): string {
    const parts = rtf.formatToParts(count, unit) // positive = future "in N X"
    const intIdx = parts.findIndex((p) => p.type === 'integer')
    if (intIdx === -1) return `${count} ${unit}`
    return parts
      .slice(intIdx)
      .map((p) => p.value)
      .join('')
      .trim()
  }

  if (days === 0) return formatUnit(hours, 'hour')
  if (hours === 0) return formatUnit(days, 'day')
  return `${formatUnit(days, 'day')} ${formatUnit(hours, 'hour')}`
}
