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
  createdAtUtc: string
  onboardingCompletedAtUtc: string | null
  proSubscriptionEndsAtUtc: string | null
  startupPackOffer: MockStartupPackOffer | null
  companies: MockCompany[]
}

export type MockStartupPackOffer = {
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

export type MockCompany = {
  id: string
  playerId: string
  name: string
  cash: number
  foundedAtUtc: string
  buildings: MockBuilding[]
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
  powerPlantType?: string | null
  powerOutput?: number | null
  mediaType?: string | null
  interestRate?: number | null
  builtAtUtc?: string
  units: MockBuildingUnit[]
  pendingConfiguration: MockBuildingConfigurationPlan | null
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
  price: number
  suitableTypes: string
  ownerCompanyId: string | null
  buildingId: string | null
  ownerCompany: { id: string; name: string } | null
  building: { id: string; name: string; type: string } | null
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

export type MockState = {
  players: MockPlayer[]
  cities: MockCity[]
  buildingLots: MockBuildingLot[]
  resourceTypes: MockResourceType[]
  productTypes: MockProductType[]
  currentUserId: string | null
  currentToken: string | null
  gameState: { currentTick: number; tickIntervalSeconds: number; taxCycleTicks: number; taxRate: number }
}

function cloneUnit(unit: MockBuildingUnit): MockBuildingUnit {
  return { ...unit }
}

function areUnitsEquivalent(currentUnit: MockBuildingUnit | undefined, nextUnit: MockBuildingUnit): boolean {
  if (!currentUnit) {
    return false
  }

  return currentUnit.unitType === nextUnit.unitType
    && currentUnit.gridX === nextUnit.gridX
    && currentUnit.gridY === nextUnit.gridY
    && currentUnit.linkUp === nextUnit.linkUp
    && currentUnit.linkDown === nextUnit.linkDown
    && currentUnit.linkLeft === nextUnit.linkLeft
    && currentUnit.linkRight === nextUnit.linkRight
    && currentUnit.linkUpLeft === nextUnit.linkUpLeft
    && currentUnit.linkUpRight === nextUnit.linkUpRight
    && currentUnit.linkDownLeft === nextUnit.linkDownLeft
    && currentUnit.linkDownRight === nextUnit.linkDownRight
    && (currentUnit.resourceTypeId ?? null) === (nextUnit.resourceTypeId ?? null)
    && (currentUnit.productTypeId ?? null) === (nextUnit.productTypeId ?? null)
    && (currentUnit.minPrice ?? null) === (nextUnit.minPrice ?? null)
    && (currentUnit.maxPrice ?? null) === (nextUnit.maxPrice ?? null)
    && (currentUnit.purchaseSource ?? null) === (nextUnit.purchaseSource ?? null)
    && (currentUnit.saleVisibility ?? null) === (nextUnit.saleVisibility ?? null)
    && (currentUnit.budget ?? null) === (nextUnit.budget ?? null)
    && (currentUnit.mediaHouseBuildingId ?? null) === (nextUnit.mediaHouseBuildingId ?? null)
    && (currentUnit.minQuality ?? null) === (nextUnit.minQuality ?? null)
    && (currentUnit.brandScope ?? null) === (nextUnit.brandScope ?? null)
    && (currentUnit.vendorLockCompanyId ?? null) === (nextUnit.vendorLockCompanyId ?? null)
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
    ...plan.units
      .filter((unit) => unit.isChanged)
      .map((unit) => Math.max(unit.appliesAtTick - currentTick, 0)),
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
    currentUnit.linkUp !== nextUnit.linkUp
    || currentUnit.linkDown !== nextUnit.linkDown
    || currentUnit.linkLeft !== nextUnit.linkLeft
    || currentUnit.linkRight !== nextUnit.linkRight
    || currentUnit.linkUpLeft !== nextUnit.linkUpLeft
    || currentUnit.linkUpRight !== nextUnit.linkUpRight
    || currentUnit.linkDownLeft !== nextUnit.linkDownLeft
    || currentUnit.linkDownRight !== nextUnit.linkDownRight
  ) {
    return 1
  }

  if (
    (currentUnit.resourceTypeId ?? null) !== (nextUnit.resourceTypeId ?? null)
    || (currentUnit.productTypeId ?? null) !== (nextUnit.productTypeId ?? null)
    || (currentUnit.minPrice ?? null) !== (nextUnit.minPrice ?? null)
    || (currentUnit.maxPrice ?? null) !== (nextUnit.maxPrice ?? null)
    || (currentUnit.purchaseSource ?? null) !== (nextUnit.purchaseSource ?? null)
    || (currentUnit.saleVisibility ?? null) !== (nextUnit.saleVisibility ?? null)
    || (currentUnit.budget ?? null) !== (nextUnit.budget ?? null)
    || (currentUnit.mediaHouseBuildingId ?? null) !== (nextUnit.mediaHouseBuildingId ?? null)
    || (currentUnit.minQuality ?? null) !== (nextUnit.minQuality ?? null)
    || (currentUnit.brandScope ?? null) !== (nextUnit.brandScope ?? null)
    || (currentUnit.vendorLockCompanyId ?? null) !== (nextUnit.vendorLockCompanyId ?? null)
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

        building.units = Array.from(liveUnits.values())
          .sort((left, right) => (left.gridY - right.gridY) || (left.gridX - right.gridX))

        building.pendingConfiguration.removals = building.pendingConfiguration.removals
          .filter((candidate) => candidate.appliesAtTick > state.gameState.currentTick)

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
    createdAtUtc: '2026-01-01T00:00:00Z',
    onboardingCompletedAtUtc: null,
    proSubscriptionEndsAtUtc: null,
    startupPackOffer: null,
    companies: [],
    ...overrides,
  }
}

export function makeStartupPackOffer(overrides?: Partial<MockStartupPackOffer>): MockStartupPackOffer {
  const now = Date.now()
  return {
    id: `startup-pack-${now}`,
    offerKey: 'STARTUP_PACK_V1',
    status: 'ELIGIBLE',
    createdAtUtc: new Date(now).toISOString(),
    expiresAtUtc: new Date(now + 72 * 60 * 60 * 1000).toISOString(),
    shownAtUtc: null,
    dismissedAtUtc: null,
    claimedAtUtc: null,
    companyCashGrant: 250000,
    proDurationDays: 90,
    grantedCompanyId: null,
    ...overrides,
  }
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
      description: 'Large industrial plot near the eastern logistics corridor.',
      district: 'Industrial Zone',
      latitude: 48.152,
      longitude: 17.125,
      price: 80000,
      suitableTypes: 'FACTORY,MINE',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
    },
    {
      id: 'lot-commercial-1',
      cityId: 'city-ba',
      name: 'High Street Retail Space',
      description: 'Prime storefront on the main pedestrian avenue.',
      district: 'Commercial District',
      latitude: 48.145,
      longitude: 17.107,
      price: 120000,
      suitableTypes: 'SALES_SHOP,COMMERCIAL',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
    },
    {
      id: 'lot-residential-1',
      cityId: 'city-ba',
      name: 'Riverside Apartment Block',
      description: 'Scenic residential plot overlooking the Danube.',
      district: 'Residential Quarter',
      latitude: 48.14,
      longitude: 17.1,
      price: 110000,
      suitableTypes: 'APARTMENT',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
    },
    {
      id: 'lot-business-1',
      cityId: 'city-ba',
      name: 'Innovation Campus Office',
      description: 'Modern office complex in the technology business park.',
      district: 'Business Park',
      latitude: 48.156,
      longitude: 17.11,
      price: 130000,
      suitableTypes: 'RESEARCH_DEVELOPMENT,BANK',
      ownerCompanyId: null,
      buildingId: null,
      ownerCompany: null,
      building: null,
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
    players: [],
    cities: makeDefaultCities(),
    buildingLots: makeDefaultBuildingLots(),
    resourceTypes: makeDefaultResources(),
    productTypes: makeDefaultProducts(),
    currentUserId: null,
    currentToken: null,
    gameState: { currentTick: 42, tickIntervalSeconds: 60, taxCycleTicks: 1440, taxRate: 15 },
    ...initial,
  }

  page.route('**/graphql', async (route) => {
    const body = route.request().postDataJSON()
    const query: string = body?.query ?? ''

    // Auth token check
    const authHeader = route.request().headers()['authorization'] ?? ''
    if (authHeader.startsWith('Bearer token-')) {
      state.currentToken = authHeader.replace('Bearer ', '')
      state.currentUserId = authHeader.replace('Bearer token-', '')
    }

    // Mutations
    if (query.includes('Register')) {
      const input = body.variables?.input
      if (state.players.some((p) => p.email === input?.email)) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'A player with this email already exists.' }] }) })
      }
      const newPlayer: MockPlayer = {
        id: `player-${Date.now()}`,
        email: input.email,
        password: input.password,
        displayName: input.displayName,
        role: 'PLAYER',
        createdAtUtc: new Date().toISOString(),
        onboardingCompletedAtUtc: null,
        proSubscriptionEndsAtUtc: null,
        startupPackOffer: null,
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
              player: { ...newPlayer, password: undefined },
            },
          },
        }),
      })
    }

    if (query.includes('Login')) {
      const input = body.variables?.input
      const player = state.players.find((p) => p.email === input?.email && p.password === input?.password)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Invalid email or password.' }] }) })
      }
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
              player: { ...player, password: undefined },
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
      const company: MockCompany = {
        id: `company-${Date.now()}`,
        playerId: player.id,
        name: input.companyName,
        cash: 500000,
        foundedAtUtc: new Date().toISOString(),
        buildings: [
          { id: `building-factory-${Date.now()}`, companyId: '', cityId: input.cityId, type: 'FACTORY', name: `${input.companyName} Factory`, latitude: 48.15, longitude: 17.11, level: 1, powerConsumption: 2, isForSale: false, builtAtUtc: new Date().toISOString(), units: [], pendingConfiguration: null },
          { id: `building-shop-${Date.now()}`, companyId: '', cityId: input.cityId, type: 'SALES_SHOP', name: `${input.companyName} Shop`, latitude: 48.15, longitude: 17.11, level: 1, powerConsumption: 1, isForSale: false, builtAtUtc: new Date().toISOString(), units: [], pendingConfiguration: null },
        ],
      }
      player.companies.push(company)
      player.onboardingCompletedAtUtc = new Date().toISOString()
      player.startupPackOffer = player.startupPackOffer ?? makeStartupPackOffer()
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
              startupPackOffer: player.startupPackOffer,
            },
          },
        }),
      })
    }

    if (query.includes('markStartupPackOfferShown')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player?.startupPackOffer) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ data: { markStartupPackOfferShown: null } }) })
      }

      if (player.startupPackOffer.status === 'ELIGIBLE') {
        player.startupPackOffer.status = 'SHOWN'
      }
      player.startupPackOffer.shownAtUtc ??= new Date().toISOString()

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { markStartupPackOfferShown: player.startupPackOffer } }),
      })
    }

    if (query.includes('dismissStartupPackOffer')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player?.startupPackOffer) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ data: { dismissStartupPackOffer: null } }) })
      }

      if (new Date(player.startupPackOffer.expiresAtUtc).getTime() <= Date.now()) {
        player.startupPackOffer.status = 'EXPIRED'
      } else if (player.startupPackOffer.status !== 'CLAIMED') {
        player.startupPackOffer.status = 'DISMISSED'
        player.startupPackOffer.shownAtUtc ??= new Date().toISOString()
        player.startupPackOffer.dismissedAtUtc ??= new Date().toISOString()
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { dismissStartupPackOffer: player.startupPackOffer } }),
      })
    }

    if (query.includes('ClaimStartupPack')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      const companyId = body.variables?.input?.companyId
      const company = player?.companies.find((candidate) => candidate.id === companyId)

      if (!player?.startupPackOffer || !company) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Company not found or you do not own it.' }] }) })
      }

      if (new Date(player.startupPackOffer.expiresAtUtc).getTime() <= Date.now() && player.startupPackOffer.status !== 'CLAIMED') {
        player.startupPackOffer.status = 'EXPIRED'
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Startup pack offer has expired.', extensions: { code: 'STARTUP_PACK_EXPIRED' } }] }) })
      }

      if (player.startupPackOffer.status !== 'CLAIMED') {
        company.cash += player.startupPackOffer.companyCashGrant
        const now = new Date()
        // Keep this stacking rule aligned with projects/Api/Types/Mutation.cs so
        // the mock mirrors the backend-authoritative entitlement behavior.
        const baseDate = player.proSubscriptionEndsAtUtc && new Date(player.proSubscriptionEndsAtUtc) > now
          ? new Date(player.proSubscriptionEndsAtUtc)
          : now
        baseDate.setUTCDate(baseDate.getUTCDate() + player.startupPackOffer.proDurationDays)
        player.proSubscriptionEndsAtUtc = baseDate.toISOString()
        player.startupPackOffer.status = 'CLAIMED'
        player.startupPackOffer.claimedAtUtc = now.toISOString()
        player.startupPackOffer.shownAtUtc ??= now.toISOString()
        player.startupPackOffer.grantedCompanyId = company.id
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            claimStartupPack: {
              offer: player.startupPackOffer,
              company,
              proSubscriptionEndsAtUtc: player.proSubscriptionEndsAtUtc,
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
        foundedAtUtc: new Date().toISOString(),
        buildings: [],
      }
      player.companies.push(company)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { createCompany: company } }),
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

      const activePlayer = state.players.find((candidate) => candidate.id === state.currentUserId)
      const hasActiveProSubscription = !!activePlayer?.proSubscriptionEndsAtUtc
        && new Date(activePlayer.proSubscriptionEndsAtUtc).getTime() > Date.now()

      for (const unit of input.units ?? []) {
        if (!unit.productTypeId) {
          continue
        }

        const product = state.productTypes.find((candidate) => candidate.id === unit.productTypeId)
        const isRetainingExistingProduct = [
          ...(building.units ?? []),
          ...(building.pendingConfiguration?.units ?? []),
        ].some((candidate) =>
          candidate.unitType === unit.unitType
          && candidate.gridX === unit.gridX
          && candidate.gridY === unit.gridY
          && (candidate.productTypeId ?? null) === unit.productTypeId,
        )

        if (product?.isProOnly && !hasActiveProSubscription && !isRetainingExistingProduct) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              errors: [{
                message: `Pro subscription unlocks additional products to manufacture and sell. Activate Pro to use ${product.name}.`,
                extensions: { code: 'PRO_SUBSCRIPTION_REQUIRED' },
              }],
            }),
          })
        }
      }

      const currentUnits = new Map(building.units.map((unit) => [`${unit.gridX},${unit.gridY}`, unit]))
      const desiredUnits = new Map(
        (input.units ?? []).map((unit: MockBuildingUnit, index: number) => {
          const current = building.units.find((candidate) => candidate.gridX === unit.gridX && candidate.gridY === unit.gridY)

          return [`${unit.gridX},${unit.gridY}`, {
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
          } satisfies MockBuildingUnit]
        }),
      )
      const existingUnits = new Map((building.pendingConfiguration?.units ?? []).map((unit) => [`${unit.gridX},${unit.gridY}`, unit]))
      const existingRemovals = new Map((building.pendingConfiguration?.removals ?? []).map((removal) => [`${removal.gridX},${removal.gridY}`, removal]))
      const allPositions = new Set<string>([
        ...currentUnits.keys(),
        ...desiredUnits.keys(),
        ...existingUnits.keys(),
        ...existingRemovals.keys(),
      ])

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

      building.pendingConfiguration = buildPlanSummary({
        id: planId,
        buildingId: building.id,
        submittedAtUtc: new Date().toISOString(),
        submittedAtTick: state.gameState.currentTick,
        appliesAtTick: state.gameState.currentTick,
        totalTicksRequired: 0,
        units: nextPendingUnits.sort((left, right) => (left.gridY - right.gridY) || (left.gridX - right.gridX)),
        removals: nextPendingRemovals,
      }, state.gameState.currentTick)

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { storeBuildingConfiguration: building.pendingConfiguration } }),
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
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'This lot has already been purchased.' }] }) })
      }
      const suitableTypes = lot.suitableTypes.split(',').map((s) => s.trim())
      if (!suitableTypes.includes(input.buildingType)) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: `Building type ${input.buildingType} is not suitable for this lot.` }] }) })
      }
      if (company.cash < lot.price) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: `Insufficient funds. This lot costs $${lot.price.toLocaleString()} but you only have $${company.cash.toLocaleString()}.` }] }) })
      }

      company.cash -= lot.price
      const newBuilding: MockBuilding = {
        id: `building-lot-${Date.now()}`,
        companyId: company.id,
        cityId: lot.cityId,
        type: input.buildingType,
        name: input.buildingName,
        latitude: lot.latitude,
        longitude: lot.longitude,
        level: 1,
        powerConsumption: 1,
        isForSale: false,
        builtAtUtc: new Date().toISOString(),
        units: [],
        pendingConfiguration: null,
      }
      company.buildings.push(newBuilding)
      lot.ownerCompanyId = company.id
      lot.buildingId = newBuilding.id
      lot.ownerCompany = { id: company.id, name: company.name }
      lot.building = { id: newBuilding.id, name: newBuilding.name, type: newBuilding.type }

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

    // Queries - order specific handlers before generic ones
    if (query.includes('starterIndustries')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: { starterIndustries: { industries: ['FURNITURE', 'FOOD_PROCESSING', 'HEALTHCARE'] } },
        }),
      })
    }

    if (query.includes('productTypes')) {
      const industry = body.variables?.industry
      const activePlayer = state.players.find((player) => player.id === state.currentUserId)
      const hasActiveProSubscription = !!activePlayer?.proSubscriptionEndsAtUtc
        && new Date(activePlayer.proSubscriptionEndsAtUtc).getTime() > Date.now()
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

    if (query.includes('myCompanies')) {
      applyDueBuildingUpgrades(state)
      const player = state.players.find((p) => p.id === state.currentUserId)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { myCompanies: player?.companies ?? [] } }),
      })
    }

    if (query.includes('rankings')) {
      const rankings = state.players
        .filter((p) => p.role !== 'ADMIN')
        .map((p) => {
          const cashTotal = p.companies.reduce((sum, c) => sum + c.cash, 0)
          const buildingValue = p.companies.reduce(
            (sum, c) =>
              sum +
              c.buildings.reduce((bSum, b) => {
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
                return bSum + (baseValues[b.type] ?? 0) * b.level
              }, 0),
            0,
          )
          const inventoryValue = 0 // inventory not tracked in mock state
          return {
            playerId: p.id,
            displayName: p.displayName,
            cashTotal,
            buildingValue,
            inventoryValue,
            totalWealth: cashTotal + buildingValue + inventoryValue,
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

    if (query.includes('gameState')) {
      applyDueBuildingUpgrades(state)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { gameState: state.gameState } }),
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

    if (query.includes('resourceTypes')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { resourceTypes: state.resourceTypes } }),
      })
    }

    if (query.includes('startupPackOffer')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }

      if (player.startupPackOffer && new Date(player.startupPackOffer.expiresAtUtc).getTime() <= Date.now() && player.startupPackOffer.status !== 'CLAIMED') {
        player.startupPackOffer.status = 'EXPIRED'
      }

      if (query.includes('me')) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            data: {
              me: { ...player, password: undefined },
              startupPackOffer: player.startupPackOffer,
            },
          }),
        })
      }

      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { startupPackOffer: player.startupPackOffer } }),
      })
    }

    if (query.includes('me')) {
      const player = state.players.find((p) => p.id === state.currentUserId)
      if (!player) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ errors: [{ message: 'Not authenticated' }] }) })
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            me: { ...player, password: undefined },
            startupPackOffer: player.startupPackOffer,
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

export async function loginAs(page: Page, state: MockState, player: MockPlayer): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('Email').fill(player.email)
  await page.getByLabel('Password').fill(player.password)
  await page.getByRole('button', { name: 'Sign In' }).click()
  await page.waitForURL('/')
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`
}
