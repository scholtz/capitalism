import type { BuildingFinancialTickSnapshot } from '@/types'

export type BuildingFinancialSeriesKey = 'sales' | 'costs' | 'profit'

export type BuildingFinancialChartPoint = {
  x: number
  y: number
  tick: number
  value: number
}

export type BuildingFinancialChartTick = {
  tick: number
  x: number
  sales: number
  costs: number
  profit: number
}

export type BuildingFinancialChartSeries = {
  key: BuildingFinancialSeriesKey
  total: number
  latestValue: number
  polylinePoints: string
  points: BuildingFinancialChartPoint[]
}

export type BuildingFinancialChartModel = {
  hasData: boolean
  minValue: number
  maxValue: number
  zeroLineY: number
  tickRange: { start: number | null; end: number | null }
  ticks: BuildingFinancialChartTick[]
  series: BuildingFinancialChartSeries[]
}

const seriesKeys: BuildingFinancialSeriesKey[] = ['sales', 'costs', 'profit']

function normalizeMetric(value: number | null | undefined): number {
  return Number.isFinite(value) ? Number(value ?? 0) : 0
}

function roundMetric(value: number): number {
  return Number(value.toFixed(4))
}

function getScaleCeiling(value: number): number {
  if (value <= 0) {
    return 1
  }

  const magnitude = 10 ** Math.floor(Math.log10(value))
  const normalized = value / magnitude

  if (normalized <= 1) return magnitude
  if (normalized <= 2) return 2 * magnitude
  if (normalized <= 5) return 5 * magnitude
  return 10 * magnitude
}

function getScaleBounds(entries: BuildingFinancialTickSnapshot[]): { minValue: number; maxValue: number } {
  const values = entries.flatMap((entry) => [normalizeMetric(entry.sales), normalizeMetric(entry.costs), normalizeMetric(entry.profit)])

  const minObservedValue = values.reduce((currentMin, value) => Math.min(currentMin, value), 0)
  const maxObservedValue = values.reduce((currentMax, value) => Math.max(currentMax, value), 0)

  if (minObservedValue >= 0) {
    return {
      minValue: 0,
      maxValue: getScaleCeiling(maxObservedValue),
    }
  }

  const bound = getScaleCeiling(Math.max(Math.abs(minObservedValue), Math.abs(maxObservedValue)))
  return {
    minValue: -bound,
    maxValue: bound,
  }
}

function getYCoordinate(value: number, minValue: number, maxValue: number, height: number): number {
  if (maxValue <= minValue) {
    return height
  }

  const ratio = (value - minValue) / (maxValue - minValue)
  return roundMetric(height - ratio * height)
}

export function buildBuildingFinancialChartModel(entries: BuildingFinancialTickSnapshot[], width: number, height: number): BuildingFinancialChartModel {
  const sortedEntries = [...entries].sort((left, right) => left.tick - right.tick)
  const hasData = sortedEntries.some((entry) => entry.sales > 0 || entry.costs > 0 || entry.profit !== 0)
  const { minValue, maxValue } = getScaleBounds(sortedEntries)
  const stepX = sortedEntries.length > 1 ? width / (sortedEntries.length - 1) : 0

  const ticks = sortedEntries.map((entry, index) => ({
    tick: entry.tick,
    x: roundMetric(sortedEntries.length === 1 ? width / 2 : stepX * index),
    sales: roundMetric(normalizeMetric(entry.sales)),
    costs: roundMetric(normalizeMetric(entry.costs)),
    profit: roundMetric(normalizeMetric(entry.profit)),
  }))

  const series = seriesKeys.map((key) => {
    const points = ticks.map((entry) => ({
      x: entry.x,
      y: getYCoordinate(entry[key], minValue, maxValue, height),
      tick: entry.tick,
      value: entry[key],
    }))
    const lastPoint = points.length > 0 ? points[points.length - 1] : null

    return {
      key,
      total: roundMetric(points.reduce((sum, point) => sum + point.value, 0)),
      latestValue: lastPoint?.value ?? 0,
      polylinePoints: points.map((point) => `${point.x},${point.y}`).join(' '),
      points,
    }
  })

  return {
    hasData,
    minValue,
    maxValue,
    zeroLineY: getYCoordinate(0, minValue, maxValue, height),
    tickRange: {
      start: sortedEntries[0]?.tick ?? null,
      end: sortedEntries.length > 0 ? sortedEntries[sortedEntries.length - 1]!.tick : null,
    },
    ticks,
    series,
  }
}
