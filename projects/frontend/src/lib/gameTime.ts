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
