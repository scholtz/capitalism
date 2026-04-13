<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { formatGameTickTime, formatTickDuration } from '@/lib/gameTime'
import type { ScheduledActionSummary } from '@/types'

const { t, locale } = useI18n()

const props = defineProps<{
  actions: ScheduledActionSummary[]
  loading: boolean
  currentTick: number | null
}>()

const buildingTypeIcons: Record<string, string> = {
  MINE: '⛏️',
  FACTORY: '🏭',
  SALES_SHOP: '🏪',
  RESEARCH_DEVELOPMENT: '🔬',
  APARTMENT: '🏢',
  COMMERCIAL: '🏛️',
  MEDIA_HOUSE: '📺',
  BANK: '🏦',
  EXCHANGE: '📊',
  POWER_PLANT: '⚡',
}

function getBuildingIcon(type: string): string {
  return buildingTypeIcons[type] || '🏗️'
}

function actionLabel(actionType: string): string {
  if (actionType === 'BUILDING_UPGRADE') {
    return t('pendingActions.buildingUpgrade')
  }
  return actionType
}

function formatApplyTime(appliesAtTick: number): string {
  return formatGameTickTime(appliesAtTick, locale.value)
}

function actionDebugTitle(action: ScheduledActionSummary): string {
  return `Tick ${action.appliesAtTick} · ${formatTickDuration(action.ticksRemaining, locale.value)}`
}
</script>

<template>
  <section class="pending-actions-timeline" aria-labelledby="pending-actions-title">
    <h2 id="pending-actions-title" class="section-title">{{ t('pendingActions.title') }}</h2>

    <div v-if="loading" class="pending-loading">{{ t('common.loading') }}</div>

    <div v-else-if="actions.length === 0" class="pending-empty" role="status">
      <span class="empty-icon">✅</span>
      <p>{{ t('pendingActions.empty') }}</p>
    </div>

    <ol v-else class="actions-list">
      <li v-for="action in actions" :key="action.id" class="action-item">
        <span class="action-building-icon" aria-hidden="true">{{ getBuildingIcon(action.buildingType) }}</span>
        <div class="action-body">
          <div class="action-header-row">
            <strong class="action-label">{{ actionLabel(action.actionType) }}</strong>
            <span class="action-building-name">{{ action.buildingName }}</span>
          </div>
          <div class="action-meta">
            <span
              v-if="props.currentTick !== null"
              class="applies-at"
              role="timer"
              :title="actionDebugTitle(action)"
            >
              {{ t('pendingActions.appliesAtTime', { time: formatApplyTime(action.appliesAtTick) }) }}
            </span>
          </div>
          <div class="action-progress">
            <div
              class="progress-bar"
              :style="{
                width:
                  action.totalTicksRequired > 0
                    ? Math.max(
                        0,
                        Math.min(
                          100,
                          ((action.totalTicksRequired - action.ticksRemaining) /
                            action.totalTicksRequired) *
                            100,
                        ),
                      ) + '%'
                    : '100%',
              }"
              role="progressbar"
              :aria-valuenow="action.totalTicksRequired - action.ticksRemaining"
              :aria-valuemin="0"
              :aria-valuemax="action.totalTicksRequired"
            ></div>
          </div>
        </div>
        <RouterLink
          :to="`/building/${action.buildingId}`"
          class="btn btn-secondary action-link"
          :aria-label="t('pendingActions.viewBuilding') + ': ' + action.buildingName"
        >
          {{ t('pendingActions.viewBuilding') }}
        </RouterLink>
      </li>
    </ol>
  </section>
</template>

<style scoped>
.pending-actions-timeline {
  margin-bottom: 2rem;
}

.section-title {
  font-size: 1.125rem;
  font-weight: 700;
  margin-bottom: 1rem;
  color: var(--color-text);
}

.pending-loading {
  color: var(--color-text-secondary);
  font-size: 0.9rem;
}

.pending-empty {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border-radius: var(--radius-md);
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid var(--color-border);
  color: var(--color-text-secondary);
  font-size: 0.9rem;
}

.empty-icon {
  font-size: 1.25rem;
}

.actions-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.action-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem 1.25rem;
  border-radius: var(--radius-md);
  background: var(--color-surface-raised);
  border: 1px solid var(--color-border);
}

.action-building-icon {
  font-size: 1.5rem;
  flex-shrink: 0;
}

.action-body {
  flex: 1;
  min-width: 0;
}

.action-header-row {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.25rem;
}

.action-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text);
}

.action-building-name {
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.action-meta {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 0.5rem;
}

.ticks-remaining {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--color-primary);
}

.applies-at {
  font-size: 0.75rem;
  color: var(--color-text-muted, var(--color-text-secondary));
}

.action-progress {
  height: 4px;
  border-radius: 2px;
  background: rgba(255, 255, 255, 0.08);
  overflow: hidden;
}

.progress-bar {
  height: 100%;
  border-radius: 2px;
  background: var(--color-primary);
  transition: width 0.5s ease;
}

.action-link {
  flex-shrink: 0;
  font-size: 0.8125rem;
  padding: 0.375rem 0.75rem;
}

@media (max-width: 640px) {
  .action-item {
    flex-wrap: wrap;
  }

  .action-link {
    width: 100%;
    text-align: center;
  }
}
</style>
