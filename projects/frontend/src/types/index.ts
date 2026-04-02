/** Matches backend PlayerRole constants */
export type PlayerRole = 'PLAYER' | 'ADMIN'

/** Matches backend Player entity */
export interface Player {
  id: string
  email: string
  displayName: string
  role: PlayerRole
  createdAtUtc: string
  lastLoginAtUtc: string | null
  onboardingCompletedAtUtc: string | null
  onboardingCurrentStep: string | null
  onboardingIndustry: string | null
  onboardingCityId: string | null
  onboardingCompanyId: string | null
  onboardingFactoryLotId: string | null
  onboardingShopBuildingId: string | null
  onboardingFirstSaleCompletedAtUtc: string | null
  proSubscriptionEndsAtUtc: string | null
  companies: Company[]
}

/** Matches backend AuthPayload response */
export interface AuthPayload {
  token: string
  expiresAtUtc: string
  player: Player
}

/** Matches backend Company entity */
export interface Company {
  id: string
  playerId: string
  name: string
  cash: number
  foundedAtUtc: string
  foundedAtTick?: number
  buildings: Building[]
}

export interface CompanyCitySalarySetting {
  cityId: string
  cityName: string
  baseSalaryPerManhour: number
  salaryMultiplier: number
  effectiveSalaryPerManhour: number
}

export interface CompanySettings {
  companyId: string
  companyName: string
  cash: number
  foundedAtTick: number
  administrationOverheadRate: number
  /** 0–1: how much company age contributes to overhead (reaches 1 at 2 years old) */
  ageFactor: number
  /** 0–1: how much company scale (assets) contributes to overhead */
  assetFactor: number
  assetValue: number
  citySalarySettings: CompanyCitySalarySetting[]
}

/** Matches backend Building entity */
export interface Building {
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
  askingPrice: number | null
  pricePerSqm: number | null
  /** Pending rent per m² scheduled by the player; activates at pendingPriceActivationTick. */
  pendingPricePerSqm: number | null
  /** Tick number when pendingPricePerSqm becomes active. Null if no pending change. */
  pendingPriceActivationTick: number | null
  occupancyPercent: number | null
  totalAreaSqm: number | null
  powerPlantType: string | null
  powerOutput: number | null
  /** Power supply status set by the tick engine: POWERED | CONSTRAINED | OFFLINE */
  powerStatus: string
  mediaType: string | null
  interestRate: number | null
  builtAtUtc: string
  units: BuildingUnit[]
  pendingConfiguration: BuildingConfigurationPlan | null
}

/** Queued building configuration that becomes active on a future tick. */
export interface BuildingConfigurationPlan {
  id: string
  buildingId: string
  submittedAtUtc: string
  submittedAtTick: number
  appliesAtTick: number
  totalTicksRequired: number
  units: BuildingConfigurationPlanUnit[]
  removals: BuildingConfigurationPlanRemoval[]
}

/** Pending unit snapshot inside a queued building configuration. */
export interface BuildingConfigurationPlanUnit {
  id: string
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
  startedAtTick: number
  appliesAtTick: number
  ticksRequired: number
  isChanged: boolean
  isReverting: boolean
  resourceTypeId: string | null
  productTypeId: string | null
  minPrice: number | null
  maxPrice: number | null
  purchaseSource: string | null
  saleVisibility: string | null
  budget: number | null
  mediaHouseBuildingId: string | null
  minQuality: number | null
  brandScope: string | null
  vendorLockCompanyId: string | null
}

export interface BuildingConfigurationPlanRemoval {
  id: string
  gridX: number
  gridY: number
  startedAtTick: number
  appliesAtTick: number
  ticksRequired: number
  isReverting: boolean
}

/** Matches backend BuildingUnit entity */
export interface BuildingUnit {
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
  resourceTypeId: string | null
  productTypeId: string | null
  minPrice: number | null
  maxPrice: number | null
  purchaseSource: string | null
  saleVisibility: string | null
  budget: number | null
  mediaHouseBuildingId: string | null
  minQuality: number | null
  brandScope: string | null
  vendorLockCompanyId: string | null
}

export interface BuildingUnitInventorySummary {
  buildingUnitId: string
  quantity: number
  capacity: number
  fillPercent: number
  averageQuality: number | null
  totalSourcingCost: number
  sourcingCostPerUnit: number
}

export interface BuildingUnitInventory {
  id: string
  buildingUnitId: string
  resourceTypeId: string | null
  productTypeId: string | null
  quantity: number
  sourcingCostTotal: number
  sourcingCostPerUnit: number
  quality: number
}

export interface BuildingUnitResourceHistoryPoint {
  buildingUnitId: string
  resourceTypeId: string | null
  productTypeId: string | null
  tick: number
  inflowQuantity: number
  outflowQuantity: number
  consumedQuantity: number
  producedQuantity: number
}

export interface GlobalExchangeOffer {
  cityId: string
  cityName: string
  resourceTypeId: string
  resourceName: string
  resourceSlug: string
  unitSymbol: string
  localAbundance: number
  exchangePricePerUnit: number
  estimatedQuality: number
  transitCostPerUnit: number
  deliveredPricePerUnit: number
  distanceKm: number
}

/** Matches backend ApplicationUser entity */
export interface User {
  id: string
  email: string
  displayName: string
  role: 'ADMIN' | 'CONTRIBUTOR'
  createdAtUtc: string
  lastLoginAtUtc: string | null
}

/** Matches backend CatalogEvent entity */
export interface CatalogEvent {
  id: string
  name: string
  slug: string
  description: string
  startDate: string
  endDate: string | null
  startsAtUtc: string
  endsAtUtc: string | null
  venueName: string | null
  addressLine1: string | null
  city: string | null
  countryCode: string | null
  latitude: number | null
  longitude: number | null
  mapUrl: string | null
  attendanceMode: 'IN_PERSON' | 'ONLINE' | 'HYBRID'
  isFree: boolean
  currencyCode: string
  price: number | null
  eventUrl: string | null
  timezone: string | null
  submittedBy: User
  submittedAtUtc: string
  status: 'PUBLISHED' | 'PENDING_APPROVAL' | 'REJECTED' | 'DRAFT'
  interestedCount: number
  domain: EventDomain | null
}

/** Matches backend EventDomain entity */
export interface EventDomain {
  id: string
  name: string
  slug: string
  subdomain: string
  isActive: boolean
  description: string | null
  logoUrl: string | null
  bannerUrl: string | null
  primaryColor: string | null
  accentColor: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

/** Event filters for discovery */
export interface EventFilters {
  keyword?: string
  search?: string
  location?: string
  mode?: 'IN_PERSON' | 'ONLINE' | 'HYBRID'
  price?: 'free' | 'paid'
  priceType?: 'ALL' | 'FREE' | 'PAID'
  priceMin?: string
  priceMax?: string
  date?: 'upcoming' | 'past'
  dateFrom?: string
  dateTo?: string
  sort?: 'newest' | 'oldest' | 'name' | 'RELEVANCE'
  sortBy?: 'UPCOMING' | 'NEWEST' | 'RELEVANCE'
  domain?: string
  attendanceMode?: '' | 'IN_PERSON' | 'ONLINE' | 'HYBRID'
  language?: string
  timezone?: string
}

export interface SavedSearch {
  id: string
  name: string
  searchText: string | null
  domainSlug: string | null
  locationText: string | null
  startsFromUtc: string | null
  startsToUtc: string | null
  isFree: boolean | null
  priceMin: number | null
  priceMax: number | null
  sortBy: 'UPCOMING' | 'NEWEST' | 'RELEVANCE'
  attendanceMode: 'IN_PERSON' | 'ONLINE' | 'HYBRID' | null
  language: string | null
  timezone: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

/** Onboarding types */
export interface City {
  id: string
  name: string
  countryCode: string
  latitude: number
  longitude: number
  population: number
  baseSalaryPerManhour?: number
  resources: Resource[]
}

export interface Resource {
  resourceType: ResourceType
  abundance: number
}

export interface ResourceType {
  id: string
  name: string
  slug: string
  category: string
  basePrice: number
  weightPerUnit: number
  unitName: string
  unitSymbol: string
  imageUrl: string | null
  description: string | null
}

export interface ProductType {
  id: string
  name: string
  slug: string
  imageUrl?: string | null
  industry: string
  basePrice: number
  baseCraftTicks: number
  outputQuantity: number
  energyConsumptionMwh: number
  basicLaborHours: number
  unitName: string
  unitSymbol: string
  isProOnly: boolean
  isUnlockedForCurrentPlayer: boolean
  description: string | null
  recipes: Recipe[]
}

export interface Recipe {
  resourceType: ResourceType | null
  inputProductType: Pick<ProductType, 'id' | 'name' | 'slug' | 'unitName' | 'unitSymbol'> | null
  quantity: number
}

export interface OnboardingResult {
  company: Company
  factory: Building
  salesShop: Building
  selectedProduct: ProductType
  startupPackOffer: StartupPackOffer | null
}

export interface OnboardingStartResult {
  company: Company
  factory: Building
  factoryLot: BuildingLot
  nextStep: string
}

export interface StartupPackOffer {
  id: string
  offerKey: string
  status: 'ELIGIBLE' | 'SHOWN' | 'DISMISSED' | 'CLAIMED' | 'EXPIRED'
  createdAtUtc: string
  expiresAtUtc: string
  shownAtUtc: string | null
  dismissedAtUtc: string | null
  claimedAtUtc: string | null
  companyCashGrant: number
  proDurationDays: number
  grantedCompanyId: string | null
}

export interface StartupPackClaimResult {
  offer: StartupPackOffer
  company: Company
  proSubscriptionEndsAtUtc: string
}

/** Matches backend PlayerRanking response */
export interface PlayerRanking {
  playerId: string
  displayName: string
  totalWealth: number
  cashTotal: number
  buildingValue: number
  inventoryValue: number
  companyCount: number
}

/** Matches backend GameState entity */
export interface GameState {
  currentTick: number
  lastTickAtUtc: string
  tickIntervalSeconds: number
  taxCycleTicks: number
  taxRate: number
  currentGameYear: number
  currentGameTimeUtc: string
  ticksPerDay: number
  ticksPerYear: number
  nextTaxTick: number
  nextTaxGameTimeUtc: string
  nextTaxGameYear: number
}

/** Matches backend BuildingLot entity */
export interface BuildingLot {
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
  building: { id: string; name: string; type: string } | null
  /** Raw material available for extraction — null when no resource on this lot */
  resourceType: { id: string; name: string; slug: string } | null
  /** Quality of the raw material (0.0–1.0); null when no resource */
  materialQuality: number | null
  /** Estimated extractable quantity in tonnes; null when no resource */
  materialQuantity: number | null
}

/** Result of purchasing a building lot */
export interface PurchaseLotResult {
  lot: BuildingLot
  building: Building
  company: Company
}

/** Matches backend ScheduledActionSummary — a pending player action waiting for tick resolution. */
export interface ScheduledActionSummary {
  id: string
  actionType: string
  buildingId: string
  buildingName: string
  buildingType: string
  submittedAtUtc: string
  submittedAtTick: number
  appliesAtTick: number
  ticksRemaining: number
  totalTicksRequired: number
}

/** Company financial ledger summary */
export interface CompanyLedgerSummary {
  companyId: string
  companyName: string
  gameYear: number
  isCurrentGameYear: boolean
  currentCash: number
  totalRevenue: number
  totalPurchasingCosts: number
  totalLaborCosts: number
  totalEnergyCosts: number
  totalMarketingCosts: number
  totalTaxPaid: number
  totalOtherCosts: number
  taxableIncome: number
  estimatedIncomeTax: number
  netIncome: number
  propertyValue: number
  propertyAppreciation: number
  buildingValue: number
  inventoryValue: number
  totalAssets: number
  totalPropertyPurchases: number
  cashFromOperations: number
  cashFromInvestments: number
  firstRecordedTick: number
  lastRecordedTick: number
  incomeTaxDueAtTick: number
  incomeTaxDueGameTimeUtc: string
  incomeTaxDueGameYear: number
  isIncomeTaxSettled: boolean
  buildingSummaries: BuildingLedgerSummary[]
  history: CompanyLedgerHistoryYear[]
}

export interface CompanyLedgerHistoryYear {
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

export interface BuildingLedgerSummary {
  buildingId: string
  buildingName: string
  buildingType: string
  revenue: number
  costs: number
}

export interface LedgerEntryResult {
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

export interface PublicSalesAnalytics {
  buildingUnitId: string
  buildingId: string
  buildingName: string
  cityName: string
  totalRevenue: number
  totalQuantitySold: number
  averagePricePerUnit: number
  currentSalesCapacity: number
  dataFromTick: number
  dataToTick: number
  revenueHistory: SalesTickSnapshot[]
  marketShare: MarketShareEntry[]
  priceHistory: PriceTickSnapshot[]
}

export interface SalesTickSnapshot {
  tick: number
  revenue: number
  quantitySold: number
}

export interface PriceTickSnapshot {
  tick: number
  pricePerUnit: number
}

export interface MarketShareEntry {
  label: string
  companyId: string | null
  share: number
}

/** Summary of a single power plant in the city power balance view. */
export interface PowerPlantSummary {
  buildingId: string
  buildingName: string
  /** Plant type: COAL | GAS | SOLAR | WIND | NUCLEAR */
  plantType: string
  outputMw: number
  powerStatus: string
}

/** City-level power balance snapshot returned by the cityPowerBalance query. */
export interface CityPowerBalance {
  cityId: string
  totalSupplyMw: number
  totalDemandMw: number
  reserveMw: number
  reservePercent: number
  /** BALANCED | CONSTRAINED | CRITICAL */
  status: string
  powerPlants: PowerPlantSummary[]
  powerPlantCount: number
  consumerBuildingCount: number
}

/**
 * Research brand state returned by the companyBrands query.
 * Represents a brand entity accumulated by R&D research (product quality)
 * and marketing activity (brand awareness).
 */
export interface ResearchBrandState {
  id: string
  companyId: string
  name: string
  /** PRODUCT | CATEGORY | COMPANY */
  scope: string
  productTypeId: string | null
  productName: string | null
  industryCategory: string | null
  /** 0.0–1.0: Driven by marketing unit spend. Higher = stronger brand recognition with customers. */
  awareness: number
  /** 0.0–1.0: Driven by PRODUCT_QUALITY R&D. Higher = better manufactured output quality. */
  quality: number
  /**
   * ≥ 1.0: Driven by BRAND_QUALITY R&D. A value of 1.5 means each unit of marketing budget
   * produces 50% more brand awareness than baseline. Does NOT directly grant awareness.
   */
  marketingEfficiencyMultiplier: number
}
