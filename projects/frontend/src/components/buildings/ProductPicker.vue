<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
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

const isOpen = ref(false)
const searchQuery = ref('')
const triggerRef = ref<HTMLElement | null>(null)

/** Position of the dropdown panel calculated from the trigger's bounding rect. */
const panelStyle = ref<{ top: string; left: string; width: string } | null>(null)

/** Layout constants for the dropdown panel. */
const PANEL_MAX_HEIGHT = 340
const MIN_SPACE_BELOW = 200
const VIEWPORT_HEIGHT_FRACTION = 0.5
const PANEL_GAP = 4

const filteredProducts = computed(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return props.rankedProducts
  return props.rankedProducts.filter(
    (r) =>
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

/** The currently selected product entry, if any. */
const selectedProduct = computed(() => {
  if (!props.modelValue) return null
  return props.rankedProducts.find((r) => r.productType.id === props.modelValue) ?? null
})

/**
 * True when a saved selection exists but the product is not in the ranked list.
 * This signals a stale/invalid selection that the player should replace.
 */
const hasStaleSelection = computed(() => {
  if (!props.modelValue || props.loading) return false
  return !selectedProduct.value && props.rankedProducts.length > 0
})

const selectedId = computed({
  get: () => props.modelValue ?? null,
  set: (v) => emit('update:modelValue', v),
})

function computePanelPosition() {
  if (!triggerRef.value) return
  const rect = triggerRef.value.getBoundingClientRect()
  const spaceBelow = window.innerHeight - rect.bottom
  panelStyle.value = {
    top: `${rect.bottom + PANEL_GAP}px`,
    left: `${rect.left}px`,
    width: `${rect.width}px`,
  }
  // If not enough space below, position above the trigger
  if (spaceBelow < MIN_SPACE_BELOW) {
    const maxHeight = Math.min(PANEL_MAX_HEIGHT, window.innerHeight * VIEWPORT_HEIGHT_FRACTION)
    panelStyle.value.top = `${rect.top - maxHeight - PANEL_GAP}px`
  }
}

const searchInputRef = ref<HTMLInputElement | null>(null)

async function open() {
  isOpen.value = true
  searchQuery.value = ''
  await nextTick()
  computePanelPosition()
  // Move focus to search input so keyboard users can type immediately
  searchInputRef.value?.focus()
}

function close() {
  isOpen.value = false
}

function toggle() {
  if (isOpen.value) {
    close()
  } else {
    void open()
  }
}

function select(id: string | null) {
  selectedId.value = id
  close()
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

/** Close panel when clicking outside. */
function onDocumentClick(e: MouseEvent) {
  if (!triggerRef.value) return
  const target = e.target as Node
  if (triggerRef.value.contains(target)) return
  const panel = document.querySelector('.product-picker-panel')
  if (panel && panel.contains(target)) return
  close()
}

onMounted(() => document.addEventListener('mousedown', onDocumentClick))
onUnmounted(() => document.removeEventListener('mousedown', onDocumentClick))

// Reset search when products list changes significantly
watch(
  () => props.rankedProducts.length,
  () => {
    searchQuery.value = ''
  },
)
</script>

<template>
  <div class="product-picker" ref="triggerRef">
    <!-- Stale/invalid selection warning -->
    <div v-if="hasStaleSelection" class="picker-stale-warning" role="alert">
      <span>⚠</span> {{ t('productPicker.invalidSelectionWarning') }}
    </div>

    <!-- Trigger button showing current selection -->
    <button
      type="button"
      class="picker-trigger"
      :class="{ 'picker-trigger-open': isOpen, 'picker-trigger-stale': hasStaleSelection }"
      :aria-expanded="isOpen"
      :aria-haspopup="'listbox'"
      @click="toggle"
    >
      <template v-if="loading">
        <span class="picker-trigger-label picker-trigger-loading">{{ t('productPicker.loading') }}</span>
      </template>
      <template v-else-if="selectedProduct">
        <img
          :src="productImage(selectedProduct)"
          :alt="selectedProduct.productType.name"
          class="picker-trigger-img"
          aria-hidden="true"
        />
        <span class="picker-trigger-label picker-trigger-selected-name">{{ selectedProduct.productType.name }}</span>
        <span class="picker-trigger-industry">{{ selectedProduct.productType.industry }}</span>
      </template>
      <template v-else-if="selectedId === null && allowNone">
        <span class="picker-trigger-label picker-trigger-none">{{ noneLabelKey ? t(noneLabelKey) : t('productPicker.noneLabel') }}</span>
      </template>
      <template v-else>
        <span class="picker-trigger-label picker-trigger-placeholder">{{ t('productPicker.triggerPlaceholder') }}</span>
      </template>
      <span class="picker-trigger-arrow" aria-hidden="true">{{ isOpen ? '▲' : '▼' }}</span>
    </button>

    <!-- Help text -->
    <p class="picker-help-text">{{ t('productPicker.helpText') }}</p>

    <!-- Dropdown panel teleported to body to escape overflow:hidden containers -->
    <Teleport to="body">
      <div
        v-if="isOpen"
        class="product-picker-panel"
        role="listbox"
        :aria-label="t('productPicker.ariaLabel')"
        :style="panelStyle ? { position: 'fixed', top: panelStyle.top, left: panelStyle.left, width: panelStyle.width } : {}"
      >
      <!-- Search input -->
      <div class="picker-search">
        <input
          ref="searchInputRef"
          v-model="searchQuery"
          type="text"
          class="picker-search-input"
          :placeholder="t('productPicker.searchPlaceholder')"
          :aria-label="t('productPicker.searchPlaceholder')"
        />
      </div>

      <div class="picker-panel-body">
        <div v-if="loading" class="picker-loading">{{ t('productPicker.loading') }}</div>

        <div v-else-if="filteredProducts.length === 0 && searchQuery" class="picker-empty">
          {{ t('productPicker.noResults') }}
        </div>

        <div v-else-if="props.rankedProducts.length === 0" class="picker-empty picker-empty-no-connected">
          {{ t('productPicker.noConnectedProducts') }}
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
    </div>
    </Teleport>
  </div>
</template>

<style scoped>
.product-picker {
  position: relative;
}

/* === Trigger button === */
.picker-trigger {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  background: var(--color-surface, #fff);
  color: var(--color-text, #111);
  cursor: pointer;
  text-align: left;
  font-size: 0.875rem;
  transition: border-color 0.15s, box-shadow 0.15s;
  min-height: 42px;
}

.picker-trigger:hover {
  border-color: var(--color-primary, #4f46e5);
}

.picker-trigger:focus {
  outline: 2px solid var(--color-primary, #4f46e5);
  outline-offset: 1px;
}

.picker-trigger-open {
  border-color: var(--color-primary, #4f46e5);
  box-shadow: 0 0 0 2px rgba(79, 70, 229, 0.15);
}

.picker-trigger-stale {
  border-color: #f59e0b;
}

.picker-trigger-img {
  width: 28px;
  height: 28px;
  border-radius: 5px;
  object-fit: cover;
  flex-shrink: 0;
  background: var(--color-background, #f3f4f6);
}

.picker-trigger-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.picker-trigger-selected-name {
  font-weight: 500;
}

.picker-trigger-placeholder,
.picker-trigger-loading {
  color: var(--color-text-muted, #6b7280);
  font-style: italic;
}

.picker-trigger-none {
  color: var(--color-text-muted, #6b7280);
  font-style: italic;
}

.picker-trigger-industry {
  font-size: 0.72rem;
  color: var(--color-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  flex-shrink: 0;
}

.picker-trigger-arrow {
  font-size: 0.7rem;
  color: var(--color-text-muted, #6b7280);
  flex-shrink: 0;
}

/* === Help text === */
.picker-help-text {
  margin: 4px 0 0;
  font-size: 0.75rem;
  color: var(--color-text-muted, #6b7280);
  font-style: italic;
}

/* === Stale warning === */
.picker-stale-warning {
  margin-bottom: 4px;
  padding: 6px 10px;
  background: #fef3c7;
  border: 1px solid #f59e0b;
  border-radius: 6px;
  font-size: 0.8rem;
  color: #92400e;
}

/* === Dropdown panel — teleported to body, positioned via JS === */
.product-picker-panel {
  z-index: 9999;
  max-height: 340px;
  background: var(--color-surface, #fff);
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.18);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.picker-search {
  padding: 8px;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
  background: var(--color-surface, #fff);
  flex-shrink: 0;
}

.picker-search-input {
  width: 100%;
  padding: 7px 10px;
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 6px;
  font-size: 0.875rem;
  background: var(--color-background, #f9fafb);
  color: var(--color-text, #111);
  box-sizing: border-box;
}

.picker-search-input:focus {
  outline: 2px solid var(--color-primary, #4f46e5);
  outline-offset: -1px;
}

.picker-panel-body {
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}

.picker-loading,
.picker-empty {
  padding: 16px;
  text-align: center;
  font-size: 0.875rem;
  color: var(--color-text-muted, #6b7280);
}

.picker-empty-no-connected {
  padding: 20px 16px;
  line-height: 1.5;
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
  position: sticky;
  top: 0;
}

.picker-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  cursor: pointer;
  transition: background 0.12s;
  border-bottom: 1px solid var(--color-border-light, #f0f0f0);
  min-height: 44px;
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
  font-size: 0.875rem;
}

.picker-item-img {
  width: 36px;
  height: 36px;
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
