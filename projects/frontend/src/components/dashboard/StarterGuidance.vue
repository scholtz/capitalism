<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Company } from '@/types'

interface Props {
  company: Company
  revenue: number
  /** Backend-authoritative net income (after tax). Used for profitability decisions. */
  netIncome: number
}

const props = defineProps<Props>()
const { t } = useI18n()

const hasBuildings = computed(() => props.company.buildings.length > 0)
const hasFactory = computed(() => props.company.buildings.some((b) => b.type === 'FACTORY'))
const hasShop = computed(() => props.company.buildings.some((b) => b.type === 'SALES_SHOP'))
const isStarter = computed(() => hasFactory.value && hasShop.value && props.company.buildings.length <= 2)
const hasRevenue = computed(() => props.revenue > 0)
/** Profitability is determined by the backend's netIncome (includes taxes, not a frontend estimate). */
const isProfitable = computed(() => props.netIncome > 0)

interface GuidanceItem {
  icon: string
  title: string
  body: string
  linkTo?: string
  linkLabel?: string
}

const items = computed<GuidanceItem[]>(() => {
  const result: GuidanceItem[] = []

  if (!hasBuildings.value) {
    result.push({
      icon: '🏗️',
      title: t('starterGuidance.noBuildings.title'),
      body: t('starterGuidance.noBuildings.body'),
      linkTo: `/buy-building/${props.company.id}`,
      linkLabel: t('starterGuidance.noBuildings.action'),
    })
    return result
  }

  if (!hasRevenue.value) {
    result.push({
      icon: '⏳',
      title: t('starterGuidance.awaitingRevenue.title'),
      body: t('starterGuidance.awaitingRevenue.body'),
    })
  } else if (!isProfitable.value) {
    result.push({
      icon: '📉',
      title: t('starterGuidance.unprofitable.title'),
      body: t('starterGuidance.unprofitable.body'),
    })
  } else {
    result.push({
      icon: '📈',
      title: t('starterGuidance.profitable.title'),
      body: t('starterGuidance.profitable.body'),
    })
  }

  if (hasFactory.value) {
    const factory = props.company.buildings.find((b) => b.type === 'FACTORY')
    if (factory) {
      result.push({
        icon: '🏭',
        title: t('starterGuidance.checkFactory.title'),
        body: t('starterGuidance.checkFactory.body'),
        linkTo: `/building/${factory.id}`,
        linkLabel: t('starterGuidance.checkFactory.action'),
      })
    }
  }

  if (hasShop.value) {
    const shop = props.company.buildings.find((b) => b.type === 'SALES_SHOP')
    if (shop) {
      result.push({
        icon: '🏪',
        title: t('starterGuidance.checkShop.title'),
        body: t('starterGuidance.checkShop.body'),
        linkTo: `/building/${shop.id}`,
        linkLabel: t('starterGuidance.checkShop.action'),
      })
    }
  }

  if (isProfitable.value && isStarter.value) {
    result.push({
      icon: '🚀',
      title: t('starterGuidance.expand.title'),
      body: t('starterGuidance.expand.body'),
      linkTo: `/buy-building/${props.company.id}`,
      linkLabel: t('starterGuidance.expand.action'),
    })
  }

  // Limit to 3 items to avoid overwhelming the player with too many action items at once.
  return result.slice(0, 3)
})
</script>

<template>
  <div class="starter-guidance" aria-labelledby="starter-guidance-title">
    <h3 id="starter-guidance-title" class="starter-guidance-title">
      {{ t('starterGuidance.title') }}
    </h3>
    <ul class="guidance-list">
      <li v-for="(item, i) in items" :key="i" class="guidance-item">
        <span class="guidance-icon" aria-hidden="true">{{ item.icon }}</span>
        <div class="guidance-content">
          <strong class="guidance-item-title">{{ item.title }}</strong>
          <p class="guidance-item-body">{{ item.body }}</p>
          <RouterLink v-if="item.linkTo && item.linkLabel" :to="item.linkTo" class="guidance-link">
            {{ item.linkLabel }} →
          </RouterLink>
        </div>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.starter-guidance {
  padding: 1rem 1.25rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}

.starter-guidance-title {
  margin: 0 0 0.75rem;
  font-size: 0.8125rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.guidance-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.guidance-item {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.guidance-icon {
  font-size: 1.25rem;
  flex-shrink: 0;
  margin-top: 0.125rem;
}

.guidance-content {
  flex: 1;
}

.guidance-item-title {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.125rem;
}

.guidance-item-body {
  margin: 0 0 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-secondary);
  line-height: 1.45;
}

.guidance-link {
  font-size: 0.8125rem;
  color: var(--color-primary);
  text-decoration: none;
  font-weight: 500;
}

.guidance-link:hover {
  text-decoration: underline;
}
</style>
