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
})
