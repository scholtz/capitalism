<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { BuildingUnit } from '@/types'

interface Props {
  units: BuildingUnit[]
  buildingType: string
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

function unitIcon(unitType: string): string {
  return UNIT_TYPE_ICONS[unitType] ?? '🔲'
}

function unitLabel(unitType: string): string {
  const key = `supplyChain.unitTypes.${unitType}` as Parameters<typeof t>[0]
  return t(key)
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
        <div class="unit-node" role="listitem">
          <span class="unit-icon" :aria-hidden="true">{{ unitIcon(unit.unitType) }}</span>
          <span class="unit-label">{{ unitLabel(unit.unitType) }}</span>
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
