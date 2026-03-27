<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

type SelectorItem = {
  kind: 'resource' | 'product'
  id: string
  name: string
  description?: string | null
  helperText?: string | null
  groupLabel: string
  unitSymbol?: string | null
  badge?: string | null
  disabled?: boolean
}

const props = defineProps<{
  modelValue: { kind: 'resource' | 'product'; id: string } | null
  items: SelectorItem[]
  label: string
  placeholder?: string
  emptyText?: string
}>()

const { t } = useI18n()
const emit = defineEmits<{
  'update:modelValue': [value: { kind: 'resource' | 'product'; id: string } | null]
}>()

const search = ref('')

const selectedItem = computed(() =>
  props.modelValue
    ? props.items.find((item) => item.kind === props.modelValue!.kind && item.id === props.modelValue!.id) ?? null
    : null,
)

const filteredItems = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (!query) return props.items
  return props.items.filter((item) =>
    item.name.toLowerCase().includes(query)
    || item.groupLabel.toLowerCase().includes(query)
    || (item.description ?? '').toLowerCase().includes(query),
  )
})

const groupedItems = computed(() => {
  const groups = new Map<string, SelectorItem[]>()
  for (const item of filteredItems.value) {
    if (!groups.has(item.groupLabel)) {
      groups.set(item.groupLabel, [])
    }
    groups.get(item.groupLabel)!.push(item)
  }
  return Array.from(groups.entries())
})

function selectItem(item: SelectorItem) {
  if (item.disabled) {
    return
  }

  emit('update:modelValue', { kind: item.kind, id: item.id })
}
</script>

<template>
  <div class="advanced-selector">
    <label class="selector-label">{{ label }}</label>
    <input
      v-model="search"
      type="search"
      class="selector-search"
      :placeholder="placeholder || label"
    />

    <div v-if="selectedItem" class="selected-chip" role="status">
      <strong>{{ selectedItem.name }}</strong>
      <span v-if="selectedItem.badge" class="selected-badge">{{ selectedItem.badge }}</span>
      <span class="selected-meta">{{ selectedItem.groupLabel }} <template v-if="selectedItem.unitSymbol">· {{ selectedItem.unitSymbol }}</template></span>
      <button type="button" class="clear-btn" @click="emit('update:modelValue', null)">×</button>
    </div>

    <div class="selector-list" role="listbox" :aria-label="label">
      <template v-if="groupedItems.length > 0">
        <div v-for="[group, groupItems] in groupedItems" :key="group" class="selector-group">
          <div class="selector-group-label">{{ group }}</div>
          <button
            v-for="item in groupItems"
            :key="`${item.kind}-${item.id}`"
            type="button"
            class="selector-option"
            :class="{ active: selectedItem?.kind === item.kind && selectedItem?.id === item.id, disabled: item.disabled }"
            :disabled="item.disabled"
            @click="selectItem(item)"
          >
            <span class="option-title-row">
              <span class="option-title">{{ item.name }}</span>
              <span v-if="item.badge" class="option-badge">{{ item.badge }}</span>
            </span>
            <span class="option-meta">{{ item.kind === 'resource' ? t('buildingDetail.selector.itemKindResource') : t('buildingDetail.selector.itemKindProduct') }}<template v-if="item.unitSymbol"> · {{ item.unitSymbol }}</template></span>
            <span v-if="item.description" class="option-description">{{ item.description }}</span>
            <span v-if="item.helperText" class="option-description">{{ item.helperText }}</span>
          </button>
        </div>
      </template>
      <p v-else class="selector-empty">{{ emptyText || 'No matching items available.' }}</p>
    </div>
  </div>
</template>

<style scoped>
.advanced-selector {
  display: grid;
  gap: 0.5rem;
}

.selector-label {
  display: block;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-secondary);
}

.selector-search {
  width: 100%;
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-bg);
  color: var(--color-text);
}

.selector-search:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(0, 71, 255, 0.12);
}

.selected-chip {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  padding: 0.625rem 0.75rem;
  border-radius: 10px;
  background: rgba(0, 71, 255, 0.08);
  color: var(--color-text);
}

.selected-meta,
.selected-badge,
.option-meta,
.option-description,
.selector-empty,
.selector-group-label {
  color: var(--color-text-secondary);
}

.selected-badge,
.option-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.1rem 0.45rem;
  border-radius: 999px;
  border: 1px solid rgba(255, 109, 0, 0.35);
  background: rgba(255, 109, 0, 0.12);
  color: var(--color-tertiary);
  font-size: 0.7rem;
  font-weight: 700;
}

.clear-btn {
  margin-left: auto;
  border: none;
  background: transparent;
  color: var(--color-text-secondary);
  cursor: pointer;
  font-size: 1rem;
}

.selector-list {
  max-height: 260px;
  overflow: auto;
  display: grid;
  gap: 0.75rem;
  padding-right: 0.25rem;
}

.selector-group {
  display: grid;
  gap: 0.4rem;
}

.selector-group-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.selector-option {
  width: 100%;
  text-align: left;
  display: grid;
  gap: 0.125rem;
  padding: 0.65rem 0.75rem;
  border-radius: 10px;
  border: 1px solid var(--color-border);
  background: var(--color-bg);
  cursor: pointer;
}

.selector-option.disabled {
  cursor: not-allowed;
  opacity: 0.72;
}

.selector-option.active {
  border-color: var(--color-primary);
  box-shadow: inset 0 0 0 1px var(--color-primary);
}

.option-title-row {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
  align-items: center;
}

.option-title {
  font-weight: 600;
  color: var(--color-text);
}

.option-meta,
.option-description,
.selector-empty {
  font-size: 0.75rem;
}
</style>
