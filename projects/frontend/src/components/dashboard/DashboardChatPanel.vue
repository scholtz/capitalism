<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { gqlRequest } from '@/lib/graphql'
import { deepEqual } from '@/lib/utils'
import type { InGameChatMessage } from '@/types'

const { t, locale } = useI18n()
const auth = useAuthStore()

const messages = ref<InGameChatMessage[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const draftMessage = ref('')
const sendError = ref<string | null>(null)
const sending = ref(false)
const CHAT_REFRESH_INTERVAL_MS = 10000

let refreshTimer: ReturnType<typeof setInterval> | null = null

const trimmedDraft = computed(() => draftMessage.value.trim())

function formatSentAt(sentAtUtc: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(sentAtUtc))
}

async function loadMessages(isRefresh = false) {
  if (!auth.isAuthenticated) {
    loading.value = false
    return
  }

  if (!isRefresh) {
    loading.value = true
  }

  error.value = null

  try {
    const data = await gqlRequest<{ chatMessages: InGameChatMessage[] }>(
      `query ChatMessages($limit: Int!) {
        chatMessages(limit: $limit) {
          id
          playerId
          playerDisplayName
          message
          sentAtUtc
          isOwnMessage
        }
      }`,
      { limit: 50 },
    )

    if (!deepEqual(messages.value, data.chatMessages)) {
      messages.value = data.chatMessages
    }
  } catch (reason: unknown) {
    error.value = reason instanceof Error ? reason.message : t('chat.loadFailed')
  } finally {
    if (!isRefresh) {
      loading.value = false
    }
  }
}

async function sendMessage() {
  if (!trimmedDraft.value) {
    return
  }

  sending.value = true
  sendError.value = null

  try {
    await gqlRequest<{ sendChatMessage: InGameChatMessage }>(
      `mutation SendChatMessage($input: SendChatMessageInput!) {
        sendChatMessage(input: $input) {
          id
        }
      }`,
      { input: { message: trimmedDraft.value } },
    )
    draftMessage.value = ''
    await loadMessages(true)
  } catch (reason: unknown) {
    sendError.value = reason instanceof Error ? reason.message : t('chat.sendFailed')
  } finally {
    sending.value = false
  }
}

onMounted(async () => {
  await loadMessages()
  refreshTimer = setInterval(() => {
    void loadMessages(true)
  }, CHAT_REFRESH_INTERVAL_MS)
})

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
  }
})
</script>

<template>
  <section class="chat-panel" aria-labelledby="dashboard-chat-title">
    <div class="chat-header">
      <div>
        <p class="chat-eyebrow">{{ t('chat.eyebrow') }}</p>
        <h2 id="dashboard-chat-title">{{ t('chat.title') }}</h2>
      </div>
      <span class="chat-online-indicator">{{ t('chat.sharedRoom') }}</span>
    </div>

    <p class="chat-description">{{ t('chat.description') }}</p>

    <div v-if="loading" class="chat-state">{{ t('common.loading') }}</div>
    <div v-else-if="error" class="chat-state chat-state-error" role="alert">{{ error }}</div>
    <div v-else-if="messages.length === 0" class="chat-state">{{ t('chat.empty') }}</div>
    <div v-else class="chat-log" role="log" aria-live="polite">
      <article
        v-for="message in messages"
        :key="message.id"
        :class="['chat-message', { 'chat-message-own': message.isOwnMessage }]"
      >
        <div class="chat-message-meta">
          <strong>{{ message.playerDisplayName }}</strong>
          <span>{{ formatSentAt(message.sentAtUtc) }}</span>
        </div>
        <p class="chat-message-body">{{ message.message }}</p>
      </article>
    </div>

    <form class="chat-form" @submit.prevent="sendMessage">
      <label class="chat-input-wrapper">
        <span class="sr-only">{{ t('chat.inputLabel') }}</span>
        <input
          v-model="draftMessage"
          class="chat-input"
          type="text"
          maxlength="300"
          :placeholder="t('chat.placeholder')"
          :aria-label="t('chat.inputLabel')"
        />
      </label>
      <button class="btn btn-primary chat-send-button" type="submit" :disabled="sending || !trimmedDraft">
        {{ sending ? t('common.saving') : t('chat.send') }}
      </button>
    </form>

    <p v-if="sendError" class="chat-state chat-state-error" role="alert">{{ sendError }}</p>
  </section>
</template>

<style scoped>
.chat-panel {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 1.25rem;
  margin-bottom: 1.5rem;
}

.chat-header {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.chat-eyebrow {
  margin: 0 0 0.25rem;
  color: var(--color-primary);
  text-transform: uppercase;
  font-size: 0.75rem;
  letter-spacing: 0.08em;
}

.chat-header h2 {
  margin: 0;
}

.chat-online-indicator {
  color: var(--color-text-secondary);
  font-size: 0.875rem;
}

.chat-description {
  margin: 0.75rem 0 1rem;
  color: var(--color-text-secondary);
}

.chat-log {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-height: 20rem;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.chat-message {
  background: var(--color-surface-secondary);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 0.75rem;
}

.chat-message-own {
  border-color: var(--color-primary);
}

.chat-message-meta {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.35rem;
  color: var(--color-text-secondary);
  font-size: 0.8rem;
}

.chat-message-body {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
}

.chat-form {
  display: flex;
  gap: 0.75rem;
  margin-top: 1rem;
}

.chat-input-wrapper {
  flex: 1;
}

.chat-input {
  width: 100%;
  min-width: 0;
}

.chat-state {
  padding: 0.75rem 0;
  color: var(--color-text-secondary);
}

.chat-state-error {
  color: #ff9b9b;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 720px) {
  .chat-form {
    flex-direction: column;
  }
}
</style>
