<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { buildBuildingFinancialChartModel, type BuildingFinancialChartTick, type BuildingFinancialSeriesKey } from '@/lib/buildingFinancialChart'
import type { BuildingFinancialTickSnapshot } from '@/types'

const props = defineProps<{
  timeline: BuildingFinancialTickSnapshot[]
}>()

const { t } = useI18n()

const plotWidth = 560
const plotHeight = 180
const gridLineRatios = [0.25, 0.5, 0.75]

const seriesDisplay: Record<BuildingFinancialSeriesKey, { color: string; labelKey: string }> = {
  sales: {
    color: '#2563eb',
    labelKey: 'buildingDetail.overview.sales',
  },
  costs: {
    color: '#dc2626',
    labelKey: 'buildingDetail.overview.costs',
  },
  profit: {
    color: '#059669',
    labelKey: 'buildingDetail.overview.profit',
  },
}

const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  maximumFractionDigits: 0,
})

const chartModel = computed(() => buildBuildingFinancialChartModel(props.timeline, plotWidth, plotHeight))
const activeTickIndex = ref<number | null>(null)

const activeTick = computed<BuildingFinancialChartTick | null>(() => {
  const index = activeTickIndex.value
  if (index == null) return null
  return chartModel.value.ticks[index] ?? null
})

watch(
  () => props.timeline,
  (timeline) => {
    activeTickIndex.value = timeline.length > 0 ? timeline.length - 1 : null
  },
  { immediate: true },
)

function formatCurrency(value: number): string {
  return currencyFormatter.format(value)
}

function setActiveTick(index: number) {
  activeTickIndex.value = index
}

function resetActiveTick() {
  activeTickIndex.value = chartModel.value.ticks.length > 0 ? chartModel.value.ticks.length - 1 : null
}

function getMetricValue(key: BuildingFinancialSeriesKey): number {
  if (!activeTick.value) return 0
  return activeTick.value[key]
}

function getTickTitle(tick: BuildingFinancialChartTick): string {
  return `${t('buildingDetail.overview.tickLabel', { tick: tick.tick })} · ${t('buildingDetail.overview.sales')}: ${formatCurrency(tick.sales)} · ${t('buildingDetail.overview.costs')}: ${formatCurrency(tick.costs)} · ${t('buildingDetail.overview.profit')}: ${formatCurrency(tick.profit)}`
}
</script>

<template>
  <div class="building-financial-chart-card">
    <template v-if="chartModel.hasData">
      <div class="building-financial-chart-header">
        <div>
          <span class="building-financial-chart-caption">{{ t('buildingDetail.overview.chartTitle') }}</span>
          <strong v-if="activeTick" class="building-financial-active-tick">{{ t('buildingDetail.overview.tickLabel', { tick: activeTick.tick }) }}</strong>
        </div>
        <p class="config-help">{{ t('buildingDetail.overview.hoverHint') }}</p>
      </div>

      <div class="building-financial-chart-detail-grid">
        <div v-for="series in chartModel.series" :key="series.key" class="building-financial-chart-detail-stat">
          <span class="building-financial-chart-detail-label">{{ t(seriesDisplay[series.key].labelKey) }}</span>
          <strong>{{ formatCurrency(getMetricValue(series.key)) }}</strong>
        </div>
      </div>

      <div class="building-financial-chart-shell">
        <svg class="building-financial-chart" :viewBox="`0 0 ${plotWidth} ${plotHeight}`" role="img" :aria-label="t('buildingDetail.overview.chartAriaLabel')">
          <line
            v-for="ratio in gridLineRatios"
            :key="ratio"
            class="building-financial-chart-grid-line"
            x1="0"
            :y1="plotHeight - plotHeight * ratio"
            :x2="plotWidth"
            :y2="plotHeight - plotHeight * ratio"
          />
          <line class="building-financial-chart-grid-line building-financial-chart-baseline" x1="0" :y1="chartModel.zeroLineY" :x2="plotWidth" :y2="chartModel.zeroLineY" />

          <g v-for="series in chartModel.series" :key="series.key">
            <polyline class="building-financial-chart-line" fill="none" stroke-linecap="round" stroke-linejoin="round" :stroke="seriesDisplay[series.key].color" :points="series.polylinePoints" />
            <circle
              v-for="point in series.points"
              :key="`${series.key}-${point.tick}`"
              class="building-financial-chart-point"
              :class="{ active: point.tick === activeTick?.tick }"
              :cx="point.x"
              :cy="point.y"
              :r="point.tick === activeTick?.tick ? 5 : 4"
              :fill="seriesDisplay[series.key].color"
            >
              <title>{{ `${t(seriesDisplay[series.key].labelKey)} · ${t('buildingDetail.overview.tickLabel', { tick: point.tick })} · ${formatCurrency(point.value)}` }}</title>
            </circle>
          </g>
        </svg>

        <div class="building-financial-chart-hit-grid" :style="{ gridTemplateColumns: `repeat(${Math.max(chartModel.ticks.length, 1)}, minmax(0, 1fr))` }">
          <button
            v-for="(tick, index) in chartModel.ticks"
            :key="`tick-hit-${tick.tick}`"
            type="button"
            class="building-financial-hit-area"
            :class="{ active: activeTick?.tick === tick.tick }"
            :aria-label="getTickTitle(tick)"
            @mouseenter="setActiveTick(index)"
            @focus="setActiveTick(index)"
            @mouseleave="resetActiveTick"
            @blur="resetActiveTick"
          ></button>
        </div>
      </div>

      <div class="building-financial-chart-legend">
        <div v-for="series in chartModel.series" :key="`legend-${series.key}`" class="building-financial-chart-legend-item">
          <span class="building-financial-chart-legend-swatch" :style="{ background: seriesDisplay[series.key].color }"></span>
          <span>{{ t(seriesDisplay[series.key].labelKey) }}</span>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.building-financial-chart-card {
  display: grid;
  gap: 0.85rem;
}

.building-financial-chart-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.building-financial-chart-caption {
  display: block;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
  margin-bottom: 0.2rem;
}

.building-financial-chart-detail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
  gap: 0.6rem;
}

.building-financial-chart-detail-stat {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-bg);
  padding: 0.6rem 0.75rem;
}

.building-financial-chart-detail-label {
  display: block;
  margin-bottom: 0.2rem;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.building-financial-chart-shell {
  position: relative;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: linear-gradient(180deg, color-mix(in srgb, var(--color-surface-muted) 84%, white 16%), var(--color-surface));
  padding: 0.85rem;
  overflow: hidden;
}

.building-financial-chart {
  width: 100%;
  height: auto;
  display: block;
}

.building-financial-chart-grid-line {
  stroke: color-mix(in srgb, var(--color-border) 80%, transparent 20%);
  stroke-width: 1;
  stroke-dasharray: 4 6;
}

.building-financial-chart-baseline {
  stroke-dasharray: none;
}

.building-financial-chart-line {
  stroke-width: 3;
}

.building-financial-chart-point {
  stroke: var(--color-surface);
  stroke-width: 2;
  opacity: 0.8;
}

.building-financial-chart-point.active {
  opacity: 1;
}

.building-financial-chart-hit-grid {
  position: absolute;
  inset: 0.85rem;
  display: grid;
}

.building-financial-hit-area {
  border: 0;
  background: transparent;
  cursor: crosshair;
  padding: 0;
  margin: 0;
}

.building-financial-hit-area.active,
.building-financial-hit-area:focus-visible,
.building-financial-hit-area:hover {
  background: color-mix(in srgb, var(--color-primary, #2563eb) 10%, transparent);
  outline: none;
}

.building-financial-chart-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1rem;
}

.building-financial-chart-legend-item {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  color: var(--color-text-secondary);
  font-size: 0.9rem;
}

.building-financial-chart-legend-swatch {
  width: 0.75rem;
  height: 0.75rem;
  border-radius: 999px;
}

@media (max-width: 640px) {
  .building-financial-chart-header {
    flex-direction: column;
  }
}
</style>
