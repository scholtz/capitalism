<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest as gqlMasterRequest, GraphQLError } from '@/lib/graphqlMasterServer'

const { t } = useI18n()
const auth = useAuthStore()

const loading = ref(true)
const saving = ref(false)
const saveError = ref<string | null>(null)
const saveSuccess = ref<string | null>(null)

const personalAccountName = ref('')
const originalPersonalAccountName = ref('')
const availabilityChecking = ref(false)
const availabilityMessage = ref<string | null>(null)
const isAvailable = ref<boolean | null>(null)

const hasChanges = computed(() => personalAccountName.value.trim() !== originalPersonalAccountName.value.trim())

const PERSONAL_ACCOUNT_NAME_QUERY = `
  query {
    personalAccountName
  }
`

const NAME_AVAILABILITY_QUERY = `
  query IsPersonalAccountNameAvailable($personalAccountName: String!) {
    isPersonalAccountNameAvailable(personalAccountName: $personalAccountName)
  }
`

const UPDATE_PERSONAL_ACCOUNT_NAME_MUTATION = `
  mutation UpdatePersonalAccountName($input: UpdatePersonalAccountNameInput!) {
    updatePersonalAccountName(input: $input) {
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
    const nextName = data.personalAccountName ?? auth.player?.personalAccountName ?? auth.player?.displayName ?? ''
    personalAccountName.value = nextName
    originalPersonalAccountName.value = nextName
    isAvailable.value = null
    availabilityMessage.value = null
  } catch (e: unknown) {
    saveError.value = e instanceof Error ? e.message : t('playerSettings.loadFailed')
  } finally {
    loading.value = false
  }
}

async function checkAvailability() {
  const trimmedName = personalAccountName.value.trim()
  if (!trimmedName || trimmedName === originalPersonalAccountName.value.trim()) {
    isAvailable.value = null
    availabilityMessage.value = null
    return
  }

  availabilityChecking.value = true
  try {
    const data = await gqlMasterRequest<{ isPersonalAccountNameAvailable: boolean }>(
      NAME_AVAILABILITY_QUERY,
      { personalAccountName: trimmedName },
    )
    isAvailable.value = data.isPersonalAccountNameAvailable
    availabilityMessage.value = data.isPersonalAccountNameAvailable
      ? t('playerSettings.nameAvailable')
      : t('playerSettings.nameUnavailable')
  } catch {
    isAvailable.value = null
    availabilityMessage.value = null
  } finally {
    availabilityChecking.value = false
  }
}

let availabilityTimeout: ReturnType<typeof setTimeout> | null = null
watch(personalAccountName, () => {
  if (availabilityTimeout) {
    clearTimeout(availabilityTimeout)
  }
  availabilityTimeout = setTimeout(() => {
    void checkAvailability()
  }, 250)
})

async function savePersonalAccountName() {
  const trimmedName = personalAccountName.value.trim()
  if (!trimmedName || !hasChanges.value) {
    return
  }

  saving.value = true
  saveError.value = null
  saveSuccess.value = null
  try {
    const data = await gqlMasterRequest<{
      updatePersonalAccountName: { id: string; personalAccountName: string | null }
    }>(UPDATE_PERSONAL_ACCOUNT_NAME_MUTATION, {
      input: {
        personalAccountName: trimmedName,
      },
    })

    const updatedName = data.updatePersonalAccountName.personalAccountName ?? trimmedName
    originalPersonalAccountName.value = updatedName
    personalAccountName.value = updatedName
    isAvailable.value = true
    availabilityMessage.value = t('playerSettings.nameAvailable')
    if (auth.player) {
      auth.player.personalAccountName = updatedName
    }
    saveSuccess.value = t('playerSettings.saveSuccess')
  } catch (e: unknown) {
    if (e instanceof GraphQLError && e.code === 'DUPLICATE_PERSONAL_ACCOUNT_NAME') {
      isAvailable.value = false
      availabilityMessage.value = t('playerSettings.nameUnavailable')
    }
    saveError.value = e instanceof Error ? e.message : t('playerSettings.saveFailed')
  } finally {
    saving.value = false
  }
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
      <label for="personalAccountName">{{ t('playerSettings.displayNameLabel') }}</label>
      <input
        id="personalAccountName"
        v-model="personalAccountName"
        type="text"
        maxlength="120"
        :placeholder="t('playerSettings.displayNamePlaceholder')"
      />

      <p v-if="availabilityChecking" class="availability-hint">{{ t('playerSettings.checkingAvailability') }}</p>
      <p v-else-if="availabilityMessage" class="availability-hint" :class="{ available: isAvailable, unavailable: isAvailable === false }">
        {{ availabilityMessage }}
      </p>

      <div class="actions">
        <button class="btn btn-primary" :disabled="saving || !hasChanges" @click="savePersonalAccountName">
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
  padding: 1rem;
  background: var(--color-surface);
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

.availability-hint {
  margin: 0 0 0.75rem;
  font-size: 0.9rem;
}

.availability-hint.available {
  color: var(--color-success);
}

.availability-hint.unavailable {
  color: var(--color-danger);
}

.success-message {
  color: var(--color-success);
  margin-top: 0.75rem;
}

.error-message {
  color: var(--color-danger);
  margin-top: 0.75rem;
}
</style>
