<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useGameAdminStore } from '@/stores/gameAdmin'
import type { OperationsStatistics } from '@/types'

const { t, locale } = useI18n()
const adminStore = useGameAdminStore()
const statistics = ref<OperationsStatistics | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

function formatCurrency(value: number) {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value)
}

async function load() {
  loading.value = true
  error.value = null
  try {
    statistics.value = await adminStore.fetchOperationsStatistics()
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.loadFailed')
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="card page-card">
    <h2>{{ t('admin.menuStatistics') }}</h2>
    <p class="description">{{ t('admin.statisticsBody') }}</p>

    <div v-if="loading" class="state">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="state">{{ error }}</div>
    <div v-else-if="statistics" class="columns">
      <article class="flow-column">
        <h3>{{ t('admin.statisticsIncome') }}</h3>
        <div class="flow-list">
          <div v-for="item in statistics.incomeItems" :key="item.category" class="flow-item">
            <div>
              <strong>{{ item.category }}</strong>
              <p>{{ item.description }}</p>
            </div>
            <span>{{ formatCurrency(item.amount) }}</span>
          </div>
        </div>
      </article>
      <article class="flow-column">
        <h3>{{ t('admin.statisticsExpenses') }}</h3>
        <div class="flow-list">
          <div v-for="item in statistics.expenseItems" :key="item.category" class="flow-item">
            <div>
              <strong>{{ item.category }}</strong>
              <p>{{ item.description }}</p>
            </div>
            <span>{{ formatCurrency(item.amount) }}</span>
          </div>
        </div>
      </article>
    </div>
  </section>
</template>

<style scoped>
.page-card {
  padding: 1.1rem;
}

.description {
  color: var(--color-text-secondary);
  margin-top: 0.35rem;
}

.columns {
  margin-top: 1rem;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1rem;
}

.flow-column {
  display: grid;
  gap: 0.7rem;
}

.flow-list {
  display: grid;
  gap: 0.6rem;
}

.flow-item {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.8rem;
}

.flow-item p {
  color: var(--color-text-secondary);
  margin-top: 0.2rem;
  font-size: 0.88rem;
}

.state {
  margin-top: 1rem;
}

@media (max-width: 900px) {
  .columns {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
