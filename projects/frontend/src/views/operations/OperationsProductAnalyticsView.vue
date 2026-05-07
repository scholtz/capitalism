<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useGameAdminStore } from '@/stores/gameAdmin'
import type { AdminProductAnalyticsRow } from '@/types'

type SortField = keyof Pick<
  AdminProductAnalyticsRow,
  'productName' | 'materialCost' | 'energyCost' | 'laborCost' | 'unitsProduced' | 'unitsSold' | 'marketSize' | 'marketSaturationPercent' | 'currentMarketingSpend' | 'researchQualityLevel'
>

const { t, locale } = useI18n()
const adminStore = useGameAdminStore()
const rows = ref<AdminProductAnalyticsRow[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const searchTerm = ref('')
const sortField = ref<SortField>('productName')
const sortDirection = ref<'asc' | 'desc'>('asc')

function formatCurrency(value: number) {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(value)
}

const filteredRows = computed(() => {
  const query = searchTerm.value.trim().toLowerCase()
  const filtered = rows.value.filter((row) => !query || row.productName.toLowerCase().includes(query))
  const multiplier = sortDirection.value === 'asc' ? 1 : -1
  return [...filtered].sort((left, right) => {
    if (sortField.value === 'productName') {
      return left.productName.localeCompare(right.productName) * multiplier
    }

    return ((left[sortField.value] as number) - (right[sortField.value] as number)) * multiplier
  })
})

async function load() {
  loading.value = true
  error.value = null
  try {
    rows.value = await adminStore.fetchAdminProductAnalytics()
  } catch (caughtError) {
    error.value = caughtError instanceof Error ? caughtError.message : t('admin.loadFailed')
  } finally {
    loading.value = false
  }
}

function exportCsv() {
  const header = [
    'Product',
    'Material Cost',
    'Energy Cost',
    'Labor Cost',
    'Units Produced',
    'Units Sold',
    'Market Size',
    'Market Saturation %',
    'Marketing Spend',
    'R&D Quality',
  ]
  const lines = filteredRows.value.map((row) =>
    [
      row.productName,
      row.materialCost,
      row.energyCost,
      row.laborCost,
      row.unitsProduced,
      row.unitsSold,
      row.marketSize,
      row.marketSaturationPercent,
      row.currentMarketingSpend,
      row.researchQualityLevel,
    ].join(','),
  )

  const csv = [header.join(','), ...lines].join('\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = 'product-analytics.csv'
  link.click()
  URL.revokeObjectURL(url)
}

onMounted(load)
</script>

<template>
  <section class="card page-card">
    <div class="header">
      <div>
        <h2>{{ t('admin.menuProducts') }}</h2>
        <p>{{ t('admin.productAnalyticsBody') }}</p>
      </div>
      <button type="button" class="btn btn-secondary" @click="exportCsv">{{ t('admin.exportCsv') }}</button>
    </div>

    <div class="controls">
      <input v-model="searchTerm" class="form-input" :placeholder="t('admin.productSearchPlaceholder')" />
      <select v-model="sortField" class="form-select">
        <option value="productName">{{ t('admin.productSortName') }}</option>
        <option value="unitsSold">{{ t('admin.productSortUnitsSold') }}</option>
        <option value="unitsProduced">{{ t('admin.productSortUnitsProduced') }}</option>
        <option value="marketSaturationPercent">{{ t('admin.productSortSaturation') }}</option>
      </select>
      <button type="button" class="btn btn-secondary" @click="sortDirection = sortDirection === 'asc' ? 'desc' : 'asc'">
        {{ sortDirection === 'asc' ? '↑' : '↓' }}
      </button>
    </div>

    <p v-if="loading" class="state">{{ t('common.loading') }}</p>
    <p v-else-if="error" class="state">{{ error }}</p>
    <table v-else class="analytics-table">
      <thead>
        <tr>
          <th>{{ t('admin.productColumnName') }}</th>
          <th>{{ t('admin.productColumnMaterialCost') }}</th>
          <th>{{ t('admin.productColumnEnergyCost') }}</th>
          <th>{{ t('admin.productColumnLaborCost') }}</th>
          <th>{{ t('admin.productColumnUnitsProduced') }}</th>
          <th>{{ t('admin.productColumnUnitsSold') }}</th>
          <th>{{ t('admin.productColumnMarketSize') }}</th>
          <th>{{ t('admin.productColumnSaturation') }}</th>
          <th>{{ t('admin.productColumnMarketingSpend') }}</th>
          <th>{{ t('admin.productColumnResearchQuality') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in filteredRows" :key="row.productTypeId">
          <td>{{ row.productName }}</td>
          <td>{{ formatCurrency(row.materialCost) }}</td>
          <td>{{ formatCurrency(row.energyCost) }}</td>
          <td>{{ formatCurrency(row.laborCost) }}</td>
          <td>{{ row.unitsProduced }}</td>
          <td>{{ row.unitsSold }}</td>
          <td>{{ row.marketSize }}</td>
          <td>{{ row.marketSaturationPercent.toFixed(2) }}%</td>
          <td>{{ formatCurrency(row.currentMarketingSpend) }}</td>
          <td>{{ row.researchQualityLevel.toFixed(2) }}</td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.page-card {
  padding: 1.1rem;
}

.header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.header p {
  color: var(--color-text-secondary);
  margin-top: 0.3rem;
}

.controls {
  display: flex;
  flex-wrap: wrap;
  gap: 0.7rem;
  margin-bottom: 1rem;
}

.analytics-table {
  width: 100%;
  border-collapse: collapse;
}

.analytics-table th,
.analytics-table td {
  border-bottom: 1px solid var(--color-border);
  padding: 0.55rem;
  text-align: left;
}

.state {
  color: var(--color-text-secondary);
}

@media (max-width: 900px) {
  .header {
    flex-direction: column;
  }

  .analytics-table {
    display: block;
    overflow-x: auto;
  }
}
</style>
