<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { BuildingUnit, BuildingUnitOperationalStatus } from '@/types'

interface Props {
  units: BuildingUnit[]
  /** Optional per-unit operational statuses fetched from buildingUnitOperationalStatuses query. */
  statuses?: BuildingUnitOperationalStatus[]
}

const props = defineProps<Props>()
const { t } = useI18n()

const UNIT_TYPE_ICONS: Record<string, string> = {
  PURCHASE: '🛒',
  MINING: '⛏️',
  MANUFACTURING: '⚙️',
  STORAGE: '📦',
  B2B_SALES: '🤝',
  PUBLIC_SALES: '🏷️',
  BRANDING: '🎨',
  MARKETING: '📢',
  PRODUCT_QUALITY: '🔬',
  BRAND_QUALITY: '⭐',
}

/** Sort units left to right (by gridX) to form the visual chain. */
const chainUnits = computed<BuildingUnit[]>(() => {
  if (props.units.length === 0) return []
  return [...props.units].sort((a, b) => a.gridX - b.gridX || a.gridY - b.gridY)
})

const statusMap = computed<Record<string, BuildingUnitOperationalStatus>>(() => {
  const map: Record<string, BuildingUnitOperationalStatus> = {}
  for (const s of props.statuses ?? []) {
    map[s.buildingUnitId] = s
  }
  return map
})

function unitIcon(unitType: string): string {
  return UNIT_TYPE_ICONS[unitType] ?? '🔲'
}

function unitLabel(unitType: string): string {
  const key = `supplyChain.unitTypes.${unitType}` as Parameters<typeof t>[0]
  return t(key)
}

function unitStatusClass(unitId: string): string {
  const s = statusMap.value[unitId]
  if (!s) return ''
  return `unit-node--${s.status.toLowerCase()}`
}

function unitStatusBadge(unitId: string): string {
  const s = statusMap.value[unitId]
  if (!s || s.status === 'ACTIVE') return ''
  const badges: Record<string, string> = {
    IDLE: '💤',
    BLOCKED: '⛔',
    FULL: '📊',
    UNCONFIGURED: '❓',
  }
  return badges[s.status] ?? ''
}

function unitStatusTitle(unitId: string): string {
  const s = statusMap.value[unitId]
  if (!s) return ''
  if (s.blockedReason) return s.blockedReason
  return s.status
}
</script>

<template>
  <div class="supply-chain-panel" :aria-label="t('supplyChain.title')">
    <h4 class="supply-chain-title">{{ t('supplyChain.title') }}</h4>
    <div v-if="chainUnits.length === 0" class="supply-chain-empty">
      {{ t('supplyChain.empty') }}
    </div>
    <div v-else class="supply-chain-flow" role="list">
      <template v-for="(unit, index) in chainUnits" :key="unit.id">
        <div
          class="unit-node"
          :class="unitStatusClass(unit.id)"
          :title="unitStatusTitle(unit.id)"
          role="listitem"
        >
          <span class="unit-icon" :aria-hidden="true">{{ unitIcon(unit.unitType) }}</span>
          <span class="unit-label">{{ unitLabel(unit.unitType) }}</span>
          <span v-if="unitStatusBadge(unit.id)" class="unit-status-badge" :aria-label="statusMap[unit.id]?.status">
            {{ unitStatusBadge(unit.id) }}
          </span>
        </div>
        <span v-if="index < chainUnits.length - 1" class="unit-arrow" aria-hidden="true">→</span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.supply-chain-panel {
  margin-top: 0.75rem;
  padding: 0.75rem 1rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}

.supply-chain-title {
  margin: 0 0 0.5rem;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.supply-chain-empty {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  font-style: italic;
}

.supply-chain-flow {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.25rem;
}

.unit-node {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.125rem;
  padding: 0.375rem 0.5rem;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  min-width: 4rem;
  position: relative;
}

.unit-node--active {
  border-color: var(--color-secondary);
  background: rgba(0, 200, 83, 0.06);
}

.unit-node--blocked {
  border-color: var(--color-danger);
  background: rgba(248, 113, 113, 0.06);
}

.unit-node--full {
  border-color: var(--color-primary);
  background: rgba(0, 71, 255, 0.06);
}

.unit-node--idle {
  opacity: 0.7;
}

.unit-node--unconfigured {
  opacity: 0.5;
  border-style: dashed;
}

.unit-icon {
  font-size: 1.125rem;
  line-height: 1;
}

.unit-label {
  font-size: 0.6875rem;
  font-weight: 500;
  color: var(--color-text-secondary);
  text-align: center;
  white-space: nowrap;
}

.unit-status-badge {
  font-size: 0.625rem;
  line-height: 1;
}

.unit-arrow {
  font-size: 1rem;
  color: var(--color-text-secondary);
  flex-shrink: 0;
}

@media (max-width: 640px) {
  .supply-chain-flow {
    gap: 0.125rem;
  }

  .unit-node {
    min-width: 3rem;
    padding: 0.25rem 0.375rem;
  }

  .unit-label {
    font-size: 0.625rem;
  }
}
</style>
