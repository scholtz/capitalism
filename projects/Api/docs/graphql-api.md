# Capitalism V — GraphQL API Reference

## Endpoint
`POST /graphql`

## Authentication
JWT Bearer tokens. Obtained via `register` or `login` mutations.  
Include in request header: `Authorization: Bearer <token>`

## Queries

### `me` *(requires auth)*
Returns the authenticated player's profile with companies.

```graphql
{
  me {
    id
    displayName
    email
    role
    createdAtUtc
    companies { id name cash }
  }
}
```

### `cities`
Lists all game map cities with their resources.

```graphql
{
  cities {
    id name countryCode latitude longitude population averageRentPerSqm
    resources {
      resourceType { id name slug category }
      abundance
    }
  }
}
```

### `city(id: UUID!)`
Gets a single city with buildings.

### `resourceTypes`
Lists all raw material types (encyclopaedia).

```graphql
{
  resourceTypes { id name slug category basePrice weightPerUnit description }
}
```

### `productTypes(industry: String)`
Lists product types, optionally filtered by industry.

- `isProOnly` marks premium-only catalog entries.
- `isUnlockedForCurrentPlayer` reflects the authenticated caller's current entitlement state.

```graphql
query {
  productTypes(industry: "FURNITURE") {
    id name slug industry basePrice baseCraftTicks isProOnly isUnlockedForCurrentPlayer
    recipes { resourceType { name } quantity }
  }
}
```

### `rankings`
Player leaderboard sorted by total wealth across all owned companies.

**Wealth formula (interim — evolves as the economy matures):**
- `totalWealth = cashTotal + buildingValue + inventoryValue`
- `cashTotal` — sum of `company.cash` for all companies owned by the player
- `buildingValue` — sum of `buildingBaseValue[type] × level` for every building owned.
  Base values by type: MINE $250k, FACTORY $200k, SALES_SHOP $150k, R&D $300k,
  APARTMENT $400k, COMMERCIAL $350k, MEDIA_HOUSE $500k, BANK $600k,
  EXCHANGE $450k, POWER_PLANT $350k
- `inventoryValue` — sum of `quantity × item.basePrice` for all resources and
  products stored in company buildings (quality/brand premium not yet included)

Admin players are excluded from rankings.

```graphql
{
  rankings {
    playerId
    displayName
    totalWealth
    cashTotal
    buildingValue
    inventoryValue
    companyCount
  }
}
```

### `myCompanies` *(requires auth)*
Lists the current player's companies with their buildings and units.

### `gameState`
Returns current tick, tax configuration, etc.

```graphql
{
  gameState { currentTick tickIntervalSeconds taxCycleTicks taxRate }
}
```

### `starterIndustries`
Returns available industries for new player onboarding.

```graphql
{
  starterIndustries { industries }
}
```

### `cityLots(cityId: UUID!)`
Lists building lots for a city, including ownership and availability state.

```graphql
query CityLots($cityId: UUID!) {
  cityLots(cityId: $cityId) {
    id name description district latitude longitude price suitableTypes
    ownerCompanyId buildingId
    ownerCompany { id name }
    building { id name type }
  }
}
```

### `lot(id: UUID!)`
Gets a single building lot by ID.

```graphql
query GetLot($id: UUID!) {
  lot(id: $id) {
    id name description district latitude longitude price suitableTypes
    ownerCompanyId buildingId
    ownerCompany { id name }
    building { id name type }
  }
}
```

## Mutations

### `register(input: RegisterInput!)`
Creates a new player account.

**Input:**
| Field | Type | Required |
|-------|------|----------|
| email | String | Yes |
| displayName | String | Yes |
| password | String | Yes (min 8 chars) |

**Returns:** `AuthPayload { token, expiresAtUtc, player }`

### `login(input: LoginInput!)`
Authenticates an existing player.

**Input:**
| Field | Type | Required |
|-------|------|----------|
| email | String | Yes |
| password | String | Yes |

**Returns:** `AuthPayload { token, expiresAtUtc, player }`

### `createCompany(input: CreateCompanyInput!)` *(requires auth)*
Creates a new company for the player (starting capital: 1,000,000).

**Input:**
| Field | Type | Required |
|-------|------|----------|
| name | String | Yes |

**Returns:** `Company`

### `placeBuilding(input: PlaceBuildingInput!)` *(requires auth)*
Places a building in a city.

**Input:**
| Field | Type | Required |
|-------|------|----------|
| companyId | UUID | Yes |
| cityId | UUID | Yes |
| type | String | Yes (valid BuildingType) |
| name | String | Yes |
| initialProductTypeId | UUID | No |

**Returns:** `Building`

### `completeOnboarding(input: OnboardingInput!)` *(requires auth)*
Onboarding wizard completion: creates a company, factory (with default units), and sales shop.

**Input:**
| Field | Type | Required |
|-------|------|----------|
| industry | String | Yes (FURNITURE, FOOD_PROCESSING, HEALTHCARE) |
| cityId | UUID | Yes |
| productTypeId | UUID | Yes |
| companyName | String | Yes |

**Returns:** `OnboardingResult { company, factory, salesShop, selectedProduct }`

### `purchaseLot(input: PurchaseLotInput!)` *(requires auth)*
Purchases a building lot and places a building on it. Validates lot availability, building type suitability, and company funds. Deducts lot price from company cash.

**Input:**
| Field | Type | Required |
|-------|------|----------|
| companyId | UUID | Yes |
| lotId | UUID | Yes |
| buildingType | String | Yes (must be in lot's suitableTypes) |
| buildingName | String | Yes |

**Returns:** `PurchaseLotResult { lot, building, company }`

**Error codes:**
| Code | Description |
|------|-------------|
| `LOT_NOT_FOUND` | Building lot doesn't exist |
| `LOT_ALREADY_OWNED` | Lot has already been purchased |
| `UNSUITABLE_BUILDING_TYPE` | Building type not suitable for this lot |
| `INSUFFICIENT_FUNDS` | Company doesn't have enough cash |

## Error Codes
| Code | Description |
|------|-------------|
| `DUPLICATE_EMAIL` | Email already registered |
| `INVALID_CREDENTIALS` | Wrong email or password |
| `COMPANY_NOT_FOUND` | Company doesn't exist or not owned by player |
| `INVALID_BUILDING_TYPE` | Invalid building type string |
| `CITY_NOT_FOUND` | City ID doesn't exist |
| `INVALID_INDUSTRY` | Not a valid starter industry |
| `INVALID_PRODUCT` | Product not found or wrong industry |
| `PRO_SUBSCRIPTION_REQUIRED` | The selected product is locked to active Pro subscribers |
| `LOT_NOT_FOUND` | Building lot doesn't exist |
| `LOT_ALREADY_OWNED` | Lot has already been purchased by another company |
| `UNSUITABLE_BUILDING_TYPE` | Building type not in the lot's suitable types list |
| `INSUFFICIENT_FUNDS` | Company doesn't have enough cash for the lot price |
