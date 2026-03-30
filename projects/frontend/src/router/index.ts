import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue') },
    { path: '/onboarding', name: 'onboarding', component: () => import('@/views/OnboardingView.vue') },
    { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
    { path: '/leaderboard', name: 'leaderboard', component: () => import('@/views/LeaderboardView.vue') },
    { path: '/encyclopedia', name: 'encyclopedia', component: () => import('@/views/ManufacturingEncyclopediaView.vue') },
    { path: '/encyclopedia/resources/:slug', name: 'encyclopedia-detail', component: () => import('@/views/ResourceDetailView.vue') },
    { path: '/buy-building/:companyId', name: 'buy-building', component: () => import('@/views/BuyBuildingView.vue') },
    { path: '/building/:id', name: 'building-detail', component: () => import('@/views/BuildingDetailView.vue') },
    { path: '/city/:id', name: 'city-map', component: () => import('@/views/CityMapView.vue') },
    { path: '/ledger/:companyId', name: 'ledger', component: () => import('@/views/LedgerView.vue') },
    { path: '/company/:companyId/settings', name: 'company-settings', component: () => import('@/views/CompanySettingsView.vue') },
  ],
  scrollBehavior() {
    return { top: 0 }
  },
})

export default router
