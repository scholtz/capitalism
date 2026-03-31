<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { gqlRequest } from '@/lib/graphql'
import { getOverheadStatus } from '@/lib/companyOverhead'
import type { CompanySettings } from '@/types'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()

const companyId = computed(() => route.params.companyId as string)
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
const success = ref<string | null>(null)
const settings = ref<CompanySettings | null>(null)
const companyName = ref('')
const salaryMultipliers = ref<Record<string, number>>({})

const SETTINGS_QUERY = `
  query GetCompanySettings($companyId: UUID!) {
    companySettings(companyId: $companyId) {
      companyId
      companyName
      cash
      foundedAtTick
      administrationOverheadRate
      ageFactor
      assetFactor
      assetValue
      citySalarySettings {
        cityId
        cityName
        baseSalaryPerManhour
        salaryMultiplier
        effectiveSalaryPerManhour
      }
    }
  }
`

const UPDATE_MUTATION = `
  mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
    updateCompanySettings(input: $input) {
      id
      name
    }
  }
`

async function loadSettings() {
  loading.value = true
  error.value = null
  success.value = null

  try {
    const data = await gqlRequest<{ companySettings: CompanySettings | null }>(SETTINGS_QUERY, {
      companyId: companyId.value,
    })

    if (!data.companySettings) {
      error.value = t('companySettings.notFound')
      return
    }

    settings.value = data.companySettings
    companyName.value = data.companySettings.companyName
    salaryMultipliers.value = Object.fromEntries(data.companySettings.citySalarySettings.map((entry) => [entry.cityId, entry.salaryMultiplier]))
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('companySettings.loadFailed')
  } finally {
    loading.value = false
  }
}

async function saveSettings() {
  if (!settings.value) {
    return
  }

  saving.value = true
  error.value = null
  success.value = null

  try {
    await gqlRequest(UPDATE_MUTATION, {
      input: {
        companyId: settings.value.companyId,
        name: companyName.value,
        citySalarySettings: settings.value.citySalarySettings.map((entry) => ({
          cityId: entry.cityId,
          salaryMultiplier: Number(salaryMultipliers.value[entry.cityId] ?? entry.salaryMultiplier),
        })),
      },
    })

    await loadSettings()
    success.value = t('companySettings.saved')
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('companySettings.saveFailed')
  } finally {
    saving.value = false
  }
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2,
  }).format(value)
}

function formatPercent(value: number): string {
  return `${(value * 100).toFixed(1)}%`
}

const overheadStatus = computed(() =>
  settings.value ? getOverheadStatus(settings.value.administrationOverheadRate) : 'low',
)

onMounted(loadSettings)
</script>

<template>
  <div class="company-settings-view container">
    <div class="company-settings-header">
      <button class="btn btn-ghost" @click="router.push('/dashboard')">← {{ t('common.back') }}</button>
      <div>
        <p class="settings-eyebrow">{{ t('companySettings.eyebrow') }}</p>
        <h1>{{ settings?.companyName ?? t('companySettings.title') }}</h1>
      </div>
    </div>

    <div v-if="loading" class="state-box">
      <p>{{ t('common.loading') }}</p>
    </div>

    <div v-else-if="error" class="state-box state-error" role="alert">
      <p>{{ error }}</p>
      <button class="btn btn-secondary" @click="loadSettings">{{ t('common.tryAgain') }}</button>
    </div>

    <div v-else-if="settings" class="settings-grid">
      <section class="settings-card overview-card">
        <h2>{{ t('companySettings.overviewTitle') }}</h2>
        <div class="overview-grid">
          <div>
            <span class="overview-label">{{ t('companySettings.assetValue') }}</span>
            <strong>{{ formatCurrency(settings.assetValue) }}</strong>
          </div>
          <div>
            <span class="overview-label">{{ t('companySettings.cash') }}</span>
            <strong>{{ formatCurrency(settings.cash) }}</strong>
          </div>
          <div>
            <span class="overview-label">{{ t('companySettings.foundedTick') }}</span>
            <strong>{{ settings.foundedAtTick }}</strong>
          </div>
          <div>
            <span class="overview-label">{{ t('companySettings.administrationOverhead') }}</span>
            <strong :class="`overhead-value overhead-${overheadStatus}`">
              {{ formatPercent(settings.administrationOverheadRate) }}
              <span class="overhead-badge">
                {{ t(`companySettings.overheadStatus.${overheadStatus}`) }}
              </span>
            </strong>
          </div>
        </div>

        <div class="overhead-drivers">
          <span class="driver-chip">
            {{ t('companySettings.overheadDriverAge') }}: {{ formatPercent(settings.ageFactor) }}
          </span>
          <span class="driver-chip">
            {{ t('companySettings.overheadDriverScale') }}: {{ formatPercent(settings.assetFactor) }}
          </span>
        </div>

        <p class="overview-copy">{{ t('companySettings.overheadHelp') }}</p>
        <p class="overview-copy overhead-tip">{{ t('companySettings.overheadReduceTip') }}</p>
      </section>

      <section class="settings-card form-card">
        <h2>{{ t('companySettings.profileTitle') }}</h2>
        <label class="settings-field">
          <span>{{ t('companySettings.companyName') }}</span>
          <input v-model="companyName" type="text" maxlength="200" />
        </label>

        <div class="salary-table-wrapper">
          <table class="salary-table">
            <thead>
              <tr>
                <th>{{ t('companySettings.city') }}</th>
                <th>{{ t('companySettings.baseSalary') }}</th>
                <th>{{ t('companySettings.salaryMultiplier') }}</th>
                <th>{{ t('companySettings.effectiveSalary') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in settings.citySalarySettings" :key="entry.cityId">
                <td>{{ entry.cityName }}</td>
                <td>{{ formatCurrency(entry.baseSalaryPerManhour) }}</td>
                <td>
                  <input
                    v-model.number="salaryMultipliers[entry.cityId]"
                    type="number"
                    min="0.5"
                    max="2"
                    step="0.05"
                    class="salary-input"
                    :aria-label="`${t('companySettings.salaryMultiplier')} ${entry.cityName}`"
                  />
                </td>
                <td>
                  {{ formatCurrency(entry.baseSalaryPerManhour * (salaryMultipliers[entry.cityId] ?? entry.salaryMultiplier)) }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <p class="salary-impact-hint">{{ t('companySettings.salaryImpactHint') }}</p>

        <p v-if="success" class="save-message" role="status">{{ success }}</p>
        <p v-if="error" class="save-error" role="alert">{{ error }}</p>

        <div class="settings-actions">
          <button class="btn btn-primary" :disabled="saving" @click="saveSettings">
            {{ saving ? t('common.loading') : t('common.save') }}
          </button>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.company-settings-view {
  padding: 2rem 1rem 3rem;
  display: grid;
  gap: 1.5rem;
}

.company-settings-header {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.settings-eyebrow,
.overview-copy,
.overview-label {
  color: var(--color-text-secondary);
}

.settings-grid {
  display: grid;
  gap: 1.5rem;
}

.settings-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 16px;
  padding: 1.5rem;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 1rem;
  margin: 1rem 0;
}

.overview-label {
  display: block;
  font-size: 0.8rem;
  margin-bottom: 0.25rem;
}

/* Overhead status colors */
.overhead-value {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
}

.overhead-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.1rem 0.45rem;
  border-radius: 999px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.overhead-low .overhead-badge {
  background: color-mix(in srgb, var(--color-success, #22c55e) 15%, transparent);
  color: var(--color-success, #16a34a);
}

.overhead-medium .overhead-badge {
  background: color-mix(in srgb, #f59e0b 15%, transparent);
  color: #b45309;
}

.overhead-high .overhead-badge {
  background: color-mix(in srgb, var(--color-danger, #ef4444) 15%, transparent);
  color: var(--color-danger, #dc2626);
}

/* Driver chips */
.overhead-drivers {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.driver-chip {
  font-size: 0.78rem;
  padding: 0.2rem 0.6rem;
  border-radius: 999px;
  background: var(--color-surface-elevated, var(--color-surface));
  border: 1px solid var(--color-border);
  color: var(--color-text-secondary);
}

.overhead-tip {
  font-size: 0.82rem;
  margin-top: 0.25rem;
}

/* Salary impact hint */
.salary-impact-hint {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
  margin: 0.75rem 0 0.25rem;
}

.settings-field {
  display: grid;
  gap: 0.4rem;
  margin-bottom: 1rem;
}

.settings-field input,
.salary-input {
  width: 100%;
  border: 1px solid var(--color-border);
  border-radius: 10px;
  background: var(--color-surface-elevated, var(--color-surface));
  color: var(--color-text);
  padding: 0.65rem 0.8rem;
}

.salary-table-wrapper {
  overflow-x: auto;
}

.salary-table {
  width: 100%;
  border-collapse: collapse;
}

.salary-table th,
.salary-table td {
  text-align: left;
  padding: 0.75rem;
  border-bottom: 1px solid var(--color-border);
}

.settings-actions {
  margin-top: 1rem;
  display: flex;
  justify-content: flex-end;
}

.save-message {
  color: var(--color-secondary);
}

.save-error,
.state-error {
  color: var(--color-danger);
}

@media (max-width: 720px) {
  .company-settings-header {
    flex-direction: column;
  }
}
</style>
