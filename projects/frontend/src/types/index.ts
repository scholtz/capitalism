/** Matches backend PlayerRole constants */
export type PlayerRole = 'PLAYER' | 'ADMIN'

export type AccountContextType = 'PERSON' | 'COMPANY'

/** Matches backend Player entity */
export interface Player {
  id: string
  email: string
  displayName: string
  role: PlayerRole
  createdAtUtc: string
  lastLoginAtUtc: string | null
  personalCash: number
  activeAccountType: AccountContextType
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
  totalSharesIssued?: number
  dividendPayoutRatio?: number
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
  totalSharesIssued: number
  dividendPayoutRatio: number
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
  /** True while the building is still under construction (before constructionCompletesAtTick). */
  isUnderConstruction: boolean
  /** Tick number when construction finishes and the building becomes operational. Null for legacy buildings. */
  constructionCompletesAtTick: number | null
  /** Cash cost charged for construction (separate from the land price). */
  constructionCost: number
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
  lockedCityId: string | null
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
  lockedCityId: string | null
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

/** A product marketplace listing from a player-placed SELL exchange order. */
export interface GlobalExchangeProductListing {
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

export interface RankedProductResult {
  productType: ProductType
  rankingReason: 'connected' | 'used_by_company' | 'catalog'
  rankingScore: number
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

export interface AccountContextResult {
  activeAccountType: AccountContextType
  activeCompanyId: string | null
  activeAccountName: string
}

export interface PortfolioHolding {
  companyId: string
  companyName: string
  shareCount: number
  ownershipRatio: number
  sharePrice: number
  marketValue: number
}

export interface DividendPayment {
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

export interface PersonAccount {
  playerId: string
  displayName: string
  personalCash: number
  activeAccountType: AccountContextType
  activeCompanyId: string | null
  shareholdings: PortfolioHolding[]
  dividendPayments: DividendPayment[]
}

export interface StockExchangeListing {
  companyId: string
  companyName: string
  totalSharesIssued: number
  publicFloatShares: number
  sharePrice: number
  bidPrice: number
  askPrice: number
  dividendPayoutRatio: number
  playerOwnedShares: number
  controlledCompanyOwnedShares: number
  combinedControlledOwnershipRatio: number
  canClaimControl: boolean
}

export interface ShareTradeResult {
  companyId: string
  companyName: string
  accountType: AccountContextType
  accountCompanyId: string | null
  accountName: string
  shareCount: number
  pricePerShare: number
  totalValue: number
  ownedShareCount: number
  publicFloatShares: number
  personalCash: number
  companyCash: number | null
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

/** Matches backend CompanyRanking response */
export interface CompanyRanking {
  companyId: string
  companyName: string
  playerId: string
  ownerDisplayName: string
  totalWealth: number
  cash: number
  buildingValue: number
  inventoryValue: number
  buildingCount: number
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
  building: {
    id: string
    name: string
    type: string
    isUnderConstruction: boolean
    constructionCompletesAtTick: number | null
    constructionCost: number
  } | null
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

export interface BuildingFinancialTickSnapshot {
  tick: number
  sales: number
  costs: number
  profit: number
}

export interface BuildingFinancialTimeline {
  buildingId: string
  buildingName: string
  dataFromTick: number
  dataToTick: number
  totalSales: number
  totalCosts: number
  totalProfit: number
  timeline: BuildingFinancialTickSnapshot[]
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
  /** Product type ID being tracked. Null when no product configured or sold. */
  productTypeId: string | null
  /** Display name of the product being sold. Null when no product data available. */
  productName: string | null
  totalRevenue: number
  totalQuantitySold: number
  averagePricePerUnit: number
  currentSalesCapacity: number
  dataFromTick: number
  dataToTick: number
  revenueHistory: SalesTickSnapshot[]
  marketShare: MarketShareEntry[]
  priceHistory: PriceTickSnapshot[]
  /**
   * Revenue trend vs prior 5-tick window.
   * UP | FLAT | DOWN | NO_DATA
   */
  trendDirection: string
  /** NO_DATA | SUPPLY_CONSTRAINED | STRONG | MODERATE | WEAK */
  demandSignal: string
  actionHint: string
  recentUtilization: number
  /** Price elasticity index ≤ 0; more negative = more elastic (price-sensitive). Null if insufficient data. */
  elasticityIndex: number | null
  /** Fraction of city demand that went unserved (0–1). Null when no data. */
  unmetDemandShare: number | null
  /** Population index of the building lot (1.0 = city average). */
  populationIndex: number | null
  /** Current inventory quality (0–1). */
  inventoryQuality: number | null
  /** Brand awareness for this product (0–1). */
  brandAwareness: number | null
  /** Total gross profit (revenue − quantity × basePrice). Null when base price unavailable. */
  totalProfit: number | null
  /** Per-tick gross profit history, ordered by tick ascending. Null when base price unavailable. */
  profitHistory: ProfitTickSnapshot[] | null
  /** Structured demand driver explanations: price, quality, brand, location factors. */
  demandDrivers: DemandDriverEntry[]
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

export interface ProfitTickSnapshot {
  tick: number
  /** Gross profit for the tick (revenue − quantity × basePrice). */
  profit: number
  /** Gross margin percentage (0–100+). Null when basePrice is zero. */
  grossMarginPct: number | null
}

/** Explains one demand-influencing factor for a public sales unit. */
export interface DemandDriverEntry {
  /** PRICE | QUALITY | BRAND | LOCATION | SATURATION | COMPETITION */
  factor: string
  /** POSITIVE | NEUTRAL | NEGATIVE */
  impact: string
  /** Strength of the factor (0.0–1.0). */
  score: number
  /** Short player-facing description. */
  description: string
}

export interface MarketShareEntry {
  label: string
  companyId: string | null
  share: number
  /** True when this entry represents unserved market demand, not an actual seller. */
  isUnmet: boolean
}

/** Per-tick cost and production snapshot for a MANUFACTURING unit. */
export interface UnitProductTickSnapshot {
  tick: number
  /** Labor cost charged on this tick. */
  laborCost: number
  /** Energy cost charged on this tick. */
  energyCost: number
  /** Total operating cost (labor + energy) for this tick. */
  totalCost: number
  /** Quantity of the product produced on this tick. */
  quantityProduced: number
  /**
   * Estimated revenue = quantityProduced × product.basePrice.
   * Null when base price is unavailable.
   */
  estimatedRevenue: number | null
  /**
   * Estimated profit = estimatedRevenue − totalCost.
   * Null when base price is unavailable.
   */
  estimatedProfit: number | null
}

/**
 * Product-level analytics for a MANUFACTURING unit.
 * Shows cost history, production quantity, and estimated economics per tick.
 */
export interface UnitProductAnalytics {
  buildingUnitId: string
  /** The unit type (e.g. MANUFACTURING). */
  unitType: string
  /** Product type ID being produced. Null when no product is configured. */
  productTypeId: string | null
  /** Display name of the product being produced. Null when no product data is available. */
  productName: string | null
  /** First tick in the analytics window (oldest data). */
  dataFromTick: number
  /** Last tick in the analytics window (most recent data). */
  dataToTick: number
  /** Total labor + energy cost over the analytics window. */
  totalCost: number
  /** Total units produced over the analytics window. */
  totalQuantityProduced: number
  /**
   * Estimated total revenue = totalQuantityProduced × product.basePrice.
   * Null when base price is unavailable.
   */
  estimatedRevenue: number | null
  /**
   * Estimated total profit = estimatedRevenue − totalCost.
   * Null when base price is unavailable.
   */
  estimatedProfit: number | null
  /** Per-tick snapshots ordered by tick ascending. */
  snapshots: UnitProductTickSnapshot[]
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

/**
 * First-sale mission status returned by the firstSaleMission query.
 * Tracks the post-onboarding mission from shop configuration through to the first real public sale.
 */
export interface FirstSaleMission {
  /**
   * Current phase of the first-sale mission.
   * Values: NO_SHOP | CONFIGURE_SHOP | AWAITING_FIRST_SALE | FIRST_SALE_RECORDED | ALREADY_COMPLETED
   */
  phase: 'NO_SHOP' | 'CONFIGURE_SHOP' | 'AWAITING_FIRST_SALE' | 'FIRST_SALE_RECORDED' | 'ALREADY_COMPLETED'
  /** The onboarding sales shop building ID being tracked (null when phase is NO_SHOP). */
  shopBuildingId: string | null
  /** Display name of the onboarding sales shop. */
  shopName: string | null
  /**
   * Blocker codes explaining why the shop is not yet ready.
   * Values: BUILDING_UNDER_CONSTRUCTION | PUBLIC_SALES_UNIT_MISSING | PRICE_NOT_SET | NO_INVENTORY
   */
  blockers: string[]
  /** Revenue from the first recorded sale. */
  firstSaleRevenue: number | null
  /** Name of the product sold in the first sale. */
  firstSaleProductName: string | null
  /** Game tick at which the first sale occurred. */
  firstSaleTick: number | null
  /** Quantity sold in the first sale. */
  firstSaleQuantity: number | null
  /** Price per unit in the first sale. */
  firstSalePricePerUnit: number | null
}

/**
 * Operational status for a single building unit.
 * Tells the player whether the unit is actively processing, idle, blocked, or unconfigured.
 */
export interface BuildingUnitOperationalStatus {
  buildingUnitId: string
  /**
   * Machine-readable status.
   * Values: ACTIVE | IDLE | BLOCKED | FULL | UNCONFIGURED
   */
  status: 'ACTIVE' | 'IDLE' | 'BLOCKED' | 'FULL' | 'UNCONFIGURED'
  /**
   * Machine-readable blocked reason code (null when status is not BLOCKED or FULL).
   * Values: NO_INPUTS | OUTPUT_FULL | NO_INVENTORY | PRICE_TOO_HIGH | UNCONFIGURED | AWAITING_STOCK | NO_DEMAND
   */
  blockedCode: string | null
  /** Human-readable explanation shown to the player. */
  blockedReason: string | null
  /** Number of consecutive ticks the unit has had no activity. */
  idleTicks: number
  /**
   * Estimated base labor cost per tick for this unit, in game currency.
   * Derived from unit type, level, and the company's effective hourly wage for the building's city.
   * Null if the unit carries no labor cost.
   */
  nextTickLaborCost: number | null
  /**
   * Estimated energy cost per tick for this unit, in game currency.
   * Derived from unit type, level, and the fixed energy price per MWh.
   * Null if the unit carries no energy cost.
   */
  nextTickEnergyCost: number | null
}

/**
 * A single human-readable event from a building's recent tick history.
 * Covers purchasing, manufacturing, resource movement, and public sales.
 */
export interface BuildingRecentActivityEvent {
  tick: number
  buildingUnitId: string
  /**
   * Machine-readable event type.
   * Values: PURCHASED | MANUFACTURED | MOVED | SOLD | IDLE | BLOCKED
   */
  eventType: 'PURCHASED' | 'MANUFACTURED' | 'MOVED' | 'SOLD' | 'IDLE' | 'BLOCKED'
  /** Human-readable one-line description. */
  description: string
  quantity: number | null
  amount: number | null
  resourceTypeId: string | null
  productTypeId: string | null
}

/** A media house building in a city, returned by cityMediaHouses query. */
export interface CityMediaHouseInfo {
  id: string
  name: string
  cityId: string
  /** NEWSPAPER | RADIO | TV — null if not yet configured */
  mediaType: string | null
  ownerCompanyId: string
  ownerCompanyName: string
  /** 1.0 = Newspaper, 1.5 = Radio, 2.0 = TV */
  effectivenessMultiplier: number
  /** POWERED | CONSTRAINED | OFFLINE */
  powerStatus: string
  isUnderConstruction: boolean
}

/** A loan offer published by a bank building. */
export interface LoanOfferSummary {
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

/** Status values for a loan. */
export type LoanStatus = 'ACTIVE' | 'OVERDUE' | 'DEFAULTED' | 'REPAID'

/** An active or historical loan (borrower or lender view). */
export interface LoanSummary {
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
  status: LoanStatus
  missedPayments: number
  accumulatedPenalty: number
  acceptedAtUtc: string
  closedAtUtc: string | null
}

/** Procurement preview result from the backend. */
export interface ProcurementPreview {
  sourceType: 'GLOBAL_EXCHANGE' | 'LOCAL_B2B' | 'LOCKED_VENDOR' | 'NO_SOURCE'
  sourceCityId: string | null
  sourceCityName: string | null
  sourceVendorCompanyId: string | null
  sourceVendorName: string | null
  exchangePricePerUnit: number | null
  transitCostPerUnit: number | null
  deliveredPricePerUnit: number | null
  estimatedQuality: number | null
  canExecute: boolean
  blockReason: string | null
  blockMessage: string | null
}

/**
 * A single sourcing candidate for a purchase unit.
 * Each candidate represents one possible supply route with a full landed-cost
 * breakdown (exchange price + transit cost = delivered price).
 * Returned by the `sourcingCandidates` GraphQL query.
 */
export interface SourcingCandidate {
  sourceType: 'GLOBAL_EXCHANGE' | 'PLAYER_EXCHANGE_ORDER' | 'LOCAL_B2B' | 'LOCKED_VENDOR' | 'NO_SOURCE'
  sourceCityId: string | null
  sourceCityName: string | null
  sourceVendorCompanyId: string | null
  sourceVendorName: string | null
  exchangePricePerUnit: number | null
  transitCostPerUnit: number | null
  deliveredPricePerUnit: number | null
  estimatedQuality: number | null
  distanceKm: number | null
  isEligible: boolean
  blockReason: string | null
  blockMessage: string | null
  isRecommended: boolean
  rank: number
}

/** Upgrade info for a single building unit: cost, timing, and stat projections. */
export interface UnitUpgradeInfo {
  unitId: string
  unitType: string
  currentLevel: number
  nextLevel: number
  isMaxLevel: boolean
  isUpgradable: boolean
  upgradeCost: number
  upgradeTicks: number
  currentStat: number
  nextStat: number
  statLabel: string
}
