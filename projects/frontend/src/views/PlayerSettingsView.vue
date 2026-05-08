<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest as gqlMasterRequest, GraphQLError } from '@/lib/graphqlMasterServer'
import { generatePersonalAccountName } from '@/lib/personalAccountNameGenerator'
import {
  normalizePersonalAccountName,
  validatePersonalAccountName,
  type PersonalAccountNameValidationCode,
} from '@/lib/personalAccountName'

const { t } = useI18n()
const auth = useAuthStore()

const loading = ref(true)
const saving = ref(false)
const saveError = ref<string | null>(null)
const saveSuccess = ref<string | null>(null)

const personalAccountName = ref('')
const originalPersonalAccountName = ref('')

const normalizedPersonalAccountName = computed(() => normalizePersonalAccountName(personalAccountName.value))
const normalizedOriginalPersonalAccountName = computed(() =>
  normalizePersonalAccountName(originalPersonalAccountName.value),
)
const hasChanges = computed(
  () => normalizedPersonalAccountName.value !== normalizedOriginalPersonalAccountName.value,
)
const validationCode = computed(() => validatePersonalAccountName(personalAccountName.value))
const previewName = computed(
  () => normalizedPersonalAccountName.value || normalizedOriginalPersonalAccountName.value || t('playerSettings.previewFallback'),
)
const canSave = computed(() => hasChanges.value && !validationCode.value && !saving.value)

const PERSONAL_ACCOUNT_NAME_QUERY = `
  query {
    personalAccountName
  }
`

const UPDATE_PERSONAL_ACCOUNT_NAME_MUTATION = `
  mutation SetPersonalAccountName($name: String!) {
    setPersonalAccountName(name: $name) {
      id
      personalAccountName
    }
  }
`

async function loadPersonalAccountName() {
  loading.value = true
  saveError.value = null
  try {
    const data = await gqlMasterRequest<{ personalAccountName: string | null }>(PERSONAL_ACCOUNT_NAME_QUERY)
    const nextName =
      data.personalAccountName ??
      auth.player?.personalAccountName ??
      auth.player?.displayName ??
      generatePersonalAccountName()
    personalAccountName.value = nextName
    originalPersonalAccountName.value = nextName
  } catch (e: unknown) {
    saveError.value = e instanceof Error ? e.message : t('playerSettings.loadFailed')
  } finally {
    loading.value = false
  }
}

function getValidationMessage(code: PersonalAccountNameValidationCode | null) {
  if (code === 'required') return t('playerSettings.validationRequired')
  if (code === 'tooShort') return t('playerSettings.validationTooShort')
  if (code === 'tooLong') return t('playerSettings.validationTooLong')
  if (code === 'invalidCharacters') return t('playerSettings.validationInvalidCharacters')
  return null
}

function getGraphQlValidationMessage(code?: string) {
  if (code === 'PERSONAL_ACCOUNT_NAME_REQUIRED') return t('playerSettings.validationRequired')
  if (code === 'PERSONAL_ACCOUNT_NAME_TOO_SHORT') return t('playerSettings.validationTooShort')
  if (code === 'PERSONAL_ACCOUNT_NAME_TOO_LONG') return t('playerSettings.validationTooLong')
  if (code === 'PERSONAL_ACCOUNT_NAME_INVALID_CHARACTERS') {
    return t('playerSettings.validationInvalidCharacters')
  }
  return null
}

async function savePersonalAccountName() {
  const clientValidationMessage = getValidationMessage(validationCode.value)
  if (clientValidationMessage) {
    saveError.value = clientValidationMessage
    saveSuccess.value = null
    return
  }

  if (!hasChanges.value) {
    return
  }

  saving.value = true
  saveError.value = null
  saveSuccess.value = null
  try {
    const data = await gqlMasterRequest<{
      setPersonalAccountName: { id: string; personalAccountName: string | null }
    }>(UPDATE_PERSONAL_ACCOUNT_NAME_MUTATION, {
      name: normalizedPersonalAccountName.value,
    })

    const updatedName = data.setPersonalAccountName.personalAccountName ?? normalizedPersonalAccountName.value
    originalPersonalAccountName.value = updatedName
    personalAccountName.value = updatedName
    if (auth.player) {
      auth.player.personalAccountName = updatedName
    }
    saveSuccess.value = t('playerSettings.saveSuccess')
  } catch (e: unknown) {
    const validationMessage = e instanceof GraphQLError ? getGraphQlValidationMessage(e.code) : null
    if (validationMessage) {
      saveError.value = validationMessage
    } else {
      saveError.value = e instanceof Error ? e.message : t('playerSettings.saveFailed')
    }
  } finally {
    saving.value = false
  }
}

function generateRandomPersonalAccountName() {
  personalAccountName.value = generatePersonalAccountName()
}

onMounted(() => {
  auth.initFromStorage()
  if (!auth.player && auth.isAuthenticated) {
    void auth.fetchMe().finally(() => {
      void loadPersonalAccountName()
    })
    return
  }
  void loadPersonalAccountName()
})
</script>

<template>
  <div class="container player-settings-view">
    <h1>{{ t('playerSettings.title') }}</h1>
    <p class="settings-warning">{{ t('playerSettings.warning') }}</p>

    <div v-if="loading" class="loading">{{ t('common.loading') }}</div>
    <div v-else class="settings-card">
      <p class="section-kicker">{{ t('playerSettings.publicProfileTitle') }}</p>
      <h2 class="section-title">{{ t('playerSettings.changeDisplayNameTitle') }}</h2>
      <p class="section-description">{{ t('playerSettings.publicProfileDescription') }}</p>

      <label for="personalAccountName">{{ t('playerSettings.displayNameLabel') }}</label>
      <input
        id="personalAccountName"
        v-model="personalAccountName"
        type="text"
        maxlength="60"
        :placeholder="t('playerSettings.displayNamePlaceholder')"
      />

      <div class="actions actions-secondary">
        <button class="btn btn-secondary" type="button" :disabled="saving" @click="generateRandomPersonalAccountName">
          {{ t('playerSettings.generateRandomName') }}
        </button>
      </div>

      <p class="privacy-hint">{{ t('playerSettings.privacyHint') }}</p>
      <p v-if="validationCode" class="validation-message" role="status">
        {{ getValidationMessage(validationCode) }}
      </p>

      <div class="preview-card" aria-label="Leaderboard preview">
        <span class="preview-label">{{ t('playerSettings.previewLabel') }}</span>
        <div class="preview-row">
          <span class="preview-rank">#12</span>
          <div class="preview-identity">
            <strong class="preview-name">{{ previewName }}</strong>
            <span class="preview-badge">{{ t('leaderboard.you') }}</span>
          </div>
          <span class="preview-wealth">$1.25M</span>
        </div>
      </div>

      <div class="actions">
        <button class="btn btn-primary" :disabled="!canSave" @click="savePersonalAccountName">
          {{ saving ? t('common.saving') : t('common.save') }}
        </button>
      </div>

      <p v-if="saveSuccess" class="success-message" role="status">{{ saveSuccess }}</p>
      <p v-if="saveError" class="error-message" role="alert">{{ saveError }}</p>
    </div>
  </div>
</template>

<style scoped>
.player-settings-view {
  padding-top: 2rem;
  padding-bottom: 2rem;
}

.settings-warning {
  margin-bottom: 1rem;
  color: var(--color-warning);
  font-weight: 600;
}

.settings-card {
  max-width: 540px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 1.25rem;
  background: var(--color-surface);
}

.section-kicker {
  margin: 0 0 0.35rem;
  color: var(--color-primary);
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.section-title {
  margin: 0;
}

.section-description {
  margin: 0.5rem 0 1rem;
  color: var(--color-text-muted);
}

.settings-card label {
  display: block;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.settings-card input {
  width: 100%;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  padding: 0.65rem 0.75rem;
  margin-bottom: 0.75rem;
}

.actions {
  display: flex;
  justify-content: flex-end;
}

.actions-secondary {
  justify-content: flex-start;
  margin-bottom: 0.75rem;
}

.privacy-hint,
.validation-message {
  margin: 0 0 0.75rem;
  font-size: 0.9rem;
}

.privacy-hint {
  color: var(--color-text-muted);
}

.validation-message {
  color: var(--color-warning);
}

.preview-card {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: color-mix(in srgb, var(--color-surface) 92%, var(--color-primary) 8%);
  padding: 0.9rem 1rem;
  margin-bottom: 1rem;
}

.preview-label {
  display: block;
  margin-bottom: 0.55rem;
  color: var(--color-text-muted);
  font-size: 0.85rem;
  font-weight: 600;
}

.preview-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.preview-rank {
  font-weight: 700;
}

.preview-identity {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
  min-width: 0;
}

.preview-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-badge {
  border-radius: 999px;
  background: var(--color-primary);
  color: white;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.5rem;
}

.preview-wealth {
  font-weight: 700;
  white-space: nowrap;
}

.success-message {
  color: var(--color-success);
  margin-top: 0.75rem;
}

.error-message {
  color: var(--color-danger);
  margin-top: 0.75rem;
}

@media (max-width: 640px) {
  .preview-row {
    align-items: flex-start;
    flex-direction: column;
  }

  .preview-identity {
    width: 100%;
  }
}
</style>
