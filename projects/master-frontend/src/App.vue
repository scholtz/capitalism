<script setup lang="ts">
import { onMounted } from 'vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()

onMounted(() => {
  auth.initFromStorage()
  if (auth.isAuthenticated) {
    void auth.fetchProfile()
    void auth.fetchSubscription()
  }
})
</script>

<template>
  <RouterView />
</template>

<style scoped></style>