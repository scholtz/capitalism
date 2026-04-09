import { describe, expect, it } from 'vitest'
import { buildBuildingFinancialChartModel } from '@/lib/buildingFinancialChart'

describe('buildBuildingFinancialChartModel', () => {
  it('builds a combined chart model with negative profit support', () => {
    const model = buildBuildingFinancialChartModel(
      [
        { tick: 40, sales: 120, costs: 30, profit: 90 },
        { tick: 41, sales: 0, costs: 10, profit: -10 },
        { tick: 42, sales: 80, costs: 20, profit: 60 },
      ],
      560,
      180,
    )

    expect(model.hasData).toBe(true)
    expect(model.tickRange).toEqual({ start: 40, end: 42 })
    expect(model.minValue).toBeLessThan(0)
    expect(model.maxValue).toBeGreaterThan(0)
    expect(model.zeroLineY).toBeGreaterThan(0)
    expect(model.zeroLineY).toBeLessThan(180)

    const profitSeries = model.series.find((series) => series.key === 'profit')
    expect(profitSeries?.points).toHaveLength(3)
    expect(profitSeries?.points[1]?.value).toBe(-10)
    expect(profitSeries?.points[1]?.y ?? 0).toBeGreaterThan(model.zeroLineY)
  })

  it('treats an all-zero timeline as no data', () => {
    const model = buildBuildingFinancialChartModel(
      [
        { tick: 98, sales: 0, costs: 0, profit: 0 },
        { tick: 99, sales: 0, costs: 0, profit: 0 },
        { tick: 100, sales: 0, costs: 0, profit: 0 },
      ],
      560,
      180,
    )

    expect(model.hasData).toBe(false)
    expect(model.tickRange).toEqual({ start: 98, end: 100 })
    expect(model.series.every((series) => series.total === 0)).toBe(true)
  })

  it('handles cost-only data with all-negative profit', () => {
    const model = buildBuildingFinancialChartModel(
      [
        { tick: 1, sales: 0, costs: 50, profit: -50 },
        { tick: 2, sales: 0, costs: 30, profit: -30 },
      ],
      560,
      180,
    )

    expect(model.hasData).toBe(true)
    expect(model.minValue).toBeLessThan(0)
    // zeroLineY must be above the top of the chart because all values are ≤ 0
    expect(model.zeroLineY).toBeGreaterThanOrEqual(0)

    const profitSeries = model.series.find((s) => s.key === 'profit')
    expect(profitSeries?.total).toBe(-80)
    profitSeries?.points.forEach((point) => {
      expect(point.value).toBeLessThan(0)
    })

    const salesSeries = model.series.find((s) => s.key === 'sales')
    expect(salesSeries?.total).toBe(0)
  })

  it('handles revenue-only data with positive profit', () => {
    const model = buildBuildingFinancialChartModel(
      [
        { tick: 10, sales: 100, costs: 0, profit: 100 },
        { tick: 11, sales: 80, costs: 0, profit: 80 },
      ],
      560,
      180,
    )

    expect(model.hasData).toBe(true)
    expect(model.maxValue).toBeGreaterThan(0)
    expect(model.minValue).toBeGreaterThanOrEqual(0)

    const costSeries = model.series.find((s) => s.key === 'costs')
    expect(costSeries?.total).toBe(0)

    const profitSeries = model.series.find((s) => s.key === 'profit')
    expect(profitSeries?.total).toBe(180)
    profitSeries?.points.forEach((point) => {
      expect(point.value).toBeGreaterThan(0)
    })
  })

  it('handles single-tick timeline', () => {
    const model = buildBuildingFinancialChartModel(
      [{ tick: 5, sales: 200, costs: 80, profit: 120 }],
      560,
      180,
    )

    expect(model.hasData).toBe(true)
    expect(model.tickRange).toEqual({ start: 5, end: 5 })
    expect(model.ticks).toHaveLength(1)

    const profitSeries = model.series.find((s) => s.key === 'profit')
    expect(profitSeries?.points).toHaveLength(1)
    expect(profitSeries?.total).toBe(120)
  })

  it('handles empty timeline as no data', () => {
    const model = buildBuildingFinancialChartModel([], 560, 180)

    expect(model.hasData).toBe(false)
    expect(model.ticks).toHaveLength(0)
    expect(model.series.every((s) => s.total === 0)).toBe(true)
  })

  it('series totals match sum of individual point values', () => {
    const entries = [
      { tick: 1, sales: 100, costs: 40, profit: 60 },
      { tick: 2, sales: 150, costs: 70, profit: 80 },
      { tick: 3, sales: 0, costs: 20, profit: -20 },
    ]
    const model = buildBuildingFinancialChartModel(entries, 560, 180)

    for (const series of model.series) {
      const pointSum = series.points.reduce((sum, p) => sum + p.value, 0)
      expect(series.total).toBeCloseTo(pointSum, 5)
    }
  })
})
