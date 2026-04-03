<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()

const mode = ref<'login' | 'register'>('login')
const email = ref('')
const displayName = ref('')
const password = ref('')
const formError = ref('')

async function submit() {
  formError.value = ''
  try {
    if (mode.value === 'register') {
      await auth.register(email.value, displayName.value, password.value)
    } else {
      await auth.login(email.value, password.value)
    }
    await auth.fetchSubscription()
    await router.push('/')
  } catch (e: unknown) {
    formError.value = e instanceof Error ? e.message : 'Something went wrong. Please try again.'
  }
}
</script>

<template>
  <main class="login-shell">
    <div class="login-card">
      <div class="login-brand">
        <p class="eyebrow">Capitalism Network</p>
        <h1>{{ mode === 'login' ? 'Sign in' : 'Create account' }}</h1>
        <p class="login-sub">
          {{
            mode === 'login'
              ? 'Access your Pro subscription and server directory.'
              : 'Join the Capitalism Network to track your subscription.'
          }}
        </p>
      </div>

      <form class="login-form" @submit.prevent="submit">
        <div class="field-group">
          <label for="email">Email</label>
          <input
            id="email"
            v-model="email"
            type="email"
            autocomplete="email"
            placeholder="you@example.com"
            required
          />
        </div>

        <div v-if="mode === 'register'" class="field-group">
          <label for="displayName">Display name</label>
          <input
            id="displayName"
            v-model="displayName"
            type="text"
            autocomplete="name"
            placeholder="Your name in the simulation"
            required
          />
        </div>

        <div class="field-group">
          <label for="password">Password</label>
          <input
            id="password"
            v-model="password"
            type="password"
            autocomplete="current-password"
            placeholder="••••••••"
            required
          />
        </div>

        <p v-if="formError" class="form-error" role="alert">{{ formError }}</p>

        <button class="submit-btn" type="submit" :disabled="auth.loading">
          {{ auth.loading ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account' }}
        </button>
      </form>

      <p class="toggle-mode">
        <span v-if="mode === 'login'">
          Don't have an account?
          <button class="link-btn" type="button" @click="mode = 'register'">Register</button>
        </span>
        <span v-else>
          Already have an account?
          <button class="link-btn" type="button" @click="mode = 'login'">Sign in</button>
        </span>
      </p>

      <p class="back-link">
        <a href="/">← Back to server directory</a>
      </p>
    </div>
  </main>
</template>

<style scoped>
.login-shell {
  min-height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem 1rem;
}

.login-card {
  width: min(440px, 100%);
  padding: 2.4rem;
  border-radius: 32px;
  background: rgba(255, 251, 243, 0.92);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-soft);
}

.login-brand {
  margin-bottom: 1.8rem;
}

.login-brand h1 {
  margin-top: 0.35rem;
  font-size: 2rem;
}

.login-sub {
  margin-top: 0.5rem;
  color: var(--color-muted);
  font-size: 0.95rem;
}

.eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.14em;
  font-size: 0.72rem;
  color: var(--color-accent);
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 1.1rem;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.field-group label {
  font-size: 0.88rem;
  font-weight: 500;
  color: var(--color-ink);
}

.field-group input {
  padding: 0.8rem 1rem;
  border-radius: 14px;
  border: 1px solid var(--color-border);
  background: var(--color-paper-strong);
  font: inherit;
  color: var(--color-ink);
  outline: none;
  transition: border-color 0.15s;
}

.field-group input:focus {
  border-color: var(--color-accent);
}

.form-error {
  padding: 0.75rem 1rem;
  border-radius: 14px;
  background: rgba(176, 67, 44, 0.08);
  color: #a03826;
  font-size: 0.9rem;
}

.submit-btn {
  margin-top: 0.4rem;
  padding: 0.9rem 1.2rem;
  border-radius: 999px;
  border: none;
  background: var(--color-ink);
  color: var(--color-paper);
  font: inherit;
  font-weight: 700;
  font-size: 1rem;
  cursor: pointer;
  transition: opacity 0.15s, transform 0.15s;
}

.submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.submit-btn:not(:disabled):hover {
  transform: translateY(-1px);
}

.toggle-mode {
  margin-top: 1.2rem;
  text-align: center;
  color: var(--color-muted);
  font-size: 0.9rem;
}

.link-btn {
  background: none;
  border: none;
  color: var(--color-ink);
  font: inherit;
  font-weight: 700;
  cursor: pointer;
  text-decoration: underline;
}

.back-link {
  margin-top: 0.8rem;
  text-align: center;
  font-size: 0.87rem;
  color: var(--color-muted);
}

.back-link a:hover {
  color: var(--color-ink);
}
</style>
