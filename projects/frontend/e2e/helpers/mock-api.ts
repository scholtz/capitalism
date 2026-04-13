/**
 * Shared GraphQL API mock for Capitalism V Playwright tests.
 *
 * Usage:
 *   import { setupMockApi, makePlayer } from './helpers/mock-api'
 *   test('my test', async ({ page }) => {
 *     const state = setupMockApi(page)
 *     await page.goto('/')
 *   })
 */

import type { Page } from '@playwright/test'

export type MockPlayer = {
  id: string
  email: string
  password: string
  displayName: string
  role: 'PLAYER' | 'ADMIN'
  isInvisibleInChat: boolean
  createdAtUtc: string
  lastLoginAtUtc: string | null
  personalCash: number
  activeAccountType: 'PERSON' | 'COMPANY'
  activeCompanyId: string | null
  onboardingCompletedAtUtc: string | null
  onboardingCurrentStep: string | null
  onboardingIndustry: string | null
  onboardingCityId: string | null
  onboardingCompanyId: string | null
  onboardingFactoryLotId: string | null
  onboardingShopBuildingId: string | null
  onboardingFirstSaleCompletedAtUtc: string | null
  proSubscriptionEndsAtUtc: string | null
  dividendPayments: MockDividendPayment[]
  stockTrades: MockPersonTradeRecord[]
  companies: MockCompany[]
}

export type MockDividendPayment = {
  id: string
  companyId: string
  companyName: string
  shareCount: number
  amountPerShare: number
  totalAmount: number
  gameYear: number
  recordedAtTick: number
  recordedAtUtc: string
  description: string
}

export type MockPersonTradeRecord = {
  id: string
  companyId: string
  companyName: string
  direction: 'BUY' | 'SELL'
  shareCount: number
  pricePerShare: number
  totalValue: number
  recordedAtTick: number
  recordedAtUtc: string
}
export type MockLedgerSummary = {
  companyId: string
  companyName: string
  gameYear?: number
  isCurrentGameYear?: boolean
  currentCash: number
  totalRevenue: number
  totalPurchasingCosts: number
  totalShippingCosts?: number
  totalLaborCosts: number
  totalEnergyCosts: number
  totalMarketingCosts: number
  totalTaxPaid: number
  totalOtherCosts: number
  taxableIncome?: number
  estimatedIncomeTax?: number
  netIncome: number
  propertyValue: number
  propertyAppreciation: number
  buildingValue: number
  inventoryValue: number
  totalAssets: number
  totalPropertyPurchases: number
  totalStockPurchaseCashOut?: number
  totalStockSaleCashIn?: number
  cashFromOperations: number
  cashFromInvestments: number
  firstRecordedTick: number
  lastRecordedTick: number
  incomeTaxDueAtTick?: number
  incomeTaxDueGameTimeUtc?: string
  incomeTaxDueGameYear?: number
  isIncomeTaxSettled?: boolean
  history?: MockLedgerHistoryYear[]
  buildingSummaries: Array<{
    buildingId: string
    buildingName: string
    buildingType: string
    revenue: number
    costs: number
  }>
}

export type MockLedgerHistoryYear = {
  gameYear: number
  isCurrentGameYear: boolean
  totalRevenue: number
  totalLaborCosts: number
  totalEnergyCosts: number
  netIncome: number
  totalTaxPaid: number
  taxableIncome: number
  estimatedIncomeTax: number
  firstRecordedTick: number
  lastRecordedTick: number
}

export type MockLedgerEntry = {
  id: string
  category: string
  description: string
  amount: number
  recordedAtTick: number
  buildingId: string | null
  buildingName: string | null
  buildingUnitId: string | null
  productTypeId: string | null
  productName: string | null
  resourceTypeId: string | null
  resourceName: string | null
}

export type MockCompany = {
  id: string
  playerId: string
  name: string
  cash: number
  totalSharesIssued?: number
  dividendPayoutRatio?: number
  foundedAtUtc: string
  foundedAtTick?: number
  citySalaryMultipliers?: Record<string, number>
  buildings: MockBuilding[]
}

export type MockShareholding = {
  companyId: string
  ownerPlayerId: string | null
  ownerCompanyId: string | null
  shareCount: number
}

export type MockStockPriceHistoryPoint = {
  companyId: string
  tick: number
  price: number
  recordedAtUtc: string
}

export type MockBuilding = {
  id: string
  companyId: string
  cityId: string
  type: string
  name: string
  latitude: number
  longitude: number
  level: number
  powerConsumption: number
  isForSale: boolean
  askingPrice?: number | null
  pricePerSqm?: number | null
  occupancyPercent?: number | null
  totalAreaSqm?: number | null
  pendingPricePerSqm?: number | null
  pendingPriceActivationTick?: number | null
  powerPlantType?: string | null
  powerOutput?: number | null
  /** Power supply status: POWERED | CONSTRAINED | OFFLINE */
  powerStatus?: string
  mediaType?: string | null
  interestRate?: number | null
  builtAtUtc?: string
  /** True while the building is still under construction */
  isUnderConstruction?: boolean
  /** Tick number when construction finishes; null if not under construction */
  constructionCompletesAtTick?: number | null
  /** Cash cost charged for construction */
  constructionCost?: number
  units: MockBuildingUnit[]
  pendingConfiguration: MockBuildingConfigurationPlan | null
}

export type MockUnitInventoryItem = {
  id?: string
  resourceTypeId?: string | null
  productTypeId?: string | null
  quantity: number
  quality?: number | null
  sourcingCostTotal?: number | null
}

export type MockUnitResourceHistoryPoint = {
  buildingUnitId?: string
  resourceTypeId?: string | null
  productTypeId?: string | null
  tick: number
  inflowQuantity?: number
  outflowQuantity?: number
  consumedQuantity?: number
  producedQuantity?: number
}

export type MockBuildingUnit = {
  id: string
  buildingId: string
  unitType: string
  gridX: number
  gridY: number
  level: number
  linkUp: boolean
  linkDown: boolean
  linkLeft: boolean
  linkRight: boolean
  linkUpLeft: boolean
  linkUpRight: boolean
  linkDownLeft: boolean
  linkDownRight: boolean
  resourceTypeId?: string | null
  productTypeId?: string | null
  minPrice?: number | null
  maxPrice?: number | null
  purchaseSource?: string | null
  saleVisibility?: string | null
  budget?: number | null
  mediaHouseBuildingId?: string | null
  minQuality?: number | null
  brandScope?: string | null
  vendorLockCompanyId?: string | null
  lockedCityId?: string | null
  inventoryQuantity?: number | null
  inventoryQuality?: number | null
  inventorySourcingCostTotal?: number | null
  inventoryItems?: MockUnitInventoryItem[]
  resourceHistory?: MockUnitResourceHistoryPoint[]
}

export type MockBuildingConfigurationPlanUnit = MockBuildingUnit & {
  startedAtTick: number
  appliesAtTick: number
  ticksRequired: number
  isChanged: boolean
  isReverting: boolean
}

export type MockBuildingConfigurationPlanRemoval = {
  id: string
  gridX: number
  gridY: number
  startedAtTick: number
  appliesAtTick: number
  ticksRequired: number
  isReverting: boolean
}

export type MockBuildingConfigurationPlan = {
  id: string
  buildingId: string
  submittedAtUtc: string
  submittedAtTick: number
  appliesAtTick: number
  totalTicksRequired: number
  units: MockBuildingConfigurationPlanUnit[]
  removals: MockBuildingConfigurationPlanRemoval[]
}

export type MockCity = {
  id: string
  name: string
  countryCode: string
  latitude: number
  longitude: number
  population: number
  averageRentPerSqm: number
  baseSalaryPerManhour: number
  resources: { resourceType: { id: string; name: string; slug: string; category: string }; abundance: number }[]
}

export type MockBuildingLot = {
  id: string
  cityId: string
  name: string
  description: string
  district: string
  latitude: number
  longitude: number
  populationIndex: number
  basePrice: number
  price: number
  suitableTypes: string
  ownerCompanyId: string | null
  buildingId: string | null
  ownerCompany: { id: string; name: string } | null
  building: {
    id: string
    name: string
    type: string
    isUnderConstruction?: boolean
    constructionCompletesAtTick?: number | null
    constructionCost?: number
  } | null
  resourceType: { id: string; name: string; slug: string } | null
  materialQuality: number | null
  materialQuantity: number | null
}

export type MockResourceType = {
  id: string
  name: string
  slug: string
  category: string
  basePrice: number
  weightPerUnit: number
  unitName: string
  unitSymbol: string
  imageUrl?: string | null
  description: string | null
}

export type MockProductType = {
  id: string
  name: string
  slug: string
  industry: string
  basePrice: number
  baseCraftTicks: number
  outputQuantity?: number
  energyConsumptionMwh?: number
  basicLaborHours?: number
  unitName?: string
  unitSymbol?: string
  imageUrl?: string | null
  isProOnly: boolean
  isUnlockedForCurrentPlayer?: boolean
  description: string | null
  recipes: {
    resourceType?: { id: string; name: string; slug?: string; unitName?: string; unitSymbol?: string } | null
    inputProductType?: { id: string; name: string; slug: string; unitName?: string; unitSymbol?: string } | null
    quantity: number
  }[]
}

export type MockProductExchangeListing = {
  orderId: string
  productTypeId: string
  productName: string
  productSlug: string
  productIndustry: string
  unitSymbol: string
  unitName: string
  basePrice: number
  pricePerUnit: number
  remainingQuantity: number
  sellerCityId: string
  sellerCityName: string
  sellerCompanyId: string
  sellerCompanyName: string
  createdAtUtc: string
}

export type MockChatMessage = {
  id: string
  playerId: string
  message: string
  sentAtUtc: string
}

export type MockResearchBrandState = {
  id: string
  companyId: string
  name: string
  scope: 'PRODUCT' | 'CATEGORY' | 'COMPANY'
  productTypeId: string | null
  productName: string | null
  industryCategory: string | null
  awareness: number
  quality: number
  /** ≥ 1.0: driven by BRAND_QUALITY R&D. >1.0 = marketing budget is more effective. */
  marketingEfficiencyMultiplier: number
}

export type MockPublicSalesRecord = {
  id: string
  buildingId: string
  companyId: string
  productTypeName: string | null
  tick: number
  quantitySold: number
  pricePerUnit: number
  revenue: number
}

export type MockPublicSalesAnalytics = {
  buildingUnitId: string
  buildingId: string
  buildingName: string
  cityName: string
  productTypeId?: string | null
  productName?: string | null
  totalRevenue: number
  totalQuantitySold: number
  averagePricePerUnit: number
  currentSalesCapacity: number
  dataFromTick: number
  dataToTick: number
  demandSignal: string
  actionHint: string
  recentUtilization: number
  trendDirection?: string
  revenueHistory: Array<{ tick: number; revenue: number; quantitySold: number }>
  priceHistory: Array<{ tick: number; pricePerUnit: number }>
  marketShare: Array<{ label: string; companyId: string | null; share: number; isUnmet: boolean }>
  elasticityIndex: number | null
  unmetDemandShare: number | null
  populationIndex: number | null
  inventoryQuality: number | null
  brandAwareness: number | null
  totalProfit: number | null
  profitHistory: Array<{ tick: number; profit: number; grossMarginPct: number | null }> | null
  demandDrivers: Array<{ factor: string; impact: string; score: number; description: string }>
  trendFactor?: number | null
}

export type MockUnitProductAnalytics = {
  buildingUnitId: string
  unitType: string
  productTypeId?: string | null
  productName?: string | null
  dataFromTick: number
  dataToTick: number
  totalCost: number
  totalQuantityProduced: number
  estimatedRevenue: number | null
  estimatedProfit: number | null
  snapshots: Array<{
    tick: number
    laborCost: number
    energyCost: number
    totalCost: number
    quantityProduced: number
    estimatedRevenue: number | null
    estimatedProfit: number | null
  }>
}

export type MockBuildingFinancialTimeline = {
  buildingId: string
  buildingName: string
  dataFromTick: number
  dataToTick: number
  totalSales: number
  totalCosts: number
  totalProfit: number
  timeline: Array<{ tick: number; sales: number; costs: number; profit: number }>
}

export type MockLoanOffer = {
  id: string
  bankBuildingId: string
  bankBuildingName: string
  cityId: string
  cityName: string
  lenderCompanyId: string
  lenderCompanyName: string
  annualInterestRatePercent: number
  maxPrincipalPerLoan: number
  totalCapacity: number
  usedCapacity: number
  remainingCapacity: number
  durationTicks: number
  isActive: boolean
  createdAtTick: number
  createdAtUtc: string
}

export type MockLoan = {
  id: string
  loanOfferId: string
  borrowerCompanyId: string
  borrowerCompanyName: string
  lenderCompanyId: string
  lenderCompanyName: string
  bankBuildingId: string
  bankBuildingName: string
  originalPrincipal: number
  remainingPrincipal: number
  annualInterestRatePercent: number
  durationTicks: number
  startTick: number
  dueTick: number
  nextPaymentTick: number
  paymentAmount: number
  paymentsMade: number
  totalPayments: number
  status: 'ACTIVE' | 'OVERDUE' | 'DEFAULTED' | 'REPAID'
  missedPayments: number
  accumulatedPenalty: number
  acceptedAtUtc: string
  closedAtUtc: string | null
}

export type MockGameNewsLocalization = {
  locale: string
  title: string
  summary: string
  htmlContent: string
}

export type MockGameNewsEntry = {
  id: string
  entryType: 'NEWS' | 'CHANGELOG'
  status: 'DRAFT' | 'PUBLISHED'
  targetServerKey: string | null
  createdByEmail: string
  updatedByEmail: string
  createdAtUtc: string
  updatedAtUtc: string
  publishedAtUtc: string | null
  localizations: MockGameNewsLocalization[]
  readByPlayerIds: string[]
}

export type MockGlobalGameAdminGrant = {
  id: string
  email: string
  grantedByEmail: string
  grantedAtUtc: string
  updatedAtUtc: string
}

export type MockGameAdminMoneyInflowSummary = {
  category: string
  amount: number
  description: string
}

export type MockGameAdminShippingCostSummary = {
  companyId: string
  companyName: string
  amount: number
  entryCount: number
}

export type MockGameAdminMultiAccountAlert = {
  reason: string
  exposureAmount: number
  confidenceScore: number
  supportingEntityType: string
  supportingEntityName: string
  primaryPlayerId: string
  relatedPlayerId: string
}

export type MockGameAdminAuditLog = {
  id: string
  adminActorPlayerId: string
  adminActorEmail: string
  adminActorDisplayName: string
  effectivePlayerId: string
  effectivePlayerEmail: string
  effectivePlayerDisplayName: string
  effectiveAccountType: 'PERSON' | 'COMPANY'
  effectiveCompanyId: string | null
  effectiveCompanyName: string | null
  graphQlOperationName: string | null
  mutationSummary: string
  responseStatusCode: number
  recordedAtUtc: string
}

export type MockImpersonationSession = {
  adminActorUserId: string
  effectiveUserId: string
  effectiveAccountType: 'PERSON' | 'COMPANY'
  effectiveCompanyId: string | null
}

export type MockBuildingLayoutTemplate = {
  id: string
  ownerPlayerId: string
  name: string
  description: string | null
  buildingType: string
  unitsJson: string
  updatedAtUtc: string
}

export type MockState = {
  serverKey: string
  players: MockPlayer[]
  shareholdings: MockShareholding[]
  cities: MockCity[]
  buildingLots: MockBuildingLot[]
  resourceTypes: MockResourceType[]
  productTypes: MockProductType[]
  currentUserId: string | null
  currentToken: string | null
  gameState: { currentTick: number; lastTickAtUtc: string; tickIntervalSeconds: number; taxCycleTicks: number; taxRate: number }
  stockPriceHistory: Record<string, MockStockPriceHistoryPoint[]>
  ledgerData: Record<string, MockLedgerSummary>
  drillDownData: Record<string, MockLedgerEntry[]>
  /** Research brand states keyed by companyId for the companyBrands query. */
  researchBrands: Record<string, MockResearchBrandState[]>
  /** Public sales records for first-sale milestone detection. */
  publicSalesRecords: MockPublicSalesRecord[]
  /** Public sales analytics by unit ID */
  publicSalesAnalytics: Record<string, MockPublicSalesAnalytics>
  /** Unit product analytics by unit ID (for MANUFACTURING units) */
  unitProductAnalytics: Record<string, MockUnitProductAnalytics>
  /** Building financial history keyed by building ID */
  buildingFinancialTimelines: Record<string, MockBuildingFinancialTimeline>
  /** Loan offers available in the marketplace */
  loanOffers: MockLoanOffer[]
  /** Active loans for the current player's companies */
  myLoans: MockLoan[]
  /** Mock procurement preview response keyed by unit ID. If null/missing, returns a default GLOBAL_EXCHANGE preview. */
  procurementPreviews: Record<string, object | null>
  /** Mock sourcing candidates response keyed by unit ID. If null/missing, returns default candidates. */
  sourcingCandidates: Record<string, object[] | null>
  /** Mock unit upgrade info response keyed by unit ID. If null/missing, returns a default upgradable Manufacturing/level-1 response. */
  unitUpgradeInfoOverrides: Record<string, object | null>
  /** If set to a unit ID, scheduleUnitUpgrade will return INSUFFICIENT_FUNDS for that unit. */
  upgradeInsufficientFundsUnitId: string | null
  /** Active player SELL exchange orders for products (the globalExchangeProductListings query). */
  productExchangeListings: MockProductExchangeListing[]
  chatMessages: MockChatMessage[]
  rootAdminEmails: string[]
  globalGameAdminGrants: MockGlobalGameAdminGrant[]
  gameNewsEntries: MockGameNewsEntry[]
  adminMoneyInflowSummaries: MockGameAdminMoneyInflowSummary[]
  adminShippingCostSummaries: MockGameAdminShippingCostSummary[]
  adminMultiAccountAlerts: MockGameAdminMultiAccountAlert[]
  adminAuditLogs: MockGameAdminAuditLog[]
  impersonationSession: MockImpersonationSession | null
  buildingLayouts: MockBuildingLayoutTemplate[]
  /** When set, the next StoreBuildingConfiguration call returns this string as a CONTRADICTORY_LINK error. */
  forceBuildingConfigError: string | null
}

const mockStateByPage = new WeakMap<Page, MockState>()

const PERSONAL_STARTING_CASH = 200000
const STARTER_FOUNDER_CONTRIBUTION = 200000
const DEFAULT_IPO_RAISE_TARGET = 400000
const DEFAULT_COMPANY_SHARE_COUNT = 10000
const DEFAULT_DIVIDEND_PAYOUT_RATIO = 0.2
const GAME_START_YEAR = 2000
const TICKS_PER_DAY = 24
const TICKS_PER_YEAR = 24 * 365

function resolveIpoSelection(raiseTarget?: number) {
  switch (raiseTarget ?? DEFAULT_IPO_RAISE_TARGET) {
    case 400000:
      return { raiseTarget: 400000, founderOwnershipRatio: 0.5 }
    case 600000:
      return { raiseTarget: 600000, founderOwnershipRatio: 0.3333 }
    case 800000:
      return { raiseTarget: 800000, founderOwnershipRatio: 0.25 }
    default:
      return { raiseTarget: DEFAULT_IPO_RAISE_TARGET, founderOwnershipRatio: 0.5 }
  }
}

function getCompanyTotalShares(company: MockCompany) {
  return company.totalSharesIssued ?? DEFAULT_COMPANY_SHARE_COUNT
}

function getCompanyDividendPayoutRatio(company: MockCompany) {
  return company.dividendPayoutRatio ?? DEFAULT_DIVIDEND_PAYOUT_RATIO
}

function getCompanyAssetBaseValue(company: MockCompany) {
  const baseValues: Record<string, number> = {
    MINE: 250000,
    FACTORY: 200000,
    SALES_SHOP: 150000,
    RESEARCH_DEVELOPMENT: 300000,
    APARTMENT: 400000,
    COMMERCIAL: 350000,
    MEDIA_HOUSE: 500000,
    BANK: 600000,
    EXCHANGE: 450000,
    POWER_PLANT: 350000,
  }

  return company.buildings.reduce((total, building) => total + (baseValues[building.type] ?? 0) * building.level, 0)
}

function computeMockSharePrice(company: MockCompany) {
  return Number(Math.max((company.cash + getCompanyAssetBaseValue(company)) / getCompanyTotalShares(company), 1).toFixed(2))
}

function getPlayerControlledCompanyIds(state: MockState, playerId: string) {
  return new Set(
    state.players
      .flatMap((player) => player.companies)
      .filter((company) => company.playerId === playerId)
      .map((company) => company.id),
  )
}

function getPublicFloatShares(state: MockState, company: MockCompany) {
  const issued = getCompanyTotalShares(company)
  const allocated = state.shareholdings.filter((holding) => holding.companyId === company.id).reduce((total, holding) => total + holding.shareCount, 0)

  return Number(Math.max(issued - allocated, 0).toFixed(4))
}

function getOrCreateShareholding(state: MockState, companyId: string, ownerPlayerId: string | null, ownerCompanyId: string | null) {
  let holding = state.shareholdings.find((candidate) => candidate.companyId === companyId && candidate.ownerPlayerId === ownerPlayerId && candidate.ownerCompanyId === ownerCompanyId)

  if (!holding) {
    holding = { companyId, ownerPlayerId, ownerCompanyId, shareCount: 0 }
    state.shareholdings.push(holding)
  }

  return holding
}

function getCombinedControlledOwnershipRatio(state: MockState, playerId: string, company: MockCompany) {
  const controlledCompanyIds = getPlayerControlledCompanyIds(state, playerId)
  const controlledShares = state.shareholdings
    .filter((holding) => holding.companyId === company.id && (holding.ownerPlayerId === playerId || (holding.ownerCompanyId ? controlledCompanyIds.has(holding.ownerCompanyId) : false)))
    .reduce((total, holding) => total + holding.shareCount, 0)

  return Number((controlledShares / getCompanyTotalShares(company)).toFixed(4))
}

function appendMockStockPriceHistory(state: MockState, companyId: string, price: number) {
  const existing = state.stockPriceHistory[companyId] ?? []
  const point: MockStockPriceHistoryPoint = {
    companyId,
    tick: state.gameState.currentTick,
    price,
    recordedAtUtc: new Date().toISOString(),
  }
  state.stockPriceHistory[companyId] = [...existing.filter((candidate) => candidate.tick !== point.tick), point].sort((left, right) => left.tick - right.tick)
}

function ensureMockLedgerSummary(state: MockState, company: MockCompany): MockLedgerSummary {
  const existing = state.ledgerData[company.id]
  if (existing) {
    return existing
  }

  const baseValues: Record<string, number> = {
    MINE: 250000,
    FACTORY: 200000,
    SALES_SHOP: 150000,
    RESEARCH_DEVELOPMENT: 300000,
    APARTMENT: 400000,
    COMMERCIAL: 350000,
    MEDIA_HOUSE: 500000,
    BANK: 600000,
    EXCHANGE: 450000,
    POWER_PLANT: 350000,
  }
  const buildingValue = company.buildings.reduce((sum, building) => sum + (baseValues[building.type] ?? 0) * building.level, 0)
  const summary: MockLedgerSummary = {
    companyId: company.id,
    companyName: company.name,
    gameYear: computeMockGameYear(state.gameState.currentTick),
    isCurrentGameYear: true,
    currentCash: company.cash,
    totalRevenue: 0,
    totalPurchasingCosts: 0,
    totalLaborCosts: 0,
    totalEnergyCosts: 0,
    totalMarketingCosts: 0,
    totalTaxPaid: 0,
    totalOtherCosts: 0,
    taxableIncome: 0,
    estimatedIncomeTax: 0,
    netIncome: 0,
    propertyValue: 0,
    propertyAppreciation: 0,
    buildingValue,
    inventoryValue: 0,
    totalAssets: company.cash + buildingValue,
    totalPropertyPurchases: 0,
    totalStockPurchaseCashOut: 0,
    totalStockSaleCashIn: 0,
    cashFromOperations: 0,
    cashFromInvestments: 0,
    firstRecordedTick: 0,
    lastRecordedTick: 0,
    history: [],
    buildingSummaries: [],
  }
  state.ledgerData[company.id] = summary
  return summary
}

function recordMockCompanyStockLedgerEntry(
  state: MockState,
  company: MockCompany,
  category: 'STOCK_PURCHASE' | 'STOCK_SALE',
  description: string,
  amount: number,
) {
  const summary = ensureMockLedgerSummary(state, company)
  const currentTick = state.gameState.currentTick
  const drillKey = `${company.id}:${category}`
  const existing = state.drillDownData[drillKey] ?? []

  state.drillDownData[drillKey] = [
    {
      id: `${category.toLowerCase()}-${existing.length + 1}-${currentTick}`,
      category,
      description,
      amount,
      recordedAtTick: currentTick,
      buildingId: null,
      buildingName: null,
      buildingUnitId: null,
      productTypeId: null,
      productName: null,
      resourceTypeId: null,
      resourceName: null,
    },
    ...existing,
  ]

  if (category === 'STOCK_PURCHASE') {
    summary.totalStockPurchaseCashOut = Number((summary.totalStockPurchaseCashOut + Math.abs(amount)).toFixed(2))
  } else {
    summary.totalStockSaleCashIn = Number((summary.totalStockSaleCashIn + amount).toFixed(2))
  }

  summary.currentCash = company.cash
  summary.cashFromInvestments = Number((summary.totalStockSaleCashIn - summary.totalPropertyPurchases - summary.totalStockPurchaseCashOut).toFixed(2))
  summary.totalAssets = Number((company.cash + summary.propertyValue + summary.buildingValue + summary.inventoryValue).toFixed(2))
  summary.firstRecordedTick = summary.firstRecordedTick === 0 ? currentTick : Math.min(summary.firstRecordedTick, currentTick)
  summary.lastRecordedTick = Math.max(summary.lastRecordedTick, currentTick)
}

function computeMockGameYear(currentTick: number) {
  return GAME_START_YEAR + Math.floor(Math.max(currentTick, 0) / TICKS_PER_YEAR)
}

function computeMockInGameTimeUtc(currentTick: number) {
  const gameStart = new Date(Date.UTC(GAME_START_YEAR, 0, 1, 0, 0, 0))
  gameStart.setUTCHours(gameStart.getUTCHours() + Math.max(currentTick, 0))
  return gameStart.toISOString()
}

function computeMockNextTaxTick(currentTick: number, taxCycleTicks: number) {
  const cycleTicks = taxCycleTicks > 0 ? taxCycleTicks : TICKS_PER_YEAR
  const safeTick = Math.max(currentTick, 0)
  const cyclesCompleted = Math.floor(safeTick / cycleTicks)
  const currentCycleStart = cyclesCompleted * cycleTicks
  return safeTick === currentCycleStart ? currentCycleStart + cycleTicks : (cyclesCompleted + 1) * cycleTicks
}

function buildMockGameStatePayload(gameState: MockState['gameState']) {
  const currentGameYear = computeMockGameYear(gameState.currentTick)
  const nextTaxTick = computeMockNextTaxTick(gameState.currentTick, gameState.taxCycleTicks)

  return {
    ...gameState,
    currentGameYear,
    currentGameTimeUtc: computeMockInGameTimeUtc(gameState.currentTick),
    ticksPerDay: TICKS_PER_DAY,
    ticksPerYear: TICKS_PER_YEAR,
    nextTaxTick,
    nextTaxGameTimeUtc: computeMockInGameTimeUtc(nextTaxTick),
    nextTaxGameYear: computeMockGameYear(nextTaxTick),
  }
}

function buildMockLedgerHistoryYear(summary: MockLedgerSummary, currentGameYear: number): MockLedgerHistoryYear {
  const gameYear = summary.gameYear ?? currentGameYear
  const taxableIncome =
    summary.taxableIncome ??
    Math.max(summary.totalRevenue - summary.totalPurchasingCosts - (summary.totalShippingCosts ?? 0) - summary.totalLaborCosts - summary.totalEnergyCosts - summary.totalMarketingCosts - summary.totalOtherCosts, 0)

  return {
    gameYear,
    isCurrentGameYear: summary.isCurrentGameYear ?? gameYear === currentGameYear,
    totalRevenue: summary.totalRevenue,
    totalLaborCosts: summary.totalLaborCosts,
    totalEnergyCosts: summary.totalEnergyCosts,
    netIncome: summary.netIncome,
    totalTaxPaid: summary.totalTaxPaid,
    taxableIncome,
    estimatedIncomeTax: summary.estimatedIncomeTax ?? summary.totalTaxPaid,
    firstRecordedTick: summary.firstRecordedTick,
    lastRecordedTick: summary.lastRecordedTick,
  }
}

function buildMockLedgerSummaryPayload(summary: MockLedgerSummary, gameState: MockState['gameState']) {
  const currentGameYear = computeMockGameYear(gameState.currentTick)
  const gameYear = summary.gameYear ?? currentGameYear
  const incomeTaxDueAtTick = summary.incomeTaxDueAtTick ?? (gameYear - GAME_START_YEAR + 1) * TICKS_PER_YEAR

  return {
    ...summary,
    gameYear,
    isCurrentGameYear: summary.isCurrentGameYear ?? gameYear === currentGameYear,
    totalStockPurchaseCashOut: summary.totalStockPurchaseCashOut ?? 0,
    totalStockSaleCashIn: summary.totalStockSaleCashIn ?? 0,
    totalShippingCosts: summary.totalShippingCosts ?? 0,
    taxableIncome:
      summary.taxableIncome ??
      Math.max(summary.totalRevenue - summary.totalPurchasingCosts - (summary.totalShippingCosts ?? 0) - summary.totalLaborCosts - summary.totalEnergyCosts - summary.totalMarketingCosts - summary.totalOtherCosts, 0),
    estimatedIncomeTax: summary.estimatedIncomeTax ?? summary.totalTaxPaid,
    incomeTaxDueAtTick,
    incomeTaxDueGameTimeUtc: summary.incomeTaxDueGameTimeUtc ?? computeMockInGameTimeUtc(incomeTaxDueAtTick),
    incomeTaxDueGameYear: summary.incomeTaxDueGameYear ?? computeMockGameYear(incomeTaxDueAtTick),
    isIncomeTaxSettled: summary.isIncomeTaxSettled ?? gameYear < currentGameYear,
    history: summary.history ?? [buildMockLedgerHistoryYear(summary, currentGameYear)],
  }
}

function buildMockBuildingFinancialTimeline(state: MockState, buildingId: string, limit = 100): MockBuildingFinancialTimeline | null {
  const explicitTimeline = state.buildingFinancialTimelines[buildingId]
  if (explicitTimeline) {
    return explicitTimeline
  }

  const building = state.players
    .flatMap((player) => player.companies)
    .flatMap((company) => company.buildings)
    .find((candidate) => candidate.id === buildingId)

  if (!building) {
    return null
  }

  const safeLimit = Math.max(1, limit)
  const dataToTick = state.gameState.currentTick
  const dataFromTick = Math.max(0, dataToTick - (safeLimit - 1))

  return {
    buildingId,
    buildingName: building.name,
    dataFromTick,
    dataToTick,
    totalSales: 0,
    totalCosts: 0,
    totalProfit: 0,
    timeline: Array.from({ length: dataToTick - dataFromTick + 1 }, (_, index) => ({
      tick: dataFromTick + index,
      sales: 0,
      costs: 0,
      profit: 0,
    })),
  }
}

function cloneUnit(unit: MockBuildingUnit): MockBuildingUnit {
  return {
    ...unit,
    inventoryItems: unit.inventoryItems?.map((item) => ({ ...item })) ?? undefined,
    resourceHistory: unit.resourceHistory?.map((entry) => ({ ...entry })) ?? undefined,
  }
}

function getMockUnitResourceHistory(unit: MockBuildingUnit) {
  return (unit.resourceHistory ?? []).map((entry) => ({
    buildingUnitId: entry.buildingUnitId ?? unit.id,
    resourceTypeId: entry.resourceTypeId ?? null,
    productTypeId: entry.productTypeId ?? null,
    tick: entry.tick,
    inflowQuantity: entry.inflowQuantity ?? 0,
    outflowQuantity: entry.outflowQuantity ?? 0,
    consumedQuantity: entry.consumedQuantity ?? 0,
    producedQuantity: entry.producedQuantity ?? 0,
  }))
}

function computeDistanceKm(latitudeA: number, longitudeA: number, latitudeB: number, longitudeB: number) {
  const earthRadiusKm = 6371
  const deltaLatitude = ((latitudeB - latitudeA) * Math.PI) / 180
  const deltaLongitude = ((longitudeB - longitudeA) * Math.PI) / 180
  const originLatitude = (latitudeA * Math.PI) / 180
  const destinationLatitude = (latitudeB * Math.PI) / 180

  const haversine = Math.sin(deltaLatitude / 2) ** 2 + Math.cos(originLatitude) * Math.cos(destinationLatitude) * Math.sin(deltaLongitude / 2) ** 2
  return earthRadiusKm * (2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine)))
}

function computeMockExchangePrice(basePrice: number, abundance: number, averageRentPerSqm: number) {
  const scarcityMultiplier = 1.55 - Math.min(Math.max(abundance, 0), 1) * 0.75
  const cityMultiplier = 0.95 + averageRentPerSqm / 100
  return Number((basePrice * scarcityMultiplier * cityMultiplier).toFixed(2))
}

function computeMockExchangeQuality(abundance: number) {
  return Number(Math.min(Math.max(0.35 + abundance * 0.6, 0.35), 0.95).toFixed(4))
}

function computeMockTransitCost(weightPerUnit: number, distanceKm: number) {
  const rawCost = distanceKm * Math.max(weightPerUnit, 0.1) * 0.0025
  return Number(Math.max(rawCost, 0.01).toFixed(2))
}

function getMockUnitCapacity(unit: MockBuildingUnit) {
  const level = unit.level
  switch (unit.unitType) {
    case 'STORAGE':
      // Storage units hold 10× the base capacity (mirrors GameConstants.StorageUnitHoldingCapacity)
      return level >= 4 ? 10000 : level === 3 ? 5000 : level === 2 ? 2500 : 1000
    case 'MINING':
    case 'B2B_SALES':
    case 'PURCHASE':
    case 'MANUFACTURING':
    case 'BRANDING':
    case 'PUBLIC_SALES':
      return level >= 4 ? 1000 : level === 3 ? 500 : level === 2 ? 250 : 100
    default:
      return 0
  }
}

function getMockUnitInventoryItems(unit: MockBuildingUnit) {
  if (unit.inventoryItems && unit.inventoryItems.length > 0) {
    return unit.inventoryItems.map((item, index) => ({
      id: item.id ?? `${unit.id}-inventory-${index}`,
      buildingUnitId: unit.id,
      resourceTypeId: item.resourceTypeId ?? null,
      productTypeId: item.productTypeId ?? null,
      quantity: item.quantity,
      quality: item.quality ?? unit.inventoryQuality ?? 0.5,
      sourcingCostTotal: item.sourcingCostTotal ?? 0,
      sourcingCostPerUnit: item.quantity > 0 ? Number(((item.sourcingCostTotal ?? 0) / item.quantity).toFixed(2)) : 0,
    }))
  }

  if ((unit.inventoryQuantity ?? 0) <= 0) {
    return []
  }

  return [
    {
      id: `${unit.id}-inventory-legacy`,
      buildingUnitId: unit.id,
      resourceTypeId: unit.resourceTypeId ?? null,
      productTypeId: unit.productTypeId ?? null,
      quantity: unit.inventoryQuantity ?? 0,
      quality: unit.inventoryQuality ?? 0.5,
      sourcingCostTotal: unit.inventorySourcingCostTotal ?? 0,
      sourcingCostPerUnit: (unit.inventoryQuantity ?? 0) > 0 ? Number(((unit.inventorySourcingCostTotal ?? 0) / (unit.inventoryQuantity ?? 0)).toFixed(2)) : 0,
    },
  ]
}

function areUnitsEquivalent(currentUnit: MockBuildingUnit | undefined, nextUnit: MockBuildingUnit): boolean {
  if (!currentUnit) {
    return false
  }

  return (
    currentUnit.unitType === nextUnit.unitType &&
    currentUnit.gridX === nextUnit.gridX &&
    currentUnit.gridY === nextUnit.gridY &&
    currentUnit.linkUp === nextUnit.linkUp &&
    currentUnit.linkDown === nextUnit.linkDown &&
    currentUnit.linkLeft === nextUnit.linkLeft &&
    currentUnit.linkRight === nextUnit.linkRight &&
    currentUnit.linkUpLeft === nextUnit.linkUpLeft &&
    currentUnit.linkUpRight === nextUnit.linkUpRight &&
    currentUnit.linkDownLeft === nextUnit.linkDownLeft &&
    currentUnit.linkDownRight === nextUnit.linkDownRight &&
    (currentUnit.resourceTypeId ?? null) === (nextUnit.resourceTypeId ?? null) &&
    (currentUnit.productTypeId ?? null) === (nextUnit.productTypeId ?? null) &&
    (currentUnit.minPrice ?? null) === (nextUnit.minPrice ?? null) &&
    (currentUnit.maxPrice ?? null) === (nextUnit.maxPrice ?? null) &&
    (currentUnit.purchaseSource ?? null) === (nextUnit.purchaseSource ?? null) &&
    (currentUnit.saleVisibility ?? null) === (nextUnit.saleVisibility ?? null) &&
    (currentUnit.budget ?? null) === (nextUnit.budget ?? null) &&
    (currentUnit.mediaHouseBuildingId ?? null) === (nextUnit.mediaHouseBuildingId ?? null) &&
    (currentUnit.minQuality ?? null) === (nextUnit.minQuality ?? null) &&
    (currentUnit.brandScope ?? null) === (nextUnit.brandScope ?? null) &&
    (currentUnit.vendorLockCompanyId ?? null) === (nextUnit.vendorLockCompanyId ?? null) &&
    (currentUnit.lockedCityId ?? null) === (nextUnit.lockedCityId ?? null)
  )
}

function arePendingUnitsEquivalent(currentUnit: MockBuildingConfigurationPlanUnit | undefined, nextUnit: MockBuildingUnit): boolean {
  if (!currentUnit) {
    return false
  }

  return areUnitsEquivalent(currentUnit, nextUnit)
}

function calculateCancelTicks(baseTicks: number): number {
  return Math.max(Math.ceil(baseTicks * 0.1), 1)
}

function buildPlanSummary(plan: MockBuildingConfigurationPlan, currentTick: number): MockBuildingConfigurationPlan {
  const remainingTicks = Math.max(
    0,
    ...plan.units.filter((unit) => unit.isChanged).map((unit) => Math.max(unit.appliesAtTick - currentTick, 0)),
    ...plan.removals.map((removal) => Math.max(removal.appliesAtTick - currentTick, 0)),
  )

  return {
    ...plan,
    appliesAtTick: currentTick + remainingTicks,
    totalTicksRequired: remainingTicks,
  }
}

function calculateUnitTicks(currentUnit: MockBuildingUnit | undefined, nextUnit: MockBuildingUnit): number {
  if (!currentUnit) {
    return 3
  }

  if (currentUnit.unitType !== nextUnit.unitType) {
    return 3
  }

  if (
    currentUnit.linkUp !== nextUnit.linkUp ||
    currentUnit.linkDown !== nextUnit.linkDown ||
    currentUnit.linkLeft !== nextUnit.linkLeft ||
    currentUnit.linkRight !== nextUnit.linkRight ||
    currentUnit.linkUpLeft !== nextUnit.linkUpLeft ||
    currentUnit.linkUpRight !== nextUnit.linkUpRight ||
    currentUnit.linkDownLeft !== nextUnit.linkDownLeft ||
    currentUnit.linkDownRight !== nextUnit.linkDownRight
  ) {
    return 1
  }

  if (
    (currentUnit.resourceTypeId ?? null) !== (nextUnit.resourceTypeId ?? null) ||
    (currentUnit.productTypeId ?? null) !== (nextUnit.productTypeId ?? null) ||
    (currentUnit.minPrice ?? null) !== (nextUnit.minPrice ?? null) ||
    (currentUnit.maxPrice ?? null) !== (nextUnit.maxPrice ?? null) ||
    (currentUnit.purchaseSource ?? null) !== (nextUnit.purchaseSource ?? null) ||
    (currentUnit.saleVisibility ?? null) !== (nextUnit.saleVisibility ?? null) ||
    (currentUnit.budget ?? null) !== (nextUnit.budget ?? null) ||
    (currentUnit.mediaHouseBuildingId ?? null) !== (nextUnit.mediaHouseBuildingId ?? null) ||
    (currentUnit.minQuality ?? null) !== (nextUnit.minQuality ?? null) ||
    (currentUnit.brandScope ?? null) !== (nextUnit.brandScope ?? null) ||
    (currentUnit.vendorLockCompanyId ?? null) !== (nextUnit.vendorLockCompanyId ?? null) ||
    (currentUnit.lockedCityId ?? null) !== (nextUnit.lockedCityId ?? null)
  ) {
    return 1
  }

  return 0
}

function applyDueBuildingUpgrades(state: MockState): void {
  for (const player of state.players) {
    for (const company of player.companies) {
      for (const building of company.buildings) {
        if (!building.pendingConfiguration) {
          continue
        }

        const liveUnits = new Map(building.units.map((unit) => [`${unit.gridX},${unit.gridY}`, unit]))

        for (const removal of building.pendingConfiguration.removals.filter((candidate) => candidate.appliesAtTick <= state.gameState.currentTick)) {
          liveUnits.delete(`${removal.gridX},${removal.gridY}`)
        }

        for (const unit of building.pendingConfiguration.units.filter((candidate) => candidate.isChanged && candidate.appliesAtTick <= state.gameState.currentTick)) {
          liveUnits.set(`${unit.gridX},${unit.gridY}`, cloneUnit(unit))
          unit.isChanged = false
          unit.isReverting = false
          unit.startedAtTick = state.gameState.currentTick
          unit.appliesAtTick = state.gameState.currentTick
          unit.ticksRequired = 0
        }

        building.units = Array.from(liveUnits.values()).sort((left, right) => left.gridY - right.gridY || left.gridX - right.gridX)

        building.pendingConfiguration.removals = building.pendingConfiguration.removals.filter((candidate) => candidate.appliesAtTick > state.gameState.currentTick)

        if (!building.pendingConfiguration.units.some((unit) => unit.isChanged) && building.pendingConfiguration.removals.length === 0) {
          building.pendingConfiguration = null
          continue
        }

        building.pendingConfiguration = buildPlanSummary(building.pendingConfiguration, state.gameState.currentTick)
      }
    }
  }
}

// ── Factory functions ────────────────────────────────────────────────────────

export function makePlayer(overrides?: Partial<MockPlayer>): MockPlayer {
  return {
    id: 'player-1',
    email: 'player@test.com',
    password: 'TestPass1!',
    displayName: 'Test Player',
    role: 'PLAYER',
    isInvisibleInChat: false,
    createdAtUtc: '2026-01-01T00:00:00Z',
    lastLoginAtUtc: null,
    personalCash: PERSONAL_STARTING_CASH,
    activeAccountType: 'PERSON',
    activeCompanyId: null,
    onboardingCompletedAtUtc: null,
    onboardingCurrentStep: null,
    onboardingIndustry: null,
    onboardingCityId: null,
    onboardingCompanyId: null,
    onboardingFactoryLotId: null,
    onboardingShopBuildingId: null,
    onboardingFirstSaleCompletedAtUtc: null,
    proSubscriptionEndsAtUtc: null,
    dividendPayments: [],
    stockTrades: [],
    companies: [],
    ...overrides,
  }
}

function applyImplicitCompanyAccountContext(player: MockPlayer) {
  if (player.companies.length === 0) {
    return
  }

  const activeCompanyExists = player.activeCompanyId ? player.companies.some((company) => company.id === player.activeCompanyId) : false

  if (player.activeAccountType === 'COMPANY' && activeCompanyExists) {
    return
  }

  const firstCompany = player.companies[0]
  if (!firstCompany) {
    return
  }

  player.activeAccountType = 'COMPANY'
  player.activeCompanyId = firstCompany.id
}

export function makeAdminPlayer(overrides?: Partial<MockPlayer>): MockPlayer {
  return makePlayer({
    id: 'admin-1',
    email: 'admin@test.com',
    displayName: 'Admin',
    role: 'ADMIN',
    ...overrides,
  })
}

const woodResource: MockResourceType = {
  id: 'res-wood',
  name: 'Wood',
  slug: 'wood',
  category: 'ORGANIC',
  basePrice: 10,
  weightPerUnit: 5,
  unitName: 'Ton',
  unitSymbol: 't',
  imageUrl: null,
  description: 'Harvested timber.',
}

const grainResource: MockResourceType = {
  id: 'res-grain',
  name: 'Grain',
  slug: 'grain',
  category: 'ORGANIC',
  basePrice: 5,
  weightPerUnit: 2,
  unitName: 'Ton',
  unitSymbol: 't',
  imageUrl: null,
  description: 'Cereal crops.',
}

const chemResource: MockResourceType = {
  id: 'res-chem',
  name: 'Chemical Minerals',
  slug: 'chemical-minerals',
  category: 'MINERAL',
  basePrice: 30,
  weightPerUnit: 3,
  unitName: 'Ton',
  unitSymbol: 't',
  imageUrl: null,
  description: 'Raw minerals for pharma.',
}

export function makeDefaultResources(): MockResourceType[] {
  return [woodResource, grainResource, chemResource]
}

export function makeBratislava(): MockCity {
  return {
    id: 'city-ba',
    name: 'Bratislava',
    countryCode: 'SK',
    latitude: 48.1486,
    longitude: 17.1077,
    population: 475000,
    averageRentPerSqm: 14,
    baseSalaryPerManhour: 18,
    resources: [
      { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.7 },
      { resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC' }, abundance: 0.6 },
    ],
  }
}

export function makeDefaultCities(): MockCity[] {
  return [
    makeBratislava(),
    {
      id: 'city-pr',
      name: 'Prague',
      countryCode: 'CZ',
      latitude: 50.0755,
      longitude: 14.4378,
      population: 1350000,
      averageRentPerSqm: 18,
      baseSalaryPerManhour: 22,
      resources: [
        { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.7 },
        { resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC' }, abundance: 0.6 },
      ],
    },
    {
      id: 'city-vi',
      name: 'Vienna',
      countryCode: 'AT',
      latitude: 48.2082,
      longitude: 16.3738,
      population: 1900000,
      averageRentPerSqm: 22,
      baseSalaryPerManhour: 28,
      resources: [
        { resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', category: 'ORGANIC' }, abundance: 0.7 },
        { resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', category: 'ORGANIC' }, abundance: 0.6 },
      ],
    },
  ]
}

export function makeDefaultBuildingLots(): MockBuildingLot[] {
  return [
    {
      id: 'lot-industrial-1',
      cityId: 'city-ba',
      name: 'Industrial Plot A1',
      description: 'Large industrial plot near the eastern logistics corridor. Sits above an Iron Ore deposit (18,000t at 72% quality).',
      district: 'Industrial Zone',
      latitude: 48.152,
      longitude: 17.125,
      populationIndex: 0.65,
      basePrice: 75000, // Base land value (no resource premium)
      price: 96900, // = appraised land (75000 * 0.86 = 64500) + resource premium (18000t * $25/t * 0.72 * 0.10 = 32400)
      suitableTypes: 'FACTORY,MINE',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
      resourceType: { id: 'res-iron-ore', name: 'Iron Ore', slug: 'iron-ore' },
      materialQuality: 0.72,
      materialQuantity: 18000,
    },
    {
      id: 'lot-commercial-1',
      cityId: 'city-ba',
      name: 'High Street Retail Space',
      description: 'Prime storefront on the main pedestrian avenue.',
      district: 'Commercial District',
      latitude: 48.145,
      longitude: 17.107,
      populationIndex: 1.42,
      basePrice: 108000,
      price: 120000,
      suitableTypes: 'SALES_SHOP,COMMERCIAL',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
      resourceType: null,
      materialQuality: null,
      materialQuantity: null,
    },
    {
      id: 'lot-residential-1',
      cityId: 'city-ba',
      name: 'Riverside Apartment Block',
      description: 'Scenic residential plot overlooking the Danube.',
      district: 'Residential Quarter',
      latitude: 48.14,
      longitude: 17.1,
      populationIndex: 1.18,
      basePrice: 102000,
      price: 110000,
      suitableTypes: 'APARTMENT',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
      resourceType: null,
      materialQuality: null,
      materialQuantity: null,
    },
    {
      id: 'lot-business-1',
      cityId: 'city-ba',
      name: 'Innovation Campus Office',
      description: 'Modern office complex in the technology business park.',
      district: 'Business Park',
      latitude: 48.156,
      longitude: 17.11,
      populationIndex: 1.08,
      basePrice: 124000,
      price: 130000,
      suitableTypes: 'RESEARCH_DEVELOPMENT,BANK',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
      resourceType: null,
      materialQuality: null,
      materialQuantity: null,
    },
  ]
}

export function makeChairProduct(): MockProductType {
  return {
    id: 'prod-chair',
    name: 'Wooden Chair',
    slug: 'wooden-chair',
    industry: 'FURNITURE',
    basePrice: 45,
    baseCraftTicks: 2,
    outputQuantity: 20,
    energyConsumptionMwh: 1,
    basicLaborHours: 1.6,
    unitName: 'Chair',
    unitSymbol: 'chairs',
    isProOnly: false,
    description: 'A basic wooden chair.',
    recipes: [{ resourceType: { id: 'res-wood', name: 'Wood', slug: 'wood', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
  }
}

export function makeDefaultProducts(): MockProductType[] {
  return [
    makeChairProduct(),
    {
      id: 'prod-bread',
      name: 'Bread',
      slug: 'bread',
      industry: 'FOOD_PROCESSING',
      basePrice: 3,
      baseCraftTicks: 1,
      outputQuantity: 12,
      energyConsumptionMwh: 0.5,
      basicLaborHours: 0.9,
      unitName: 'Loaf',
      unitSymbol: 'loaves',
      isProOnly: false,
      description: 'Basic wheat bread.',
      recipes: [{ resourceType: { id: 'res-grain', name: 'Grain', slug: 'grain', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
    },
    {
      id: 'prod-medicine',
      name: 'Basic Medicine',
      slug: 'basic-medicine',
      industry: 'HEALTHCARE',
      basePrice: 50,
      baseCraftTicks: 3,
      outputQuantity: 8,
      energyConsumptionMwh: 1,
      basicLaborHours: 2.1,
      unitName: 'Bottle',
      unitSymbol: 'bottles',
      isProOnly: false,
      description: 'Essential pharma product.',
      recipes: [{ resourceType: { id: 'res-chem', name: 'Chemical Minerals', slug: 'chemical-minerals', unitName: 'Ton', unitSymbol: 't' }, inputProductType: null, quantity: 1 }],
    },
  ]
}

// ── Mock API setup ───────────────────────────────────────────────────────────

export function setupMockApi(page: Page, initial?: Partial<MockState>): MockState {
  const state: MockState = {
    serverKey: 'test-server',
    players: [],
    shareholdings: [],
    cities: makeDefaultCities(),
    buildingLots: makeDefaultBuildingLots(),
    resourceTypes: makeDefaultResources(),
    productTypes: makeDefaultProducts(),
    currentUserId: null,
    currentToken: null,
    gameState: { currentTick: 42, lastTickAtUtc: new Date(Date.now() - 30000).toISOString(), tickIntervalSeconds: 60, taxCycleTicks: 8760, taxRate: 15 },
    stockPriceHistory: {},
    ledgerData: {},
    drillDownData: {},
    researchBrands: {},
    publicSalesRecords: [],
    publicSalesAnalytics: {},
    unitProductAnalytics: {},
    buildingFinancialTimelines: {},
    loanOffers: [],
    myLoans: [],
    procurementPreviews: {},
    sourcingCandidates: {},
    unitUpgradeInfoOverrides: {},
    upgradeInsufficientFundsUnitId: null,
    productExchangeListings: [],
    chatMessages: [],
    rootAdminEmails: ['root@example.com'],
    globalGameAdminGrants: [],
    gameNewsEntries: [],
    adminMoneyInflowSummaries: [],
    adminShippingCostSummaries: [],
    adminMultiAccountAlerts: [],
    adminAuditLogs: [],
    impersonationSession: null,
    buildingLayouts: [],
    forceBuildingConfigError: null,
    ...initial,
  }

  state.players.forEach(applyImplicitCompanyAccountContext)

  const resolveCurrentPlayer = () => {
    const tokenPlayerId = state.currentToken?.startsWith('token-') ? state.currentToken.slice('token-'.length) : null

    return (
      state.players.find((player) => player.id === state.currentUserId) ??
      (tokenPlayerId ? state.players.find((player) => player.id === tokenPlayerId) : undefined) ??
      (state.players.length === 1 ? state.players[0] : undefined)
    )
  }

  const resolveAdminActor = () => {
    if (state.impersonationSession) {
      return state.players.find((player) => player.id === state.impersonationSession?.adminActorUserId)
    }

    return resolveCurrentPlayer()
  }

  const resolveEffectivePlayer = () => {
    if (state.impersonationSession) {
      return state.players.find((player) => player.id === state.impersonationSession?.effectiveUserId)
    }

    return resolveCurrentPlayer()
  }

  const resolveEffectiveAccountContext = () => {
    if (state.impersonationSession) {
      return {
        activeAccountType: state.impersonationSession.effectiveAccountType,
        activeCompanyId: state.impersonationSession.effectiveCompanyId,
      }
    }

    const currentPlayer = resolveCurrentPlayer()
    return {
      activeAccountType: currentPlayer?.activeAccountType ?? 'PERSON',
      activeCompanyId: currentPlayer?.activeCompanyId ?? null,
    }
  }

  const buildPlayerPayload = (player: MockPlayer | undefined) => {
    if (!player) {
      return null
    }

    const accountContext = resolveEffectiveAccountContext()
    return {
      ...player,
      password: undefined,
      activeAccountType: player.id === resolveEffectivePlayer()?.id ? accountContext.activeAccountType : player.activeAccountType,
      activeCompanyId: player.id === resolveEffectivePlayer()?.id ? accountContext.activeCompanyId : player.activeCompanyId,
      isInvisibleInChat: player.isInvisibleInChat ?? false,
    }
  }

  const buildGameAdminPlayer = (player: MockPlayer) => ({
    id: player.id,
    email: player.email,
    displayName: player.displayName,
    role: player.role,
    isInvisibleInChat: player.isInvisibleInChat ?? false,
    lastLoginAtUtc: player.lastLoginAtUtc,
    personalCash: player.personalCash,
    totalCompanyCash: Number(player.companies.reduce((total, company) => total + company.cash, 0).toFixed(2)),
    companyCount: player.companies.length,
    companies: player.companies.map((company) => ({
      id: company.id,
      name: company.name,
      cash: company.cash,
    })),
  })

  const buildGameNewsEntry = (entry: MockGameNewsEntry) => ({
    id: entry.id,
    entryType: entry.entryType,
    status: entry.status,
    targetServerKey: entry.targetServerKey,
    createdByEmail: entry.createdByEmail,
    updatedByEmail: entry.updatedByEmail,
    createdAtUtc: entry.createdAtUtc,
    updatedAtUtc: entry.updatedAtUtc,
    publishedAtUtc: entry.publishedAtUtc,
    isRead: !!state.currentUserId && entry.readByPlayerIds.includes(state.currentUserId),
    localizations: entry.localizations.map((localization) => ({ ...localization })),
  })

  const buildGameAdminSession = () => {
    const adminActor = resolveAdminActor()
    const effectivePlayer = resolveEffectivePlayer()

    if (!adminActor) {
      return {
        isLocalAdmin: false,
        hasGlobalAdminRole: false,
        isRootAdministrator: false,
        canAccessAdminDashboard: false,
        isImpersonating: false,
        effectiveAccountType: 'PERSON',
        effectiveCompanyId: null,
        effectiveCompanyName: null,
        adminActor: null,
        effectivePlayer: null,
      }
    }

    const isRootAdministrator = state.rootAdminEmails.some((email) => email.toLowerCase() === adminActor.email.toLowerCase())
    const hasGlobalAdminRole = state.globalGameAdminGrants.some((grant) => grant.email.toLowerCase() === adminActor.email.toLowerCase())
    const isLocalAdmin = adminActor.role === 'ADMIN'
    const accountContext = resolveEffectiveAccountContext()
    const effectiveCompany = effectivePlayer?.companies.find((company) => company.id === accountContext.activeCompanyId) ?? null

    return {
      isLocalAdmin,
      hasGlobalAdminRole,
      isRootAdministrator,
      canAccessAdminDashboard: isRootAdministrator || hasGlobalAdminRole || isLocalAdmin,
      isImpersonating: !!state.impersonationSession,
      effectiveAccountType: accountContext.activeAccountType,
      effectiveCompanyId: accountContext.activeCompanyId,
      effectiveCompanyName: effectiveCompany?.name ?? null,
      adminActor: buildGameAdminPlayer(adminActor),
      effectivePlayer: effectivePlayer ? buildGameAdminPlayer(effectivePlayer) : null,
    }
  }

  const getAdminAccessFailure = (requireRoot = false) => {
    const session = buildGameAdminSession()

    if ((requireRoot && !session.isRootAdministrator) || (!requireRoot && !session.canAccessAdminDashboard)) {
      return { message: 'Not authorized for game administration.', code: 'AUTH_NOT_AUTHORIZED' }
    }

    return null
  }

  mockStateByPage.set(page, state)

  page.route('**/graphql', async (route) => {
    const routeJson = (data: unknown) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data }),
      })

    const routeJsonError = (message: string, code?: string) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ errors: [{ message, ...(code ? { extensions: { code } } : {}) }] }),
      })

    const body = route.request().postDataJSON()
    const query: string = body?.query ?? ''
    const isRegisterMutation = query.includes('mutation Register') || query.includes('register(input:')
    const isLoginMutation = query.includes('mutation Login') || query.includes('login(input:')
    // Auth token check
    const authHeader = route.request().headers()['authorization'] ?? ''
    if (authHeader.startsWith('Bearer impersonation-')) {
      const rawToken = authHeader.replace('Bearer ', '')
      const [, sessionPayload] = rawToken.split('impersonation-')
      const [adminActorUserId, effectiveUserId, effectiveAccountType, effectiveCompanyId] = sessionPayload.split(':')

      state.currentToken = rawToken
      state.currentUserId = effectiveUserId ?? null
      state.impersonationSession = {
        adminActorUserId,
        effectiveUserId,
        effectiveAccountType: effectiveAccountType === 'COMPANY' ? 'COMPANY' : 'PERSON',
        effectiveCompanyId: effectiveCompanyId && effectiveCompanyId !== 'null' ? effectiveCompanyId : null,
      }
    } else if (authHeader.startsWith('Bearer token-')) {
      state.currentToken = authHeader.replace('Bearer ', '')
      state.currentUserId = authHeader.replace('Bearer token-', '')
      state.impersonationSession = null
    }

    // Mutations
    if (isRegisterMutation) {
      const input = body.variables?.input
      if (state.players.some((p) => p.email === input?.email)) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'A player with this email already exists.', extensions: { code: 'DUPLICATE_EMAIL' } }] }),
        })
      }
      const newPlayer: MockPlayer = {
        id: `player-${Date.now()}`,
        email: input.email,
        password: input.password,
        displayName: input.displayName,
        role: 'PLAYER',
        isInvisibleInChat: false,
        createdAtUtc: new Date().toISOString(),
        lastLoginAtUtc: null,
        personalCash: PERSONAL_STARTING_CASH,
        activeAccountType: 'PERSON',
        activeCompanyId: null,
        onboardingCompletedAtUtc: null,
        onboardingCurrentStep: null,
        onboardingIndustry: null,
        onboardingCityId: null,
        onboardingCompanyId: null,
        onboardingFactoryLotId: null,
        onboardingShopBuildingId: null,
        onboardingFirstSaleCompletedAtUtc: null,
        proSubscriptionEndsAtUtc: null,
        dividendPayments: [],
        companies: [],
      }
      state.players.push(newPlayer)
      state.currentUserId = newPlayer.id
      state.currentToken = `token-${newPlayer.id}`
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            register: {
              token: `token-${newPlayer.id}`,
              expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
              player: buildPlayerPayload(newPlayer),
            },
          },
        }),
      })
    }

    if (isLoginMutation) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.email === input?.email && p.password === input?.password)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Invalid email or password.' }] }) })
      }
      player.lastLoginAtUtc = new Date().toISOString()
      state.currentUserId = player.id
      state.currentToken = `token-${player.id}`
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            login: {
              token: `token-${player.id}`,
              expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
              player: buildPlayerPayload(player),
            },
          },
        }),
      })
    }

    if (query.includes('myBuildingLayouts')) {
      if (!state.currentUserId) {
        return routeJsonError('Not authenticated.')
      }

      return routeJson({
        myBuildingLayouts: state.buildingLayouts
          .filter((layout) => layout.ownerPlayerId === state.currentUserId)
          .map((layout) => ({
            id: layout.id,
            name: layout.name,
            description: layout.description,
            buildingType: layout.buildingType,
            unitsJson: layout.unitsJson,
            updatedAtUtc: layout.updatedAtUtc,
          })),
      })
    }

    if (query.includes('saveBuildingLayout')) {
      if (!state.currentUserId) {
        return routeJsonError('Not authenticated.')
      }

      const input = body.variables?.input
      const existingId = input?.existingId as string | null | undefined
      const existingIndex = existingId
        ? state.buildingLayouts.findIndex(
            (layout) => layout.id === existingId && layout.ownerPlayerId === state.currentUserId,
          )
        : -1
      const updatedAtUtc = new Date().toISOString()
      const layout: MockBuildingLayoutTemplate = {
        id:
          existingIndex >= 0
            ? state.buildingLayouts[existingIndex]!.id
            : `layout-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        ownerPlayerId: state.currentUserId,
        name: input?.name ?? 'Untitled Layout',
        description: input?.description ?? null,
        buildingType: input?.buildingType ?? 'FACTORY',
        unitsJson: input?.unitsJson ?? '[]',
        updatedAtUtc,
      }

      if (existingIndex >= 0) {
        state.buildingLayouts.splice(existingIndex, 1, layout)
      } else {
        state.buildingLayouts.unshift(layout)
      }

      return routeJson({
        saveBuildingLayout: {
          id: layout.id,
          name: layout.name,
          description: layout.description,
          buildingType: layout.buildingType,
          unitsJson: layout.unitsJson,
          updatedAtUtc: layout.updatedAtUtc,
        },
      })
    }

    if (query.includes('deleteBuildingLayout')) {
      if (!state.currentUserId) {
        return routeJsonError('Not authenticated.')
      }

      const input = body.variables?.input
      state.buildingLayouts = state.buildingLayouts.filter(
        (layout) => !(layout.id === input?.id && layout.ownerPlayerId === state.currentUserId),
      )

      return routeJson({ deleteBuildingLayout: true })
    }

    if (query.includes('StartOnboardingCompany')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      const lot = state.buildingLots.find((candidate) => candidate.id === input?.factoryLotId)
      if (!lot) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building lot not found.' }] }) })
      }
      if (lot.ownerCompanyId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'This lot has already been purchased.', extensions: { code: 'LOT_ALREADY_OWNED' } }] }),
        })
      }
      if (
        !lot.suitableTypes
          .split(',')
          .map((type) => type.trim())
          .includes('FACTORY')
      ) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building type FACTORY is not suitable for this lot.' }] }) })
      }
      const ipoSelection = resolveIpoSelection(Number(body.variables?.input?.ipoRaiseTarget))
      const startingCompanyCash = STARTER_FOUNDER_CONTRIBUTION + ipoSelection.raiseTarget

      if (startingCompanyCash < lot.price) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: `Insufficient funds. This lot costs $${lot.price.toLocaleString()}.` }] }) })
      }

      const companyId = `company-${Date.now()}`
      const factoryId = `building-factory-${Date.now()}`
      const company: MockCompany = {
        id: companyId,
        playerId: player.id,
        name: input.companyName,
        cash: startingCompanyCash - lot.price,
        totalSharesIssued: DEFAULT_COMPANY_SHARE_COUNT,
        dividendPayoutRatio: DEFAULT_DIVIDEND_PAYOUT_RATIO,
        foundedAtUtc: new Date().toISOString(),
        foundedAtTick: state.gameState.currentTick,
        buildings: [
          {
            id: factoryId,
            companyId,
            cityId: input.cityId,
            type: 'FACTORY',
            name: `${input.companyName} Factory`,
            latitude: lot.latitude,
            longitude: lot.longitude,
            level: 1,
            powerConsumption: 2,
            powerStatus: 'POWERED',
            isForSale: false,
            builtAtUtc: new Date().toISOString(),
            units: [],
            pendingConfiguration: null,
          },
        ],
      }

      lot.ownerCompanyId = company.id
      lot.buildingId = factoryId
      lot.ownerCompany = { id: company.id, name: company.name }
      lot.building = { id: factoryId, name: `${input.companyName} Factory`, type: 'FACTORY' }

      player.personalCash -= STARTER_FOUNDER_CONTRIBUTION
      player.activeAccountType = 'COMPANY'
      player.activeCompanyId = company.id
      player.companies.push(company)
      state.shareholdings.push({
        companyId: company.id,
        ownerPlayerId: player.id,
        ownerCompanyId: null,
        shareCount: Number((DEFAULT_COMPANY_SHARE_COUNT * ipoSelection.founderOwnershipRatio).toFixed(4)),
      })
      player.onboardingCurrentStep = 'SHOP_SELECTION'
      player.onboardingIndustry = input.industry
      player.onboardingCityId = input.cityId
      player.onboardingCompanyId = company.id
      player.onboardingFactoryLotId = lot.id

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            startOnboardingCompany: {
              nextStep: 'SHOP_SELECTION',
              company: { id: company.id, name: company.name, cash: company.cash },
              factory: company.buildings[0],
              factoryLot: lot,
            },
          },
        }),
      })
    }

    if (query.includes('FinishOnboarding')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player || player.onboardingCurrentStep !== 'SHOP_SELECTION' || !player.onboardingCompanyId) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'No onboarding progress was found to resume.' }] }) })
      }

      const company = player.companies.find((candidate) => candidate.id === player.onboardingCompanyId)
      const product = state.productTypes.find((candidate) => candidate.id === input?.productTypeId)
      const shopLot = state.buildingLots.find((candidate) => candidate.id === input?.shopLotId)

      if (!company || !product) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Product not found.' }] }) })
      }
      if (!shopLot) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building lot not found.' }] }) })
      }
      if (shopLot.ownerCompanyId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'This lot has already been purchased.', extensions: { code: 'LOT_ALREADY_OWNED' } }] }),
        })
      }
      if (
        !shopLot.suitableTypes
          .split(',')
          .map((type) => type.trim())
          .includes('SALES_SHOP')
      ) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building type SALES_SHOP is not suitable for this lot.' }] }) })
      }
      if (company.cash < shopLot.price) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: `Insufficient funds. This lot costs $${shopLot.price.toLocaleString()}.` }] }),
        })
      }

      company.cash -= shopLot.price
      const shopId = `building-shop-${Date.now()}`
      const productId = product?.id ?? ''
      const shopBuilding: MockBuilding = {
        id: shopId,
        companyId: company.id,
        cityId: shopLot.cityId,
        type: 'SALES_SHOP',
        name: `${company.name} Shop`,
        latitude: shopLot.latitude,
        longitude: shopLot.longitude,
        level: 1,
        powerConsumption: 1,
        powerStatus: 'POWERED',
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        units: [
          {
            id: `unit-shop-purchase-${Date.now()}`,
            buildingId: shopId,
            unitType: 'PURCHASE',
            gridX: 0,
            gridY: 0,
            level: 1,
            linkRight: true,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: productId,
            resourceTypeId: null,
            minPrice: null,
            maxPrice: null,
            purchaseSource: 'LOCAL',
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: company.id,
          },
          {
            id: `unit-shop-publicsales-${Date.now() + 1}`,
            buildingId: shopId,
            unitType: 'PUBLIC_SALES',
            gridX: 1,
            gridY: 0,
            level: 1,
            linkRight: false,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: productId,
            resourceTypeId: null,
            minPrice: product?.basePrice != null ? product.basePrice * 1.5 : null,
            maxPrice: null,
            purchaseSource: null,
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
        ],
        pendingConfiguration: null,
      }

      // Configure the factory with starter units (mirrors ConfigureStarterFactory on backend)
      const factoryBuilding = company.buildings.find((candidate) => candidate.type === 'FACTORY')
      if (factoryBuilding && factoryBuilding.units.length === 0) {
        const resourceTypeId = product?.recipes[0]?.resourceType?.id ?? null
        factoryBuilding.units = [
          {
            id: `unit-factory-purchase-${Date.now()}`,
            buildingId: factoryBuilding.id,
            unitType: 'PURCHASE',
            gridX: 0,
            gridY: 0,
            level: 1,
            linkRight: true,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: null,
            resourceTypeId,
            minPrice: null,
            maxPrice: null,
            purchaseSource: 'OPTIMAL',
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
          {
            id: `unit-factory-manufacturing-${Date.now() + 1}`,
            buildingId: factoryBuilding.id,
            unitType: 'MANUFACTURING',
            gridX: 1,
            gridY: 0,
            level: 1,
            linkRight: true,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: productId,
            resourceTypeId: null,
            minPrice: null,
            maxPrice: null,
            purchaseSource: null,
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
          {
            id: `unit-factory-storage-${Date.now() + 2}`,
            buildingId: factoryBuilding.id,
            unitType: 'STORAGE',
            gridX: 2,
            gridY: 0,
            level: 1,
            linkRight: true,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: null,
            resourceTypeId: null,
            minPrice: null,
            maxPrice: null,
            purchaseSource: null,
            saleVisibility: null,
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
          {
            id: `unit-factory-b2bsales-${Date.now() + 3}`,
            buildingId: factoryBuilding.id,
            unitType: 'B2B_SALES',
            gridX: 3,
            gridY: 0,
            level: 1,
            linkRight: false,
            linkLeft: false,
            linkUp: false,
            linkDown: false,
            linkUpLeft: false,
            linkUpRight: false,
            linkDownLeft: false,
            linkDownRight: false,
            productTypeId: productId,
            resourceTypeId: null,
            minPrice: product?.basePrice ?? null,
            maxPrice: null,
            purchaseSource: null,
            saleVisibility: 'COMPANY',
            budget: null,
            mediaHouseBuildingId: null,
            minQuality: null,
            brandScope: null,
            vendorLockCompanyId: null,
          },
        ]
      }
      company.buildings.push(shopBuilding)

      shopLot.ownerCompanyId = company.id
      shopLot.buildingId = shopBuilding.id
      shopLot.ownerCompany = { id: company.id, name: company.name }
      shopLot.building = { id: shopBuilding.id, name: shopBuilding.name, type: shopBuilding.type }

      player.onboardingCompletedAtUtc = new Date().toISOString()
      player.onboardingShopBuildingId = shopBuilding.id
      player.onboardingCurrentStep = null
      player.onboardingIndustry = null
      player.onboardingCityId = null
      player.onboardingCompanyId = null
      player.onboardingFactoryLotId = null

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            finishOnboarding: {
              company: { id: company.id, name: company.name, cash: company.cash },
              factory: company.buildings.find((candidate) => candidate.type === 'FACTORY'),
              salesShop: shopBuilding,
              selectedProduct: product,
            },
          },
        }),
      })
    }

    if (query.includes('CompleteOnboarding')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      const product = state.productTypes.find((p) => p.id === input.productTypeId)
      const productId = product?.id ?? ''
      const resourceTypeId = product?.recipes[0]?.resourceType?.id ?? null
      const factoryBuildingId = `building-factory-${Date.now()}`
      const shopBuildingId = `building-shop-${Date.now() + 1}`
      const company: MockCompany = {
        id: `company-${Date.now()}`,
        playerId: player.id,
        name: input.companyName,
        cash: 500000,
        totalSharesIssued: DEFAULT_COMPANY_SHARE_COUNT,
        dividendPayoutRatio: DEFAULT_DIVIDEND_PAYOUT_RATIO,
        foundedAtUtc: new Date().toISOString(),
        foundedAtTick: state.gameState.currentTick,
        buildings: [
          {
            id: factoryBuildingId,
            companyId: '',
            cityId: input.cityId,
            type: 'FACTORY',
            name: `${input.companyName} Factory`,
            latitude: 48.15,
            longitude: 17.11,
            level: 1,
            powerConsumption: 2,
            powerStatus: 'POWERED',
            isForSale: false,
            builtAtUtc: new Date().toISOString(),
            units: [
              {
                id: `unit-factory-purchase-${Date.now()}`,
                buildingId: factoryBuildingId,
                unitType: 'PURCHASE',
                gridX: 0,
                gridY: 0,
                level: 1,
                linkRight: true,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: null,
                resourceTypeId,
                minPrice: null,
                maxPrice: null,
                purchaseSource: 'OPTIMAL',
                saleVisibility: null,
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
              {
                id: `unit-factory-manufacturing-${Date.now() + 1}`,
                buildingId: factoryBuildingId,
                unitType: 'MANUFACTURING',
                gridX: 1,
                gridY: 0,
                level: 1,
                linkRight: true,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: productId,
                resourceTypeId: null,
                minPrice: null,
                maxPrice: null,
                purchaseSource: null,
                saleVisibility: null,
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
              {
                id: `unit-factory-storage-${Date.now() + 2}`,
                buildingId: factoryBuildingId,
                unitType: 'STORAGE',
                gridX: 2,
                gridY: 0,
                level: 1,
                linkRight: true,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: null,
                resourceTypeId: null,
                minPrice: null,
                maxPrice: null,
                purchaseSource: null,
                saleVisibility: null,
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
              {
                id: `unit-factory-b2bsales-${Date.now() + 3}`,
                buildingId: factoryBuildingId,
                unitType: 'B2B_SALES',
                gridX: 3,
                gridY: 0,
                level: 1,
                linkRight: false,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: productId,
                resourceTypeId: null,
                minPrice: product?.basePrice ?? null,
                maxPrice: null,
                purchaseSource: null,
                saleVisibility: 'COMPANY',
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
            ],
            pendingConfiguration: null,
          },
          {
            id: shopBuildingId,
            companyId: '',
            cityId: input.cityId,
            type: 'SALES_SHOP',
            name: `${input.companyName} Shop`,
            latitude: 48.15,
            longitude: 17.11,
            level: 1,
            powerConsumption: 1,
            powerStatus: 'POWERED',
            isForSale: false,
            builtAtUtc: new Date().toISOString(),
            units: [
              {
                id: `unit-shop-purchase-${Date.now() + 4}`,
                buildingId: shopBuildingId,
                unitType: 'PURCHASE',
                gridX: 0,
                gridY: 0,
                level: 1,
                linkRight: true,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: productId,
                resourceTypeId: null,
                minPrice: null,
                maxPrice: null,
                purchaseSource: 'LOCAL',
                saleVisibility: null,
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
              {
                id: `unit-shop-publicsales-${Date.now() + 5}`,
                buildingId: shopBuildingId,
                unitType: 'PUBLIC_SALES',
                gridX: 1,
                gridY: 0,
                level: 1,
                linkRight: false,
                linkLeft: false,
                linkUp: false,
                linkDown: false,
                linkUpLeft: false,
                linkUpRight: false,
                linkDownLeft: false,
                linkDownRight: false,
                productTypeId: productId,
                resourceTypeId: null,
                minPrice: product?.basePrice != null ? product.basePrice * 1.5 : null,
                maxPrice: null,
                purchaseSource: null,
                saleVisibility: null,
                budget: null,
                mediaHouseBuildingId: null,
                minQuality: null,
                brandScope: null,
                vendorLockCompanyId: null,
              },
            ],
            pendingConfiguration: null,
          },
        ],
      }
      player.companies.push(company)
      player.activeAccountType = 'COMPANY'
      player.activeCompanyId = company.id
      state.shareholdings.push({
        companyId: company.id,
        ownerPlayerId: player.id,
        ownerCompanyId: null,
        shareCount: DEFAULT_COMPANY_SHARE_COUNT,
      })
      player.onboardingCompletedAtUtc = new Date().toISOString()
      player.onboardingShopBuildingId = company.buildings[1].id
      player.onboardingCurrentStep = null
      player.onboardingIndustry = null
      player.onboardingCityId = null
      player.onboardingCompanyId = null
      player.onboardingFactoryLotId = null
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            completeOnboarding: {
              company: { id: company.id, name: company.name, cash: company.cash },
              factory: company.buildings[0],
              salesShop: company.buildings[1],
              selectedProduct: product ?? state.productTypes[0],
            },
          },
        }),
      })
    }

    if (query.includes('CreateCompany')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      const company: MockCompany = {
        id: `company-${Date.now()}`,
        playerId: player.id,
        name: input.name,
        cash: 1000000,
        totalSharesIssued: DEFAULT_COMPANY_SHARE_COUNT,
        dividendPayoutRatio: DEFAULT_DIVIDEND_PAYOUT_RATIO,
        foundedAtUtc: new Date().toISOString(),
        foundedAtTick: state.gameState.currentTick,
        buildings: [],
      }
      player.companies.push(company)
      player.activeAccountType = 'COMPANY'
      player.activeCompanyId = company.id
      state.shareholdings.push({
        companyId: company.id,
        ownerPlayerId: player.id,
        ownerCompanyId: null,
        shareCount: DEFAULT_COMPANY_SHARE_COUNT,
      })
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { createCompany: company } }),
      })
    }

    if (query.includes('UpdateCompanySettings')) {
      const input = body.variables?.input
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      const company = player?.companies.find((candidate) => candidate.id === input?.companyId)

      if (!company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found or you do not own it.' }] }) })
      }

      company.name = input.name
      company.dividendPayoutRatio = Number(input.dividendPayoutRatio ?? company.dividendPayoutRatio ?? DEFAULT_DIVIDEND_PAYOUT_RATIO)
      company.citySalaryMultipliers = Object.fromEntries((input.citySalarySettings ?? []).map((entry: { cityId: string; salaryMultiplier: number }) => [entry.cityId, entry.salaryMultiplier]))

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { updateCompanySettings: { id: company.id, name: company.name, dividendPayoutRatio: getCompanyDividendPayoutRatio(company) } } }),
      })
    }

    if (query.includes('SwitchAccountContext')) {
      const input = body.variables?.input
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)

      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      if (input?.accountType === 'PERSON') {
        player.activeAccountType = 'PERSON'
        player.activeCompanyId = null
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { switchAccountContext: { activeAccountType: 'PERSON', activeCompanyId: null, activeAccountName: player.displayName } } }),
        })
      }

      const targetCompany = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === input?.companyId)
      if (!targetCompany) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found.' }] }) })
      }

      const controlledRatio = getCombinedControlledOwnershipRatio(state, player.id, targetCompany)
      if (targetCompany.playerId !== player.id && controlledRatio < 0.5) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'You need at least 50% combined ownership to switch into this company.', extensions: { code: 'COMPANY_CONTROL_REQUIRED' } }] }),
        })
      }

      targetCompany.playerId = player.id
      player.activeAccountType = 'COMPANY'
      player.activeCompanyId = targetCompany.id

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { switchAccountContext: { activeAccountType: 'COMPANY', activeCompanyId: targetCompany.id, activeAccountName: targetCompany.name } } }),
      })
    }

    if (query.includes('BuyShares')) {
      const input = body.variables?.input
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === input?.companyId)

      if (!player || !company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found or not authenticated.' }] }) })
      }

      const shareCount = Number(input?.shareCount ?? 0)
      const publicFloatShares = getPublicFloatShares(state, company)
      const pricePerShare = Number((computeMockSharePrice(company) * 1.01).toFixed(2))
      const totalValue = Number((shareCount * pricePerShare).toFixed(2))

      if (shareCount <= 0) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Share count must be greater than zero.' }] }) })
      }

      if (publicFloatShares < shareCount) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not enough public float shares are available.', extensions: { code: 'INSUFFICIENT_PUBLIC_FLOAT' } }] }),
        })
      }

      // Resolve trading account: prefer explicit tradeAccountType/tradeAccountCompanyId over active account
      const tradeAccountType: string = input?.tradeAccountType ?? player.activeAccountType
      const tradeAccountCompanyId: string | null = input?.tradeAccountCompanyId ?? player.activeCompanyId ?? null

      let accountName = player.displayName
      let accountCompanyId: string | null = null
      let ownedShareCount = 0
      let companyCash: number | null = null

      if (tradeAccountType === 'COMPANY' && tradeAccountCompanyId) {
        const activeCompany = player.companies.find((candidate) => candidate.id === tradeAccountCompanyId)
        if (!activeCompany || activeCompany.cash < totalValue) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ errors: [{ message: 'Not enough company cash.', extensions: { code: 'INSUFFICIENT_FUNDS' } }] }),
          })
        }

        activeCompany.cash = Number((activeCompany.cash - totalValue).toFixed(2))
        accountName = activeCompany.name
        accountCompanyId = activeCompany.id
        companyCash = activeCompany.cash
        recordMockCompanyStockLedgerEntry(
          state,
          activeCompany,
          'STOCK_PURCHASE',
          `Bought ${shareCount} shares in ${company.name} @ ${pricePerShare.toFixed(2)}`,
          -totalValue,
        )

        if (activeCompany.id === company.id) {
          company.totalSharesIssued = Number(Math.max(getCompanyTotalShares(company) - shareCount, 0).toFixed(4))
          ownedShareCount = getCompanyTotalShares(company)
        } else {
          const holding = getOrCreateShareholding(state, company.id, null, activeCompany.id)
          holding.shareCount = Number((holding.shareCount + shareCount).toFixed(4))
          ownedShareCount = holding.shareCount
        }
      } else {
        if (player.personalCash < totalValue) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ errors: [{ message: 'Not enough personal cash.', extensions: { code: 'INSUFFICIENT_FUNDS' } }] }),
          })
        }

        player.personalCash = Number((player.personalCash - totalValue).toFixed(2))
        const holding = getOrCreateShareholding(state, company.id, player.id, null)
        holding.shareCount = Number((holding.shareCount + shareCount).toFixed(4))
        ownedShareCount = holding.shareCount
        player.stockTrades.unshift({
          id: `trade-buy-${Math.random().toString(36).slice(2)}`,
          companyId: company.id,
          companyName: company.name,
          direction: 'BUY',
          shareCount,
          pricePerShare,
          totalValue,
          recordedAtTick: state.gameState.currentTick,
          recordedAtUtc: '2026-01-10T12:00:00Z',
        })
      }

      appendMockStockPriceHistory(state, company.id, pricePerShare)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            buyShares: {
              companyId: company.id,
              companyName: company.name,
              accountType: tradeAccountType,
              accountCompanyId,
              accountName,
              shareCount,
              pricePerShare,
              totalValue,
              ownedShareCount,
              publicFloatShares: getPublicFloatShares(state, company),
              personalCash: player.personalCash,
              companyCash,
            },
          },
        }),
      })
    }

    if (query.includes('SellShares')) {
      const input = body.variables?.input
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === input?.companyId)

      if (!player || !company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found or not authenticated.' }] }) })
      }

      const shareCount = Number(input?.shareCount ?? 0)
      const pricePerShare = Number((computeMockSharePrice(company) * 0.99).toFixed(2))
      const totalValue = Number((shareCount * pricePerShare).toFixed(2))

      if (shareCount <= 0) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Share count must be greater than zero.' }] }) })
      }

      // Resolve trading account: prefer explicit tradeAccountType/tradeAccountCompanyId over active account
      const tradeAccountType: string = input?.tradeAccountType ?? player.activeAccountType
      const tradeAccountCompanyId: string | null = input?.tradeAccountCompanyId ?? player.activeCompanyId ?? null

      let accountName = player.displayName
      let accountCompanyId: string | null = null
      let ownedShareCount = 0
      let companyCash: number | null = null

      if (tradeAccountType === 'COMPANY' && tradeAccountCompanyId) {
        const activeCompany = player.companies.find((candidate) => candidate.id === tradeAccountCompanyId)
        const holding = activeCompany ? getOrCreateShareholding(state, company.id, null, activeCompany.id) : null

        if (!activeCompany || !holding || holding.shareCount < shareCount) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ errors: [{ message: 'Not enough shares to sell.', extensions: { code: 'INSUFFICIENT_SHARES' } }] }),
          })
        }

        holding.shareCount = Number((holding.shareCount - shareCount).toFixed(4))
        activeCompany.cash = Number((activeCompany.cash + totalValue).toFixed(2))
        accountName = activeCompany.name
        accountCompanyId = activeCompany.id
        companyCash = activeCompany.cash
        ownedShareCount = holding.shareCount
        recordMockCompanyStockLedgerEntry(
          state,
          activeCompany,
          'STOCK_SALE',
          `Sold ${shareCount} shares in ${company.name} @ ${pricePerShare.toFixed(2)}`,
          totalValue,
        )
      } else {
        const holding = getOrCreateShareholding(state, company.id, player.id, null)
        if (holding.shareCount < shareCount) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ errors: [{ message: 'Not enough shares to sell.', extensions: { code: 'INSUFFICIENT_SHARES' } }] }),
          })
        }

        holding.shareCount = Number((holding.shareCount - shareCount).toFixed(4))
        player.personalCash = Number((player.personalCash + totalValue).toFixed(2))
        ownedShareCount = holding.shareCount
        player.stockTrades.unshift({
          id: `trade-sell-${Math.random().toString(36).slice(2)}`,
          companyId: company.id,
          companyName: company.name,
          direction: 'SELL',
          shareCount,
          pricePerShare,
          totalValue,
          recordedAtTick: state.gameState.currentTick,
          recordedAtUtc: '2026-01-10T12:00:00Z',
        })
      }

      appendMockStockPriceHistory(state, company.id, pricePerShare)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            sellShares: {
              companyId: company.id,
              companyName: company.name,
              accountType: tradeAccountType,
              accountCompanyId,
              accountName,
              shareCount,
              pricePerShare,
              totalValue,
              ownedShareCount,
              publicFloatShares: getPublicFloatShares(state, company),
              personalCash: player.personalCash,
              companyCash,
            },
          },
        }),
      })
    }

    if (query.includes('mergeCompany')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated', extensions: { code: 'UNAUTHORIZED' } }] }),
        })
      }
      // Find target company across all players
      const targetCompany = state.players.flatMap((p) => p.companies).find((c) => c.id === input.targetCompanyId)
      const destCompany = player.companies.find((c) => c.id === input.destinationCompanyId)
      if (!targetCompany || !destCompany) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Company not found', extensions: { code: 'COMPANY_NOT_FOUND' } }] }),
        })
      }
      const cashTransferred = targetCompany.cash ?? 0
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            mergeCompany: {
              destinationCompanyId: destCompany.id,
              destinationCompanyName: destCompany.name,
              absorbedCompanyName: targetCompany.name,
              cashTransferred,
              buildingsTransferred: targetCompany.buildings?.length ?? 0,
            },
          },
        }),
      })
    }

    if (query.includes('PlaceBuilding')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      const company = player.companies.find((c) => c.id === input.companyId)
      if (!company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found' }] }) })
      }
      const city = state.cities.find((c) => c.id === input.cityId)
      const newBuilding: MockBuilding = {
        id: `building-${Date.now()}`,
        companyId: company.id,
        cityId: input.cityId,
        type: input.type,
        name: input.name,
        latitude: city?.latitude ?? 0,
        longitude: city?.longitude ?? 0,
        level: 1,
        powerConsumption: 1,
        powerStatus: 'POWERED',
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        units: [],
        pendingConfiguration: null,
      }
      company.buildings.push(newBuilding)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { placeBuilding: newBuilding } }),
      })
    }

    if (query.includes('StoreBuildingConfiguration')) {
      applyDueBuildingUpgrades(state)

      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      const building = player?.companies.flatMap((company) => company.buildings).find((candidate) => candidate.id === input?.buildingId)

      if (!building) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building not found' }] }) })
      }

      // Allow tests to force a CONTRADICTORY_LINK error (or any other config error) to verify
      // the frontend correctly displays backend validation errors in the save-error-banner.
      if (state.forceBuildingConfigError) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [{ message: state.forceBuildingConfigError, extensions: { code: 'CONTRADICTORY_LINK' } }],
          }),
        })
      }

      const activePlayer = state.players.find((candidate) => candidate.id === state.currentUserId)
      const hasActiveProSubscription = !!activePlayer?.proSubscriptionEndsAtUtc && new Date(activePlayer.proSubscriptionEndsAtUtc).getTime() > Date.now()

      for (const unit of input.units ?? []) {
        if (!unit.productTypeId) {
          continue
        }

        const product = state.productTypes.find((candidate) => candidate.id === unit.productTypeId)
        const isRetainingExistingProduct = [...(building.units ?? []), ...(building.pendingConfiguration?.units ?? [])].some(
          (candidate) => candidate.unitType === unit.unitType && candidate.gridX === unit.gridX && candidate.gridY === unit.gridY && (candidate.productTypeId ?? null) === unit.productTypeId,
        )

        if (product?.isProOnly && !hasActiveProSubscription && !isRetainingExistingProduct) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              errors: [
                {
                  message: `Pro subscription unlocks additional products to manufacture and sell. Activate Pro to use ${product.name}.`,
                  extensions: { code: 'PRO_SUBSCRIPTION_REQUIRED' },
                },
              ],
            }),
          })
        }
      }

      // Recipe compatibility validation: if a MANUFACTURING unit specifies a product,
      // and at least one PURCHASE unit has a resource explicitly configured, verify
      // that the resource satisfies the product's recipe.
      if (building.type === 'FACTORY') {
        const purchaseResourceIds = (input.units ?? []).filter((u: MockBuildingUnit) => u.unitType === 'PURCHASE' && u.resourceTypeId).map((u: MockBuildingUnit) => u.resourceTypeId!)

        const purchaseProductIds = (input.units ?? []).filter((u: MockBuildingUnit) => u.unitType === 'PURCHASE' && u.productTypeId).map((u: MockBuildingUnit) => u.productTypeId!)

        if (purchaseResourceIds.length > 0 || purchaseProductIds.length > 0) {
          for (const unit of input.units ?? []) {
            if (unit.unitType !== 'MANUFACTURING' || !unit.productTypeId) continue

            const product = state.productTypes.find((p) => p.id === unit.productTypeId)
            if (!product || product.recipes.length === 0) continue

            const anyRecipeSatisfied = product.recipes.some(
              (recipe: { resourceType: { id: string } | null; inputProductType: { id: string } | null }) =>
                (recipe.resourceType?.id && purchaseResourceIds.includes(recipe.resourceType.id)) || (recipe.inputProductType?.id && purchaseProductIds.includes(recipe.inputProductType.id)),
            )

            if (!anyRecipeSatisfied) {
              return route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                  errors: [
                    {
                      message: `The Manufacturing unit's product '${product.name}' requires an input that no configured Purchase unit in this plan supplies. Update the Purchase unit to supply a resource or product required by this product's recipe.`,
                      extensions: { code: 'RECIPE_INPUT_MISMATCH' },
                    },
                  ],
                }),
              })
            }
          }
        }
      }

      // Zero/negative price validation: PUBLIC_SALES and B2B_SALES units must have a positive minPrice.
      // The runtime engine (PublicSalesPhase) silently replaces price <= 0 with base price, so
      // accepting 0 would misrepresent the actual selling price to the player.
      for (const unit of input.units ?? []) {
        if ((unit.unitType === 'PUBLIC_SALES' || unit.unitType === 'B2B_SALES') && unit.minPrice !== null && unit.minPrice !== undefined && unit.minPrice <= 0) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              errors: [
                {
                  message: 'Minimum price must be greater than zero.',
                  extensions: { code: 'INVALID_MIN_PRICE' },
                },
              ],
            }),
          })
        }
      }

      const currentUnits = new Map(building.units.map((unit) => [`${unit.gridX},${unit.gridY}`, unit]))
      const desiredUnits = new Map(
        (input.units ?? []).map((unit: MockBuildingUnit, index: number) => {
          const current = building.units.find((candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY)

          return [
            `${unit.gridX},${unit.gridY}`,
            {
              id: `pending-unit-${index}-${Date.now()}`,
              buildingId: building.id,
              unitType: unit.unitType,
              gridX: unit.gridX,
              gridY: unit.gridY,
              level: current?.level ?? 1,
              linkUp: unit.linkUp,
              linkDown: unit.linkDown,
              linkLeft: unit.linkLeft,
              linkRight: unit.linkRight,
              linkUpLeft: unit.linkUpLeft,
              linkUpRight: unit.linkUpRight,
              linkDownLeft: unit.linkDownLeft,
              linkDownRight: unit.linkDownRight,
              resourceTypeId: unit.resourceTypeId ?? null,
              productTypeId: unit.productTypeId ?? null,
              minPrice: unit.minPrice ?? null,
              maxPrice: unit.maxPrice ?? null,
              purchaseSource: unit.purchaseSource ?? null,
              saleVisibility: unit.saleVisibility ?? null,
              budget: unit.budget ?? null,
              mediaHouseBuildingId: unit.mediaHouseBuildingId ?? null,
              minQuality: unit.minQuality ?? null,
              brandScope: unit.brandScope ?? null,
              vendorLockCompanyId: unit.vendorLockCompanyId ?? null,
              lockedCityId: unit.lockedCityId ?? null,
            } satisfies MockBuildingUnit,
          ]
        }),
      )
      const existingUnits = new Map((building.pendingConfiguration?.units ?? []).map((unit) => [`${unit.gridX},${unit.gridY}`, unit]))
      const existingRemovals = new Map((building.pendingConfiguration?.removals ?? []).map((removal) => [`${removal.gridX},${removal.gridY}`, removal]))
      const allPositions = new Set<string>([...currentUnits.keys(), ...desiredUnits.keys(), ...existingUnits.keys(), ...existingRemovals.keys()])

      const nextPendingUnits: MockBuildingConfigurationPlanUnit[] = []
      const nextPendingRemovals: MockBuildingConfigurationPlanRemoval[] = []

      for (const position of allPositions) {
        const current = currentUnits.get(position)
        const desired = desiredUnits.get(position)
        const existingUnit = existingUnits.get(position)
        const existingRemoval = existingRemovals.get(position)
        const [gridX = 0, gridY = 0] = position.split(',').map(Number)

        if (desired) {
          if (existingUnit && arePendingUnitsEquivalent(existingUnit, desired)) {
            nextPendingUnits.push(
              existingUnit.appliesAtTick > state.gameState.currentTick
                ? { ...existingUnit }
                : {
                    ...existingUnit,
                    startedAtTick: state.gameState.currentTick,
                    appliesAtTick: state.gameState.currentTick,
                    ticksRequired: 0,
                    isChanged: false,
                    isReverting: false,
                  },
            )
            continue
          }

          if (existingRemoval && current && areUnitsEquivalent(current, desired)) {
            const ticksRequired = calculateCancelTicks(existingRemoval.ticksRequired)
            nextPendingUnits.push({
              ...cloneUnit(desired),
              startedAtTick: state.gameState.currentTick,
              appliesAtTick: state.gameState.currentTick + ticksRequired,
              ticksRequired,
              isChanged: true,
              isReverting: true,
            })
            continue
          }

          if (existingUnit && current && areUnitsEquivalent(current, desired)) {
            const ticksRequired = calculateCancelTicks(existingUnit.ticksRequired)
            nextPendingUnits.push({
              ...cloneUnit(desired),
              startedAtTick: state.gameState.currentTick,
              appliesAtTick: state.gameState.currentTick + ticksRequired,
              ticksRequired,
              isChanged: true,
              isReverting: true,
            })
            continue
          }

          const ticksRequired = calculateUnitTicks(current, desired)
          nextPendingUnits.push({
            ...cloneUnit(desired),
            startedAtTick: state.gameState.currentTick,
            appliesAtTick: state.gameState.currentTick + ticksRequired,
            ticksRequired,
            isChanged: !areUnitsEquivalent(current, desired),
            isReverting: false,
          })
          continue
        }

        if (existingRemoval && current) {
          nextPendingRemovals.push({ ...existingRemoval })
          continue
        }

        if (existingUnit) {
          nextPendingRemovals.push({
            id: `pending-removal-${gridX}-${gridY}-${Date.now()}`,
            gridX,
            gridY,
            startedAtTick: state.gameState.currentTick,
            appliesAtTick: state.gameState.currentTick + calculateCancelTicks(existingUnit.ticksRequired),
            ticksRequired: calculateCancelTicks(existingUnit.ticksRequired),
            isReverting: true,
          })
          continue
        }

        if (current) {
          nextPendingRemovals.push({
            id: `pending-removal-${gridX}-${gridY}-${Date.now()}`,
            gridX,
            gridY,
            startedAtTick: state.gameState.currentTick,
            appliesAtTick: state.gameState.currentTick + 3,
            ticksRequired: 3,
            isReverting: false,
          })
        }
      }

      if (!nextPendingUnits.some((unit) => unit.isChanged) && nextPendingRemovals.length === 0) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'No building configuration changes were detected.' }] }) })
      }

      const planId = building.pendingConfiguration?.id ?? `plan-${Date.now()}`

      building.pendingConfiguration = buildPlanSummary(
        {
          id: planId,
          buildingId: building.id,
          submittedAtUtc: new Date().toISOString(),
          submittedAtTick: state.gameState.currentTick,
          appliesAtTick: state.gameState.currentTick,
          totalTicksRequired: 0,
          units: nextPendingUnits.sort((left, right) => left.gridY - right.gridY || left.gridX - right.gridX),
          removals: nextPendingRemovals,
        },
        state.gameState.currentTick,
      )

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { storeBuildingConfiguration: building.pendingConfiguration } }),
      })
    }

    if (query.includes('CancelBuildingConfiguration')) {
      applyDueBuildingUpgrades(state)

      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      const building = player?.companies.flatMap((company) => company.buildings).find((candidate) => candidate.id === input?.buildingId)

      if (!building) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building not found', extensions: { code: 'BUILDING_NOT_FOUND' } }] }) })
      }

      if (!building.pendingConfiguration) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'This building does not have a pending configuration plan to cancel.', extensions: { code: 'NO_PENDING_CONFIGURATION' } }] }),
        })
      }

      // Cancel by submitting the current active layout - creates reverting removals for pending units
      const nextRemovals: MockBuildingConfigurationPlanRemoval[] = []
      for (const pendingUnit of building.pendingConfiguration.units) {
        if (pendingUnit.isChanged) {
          const ticksRequired = Math.max(Math.ceil(pendingUnit.ticksRequired * 0.1), 1)
          nextRemovals.push({
            id: `cancel-removal-${pendingUnit.gridX}-${pendingUnit.gridY}-${Date.now()}`,
            gridX: pendingUnit.gridX,
            gridY: pendingUnit.gridY,
            startedAtTick: state.gameState.currentTick,
            appliesAtTick: state.gameState.currentTick + ticksRequired,
            ticksRequired,
            isReverting: true,
          })
        }
      }
      for (const removal of building.pendingConfiguration.removals) {
        if (!removal.isReverting) {
          nextRemovals.push({ ...removal })
        }
      }

      const planId = building.pendingConfiguration.id
      building.pendingConfiguration = buildPlanSummary(
        {
          id: planId,
          buildingId: building.id,
          submittedAtUtc: new Date().toISOString(),
          submittedAtTick: state.gameState.currentTick,
          appliesAtTick: state.gameState.currentTick,
          totalTicksRequired: 0,
          units: [],
          removals: nextRemovals,
        },
        state.gameState.currentTick,
      )

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { cancelBuildingConfiguration: building.pendingConfiguration } }),
      })
    }

    if (query.includes('SetBuildingForSale') || query.includes('setBuildingForSale')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      const building = player?.companies.flatMap((company) => company.buildings).find((candidate) => candidate.id === input?.buildingId)

      if (!building) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building not found' }] }) })
      }

      building.isForSale = input.isForSale
      building.askingPrice = input.isForSale ? input.askingPrice : null

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { setBuildingForSale: { id: building.id, isForSale: building.isForSale, askingPrice: building.askingPrice } } }),
      })
    }

    if (query.includes('SetRentPerSqm') || query.includes('setRentPerSqm')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      const building = player?.companies.flatMap((company) => company.buildings).find((candidate) => candidate.id === input?.buildingId)

      if (!building) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: "Building not found or you don't own it.", extensions: { code: 'BUILDING_NOT_FOUND' } }] }),
        })
      }

      if (building.type !== 'APARTMENT' && building.type !== 'COMMERCIAL') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Only apartment and commercial buildings support rent pricing.', extensions: { code: 'INVALID_BUILDING_TYPE' } }] }),
        })
      }

      if (input.rentPerSqm < 0) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Rent per m² must be a non-negative value.', extensions: { code: 'INVALID_RENT' } }] }),
        })
      }

      const currentTick = state.gameState?.currentTick ?? 0
      building.pendingPricePerSqm = input.rentPerSqm
      building.pendingPriceActivationTick = currentTick + 24

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            setRentPerSqm: {
              id: building.id,
              pricePerSqm: building.pricePerSqm ?? null,
              pendingPricePerSqm: building.pendingPricePerSqm,
              pendingPriceActivationTick: building.pendingPriceActivationTick,
            },
          },
        }),
      })
    }

    if (query.includes('PurchaseLot') || query.includes('purchaseLot')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      const company = player.companies.find((c) => c.id === input?.companyId)
      if (!company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found' }] }) })
      }
      const lot = state.buildingLots.find((l) => l.id === input?.lotId)
      if (!lot) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Building lot not found.' }] }) })
      }
      if (lot.ownerCompanyId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'This lot has already been purchased.', extensions: { code: 'LOT_ALREADY_OWNED' } }] }),
        })
      }
      const suitableTypes = lot.suitableTypes.split(',').map((s) => s.trim())
      if (!suitableTypes.includes(input.buildingType)) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: `Building type ${input.buildingType} is not suitable for this lot.`, extensions: { code: 'UNSUITABLE_BUILDING_TYPE' } }] }),
        })
      }
      const constructionCostsByType: Record<string, number> = {
        MINE: 5000,
        FACTORY: 15000,
        SALES_SHOP: 8000,
        RESEARCH_DEVELOPMENT: 25000,
        APARTMENT: 40000,
        COMMERCIAL: 20000,
        MEDIA_HOUSE: 30000,
        BANK: 50000,
        EXCHANGE: 60000,
        POWER_PLANT: 80000,
      }
      const constructionTicksByType: Record<string, number> = {
        MINE: 24,
        FACTORY: 48,
        SALES_SHOP: 24,
        RESEARCH_DEVELOPMENT: 72,
        APARTMENT: 96,
        COMMERCIAL: 48,
        MEDIA_HOUSE: 48,
        BANK: 72,
        EXCHANGE: 96,
        POWER_PLANT: 120,
      }
      const constructionCost = constructionCostsByType[input.buildingType] ?? 10000
      const constructionTicks = constructionTicksByType[input.buildingType] ?? 24
      const currentTick = state.gameState?.currentTick ?? 1
      if (company.cash < lot.price + constructionCost) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [
              {
                message: `Insufficient funds. Total cost (lot + construction) is $${(lot.price + constructionCost).toLocaleString()} but you only have $${company.cash.toLocaleString()}.`,
                extensions: { code: 'INSUFFICIENT_FUNDS' },
              },
            ],
          }),
        })
      }
      company.cash -= lot.price + constructionCost
      const isPowerPlant = input.buildingType === 'POWER_PLANT'
      const plantType = input.powerPlantType ?? (isPowerPlant ? 'COAL' : null)
      const defaultOutputByType: Record<string, number> = {
        COAL: 50,
        GAS: 40,
        SOLAR: 20,
        WIND: 25,
        NUCLEAR: 200,
      }
      const newBuilding: MockBuilding = {
        id: `building-lot-${Date.now()}`,
        companyId: company.id,
        cityId: lot.cityId,
        type: input.buildingType,
        name: input.buildingName,
        latitude: lot.latitude,
        longitude: lot.longitude,
        level: 1,
        powerConsumption: isPowerPlant ? 0 : 1,
        powerPlantType: plantType,
        powerOutput: isPowerPlant ? (defaultOutputByType[plantType ?? ''] ?? 30) : null,
        powerStatus: 'POWERED',
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        isUnderConstruction: true,
        constructionCompletesAtTick: currentTick + constructionTicks,
        constructionCost,
        units: [],
        pendingConfiguration: null,
      }
      company.buildings.push(newBuilding)
      lot.ownerCompanyId = company.id
      lot.buildingId = newBuilding.id
      lot.ownerCompany = { id: company.id, name: company.name }
      lot.building = {
        id: newBuilding.id,
        name: newBuilding.name,
        type: newBuilding.type,
        isUnderConstruction: true,
        constructionCompletesAtTick: currentTick + constructionTicks,
        constructionCost,
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            purchaseLot: {
              lot,
              building: newBuilding,
              company: { id: company.id, name: company.name, cash: company.cash },
            },
          },
        }),
      })
    }

    if (query.includes('CompleteFirstSaleMilestone') || query.includes('completeFirstSaleMilestone')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      // Already completed — idempotent
      if (player.onboardingFirstSaleCompletedAtUtc) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { completeFirstSaleMilestone: { ...player, password: undefined } } }),
        })
      }

      // Validate backend-authoritative condition: shop building must be tracked
      if (!player.onboardingShopBuildingId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'No sales shop was found for this onboarding milestone.', extensions: { code: 'SHOP_NOT_FOUND' } }] }),
        })
      }

      // Find the shop building and check it has a public-sales unit with a price
      const shopBuilding = player.companies.flatMap((c) => c.buildings).find((b) => b.id === player.onboardingShopBuildingId)

      const hasSalesUnit = shopBuilding?.units.some((u) => u.unitType === 'PUBLIC_SALES' && (u.minPrice ?? 0) > 0)

      if (!hasSalesUnit) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [
              {
                message: 'Your sales shop is not yet configured. Please set up a public sales unit with a selling price and return here to complete the milestone.',
                extensions: { code: 'SHOP_NOT_CONFIGURED' },
              },
            ],
          }),
        })
      }

      // Validate backend-authoritative condition: a real public sale must have occurred
      const hasRealSale = state.publicSalesRecords.some((r) => r.buildingId === player.onboardingShopBuildingId && r.quantitySold > 0)

      if (!hasRealSale) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [
              {
                message: 'Your shop has not made its first real sale yet. Wait for the simulation to process the next tick and try again after your shop has sold at least one item.',
                extensions: { code: 'FIRST_SALE_NOT_RECORDED' },
              },
            ],
          }),
        })
      }

      player.onboardingFirstSaleCompletedAtUtc = new Date().toISOString()
      player.onboardingShopBuildingId = null

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { completeFirstSaleMilestone: { ...player, password: undefined } } }),
      })
    }

    // Queries - order specific handlers before generic ones
    if (query.includes('firstSaleMission') && !query.includes('CompleteFirstSaleMilestone') && !query.includes('completeFirstSaleMilestone')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      // Already completed
      if (player.onboardingFirstSaleCompletedAtUtc) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            data: {
              firstSaleMission: {
                phase: 'ALREADY_COMPLETED',
                shopBuildingId: null,
                shopName: null,
                blockers: [],
                firstSaleRevenue: null,
                firstSaleProductName: null,
                firstSaleTick: null,
                firstSaleQuantity: null,
                firstSalePricePerUnit: null,
              },
            },
          }),
        })
      }

      if (!player.onboardingShopBuildingId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            data: {
              firstSaleMission: {
                phase: 'NO_SHOP',
                shopBuildingId: null,
                shopName: null,
                blockers: [],
                firstSaleRevenue: null,
                firstSaleProductName: null,
                firstSaleTick: null,
                firstSaleQuantity: null,
                firstSalePricePerUnit: null,
              },
            },
          }),
        })
      }

      const shopBuilding = player.companies.flatMap((c) => c.buildings).find((b) => b.id === player.onboardingShopBuildingId)

      // Check for real sale
      const firstSaleRecord = state.publicSalesRecords.filter((r) => r.buildingId === player.onboardingShopBuildingId && r.quantitySold > 0).sort((a, b) => a.tick - b.tick)[0]

      if (firstSaleRecord) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            data: {
              firstSaleMission: {
                phase: 'FIRST_SALE_RECORDED',
                shopBuildingId: shopBuilding?.id ?? player.onboardingShopBuildingId,
                shopName: shopBuilding?.name ?? null,
                blockers: [],
                firstSaleRevenue: firstSaleRecord.revenue,
                firstSaleProductName: firstSaleRecord.productTypeName,
                firstSaleTick: firstSaleRecord.tick,
                firstSaleQuantity: firstSaleRecord.quantitySold,
                firstSalePricePerUnit: firstSaleRecord.pricePerUnit,
              },
            },
          }),
        })
      }

      // Compute blockers
      const blockers: string[] = []
      const publicSalesUnit = shopBuilding?.units.find((u) => u.unitType === 'PUBLIC_SALES')
      if (!publicSalesUnit) {
        blockers.push('PUBLIC_SALES_UNIT_MISSING')
      } else {
        if ((publicSalesUnit.minPrice ?? 0) <= 0) blockers.push('PRICE_NOT_SET')
        blockers.push('NO_INVENTORY') // simplified: no inventory simulation in mock
      }

      const phase = blockers.length === 0 ? 'AWAITING_FIRST_SALE' : 'CONFIGURE_SHOP'

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            firstSaleMission: {
              phase,
              shopBuildingId: shopBuilding?.id ?? player.onboardingShopBuildingId,
              shopName: shopBuilding?.name ?? null,
              blockers,
              firstSaleRevenue: null,
              firstSaleProductName: null,
              firstSaleTick: null,
              firstSaleQuantity: null,
              firstSalePricePerUnit: null,
            },
          },
        }),
      })
    }
    if (query.includes('starterIndustries')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: { starterIndustries: { industries: ['FURNITURE', 'FOOD_PROCESSING', 'HEALTHCARE'] } },
        }),
      })
    }

    if (query.includes('rankedProductTypes')) {
      const activePlayer = state.players.find((player) => player.id === state.currentUserId)
      const hasActiveProSubscription = !!activePlayer?.proSubscriptionEndsAtUtc && new Date(activePlayer.proSubscriptionEndsAtUtc).getTime() > Date.now()
      const buildingId = body.variables?.buildingId
      const unitType = (body.variables?.unitType as string | undefined)?.toUpperCase() ?? ''

      // Find the building to compute context-aware rankings (simulates backend logic)
      const building = state.players
        .flatMap((p) => p.companies)
        .flatMap((c) => c.buildings)
        .find((b) => b.id === buildingId)

      // Collect unit product IDs from active + pending configuration
      const allBuildingUnits = [
        ...(building?.units ?? []),
        ...(building?.pendingConfiguration?.units ?? []),
      ]

      const connectedProductIds = new Set<string>()
      if (unitType === 'PUBLIC_SALES') {
        // Connected = products from MANUFACTURING or B2B_SALES units
        allBuildingUnits
          .filter((u) => (u.unitType === 'MANUFACTURING' || u.unitType === 'B2B_SALES') && u.productTypeId)
          .forEach((u) => connectedProductIds.add(u.productTypeId!))
      } else if (unitType === 'STORAGE') {
        // Connected = products from MANUFACTURING units
        allBuildingUnits
          .filter((u) => u.unitType === 'MANUFACTURING' && u.productTypeId)
          .forEach((u) => connectedProductIds.add(u.productTypeId!))
        // Also products currently in inventory
        allBuildingUnits
          .filter((u) => u.inventoryItems && u.inventoryItems.length > 0)
          .flatMap((u) => u.inventoryItems ?? [])
          .filter((item) => item.productTypeId)
          .forEach((item) => connectedProductIds.add(item.productTypeId!))
      } else if (unitType === 'B2B_SALES') {
        // Connected = products from MANUFACTURING or STORAGE units
        allBuildingUnits
          .filter((u) => (u.unitType === 'MANUFACTURING' || u.unitType === 'STORAGE') && u.productTypeId)
          .forEach((u) => connectedProductIds.add(u.productTypeId!))
      }

      // Sort: connected first (score 100), then catalog (score 10), both alphabetical
      const enriched = state.productTypes.map((product) => {
        const isConnected = connectedProductIds.has(product.id)
        return {
          rankingReason: isConnected ? 'connected' : 'catalog',
          rankingScore: isConnected ? 100 : 10,
          productType: {
            ...product,
            isUnlockedForCurrentPlayer: product.isProOnly ? hasActiveProSubscription : true,
          },
        }
      })
      enriched.sort((a, b) => {
        if (b.rankingScore !== a.rankingScore) return b.rankingScore - a.rankingScore
        return a.productType.name.localeCompare(b.productType.name)
      })

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { rankedProductTypes: enriched } }),
      })
    }

    if (query.includes('productTypes')) {
      const industry = body.variables?.industry
      const activePlayer = state.players.find((player) => player.id === state.currentUserId)
      const hasActiveProSubscription = !!activePlayer?.proSubscriptionEndsAtUtc && new Date(activePlayer.proSubscriptionEndsAtUtc).getTime() > Date.now()
      const filtered = industry ? state.productTypes.filter((p) => p.industry === industry) : state.productTypes
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            productTypes: filtered.map((product) => ({
              ...product,
              isUnlockedForCurrentPlayer: product.isProOnly ? hasActiveProSubscription : true,
            })),
          },
        }),
      })
    }

    if (query.includes('buildingUnitInventorySummaries')) {
      const buildingId = body.variables?.buildingId
      const building = state.players
        .flatMap((player) => player.companies)
        .flatMap((company) => company.buildings)
        .find((candidate) => candidate.id === buildingId)

      const buildingUnitInventorySummaries = (building?.units ?? [])
        .map((unit) => {
          const inventoryItems = getMockUnitInventoryItems(unit)
          const quantity = inventoryItems.reduce((total, item) => total + item.quantity, 0)
          const capacity = getMockUnitCapacity(unit)
          return {
            buildingUnitId: unit.id,
            quantity,
            capacity,
            fillPercent: capacity > 0 ? Math.min(quantity / capacity, 1) : 0,
            averageQuality: quantity > 0 ? Number((inventoryItems.reduce((total, item) => total + item.quantity * item.quality, 0) / quantity).toFixed(4)) : null,
            totalSourcingCost: Number(inventoryItems.reduce((total, item) => total + item.sourcingCostTotal, 0).toFixed(2)),
            sourcingCostPerUnit: quantity > 0 ? Number((inventoryItems.reduce((total, item) => total + item.sourcingCostTotal, 0) / quantity).toFixed(2)) : 0,
          }
        })
        .filter((summary) => summary.capacity > 0 || summary.quantity > 0)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingUnitInventorySummaries } }),
      })
    }

    if (query.includes('buildingUnitInventories')) {
      const buildingId = body.variables?.buildingId
      const building = state.players
        .flatMap((player) => player.companies)
        .flatMap((company) => company.buildings)
        .find((candidate) => candidate.id === buildingId)

      const buildingUnitInventories = (building?.units ?? []).flatMap((unit) => getMockUnitInventoryItems(unit))

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingUnitInventories } }),
      })
    }

    if (query.includes('buildingUnitResourceHistories')) {
      const buildingId = body.variables?.buildingId
      const building = state.players
        .flatMap((player) => player.companies)
        .flatMap((company) => company.buildings)
        .find((candidate) => candidate.id === buildingId)

      const buildingUnitResourceHistories = (building?.units ?? []).flatMap((unit) => getMockUnitResourceHistory(unit)).sort((left, right) => left.tick - right.tick)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingUnitResourceHistories } }),
      })
    }

    if (query.includes('buildingUnitOperationalStatuses')) {
      const buildingId = body.variables?.buildingId
      const building = state.players
        .flatMap((player) => player.companies)
        .flatMap((company) => company.buildings)
        .find((candidate) => candidate.id === buildingId)

      const buildingUnitOperationalStatuses = (building?.units ?? []).map((unit) => {
        // Base labor hours and energy MWh per unit type, mirroring backend:
        // projects/Api/Utilities/CompanyEconomyCalculator.cs :: GetBaseUnitLaborHours / GetBaseUnitEnergyMwh
        // Update here if the backend constants change.
        const laborHoursMap: Record<string, number> = {
          MINING: 1.4,
          STORAGE: 0.15,
          B2B_SALES: 0.45,
          PURCHASE: 0.35,
          MANUFACTURING: 0.85,
          BRANDING: 0.3,
          MARKETING: 0.6,
          PUBLIC_SALES: 0.7,
          PRODUCT_QUALITY: 0.55,
          BRAND_QUALITY: 0.55,
        }
        const energyMwhMap: Record<string, number> = {
          MINING: 0.45,
          STORAGE: 0.04,
          B2B_SALES: 0.08,
          PURCHASE: 0.06,
          MANUFACTURING: 0.18,
          BRANDING: 0.05,
          MARKETING: 0.07,
          PUBLIC_SALES: 0.12,
          PRODUCT_QUALITY: 0.09,
          BRAND_QUALITY: 0.09,
        }
        const level = unit.level || 1
        const laborHours = (laborHoursMap[unit.unitType] ?? 0) * level
        const energyMwh = (energyMwhMap[unit.unitType] ?? 0) * level
        // Bratislava base wage $18/hr × default salary multiplier 1.0
        // (projects/Api/Data/AppDbInitializer.cs BaseSalaryPerManhour)
        const hourlyWage = 18
        // projects/Api/Engine/GameConstants.cs EnergyPricePerMwh
        const energyPricePerMwh = 55
        return {
          buildingUnitId: unit.id,
          status: unit.inventoryQuantity && unit.inventoryQuantity > 0 ? 'ACTIVE' : 'IDLE',
          blockedCode: null,
          blockedReason: null,
          idleTicks: 0,
          nextTickLaborCost: laborHours > 0 ? Math.round(laborHours * hourlyWage * 100) / 100 : null,
          nextTickEnergyCost: energyMwh > 0 ? Math.round(energyMwh * energyPricePerMwh * 100) / 100 : null,
        }
      })

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingUnitOperationalStatuses } }),
      })
    }

    if (query.includes('buildingRecentActivity')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingRecentActivity: [] } }),
      })
    }

    if (query.includes('buildingFinancialTimeline')) {
      const buildingId = body.variables?.buildingId
      const limit = Number(body.variables?.limit ?? 100)
      const buildingFinancialTimeline = buildMockBuildingFinancialTimeline(state, buildingId, limit)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { buildingFinancialTimeline } }),
      })
    }

    if (query.includes('globalExchangeOffers')) {
      const destinationCityId = body.variables?.destinationCityId
      const resourceTypeId = body.variables?.resourceTypeId
      const destinationCity = state.cities.find((city) => city.id === destinationCityId)
      const resources = state.resourceTypes.filter((resource) => !resourceTypeId || resource.id === resourceTypeId)

      const globalExchangeOffers = destinationCity
        ? state.cities
            .flatMap((city) =>
              resources.map((resource) => {
                const abundance = city.resources.find((entry) => entry.resourceType.id === resource.id)?.abundance ?? 0.05
                const distanceKm = computeDistanceKm(city.latitude, city.longitude, destinationCity.latitude, destinationCity.longitude)
                const exchangePricePerUnit = computeMockExchangePrice(resource.basePrice, abundance, city.averageRentPerSqm)
                const transitCostPerUnit = computeMockTransitCost(resource.weightPerUnit, distanceKm)
                return {
                  cityId: city.id,
                  cityName: city.name,
                  resourceTypeId: resource.id,
                  resourceName: resource.name,
                  resourceSlug: resource.slug,
                  unitSymbol: resource.unitSymbol,
                  localAbundance: abundance,
                  exchangePricePerUnit,
                  estimatedQuality: computeMockExchangeQuality(abundance),
                  transitCostPerUnit,
                  deliveredPricePerUnit: Number((exchangePricePerUnit + transitCostPerUnit).toFixed(2)),
                  distanceKm: Number(distanceKm.toFixed(1)),
                }
              }),
            )
            .sort((left, right) => left.deliveredPricePerUnit - right.deliveredPricePerUnit)
        : []

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { globalExchangeOffers } }),
      })
    }

    if (query.includes('globalExchangeProductListings')) {
      const productTypeIdFilter = body.variables?.productTypeId
      const listings = state.productExchangeListings.filter((l) => !productTypeIdFilter || l.productTypeId === productTypeIdFilter)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { globalExchangeProductListings: listings } }),
      })
    }

    if (query.includes('chatMessages')) {
      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated.' }] }),
        })
      }

      const currentPlayer = state.players.find((player) => player.id === state.currentUserId)
      const limit = Math.min(Math.max(Number(body.variables?.limit ?? 50), 1), 100)
      const canSeeInvisible = currentPlayer?.role === 'ADMIN'

      const messages = state.chatMessages
        .filter((message) => {
          const author = state.players.find((player) => player.id === message.playerId)
          if (!author) return false
          return !author.isInvisibleInChat || author.id === state.currentUserId || canSeeInvisible
        })
        .slice(-limit)
        .map((message) => {
          const author = state.players.find((player) => player.id === message.playerId)!
          return {
            id: message.id,
            playerId: message.playerId,
            playerDisplayName: author.displayName,
            message: message.message,
            sentAtUtc: message.sentAtUtc,
            isOwnMessage: message.playerId === state.currentUserId,
          }
        })

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { chatMessages: messages } }),
      })
    }

    if (query.includes('sendChatMessage')) {
      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated.' }] }),
        })
      }

      const currentPlayer = state.players.find((player) => player.id === state.currentUserId)
      const message = String(body.variables?.input?.message ?? '').trim()
      if (!currentPlayer || !message) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Chat message cannot be empty.' }] }),
        })
      }

      const chatMessage = {
        id: `chat-${state.chatMessages.length + 1}`,
        playerId: currentPlayer.id,
        message,
        sentAtUtc: new Date().toISOString(),
      }
      state.chatMessages.push(chatMessage)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            sendChatMessage: {
              id: chatMessage.id,
              playerId: currentPlayer.id,
              playerDisplayName: currentPlayer.displayName,
              message: chatMessage.message,
              sentAtUtc: chatMessage.sentAtUtc,
              isOwnMessage: true,
            },
          },
        }),
      })
    }

    if (query.includes('myCompanies')) {
      applyDueBuildingUpgrades(state)
      const player = state.players.find((p) => p.id === state.currentUserId)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { myCompanies: player?.companies ?? [] } }),
      })
    }

    if (query.includes('companySettings')) {
      const companyId = body.variables?.companyId
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      const company = player?.companies.find((candidate) => candidate.id === companyId)

      if (!company) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { companySettings: null } }),
        })
      }

      const computeAssetValue = (candidate: MockCompany) => candidate.cash + getCompanyAssetBaseValue(candidate)
      const companyAssetValue = computeAssetValue(company)
      const maxAssetValue = Math.max(...state.players.flatMap((candidate) => candidate.companies).map(computeAssetValue), 0)
      const ageTicks = Math.max(state.gameState.currentTick - (company.foundedAtTick ?? 0), 0)
      const ageFactor = Number(Math.min(ageTicks / (TICKS_PER_YEAR * 2), 1).toFixed(4))
      const assetFactor = Number((maxAssetValue > 0 ? Math.min(companyAssetValue / maxAssetValue, 1) : 0).toFixed(4))
      const overheadRate = Number((0.5 * ageFactor * assetFactor).toFixed(4))

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            companySettings: {
              companyId: company.id,
              companyName: company.name,
              cash: company.cash,
              totalSharesIssued: getCompanyTotalShares(company),
              dividendPayoutRatio: getCompanyDividendPayoutRatio(company),
              foundedAtTick: company.foundedAtTick ?? 0,
              administrationOverheadRate: overheadRate,
              ageFactor,
              assetFactor,
              assetValue: companyAssetValue,
              citySalarySettings: state.cities.map((city) => {
                const salaryMultiplier = company.citySalaryMultipliers?.[city.id] ?? 1
                return {
                  cityId: city.id,
                  cityName: city.name,
                  baseSalaryPerManhour: city.baseSalaryPerManhour,
                  salaryMultiplier,
                  effectiveSalaryPerManhour: Number((city.baseSalaryPerManhour * salaryMultiplier).toFixed(2)),
                }
              }),
            },
          },
        }),
      })
    }

    if (query.includes('personAccount')) {
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      const shareholdings = state.shareholdings
        .filter((holding) => holding.ownerPlayerId === player.id && holding.shareCount > 0)
        .map((holding) => {
          const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === holding.companyId)
          if (!company) {
            return null
          }

          const sharePrice = computeMockSharePrice(company)
          return {
            companyId: company.id,
            companyName: company.name,
            shareCount: holding.shareCount,
            ownershipRatio: Number((holding.shareCount / getCompanyTotalShares(company)).toFixed(4)),
            sharePrice,
            marketValue: Number((holding.shareCount * sharePrice).toFixed(2)),
          }
        })
        .filter((holding): holding is NonNullable<typeof holding> => holding !== null)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            personAccount: {
              playerId: player.id,
              displayName: player.displayName,
              personalCash: player.personalCash,
              activeAccountType: player.activeAccountType,
              activeCompanyId: player.activeCompanyId,
              shareholdings,
              dividendPayments: player.dividendPayments,
              stockTrades: player.stockTrades,
            },
          },
        }),
      })
    }

    if (query.includes('stockExchangeListings')) {
      const player = state.players.find((candidate) => candidate.id === state.currentUserId)
      const listings = state.players
        .flatMap((candidate) => candidate.companies)
        .map((company) => {
          const playerOwnedShares = player
            ? state.shareholdings.filter((holding) => holding.companyId === company.id && holding.ownerPlayerId === player.id).reduce((total, holding) => total + holding.shareCount, 0)
            : 0

          const controlledCompanyIds = player ? getPlayerControlledCompanyIds(state, player.id) : new Set<string>()
          const controlledCompanyOwnedShares = player
            ? state.shareholdings
                .filter((holding) => holding.companyId === company.id && holding.ownerCompanyId && controlledCompanyIds.has(holding.ownerCompanyId))
                .reduce((total, holding) => total + holding.shareCount, 0)
            : 0

          const combinedControlledOwnershipRatio = player ? getCombinedControlledOwnershipRatio(state, player.id, company) : 0

          return {
            companyId: company.id,
            companyName: company.name,
            totalSharesIssued: getCompanyTotalShares(company),
            publicFloatShares: getPublicFloatShares(state, company),
            sharePrice: computeMockSharePrice(company),
            marketValue: Number((getCompanyTotalShares(company) * computeMockSharePrice(company)).toFixed(2)),
            bidPrice: Number((computeMockSharePrice(company) * 0.99).toFixed(2)),
            askPrice: Number((computeMockSharePrice(company) * 1.01).toFixed(2)),
            dividendPayoutRatio: getCompanyDividendPayoutRatio(company),
            playerOwnedShares,
            controlledCompanyOwnedShares,
            combinedControlledOwnershipRatio,
            canClaimControl: combinedControlledOwnershipRatio >= 0.5,
            canMerge: combinedControlledOwnershipRatio >= 0.9,
          }
        })
        .sort((left, right) => left.companyName.localeCompare(right.companyName))

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { stockExchangeListings: listings } }),
      })
    }

    if (query.includes('companyShareholders')) {
      const companyId = body.variables?.companyId
      const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === companyId)
      if (!company) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { companyShareholders: null } }),
        })
      }

      const totalShares = getCompanyTotalShares(company)
      const companyShareholdings = state.shareholdings.filter((h) => h.companyId === companyId && h.shareCount > 0)
      const namedSharesTotal = companyShareholdings.reduce((sum, h) => sum + h.shareCount, 0)
      const publicFloatShares = Math.max(0, totalShares - namedSharesTotal)

      const shareholders = companyShareholdings.map((h) => {
        const ownerPlayer = h.ownerPlayerId ? state.players.find((p) => p.id === h.ownerPlayerId) : null
        const ownerCompany = h.ownerCompanyId ? state.players.flatMap((p) => p.companies).find((c) => c.id === h.ownerCompanyId) : null
        const holderName = ownerPlayer?.displayName ?? ownerCompany?.name ?? 'Unknown'
        const holderType = h.ownerPlayerId ? 'PERSON' : 'COMPANY'
        const ownershipRatio = totalShares > 0 ? Number((h.shareCount / totalShares).toFixed(4)) : 0

        return {
          holderName,
          holderType,
          holderPlayerId: h.ownerPlayerId ?? null,
          holderCompanyId: h.ownerCompanyId ?? null,
          shareCount: h.shareCount,
          ownershipRatio,
        }
      }).sort((a, b) => b.ownershipRatio - a.ownershipRatio)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            companyShareholders: {
              companyId: company.id,
              companyName: company.name,
              totalSharesIssued: totalShares,
              publicFloatShares,
              shareholderCount: shareholders.length,
              shareholders,
            },
          },
        }),
      })
    }

    if (query.includes('stockExchangePriceHistory')) {
      const companyId = body.variables?.companyId
      const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === companyId)
      const priceHistory =
        (companyId ? state.stockPriceHistory[companyId] : null) ??
        (company
          ? [
              {
                companyId: company.id,
                tick: state.gameState.currentTick,
                price: computeMockSharePrice(company),
                recordedAtUtc: new Date().toISOString(),
              },
            ]
          : [])

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { stockExchangePriceHistory: priceHistory } }),
      })
    }

    if (query.includes('rankings') && !query.includes('companyRankings')) {
      const rankings = state.players
        .filter((p) => p.role !== 'ADMIN')
        .map((p) => {
          const personalCash = p.personalCash ?? 0
          const sharesValue = Number(
            state.shareholdings
              .filter((holding) => holding.ownerPlayerId === p.id && holding.shareCount > 0)
              .reduce((total, holding) => {
                const company = state.players.flatMap((candidate) => candidate.companies).find((candidate) => candidate.id === holding.companyId)

                if (!company) {
                  return total
                }

                return total + holding.shareCount * computeMockSharePrice(company)
              }, 0)
              .toFixed(2),
          )

          return {
            playerId: p.id,
            displayName: p.displayName,
            personalCash,
            sharesValue,
            totalWealth: Number((personalCash + sharesValue).toFixed(2)),
            companyCount: p.companies.length,
          }
        })
        .sort((a, b) => b.totalWealth - a.totalWealth)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { rankings } }),
      })
    }

    if (query.includes('companyRankings')) {
      const companyRankings = state.players
        .filter((player) => player.role !== 'ADMIN')
        .flatMap((player) =>
          player.companies.map((company) => {
            const buildingValue = company.buildings.reduce((sum, building) => {
              const baseValues: Record<string, number> = {
                MINE: 250000,
                FACTORY: 200000,
                SALES_SHOP: 150000,
                RESEARCH_DEVELOPMENT: 300000,
                APARTMENT: 400000,
                COMMERCIAL: 350000,
                MEDIA_HOUSE: 500000,
                BANK: 600000,
                EXCHANGE: 450000,
                POWER_PLANT: 350000,
              }
              return sum + (baseValues[building.type] ?? 0) * building.level
            }, 0)
            const inventoryValue = 0

            return {
              companyId: company.id,
              companyName: company.name,
              playerId: player.id,
              ownerDisplayName: player.displayName,
              cash: company.cash,
              buildingValue,
              inventoryValue,
              totalWealth: company.cash + buildingValue + inventoryValue,
              buildingCount: company.buildings.length,
            }
          }),
        )
        .sort((left, right) => right.totalWealth - left.totalWealth)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { companyRankings } }),
      })
    }

    if (query.includes('gameState')) {
      applyDueBuildingUpgrades(state)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { gameState: buildMockGameStatePayload(state.gameState) } }),
      })
    }

    if (query.includes('myPendingActions')) {
      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }),
        })
      }
      applyDueBuildingUpgrades(state)
      const player = state.players.find((p) => p.id === state.currentUserId)
      const currentTick = state.gameState.currentTick
      const pendingActions = (player?.companies ?? [])
        .flatMap((company) =>
          company.buildings
            .filter((building) => building.pendingConfiguration !== null && building.pendingConfiguration.appliesAtTick > currentTick)
            .map((building) => ({
              id: building.pendingConfiguration!.id,
              actionType: 'BUILDING_UPGRADE',
              buildingId: building.id,
              buildingName: building.name,
              buildingType: building.type,
              submittedAtUtc: building.pendingConfiguration!.submittedAtUtc,
              submittedAtTick: building.pendingConfiguration!.submittedAtTick,
              appliesAtTick: building.pendingConfiguration!.appliesAtTick,
              ticksRemaining: building.pendingConfiguration!.appliesAtTick - currentTick,
              totalTicksRequired: building.pendingConfiguration!.totalTicksRequired,
            })),
        )
        .sort((a, b) => a.appliesAtTick - b.appliesAtTick)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { myPendingActions: pendingActions } }),
      })
    }

    if (query.includes('procurementPreview')) {
      const unitId: string = body.variables?.unitId ?? body.variables?.buildingUnitId ?? ''
      const customPreview = state.procurementPreviews[unitId]
      const defaultPreview = {
        sourceType: 'GLOBAL_EXCHANGE',
        sourceCityId: 'city-ba',
        sourceCityName: 'Bratislava',
        sourceVendorCompanyId: null,
        sourceVendorName: null,
        exchangePricePerUnit: 8.5,
        transitCostPerUnit: 1.2,
        deliveredPricePerUnit: 9.7,
        estimatedQuality: 0.7,
        canExecute: true,
        blockReason: null,
        blockMessage: null,
      }
      const procurementPreview = customPreview !== undefined ? customPreview : defaultPreview
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { procurementPreview } }),
      })
    }

    if (query.includes('sourcingCandidates')) {
      const unitId: string = body.variables?.unitId ?? body.variables?.buildingUnitId ?? ''
      const custom = state.sourcingCandidates[unitId]
      const defaultCandidates = [
        {
          sourceType: 'GLOBAL_EXCHANGE',
          sourceCityId: 'city-ba',
          sourceCityName: 'Bratislava',
          sourceVendorCompanyId: null,
          sourceVendorName: null,
          exchangePricePerUnit: 8.5,
          transitCostPerUnit: 0.01,
          deliveredPricePerUnit: 8.51,
          estimatedQuality: 0.7,
          distanceKm: 0,
          isEligible: true,
          blockReason: null,
          blockMessage: null,
          isRecommended: true,
          rank: 1,
        },
        {
          sourceType: 'GLOBAL_EXCHANGE',
          sourceCityId: 'city-pr',
          sourceCityName: 'Prague',
          sourceVendorCompanyId: null,
          sourceVendorName: null,
          exchangePricePerUnit: 7.2,
          transitCostPerUnit: 2.8,
          deliveredPricePerUnit: 10.0,
          estimatedQuality: 0.82,
          distanceKm: 310,
          isEligible: true,
          blockReason: null,
          blockMessage: null,
          isRecommended: false,
          rank: 2,
        },
        {
          sourceType: 'GLOBAL_EXCHANGE',
          sourceCityId: 'city-vi',
          sourceCityName: 'Vienna',
          sourceVendorCompanyId: null,
          sourceVendorName: null,
          exchangePricePerUnit: 9.8,
          transitCostPerUnit: 0.15,
          deliveredPricePerUnit: 9.95,
          estimatedQuality: 0.65,
          distanceKm: 55,
          isEligible: true,
          blockReason: null,
          blockMessage: null,
          isRecommended: false,
          rank: 3,
        },
      ]
      const sourcingCandidates = custom !== undefined ? custom : defaultCandidates
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { sourcingCandidates } }),
      })
    }

    if (query.includes('cityLots')) {
      const cityId = body.variables?.cityId
      const cityLots = state.buildingLots.filter((lot) => lot.cityId === cityId)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { cityLots } }),
      })
    }

    if (query.includes('cityPowerBalance')) {
      const cityId = body.variables?.cityId
      const allBuildings = state.players.flatMap((p) => p.companies.flatMap((c) => c.buildings)).filter((b) => b.cityId === cityId)
      const powerPlants = allBuildings.filter((b) => b.type === 'POWER_PLANT')
      const consumers = allBuildings.filter((b) => b.type !== 'POWER_PLANT')
      const defaultOutputByType: Record<string, number> = { COAL: 50, GAS: 40, SOLAR: 20, WIND: 25, NUCLEAR: 200 }
      const totalSupplyMw = powerPlants.reduce((sum, b) => sum + (b.powerOutput ?? defaultOutputByType[b.powerPlantType ?? ''] ?? 30), 0)
      const totalDemandMw = consumers.reduce((sum, b) => sum + b.powerConsumption, 0)
      const reserveMw = totalSupplyMw - totalDemandMw
      const reservePercent = totalDemandMw > 0 ? Math.round((reserveMw / totalDemandMw) * 1000) / 10 : 100
      let balanceStatus = 'BALANCED'
      if (totalDemandMw > 0 && totalSupplyMw < totalDemandMw) {
        balanceStatus = totalSupplyMw >= totalDemandMw * 0.5 ? 'CONSTRAINED' : 'CRITICAL'
      }
      const cityPowerBalance = {
        cityId,
        totalSupplyMw,
        totalDemandMw,
        reserveMw,
        reservePercent,
        status: balanceStatus,
        powerPlants: powerPlants.map((b) => ({
          buildingId: b.id,
          buildingName: b.name,
          plantType: b.powerPlantType ?? 'COAL',
          outputMw: b.powerOutput ?? defaultOutputByType[b.powerPlantType ?? ''] ?? 30,
          powerStatus: b.powerStatus ?? 'POWERED',
        })),
        powerPlantCount: powerPlants.length,
        consumerBuildingCount: consumers.length,
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { cityPowerBalance } }),
      })
    }

    if (query.includes('GetLot') || (query.includes('lot(') && !query.includes('cityLots'))) {
      const id = body.variables?.id
      const lot = state.buildingLots.find((l) => l.id === id)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { lot: lot ?? null } }),
      })
    }

    if (query.includes('GetCity') || (query.includes('city(') && !query.includes('cities'))) {
      const id = body.variables?.id
      const city = state.cities.find((c) => c.id === id)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { city: city ?? null } }),
      })
    }

    if (query.includes('cities')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { cities: state.cities } }),
      })
    }

    if (query.includes('encyclopediaResource')) {
      const variables = body.variables as { slug?: string } | undefined
      const slug = variables?.slug ?? ''
      const resource = state.resourceTypes.find((r) => r.slug === slug) ?? null
      const productsUsingResource = resource ? state.productTypes.filter((p) => p.recipes.some((recipe) => recipe.resourceType?.slug === slug)) : []
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            encyclopediaResource: resource ? { resource, productsUsingResource } : null,
          },
        }),
      })
    }

    if (query.includes('resourceTypes')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { resourceTypes: state.resourceTypes } }),
      })
    }

    if (query.includes('markGameNewsRead')) {
      if (!state.currentUserId) {
        return routeJsonError('Not authenticated', 'AUTH_NOT_AUTHORIZED')
      }

      const entryIds: string[] = body.variables?.input?.entryIds ?? []
      state.gameNewsEntries = state.gameNewsEntries.map((entry) =>
        entryIds.includes(entry.id) && !entry.readByPlayerIds.includes(state.currentUserId ?? '')
          ? {
              ...entry,
              readByPlayerIds: [...entry.readByPlayerIds, state.currentUserId!],
            }
          : entry,
      )

      return routeJson({ markGameNewsRead: true })
    }

    if (query.includes('gameNewsFeed')) {
      const includeDrafts = Boolean(body.variables?.includeDrafts ?? query.includes('includeDrafts: true'))
      const visibleEntries = state.gameNewsEntries
        .filter((entry) => entry.targetServerKey === null || entry.targetServerKey === state.serverKey)
        .filter((entry) => includeDrafts || entry.status === 'PUBLISHED')
        .sort((left, right) => {
          const leftTimestamp = left.publishedAtUtc ?? left.updatedAtUtc
          const rightTimestamp = right.publishedAtUtc ?? right.updatedAtUtc
          return rightTimestamp.localeCompare(leftTimestamp)
        })

      const items = visibleEntries.map(buildGameNewsEntry)
      const unreadCount = items.filter((entry) => entry.status === 'PUBLISHED' && !entry.isRead).length

      return routeJson({
        gameNewsFeed: {
          unreadCount,
          items,
        },
      })
    }

    if (query.includes('gameAdminSession')) {
      return routeJson({ gameAdminSession: buildGameAdminSession() })
    }

    if (query.includes('gameAdminDashboard')) {
      const accessFailure = getAdminAccessFailure(false)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const totalPersonalCash = Number(state.players.reduce((total, player) => total + player.personalCash, 0).toFixed(2))
      const totalCompanyCash = Number(state.players.flatMap((player) => player.companies).reduce((total, company) => total + company.cash, 0).toFixed(2))
      const inflowSummaries =
        state.adminMoneyInflowSummaries.length > 0
          ? state.adminMoneyInflowSummaries.map((summary) => ({ ...summary }))
          : [
              {
                category: 'NO_ANOMALIES',
                amount: 0,
                description: 'No exceptional money inflow is currently flagged.',
              },
            ]
      const shippingCostSummaries = state.adminShippingCostSummaries
        .map((summary) => ({ ...summary }))
        .sort((left, right) => right.amount - left.amount || left.companyName.localeCompare(right.companyName))

      const multiAccountAlerts = state.adminMultiAccountAlerts
        .map((alert) => {
          const primaryPlayer = state.players.find((player) => player.id === alert.primaryPlayerId)
          const relatedPlayer = state.players.find((player) => player.id === alert.relatedPlayerId)

          if (!primaryPlayer || !relatedPlayer) {
            return null
          }

          return {
            reason: alert.reason,
            exposureAmount: alert.exposureAmount,
            confidenceScore: alert.confidenceScore,
            supportingEntityType: alert.supportingEntityType,
            supportingEntityName: alert.supportingEntityName,
            primaryPlayer: buildGameAdminPlayer(primaryPlayer),
            relatedPlayer: buildGameAdminPlayer(relatedPlayer),
          }
        })
        .filter((alert): alert is NonNullable<typeof alert> => alert !== null)

      return routeJson({
        gameAdminDashboard: {
          serverKey: state.serverKey,
          totalPersonalCash,
          totalCompanyCash,
          moneySupply: Number((totalPersonalCash + totalCompanyCash).toFixed(2)),
          externalMoneyInflowLast100Ticks: Number(inflowSummaries.reduce((total, summary) => total + summary.amount, 0).toFixed(2)),
          totalShippingCostsLast100Ticks: Number(shippingCostSummaries.reduce((total, summary) => total + summary.amount, 0).toFixed(2)),
          inflowSummaries,
          shippingCostSummaries,
          multiAccountAlerts,
          players: state.players.map(buildGameAdminPlayer).sort((left, right) => left.displayName.localeCompare(right.displayName)),
          invisiblePlayers: state.players.filter((player) => player.isInvisibleInChat).map(buildGameAdminPlayer),
          globalGameAdminGrants: state.globalGameAdminGrants
            .map((grant) => ({ ...grant }))
            .sort((left, right) => left.email.localeCompare(right.email)),
          recentAuditLogs: [...state.adminAuditLogs].sort((left, right) => right.recordedAtUtc.localeCompare(left.recordedAtUtc)).slice(0, 12),
        },
      })
    }

    if (query.includes('startAdminImpersonation')) {
      const accessFailure = getAdminAccessFailure(false)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const adminActor = resolveAdminActor()
      const targetPlayer = state.players.find((player) => player.id === input?.targetPlayerId)

      if (!adminActor || !targetPlayer) {
        return routeJsonError('Player not found.')
      }

      const targetCompany = input?.accountType === 'COMPANY' ? targetPlayer.companies.find((company) => company.id === input?.companyId) ?? null : null
      if (input?.accountType === 'COMPANY' && !targetCompany) {
        return routeJsonError('Company not found.')
      }

      state.impersonationSession = {
        adminActorUserId: adminActor.id,
        effectiveUserId: targetPlayer.id,
        effectiveAccountType: input?.accountType === 'COMPANY' ? 'COMPANY' : 'PERSON',
        effectiveCompanyId: targetCompany?.id ?? null,
      }

      const token = `impersonation-${adminActor.id}:${targetPlayer.id}:${state.impersonationSession.effectiveAccountType}:${state.impersonationSession.effectiveCompanyId ?? 'null'}`
      state.currentToken = token
      state.currentUserId = targetPlayer.id

      return routeJson({
        startAdminImpersonation: {
          token,
          expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
          player: buildPlayerPayload(targetPlayer),
        },
      })
    }

    if (query.includes('stopAdminImpersonation')) {
      const adminActor = resolveAdminActor()
      if (!adminActor || !state.impersonationSession) {
        return routeJsonError('No impersonation session is active.')
      }

      state.impersonationSession = null
      state.currentUserId = adminActor.id
      state.currentToken = `token-${adminActor.id}`

      return routeJson({
        stopAdminImpersonation: {
          token: state.currentToken,
          expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
          player: buildPlayerPayload(adminActor),
        },
      })
    }

    if (query.includes('setPlayerInvisibleInChat')) {
      const accessFailure = getAdminAccessFailure(false)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const targetPlayer = state.players.find((player) => player.id === input?.playerId)
      if (!targetPlayer) {
        return routeJsonError('Player not found.')
      }

      targetPlayer.isInvisibleInChat = Boolean(input?.isInvisibleInChat)
      return routeJson({ setPlayerInvisibleInChat: buildGameAdminPlayer(targetPlayer) })
    }

    if (query.includes('setLocalGameAdminRole')) {
      const accessFailure = getAdminAccessFailure(true)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const targetPlayer = state.players.find((player) => player.id === input?.playerId)
      if (!targetPlayer) {
        return routeJsonError('Player not found.')
      }

      targetPlayer.role = input?.isAdmin ? 'ADMIN' : 'PLAYER'
      return routeJson({ setLocalGameAdminRole: buildGameAdminPlayer(targetPlayer) })
    }

    if (query.includes('assignGlobalGameAdminRole')) {
      const accessFailure = getAdminAccessFailure(true)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const adminActor = resolveAdminActor()
      const normalizedEmail = String(input?.email ?? '').trim().toLowerCase()

      if (!normalizedEmail || !adminActor) {
        return routeJsonError('Email is required.')
      }

      const existingGrant = state.globalGameAdminGrants.find((grant) => grant.email.toLowerCase() === normalizedEmail)
      const now = new Date().toISOString()
      const grant = existingGrant
        ? {
            ...existingGrant,
            grantedByEmail: adminActor.email,
            updatedAtUtc: now,
          }
        : {
            id: `global-admin-${Date.now()}`,
            email: normalizedEmail,
            grantedByEmail: adminActor.email,
            grantedAtUtc: now,
            updatedAtUtc: now,
          }

      state.globalGameAdminGrants = [...state.globalGameAdminGrants.filter((candidate) => candidate.email.toLowerCase() !== normalizedEmail), grant]
      return routeJson({ assignGlobalGameAdminRole: grant })
    }

    if (query.includes('removeGlobalGameAdminRole')) {
      const accessFailure = getAdminAccessFailure(true)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const normalizedEmail = String(input?.email ?? '').trim().toLowerCase()
      state.globalGameAdminGrants = state.globalGameAdminGrants.filter((grant) => grant.email.toLowerCase() !== normalizedEmail)
      return routeJson({ removeGlobalGameAdminRole: true })
    }

    if (query.includes('upsertGameNewsEntry')) {
      const accessFailure = getAdminAccessFailure(false)
      if (accessFailure) {
        return routeJsonError(accessFailure.message, accessFailure.code)
      }

      const input = body.variables?.input
      const adminActor = resolveAdminActor()
      const session = buildGameAdminSession()
      if (!adminActor) {
        return routeJsonError('Not authenticated', 'AUTH_NOT_AUTHORIZED')
      }

      const existingEntry = state.gameNewsEntries.find((entry) => entry.id === input?.entryId)
      if (existingEntry?.targetServerKey === null && !session.isRootAdministrator && !session.hasGlobalAdminRole) {
        return routeJsonError('Only global or root administrators can edit global feed entries.', 'AUTH_NOT_AUTHORIZED')
      }

      const now = new Date().toISOString()
      const targetServerKey = existingEntry
        ? existingEntry.targetServerKey
        : session.isRootAdministrator || session.hasGlobalAdminRole
          ? null
          : state.serverKey

      const nextEntry: MockGameNewsEntry = existingEntry
        ? {
            ...existingEntry,
            entryType: input?.entryType ?? existingEntry.entryType,
            status: input?.status ?? existingEntry.status,
            updatedByEmail: adminActor.email,
            updatedAtUtc: now,
            publishedAtUtc: (input?.status ?? existingEntry.status) === 'PUBLISHED' ? existingEntry.publishedAtUtc ?? now : null,
            localizations: (input?.localizations ?? []).map((localization: MockGameNewsLocalization) => ({ ...localization })),
          }
        : {
            id: `news-entry-${Date.now()}`,
            entryType: input?.entryType ?? 'NEWS',
            status: input?.status ?? 'DRAFT',
            targetServerKey,
            createdByEmail: adminActor.email,
            updatedByEmail: adminActor.email,
            createdAtUtc: now,
            updatedAtUtc: now,
            publishedAtUtc: (input?.status ?? 'DRAFT') === 'PUBLISHED' ? now : null,
            localizations: (input?.localizations ?? []).map((localization: MockGameNewsLocalization) => ({ ...localization })),
            readByPlayerIds: [],
          }

      state.gameNewsEntries = [...state.gameNewsEntries.filter((entry) => entry.id !== nextEntry.id), nextEntry]
      return routeJson({ upsertGameNewsEntry: buildGameNewsEntry(nextEntry) })
    }

    // Helper: true if the query is a standalone `me` query (not a more-specific query whose field names happen to include "me" as a substring).
    // NOTE: Many field names end in "Name" (e.g. bankBuildingName, lenderCompanyName, cityName) which contain "me" as a substring.
    // Also "payment" contains "me" (pay-me-nt). Always add exclusions here for any new query/mutation with such fields.
    const isStandaloneMeQuery = (q: string) =>
      q.includes('me') &&
      !q.includes('gameNewsFeed') &&
      !q.includes('gameAdminSession') &&
      !q.includes('gameAdminDashboard') &&
      !q.includes('companyLedger') &&
      !q.includes('ledgerDrillDown') &&
      !q.includes('companyBrands') &&
      !q.includes('publicSalesAnalytics') &&
      !q.includes('unitProductAnalytics') &&
      // Loan queries: field names like bankBuildingName, lenderCompanyName, cityName, paymentAmount contain 'me'
      !q.includes('loanOffers') &&
      !q.includes('myLoans') &&
      !q.includes('myLoanOffers') &&
      !q.includes('bankLoans') &&
      // acceptLoan mutation response includes paymentAmount which contains 'me'
      !q.includes('acceptLoan') &&
      // procurementPreview contains 'me' as substring
      !q.includes('procurementPreview') &&
      // personAccount query has companyName field which contains 'me' as substring
      !q.includes('personAccount')

    if (isStandaloneMeQuery(query)) {
      const player = resolveCurrentPlayer()
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            me: buildPlayerPayload(player),
          },
        }),
      })
    }

    if (query.includes('companyLedger')) {
      const companyId = body.variables?.companyId
      const gameYear = body.variables?.gameYear
      const summary = state.ledgerData[`${companyId}:${gameYear}`] ?? state.ledgerData[companyId]
      if (!summary) {
        const player = state.players.find((p) => p.id === state.currentUserId)
        const company = player?.companies.find((c) => c.id === companyId)
        if (!company) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ data: { companyLedger: null } }),
          })
        }
        const baseValues: Record<string, number> = {
          MINE: 250000,
          FACTORY: 200000,
          SALES_SHOP: 150000,
          RESEARCH_DEVELOPMENT: 300000,
          APARTMENT: 400000,
          COMMERCIAL: 350000,
          MEDIA_HOUSE: 500000,
          BANK: 600000,
          EXCHANGE: 450000,
          POWER_PLANT: 350000,
        }
        const buildingValue = company.buildings.reduce((sum, b) => sum + (baseValues[b.type] ?? 0) * b.level, 0)
        const auto: MockLedgerSummary = {
          companyId: company.id,
          companyName: company.name,
          gameYear: computeMockGameYear(state.gameState.currentTick),
          isCurrentGameYear: true,
          currentCash: company.cash,
          totalRevenue: 0,
          totalPurchasingCosts: 0,
          totalShippingCosts: 0,
          totalLaborCosts: 0,
          totalEnergyCosts: 0,
          totalMarketingCosts: 0,
          totalTaxPaid: 0,
          totalOtherCosts: 0,
          taxableIncome: 0,
          estimatedIncomeTax: 0,
          netIncome: 0,
          propertyValue: 0,
          propertyAppreciation: 0,
          buildingValue,
          inventoryValue: 0,
          totalAssets: company.cash + buildingValue,
          totalPropertyPurchases: 0,
          cashFromOperations: 0,
          cashFromInvestments: 0,
          firstRecordedTick: 0,
          lastRecordedTick: 0,
          history: [],
          buildingSummaries: [],
        }
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { companyLedger: buildMockLedgerSummaryPayload(auto, state.gameState) } }),
        })
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { companyLedger: buildMockLedgerSummaryPayload(summary, state.gameState) } }),
      })
    }

    if (query.includes('ledgerDrillDown')) {
      const companyId = body.variables?.companyId
      const category = body.variables?.category
      const gameYear = body.variables?.gameYear
      const entries = state.drillDownData[`${companyId}:${category}:${gameYear}`] ?? state.drillDownData[`${companyId}:${category}`] ?? []
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { ledgerDrillDown: entries } }),
      })
    }

    if (query.includes('companyBrands') || query.includes('CompanyBrands')) {
      const companyId = body.variables?.companyId
      const brands = companyId ? (state.researchBrands[companyId] ?? []) : []
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { companyBrands: brands } }),
      })
    }

    if (query.includes('publicSalesAnalytics') || query.includes('PublicSalesAnalytics')) {
      const unitId: string = body.variables?.unitId ?? ''
      const analytics = state.publicSalesAnalytics[unitId] ?? null
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { publicSalesAnalytics: analytics } }),
      })
    }

    if (query.includes('unitProductAnalytics') || query.includes('UnitProductAnalytics')) {
      const unitId: string = body.variables?.unitId ?? ''
      const analytics = state.unitProductAnalytics[unitId] ?? null
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { unitProductAnalytics: analytics } }),
      })
    }

    if (query.includes('UpdatePublicSalesPrice') || query.includes('updatePublicSalesPrice')) {
      const input = body.variables?.input
      const unitId: string = input?.unitId ?? ''
      const newMinPrice: number = input?.newMinPrice ?? 0

      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated', extensions: { code: 'AUTH_NOT_AUTHORIZED' } }] }),
        })
      }

      if (newMinPrice <= 0) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Minimum sale price must be greater than zero.', extensions: { code: 'INVALID_PRICE' } }] }),
        })
      }

      // Find the unit across all buildings owned by the current player
      const player = state.players.find((p) => p.id === state.currentUserId)
      let foundUnit: MockBuildingUnit | undefined
      for (const company of player?.companies ?? []) {
        for (const building of company.buildings ?? []) {
          const u = building.units?.find((unit) => unit.id === unitId)
          if (u) {
            foundUnit = u
            break
          }
        }
        if (foundUnit) break
      }

      if (!foundUnit) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: "Unit not found or you don't own it.", extensions: { code: 'UNIT_NOT_FOUND' } }] }),
        })
      }

      if (foundUnit.unitType !== 'PUBLIC_SALES') {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Only PUBLIC_SALES units support instant price updates.', extensions: { code: 'INVALID_UNIT_TYPE' } }] }),
        })
      }

      // Update the unit's minPrice in state
      foundUnit.minPrice = newMinPrice

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { updatePublicSalesPrice: { id: unitId, minPrice: newMinPrice } } }),
      })
    }

    // Guard: loanOffers query must not be confused with the 'me' handler (contains 'me' via no overlap here)
    if (query.includes('loanOffers') && !query.includes('myLoanOffers')) {
      const activeOffers = state.loanOffers.filter((o) => o.isActive && o.remainingCapacity > 0)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { loanOffers: activeOffers } }),
      })
    }

    if (query.includes('myLoanOffers')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { myLoanOffers: state.loanOffers } }),
      })
    }

    if (query.includes('myLoans')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { myLoans: state.myLoans } }),
      })
    }

    if (query.includes('bankLoans')) {
      const bankBuildingId = body.variables?.bankBuildingId
      const loans = bankBuildingId ? state.myLoans.filter((l) => l.bankBuildingId === bankBuildingId) : state.myLoans
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { bankLoans: loans } }),
      })
    }

    if (query.includes('publishLoanOffer')) {
      const input = body.variables?.input ?? {}
      const newOffer: MockLoanOffer = {
        id: `offer-${Date.now()}`,
        bankBuildingId: input.bankBuildingId ?? '',
        bankBuildingName: 'Test Bank',
        cityId: 'city-ba',
        cityName: 'Bratislava',
        lenderCompanyId: 'test-company',
        lenderCompanyName: 'Test Co',
        annualInterestRatePercent: input.annualInterestRatePercent ?? 10,
        maxPrincipalPerLoan: input.maxPrincipalPerLoan ?? 50000,
        totalCapacity: input.totalCapacity ?? 200000,
        usedCapacity: 0,
        remainingCapacity: input.totalCapacity ?? 200000,
        durationTicks: input.durationTicks ?? 1440,
        isActive: true,
        createdAtTick: state.gameState.currentTick,
        createdAtUtc: new Date().toISOString(),
      }
      state.loanOffers.push(newOffer)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { publishLoanOffer: newOffer } }),
      })
    }

    if (query.includes('deactivateLoanOffer')) {
      const loanOfferId = body.variables?.id
      const offer = state.loanOffers.find((o) => o.id === loanOfferId)
      if (offer) offer.isActive = false
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { deactivateLoanOffer: offer ?? null } }),
      })
    }

    if (query.includes('acceptLoan')) {
      const input = body.variables?.input ?? {}
      const offer = state.loanOffers.find((o) => o.id === input.loanOfferId)
      const principal = input.principalAmount ?? 0
      if (offer) {
        offer.usedCapacity += principal
        offer.remainingCapacity -= principal
      }
      const newLoan: MockLoan = {
        id: `loan-${Date.now()}`,
        loanOfferId: input.loanOfferId ?? '',
        borrowerCompanyId: input.borrowerCompanyId ?? '',
        borrowerCompanyName: 'Borrower Co',
        lenderCompanyId: offer?.lenderCompanyId ?? '',
        lenderCompanyName: offer?.lenderCompanyName ?? '',
        bankBuildingId: offer?.bankBuildingId ?? '',
        bankBuildingName: offer?.bankBuildingName ?? '',
        originalPrincipal: principal,
        remainingPrincipal: principal,
        annualInterestRatePercent: offer?.annualInterestRatePercent ?? 10,
        durationTicks: offer?.durationTicks ?? 1440,
        startTick: state.gameState.currentTick,
        dueTick: state.gameState.currentTick + (offer?.durationTicks ?? 1440),
        nextPaymentTick: state.gameState.currentTick + 720,
        paymentAmount: principal / 2,
        paymentsMade: 0,
        totalPayments: 2,
        status: 'ACTIVE',
        missedPayments: 0,
        accumulatedPenalty: 0,
        acceptedAtUtc: new Date().toISOString(),
        closedAtUtc: null,
      }
      state.myLoans.push(newLoan)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { acceptLoan: newLoan } }),
      })
    }

    if (query.includes('FlushStorage') || query.includes('flushStorage')) {
      const input = body.variables?.input
      const buildingUnitId: string = input?.buildingUnitId ?? ''

      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated', extensions: { code: 'AUTH_NOT_AUTHORIZED' } }] }),
        })
      }

      // Find unit and validate ownership
      const player = state.players.find((p) => p.id === state.currentUserId)
      let foundUnit: MockBuildingUnit | undefined
      for (const company of player?.companies ?? []) {
        for (const building of company.buildings ?? []) {
          const u = building.units?.find((unit) => unit.id === buildingUnitId)
          if (u) {
            foundUnit = u
            break
          }
        }
        if (foundUnit) break
      }

      if (!foundUnit) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: "Unit not found or you don't own it.", extensions: { code: 'UNIT_NOT_FOUND' } }] }),
        })
      }

      const flushableTypes = ['STORAGE', 'MINING', 'MANUFACTURING']
      if (!flushableTypes.includes(foundUnit.unitType)) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Only STORAGE, MINING and MANUFACTURING units can be flushed.', extensions: { code: 'INVALID_UNIT_TYPE' } }] }),
        })
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            flushStorage: {
              discardedItemCount: 1,
              totalDiscardedValue: 100,
              discardedEntries: [{ itemName: 'Wood', quantity: 10, sourcingCostLost: 100 }],
            },
          },
        }),
      })
    }

    // unitUpgradeInfo query — check precisely to avoid false substring matches
    if ((query.includes('unitUpgradeInfo') || query.includes('UUI')) && !query.includes('scheduleUnitUpgrade') && !query.includes('ScheduleUnitUpgrade')) {
      const unitId: string = body.variables?.unitId ?? ''
      const override = state.unitUpgradeInfoOverrides[unitId]
      if (override === null) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { unitUpgradeInfo: null } }),
        })
      }
      // Resolve the unit from state to return type-accurate stat label and values
      const allUnits = state.players
        .flatMap((p) => p.companies)
        .flatMap((c) => c.buildings)
        .flatMap((b) => b.units)
      const matchedUnit = allUnits.find((u) => u.id === unitId)
      const resolvedUnitType = matchedUnit?.unitType ?? 'MANUFACTURING'
      const resolvedLevel = matchedUnit?.level ?? 1
      // Mirror GameConstants.GetUnitStat and GetUnitStatLabel
      const isStorage = resolvedUnitType === 'STORAGE'
      const storageCapacities: Record<number, number> = { 1: 1000, 2: 2500, 3: 5000, 4: 10000 }
      const baseCapacities: Record<number, number> = { 1: 100, 2: 250, 3: 500, 4: 1000 }
      const statByType: Record<string, { label: string; current: number; next: number }> = {
        STORAGE: {
          label: 'capacity',
          current: storageCapacities[resolvedLevel] ?? 1000,
          next: storageCapacities[Math.min(resolvedLevel + 1, 4)] ?? 2500,
        },
        MINING: {
          label: 'units/tick',
          current: baseCapacities[resolvedLevel] ?? 100,
          next: baseCapacities[Math.min(resolvedLevel + 1, 4)] ?? 250,
        },
        MANUFACTURING: { label: 'Batches/tick', current: 1.0, next: 2.0 },
        PURCHASE: {
          label: 'units/tick',
          current: baseCapacities[resolvedLevel] ?? 100,
          next: baseCapacities[Math.min(resolvedLevel + 1, 4)] ?? 250,
        },
        PUBLIC_SALES: {
          label: 'units/tick',
          current: baseCapacities[resolvedLevel] ?? 100,
          next: baseCapacities[Math.min(resolvedLevel + 1, 4)] ?? 250,
        },
        B2B_SALES: {
          label: 'units/tick',
          current: baseCapacities[resolvedLevel] ?? 100,
          next: baseCapacities[Math.min(resolvedLevel + 1, 4)] ?? 250,
        },
      }
      const stat = statByType[resolvedUnitType] ?? { label: 'Batches/tick', current: 1.0, next: 2.0 }
      const defaultInfo = {
        unitId,
        unitType: resolvedUnitType,
        currentLevel: resolvedLevel,
        nextLevel: Math.min(resolvedLevel + 1, 4),
        isMaxLevel: resolvedLevel >= 4,
        isUpgradable: !['BRANDING', 'MARKETING'].includes(resolvedUnitType),
        upgradeCost: isStorage ? 8000 : 8000,
        upgradeTicks: 10,
        currentStat: stat.current,
        nextStat: stat.next,
        statLabel: stat.label,
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { unitUpgradeInfo: override ?? defaultInfo } }),
      })
    }

    // scheduleUnitUpgrade mutation
    if (query.includes('scheduleUnitUpgrade') || query.includes('ScheduleUnitUpgrade') || query.includes('SUU')) {
      if (!state.currentUserId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated', extensions: { code: 'AUTH_NOT_AUTHORIZED' } }] }),
        })
      }
      const unitId: string = body.variables?.input?.unitId ?? ''
      if (state.upgradeInsufficientFundsUnitId === unitId) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Insufficient funds.', extensions: { code: 'INSUFFICIENT_FUNDS' } }] }),
        })
      }
      const gameState = state.gameState
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            scheduleUnitUpgrade: {
              id: crypto.randomUUID(),
              appliesAtTick: gameState.currentTick + 10,
              totalTicksRequired: 10,
            },
          },
        }),
      })
    }

    // Fallback
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ data: null }),
    })
  })

  return state
}

// ── Login helper ─────────────────────────────────────────────────────────────

export async function restoreMockSession(page: Page, token: string): Promise<void> {
  const state = mockStateByPage.get(page)
  if (!state) {
    throw new Error('Mock API state was not initialized for this page.')
  }

  const playerId = token.replace(/^token-/, '')
  const player = state.players.find((candidate) => candidate.id === playerId)
  if (!player) {
    throw new Error(`No mock player found for token ${token}.`)
  }

  await loginAs(page, state, player)
}

export async function loginAs(page: Page, state: MockState, player: MockPlayer): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('Email').fill(player.email)
  await page.getByLabel('Password').fill(player.password)
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`
  const token = `token-${player.id}`
  const expiresAtUtc = new Date(Date.now() + 7200000).toISOString()
  const cookieUrl = process.env.CI ? 'http://localhost:4173' : 'http://localhost:5173'
  await page.context().addCookies([
    {
      name: 'auth_token',
      value: token,
      url: cookieUrl,
    },
    {
      name: 'auth_expires',
      value: expiresAtUtc,
      url: cookieUrl,
    },
  ])
  await page.evaluate((token) => {
    const expires = new Date(Date.now() + 7200000).toISOString()
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', expires)
    document.cookie = `auth_token=${encodeURIComponent(token)}; path=/`
    document.cookie = `auth_expires=${encodeURIComponent(expires)}; path=/`
  }, token)
  await page.getByRole('button', { name: 'Sign In' }).click()
  await page.waitForURL('/')
}
