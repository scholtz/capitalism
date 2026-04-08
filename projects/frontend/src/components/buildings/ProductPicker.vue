<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { RankedProductResult } from '@/types'
import { getProductImageUrl } from '@/lib/catalogPresentation'

const { t } = useI18n()

const props = defineProps<{
  /** Currently selected product type ID (or null/undefined for no selection). */
  modelValue: string | null | undefined
  /** Ranked products returned by the rankedProductTypes query. */
  rankedProducts: RankedProductResult[]
  /** Whether the list is loading. */
  loading?: boolean
  /** Whether to show the "none" option. */
  allowNone?: boolean
  /** Label for the none option. */
  noneLabelKey?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string | null]
}>()

const searchQuery = ref('')

const filteredProducts = computed(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return props.rankedProducts
  return props.rankedProducts.filter((r) =>
    r.productType.name.toLowerCase().includes(q) ||
    r.productType.industry.toLowerCase().includes(q),
  )
})

/** Grouped by section for visual separation. */
const groupedProducts = computed(() => {
  const connected = filteredProducts.value.filter((r) => r.rankingReason === 'connected')
  const usedByCompany = filteredProducts.value.filter((r) => r.rankingReason === 'used_by_company')
  const catalog = filteredProducts.value.filter((r) => r.rankingReason === 'catalog')
  return { connected, usedByCompany, catalog }
})

const selectedId = computed({
  get: () => props.modelValue ?? null,
  set: (v) => emit('update:modelValue', v),
})

function select(id: string | null) {
  selectedId.value = id
}

function rankingReasonLabel(reason: string): string {
  if (reason === 'connected') return t('productPicker.reasonConnected')
  if (reason === 'used_by_company') return t('productPicker.reasonUsedByCompany')
  return ''
}

function rankingReasonClass(reason: string): string {
  if (reason === 'connected') return 'badge-connected'
  if (reason === 'used_by_company') return 'badge-used'
  return ''
}

function productImage(r: RankedProductResult): string {
  return getProductImageUrl(r.productType)
}

// Reset search when products list changes significantly
watch(
  () => props.rankedProducts.length,
  () => {
    searchQuery.value = ''
  },
)
</script>

<template>
  <div class="product-picker" role="listbox" :aria-label="t('productPicker.ariaLabel')">
    <!-- Search input -->
    <div class="picker-search">
      <input
        v-model="searchQuery"
        type="text"
        class="picker-search-input"
        :placeholder="t('productPicker.searchPlaceholder')"
        aria-label="t('productPicker.searchPlaceholder')"
      />
    </div>

    <div v-if="loading" class="picker-loading">{{ t('productPicker.loading') }}</div>

    <div v-else-if="filteredProducts.length === 0" class="picker-empty">
      {{ t('productPicker.noResults') }}
    </div>

    <template v-else>
      <!-- None option -->
      <div
        v-if="allowNone"
        class="picker-item picker-item-none"
        :class="{ 'picker-item-selected': selectedId === null }"
        role="option"
        :aria-selected="selectedId === null"
        tabindex="0"
        @click="select(null)"
        @keydown.enter.space.prevent="select(null)"
      >
        {{ noneLabelKey ? t(noneLabelKey) : t('productPicker.noneLabel') }}
      </div>

      <!-- Connected products section -->
      <template v-if="groupedProducts.connected.length > 0">
        <div class="picker-section-header">{{ t('productPicker.sectionConnected') }}</div>
        <div
          v-for="r in groupedProducts.connected"
          :key="r.productType.id"
          class="picker-item"
          :class="{
            'picker-item-selected': selectedId === r.productType.id,
            'picker-item-locked': r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer,
          }"
          role="option"
          :aria-selected="selectedId === r.productType.id"
          :aria-disabled="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer"
          tabindex="0"
          @click="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
          @keydown.enter.space.prevent="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
        >
          <img
            :src="productImage(r)"
            :alt="r.productType.name"
            class="picker-item-img"
            aria-hidden="true"
          />
          <div class="picker-item-body">
            <span class="picker-item-name">{{ r.productType.name }}</span>
            <span class="picker-item-industry">{{ r.productType.industry }}</span>
          </div>
          <span
            class="picker-item-badge"
            :class="rankingReasonClass(r.rankingReason)"
            :title="rankingReasonLabel(r.rankingReason)"
          >{{ rankingReasonLabel(r.rankingReason) }}</span>
          <span v-if="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer" class="picker-item-badge badge-pro">
            {{ t('catalog.proBadge') }}
          </span>
        </div>
      </template>

      <!-- Used-by-company products section -->
      <template v-if="groupedProducts.usedByCompany.length > 0">
        <div class="picker-section-header">{{ t('productPicker.sectionUsedByCompany') }}</div>
        <div
          v-for="r in groupedProducts.usedByCompany"
          :key="r.productType.id"
          class="picker-item"
          :class="{
            'picker-item-selected': selectedId === r.productType.id,
            'picker-item-locked': r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer,
          }"
          role="option"
          :aria-selected="selectedId === r.productType.id"
          :aria-disabled="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer"
          tabindex="0"
          @click="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
          @keydown.enter.space.prevent="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
        >
          <img
            :src="productImage(r)"
            :alt="r.productType.name"
            class="picker-item-img"
            aria-hidden="true"
          />
          <div class="picker-item-body">
            <span class="picker-item-name">{{ r.productType.name }}</span>
            <span class="picker-item-industry">{{ r.productType.industry }}</span>
          </div>
          <span
            class="picker-item-badge"
            :class="rankingReasonClass(r.rankingReason)"
            :title="rankingReasonLabel(r.rankingReason)"
          >{{ rankingReasonLabel(r.rankingReason) }}</span>
          <span v-if="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer" class="picker-item-badge badge-pro">
            {{ t('catalog.proBadge') }}
          </span>
        </div>
      </template>

      <!-- Catalog section -->
      <template v-if="groupedProducts.catalog.length > 0">
        <div
          v-if="groupedProducts.connected.length > 0 || groupedProducts.usedByCompany.length > 0"
          class="picker-section-header"
        >
          {{ t('productPicker.sectionCatalog') }}
        </div>
        <div
          v-for="r in groupedProducts.catalog"
          :key="r.productType.id"
          class="picker-item"
          :class="{
            'picker-item-selected': selectedId === r.productType.id,
            'picker-item-locked': r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer,
          }"
          role="option"
          :aria-selected="selectedId === r.productType.id"
          :aria-disabled="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer"
          tabindex="0"
          @click="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
          @keydown.enter.space.prevent="!(r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer) && select(r.productType.id)"
        >
          <img
            :src="productImage(r)"
            :alt="r.productType.name"
            class="picker-item-img"
            aria-hidden="true"
          />
          <div class="picker-item-body">
            <span class="picker-item-name">{{ r.productType.name }}</span>
            <span class="picker-item-industry">{{ r.productType.industry }}</span>
          </div>
          <span v-if="r.productType.isProOnly && !r.productType.isUnlockedForCurrentPlayer" class="picker-item-badge badge-pro">
            {{ t('catalog.proBadge') }}
          </span>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
.product-picker {
  display: flex;
  flex-direction: column;
  gap: 0;
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  overflow: hidden;
  max-height: 320px;
  background: var(--color-surface, #fff);
}

.picker-search {
  padding: 8px;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
  position: sticky;
  top: 0;
  background: var(--color-surface, #fff);
  z-index: 1;
}

.picker-search-input {
  width: 100%;
  padding: 6px 10px;
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 6px;
  font-size: 0.85rem;
  background: var(--color-background, #f9fafb);
  color: var(--color-text, #111);
  box-sizing: border-box;
}

.picker-search-input:focus {
  outline: 2px solid var(--color-primary, #4f46e5);
  outline-offset: -1px;
}

.picker-loading,
.picker-empty {
  padding: 16px;
  text-align: center;
  font-size: 0.85rem;
  color: var(--color-text-muted, #6b7280);
}

.picker-section-header {
  padding: 4px 10px 2px;
  font-size: 0.72rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--color-text-muted, #6b7280);
  background: var(--color-background, #f3f4f6);
  border-top: 1px solid var(--color-border, #e5e7eb);
}

.picker-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  cursor: pointer;
  transition: background 0.12s;
  border-bottom: 1px solid var(--color-border-light, #f0f0f0);
  overflow: auto;
}

.picker-item:last-child {
  border-bottom: none;
}

.picker-item:hover:not(.picker-item-locked) {
  background: var(--color-hover, #f0f9ff);
}

.picker-item:focus {
  outline: 2px solid var(--color-primary, #4f46e5);
  outline-offset: -2px;
}

.picker-item-selected {
  background: var(--color-primary-light, #ede9fe) !important;
}

.picker-item-locked {
  opacity: 0.55;
  cursor: not-allowed;
}

.picker-item-none {
  font-style: italic;
  color: var(--color-text-muted, #6b7280);
  font-size: 0.85rem;
}

.picker-item-img {
  width: 32px;
  height: 32px;
  border-radius: 6px;
  object-fit: cover;
  flex-shrink: 0;
  background: var(--color-background, #f3f4f6);
}

.picker-item-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.picker-item-name {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--color-text, #111);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.picker-item-industry {
  font-size: 0.72rem;
  color: var(--color-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.picker-item-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 10px;
  flex-shrink: 0;
  white-space: nowrap;
}

.badge-connected {
  background: #d1fae5;
  color: #065f46;
}

.badge-used {
  background: #dbeafe;
  color: #1e40af;
}

.badge-pro {
  background: #fef3c7;
  color: #92400e;
}
</style>
