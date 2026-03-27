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
  buildings: Building[]
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
  occupancyPercent: number | null
  totalAreaSqm: number | null
  powerPlantType: string | null
  powerOutput: number | null
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
  priceType?: 'FREE' | 'PAID'
  priceMin?: number
  priceMax?: number
  date?: 'upcoming' | 'past'
  dateFrom?: string
  dateTo?: string
  sort?: 'newest' | 'oldest' | 'name' | 'RELEVANCE'
  sortBy?: 'NEWEST' | 'RELEVANCE'
  domain?: string
  attendanceMode?: 'IN_PERSON' | 'ONLINE' | 'HYBRID'
  language?: string
  timezone?: string
}

/** Onboarding types */
export interface City {
  id: string
  name: string
  countryCode: string
  latitude: number
  longitude: number
  population: number
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
  industry: string
  basePrice: number
  baseCraftTicks: number
  outputQuantity: number
  energyConsumptionMwh: number
  unitName: string
  unitSymbol: string
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
  tickIntervalSeconds: number
  taxCycleTicks: number
  taxRate: number
}
