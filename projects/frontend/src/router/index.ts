import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: HomeView },
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue') },
    { path: '/onboarding', name: 'onboarding', component: () => import('@/views/OnboardingView.vue') },
    { path: '/dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue') },
    { path: '/settings', name: 'player-settings', component: () => import('@/views/PlayerSettingsView.vue') },
    { path: '/player/:id', name: 'player-profile', component: () => import('@/views/PlayerProfileView.vue') },
    { path: '/news', name: 'news', component: () => import('@/views/NewsView.vue') },
    {
      path: '/operations',
      component: () => import('@/views/operations/OperationsLayoutView.vue'),
      children: [
        { path: '', redirect: '/operations/statistics' },
        { path: 'statistics', name: 'operations-statistics', component: () => import('@/views/operations/OperationsStatisticsView.vue') },
        { path: 'news', name: 'operations-news-list', component: () => import('@/views/operations/OperationsNewsListView.vue') },
        { path: 'news/new', name: 'operations-news-new', component: () => import('@/views/operations/OperationsNewsEditorView.vue') },
        { path: 'players', name: 'operations-players', component: () => import('@/views/operations/OperationsPlayersView.vue') },
        { path: 'players/:playerId', name: 'operations-player-detail', component: () => import('@/views/operations/OperationsPlayerDetailView.vue') },
        { path: 'products', name: 'operations-products', component: () => import('@/views/operations/OperationsProductAnalyticsView.vue') },
      ],
    },
    { path: '/admin', name: 'admin-dashboard', component: () => import('@/views/GameAdminDashboardView.vue') },
    { path: '/leaderboard', name: 'leaderboard', component: () => import('@/views/LeaderboardView.vue') },
    { path: '/encyclopedia', name: 'encyclopedia', component: () => import('@/views/ManufacturingEncyclopediaView.vue') },
    { path: '/exchange', name: 'exchange', component: () => import('@/views/GlobalExchangeView.vue') },
    { path: '/stocks', name: 'stocks', component: () => import('@/views/StockExchangeView.vue') },
    { path: '/encyclopedia/resources/:slug', name: 'encyclopedia-detail', component: () => import('@/views/ResourceDetailView.vue') },
    { path: '/buy-building/:companyId', name: 'buy-building', component: () => import('@/views/BuyBuildingView.vue') },
    { path: '/building/:id', name: 'building-detail', component: () => import('@/views/BuildingDetailView.vue') },
    { path: '/city/:id', name: 'city-map', component: () => import('@/views/CityMapView.vue') },
    { path: '/ledger/:companyId', name: 'ledger', component: () => import('@/views/LedgerView.vue') },
    { path: '/company/:companyId/settings', name: 'company-settings', component: () => import('@/views/CompanySettingsView.vue') },
    { path: '/loans', name: 'loan-marketplace', component: () => import('@/views/LoanMarketplaceView.vue') },
    { path: '/bank/:buildingId', name: 'bank-management', component: () => import('@/views/BankManagementView.vue') },
  ],
  scrollBehavior() {
    return { top: 0 }
  },
})

export default router
