# Copilot instructions for `scholtz/capitalism`

## Repository structure
- Root repository contains both frontend and backend.
- Backend API lives at `projects/Api` (ASP.NET Core).
- Backend tests live at `projects/Api.Tests`.
- Frontend Vue app lives at `projects/frontend`.
- The deployed backend is available at `https://capitalism.de-4.biatec.io/graphql`.
- Main frontend source files are in `projects/frontend/src`.
- Views (page-level components) are in `src/views/`.
- Reusable components are in `src/components/`, organized by feature (e.g., `layout/`).
- TypeScript type definitions are in `src/types/`.
- Pinia stores are in `src/stores/`.
- GraphQL client helper is in `src/lib/graphql.ts`.
- Global CSS styles and design tokens are in `src/assets/styles/main.css`.
- Vue Router configuration is in `src/router/index.ts`.

## Technology and conventions
- Frontend uses Vue 3 + TypeScript + Vite.
- Backend uses ASP.NET Core 10, Hot Chocolate GraphQL v15, Entity Framework Core (SQLite), and JWT bearer authentication.
- Frontend communicates with the backend exclusively via GraphQL using a lightweight fetch-based client (`src/lib/graphql.ts`).
- The GraphQL endpoint URL is configured via `VITE_GRAPHQL_URL` environment variable (defaults to `https://capitalism.de-4.biatec.io/graphql`).
- State management uses Pinia with the Composition API (`defineStore` with `setup` function syntax).
- Routing uses Vue Router 5 with lazy-loaded route components (except the home page).
- Follow existing formatting conventions from `projects/frontend/.prettierrc.json`:
  - `semi: false`
  - `singleQuote: true`
  - `printWidth: 100`
- Keep changes minimal and scoped to the issue being solved.
- For any UI change or new UI behavior, add or update Playwright end-to-end tests that cover the user-visible flow.
- Use `<script setup lang="ts">` in all Vue single-file components.
- Use scoped styles (`<style scoped>`) in Vue components.
- Use CSS custom properties (variables) defined in `main.css` for theming/colors.
- Prefer semantic HTML elements and accessibility attributes.
- Use `@/` path alias for imports from the `src` directory.

## Game domain model
This is a Capitalism II-style multiplayer economic strategy game. Key entities:

### Core entities (in `Api/Data/Entities/`)
- **Player**: Registered user with email, displayName, passwordHash, role (PLAYER/ADMIN).
- **Company**: Player-owned corporation with name and cash balance. One player can own multiple companies.
- **Building**: Placed in a city, owned by a company. Types: MINE, FACTORY, SALES_SHOP, RESEARCH_DEVELOPMENT, APARTMENT, COMMERCIAL, MEDIA_HOUSE, BANK, EXCHANGE, POWER_PLANT.
- **BuildingUnit**: A 4×4 grid slot inside a building. Types: MINING, STORAGE, B2B_SALES, PURCHASE, MANUFACTURING, BRANDING, MARKETING, PUBLIC_SALES, PRODUCT_QUALITY, BRAND_QUALITY. Units can be linked (right, down, diagonal).
- **City**: Game world location with population, rent rates, and available resources.
- **ResourceType**: Raw materials (Wood, Iron Ore, Coal, Gold, Chemical Minerals, Cotton, Grain, Silicon). Categories: RAW_MATERIAL, MINERAL, ORGANIC.
- **ProductType**: Manufactured goods. Industries: FURNITURE, FOOD_PROCESSING, HEALTHCARE, ELECTRONICS, CONSTRUCTION. Starter industries: FURNITURE, FOOD_PROCESSING, HEALTHCARE.
- **ProductRecipe**: Links products to required resources with quantities.
- **Brand**: Company branding with scope (PRODUCT, CATEGORY, COMPANY), awareness, and quality.
- **Inventory**: Tracks resources/products in buildings with quantity, quality, and brand.
- **GameState**: Singleton tick-based game clock with tax cycle and rate.
- **ExchangeOrder**: Buy/sell orders on the commodity exchange.

### Seed data
The game is seeded with:
- 8 resource types (Wood, Iron Ore, Coal, Gold, Chemical Minerals, Cotton, Grain, Silicon)
- 3 cities (Bratislava SK, Prague CZ, Vienna AT) with per-city resource abundances
- 7 starter products (Wooden Chair, Wooden Table, Wooden Bed, Bread, Flour, Basic Medicine, Bandages) with recipes

## GraphQL integration
- All frontend data fetching uses GraphQL queries and mutations via `gqlRequest()` from `src/lib/graphql.ts`.
- Frontend types in `src/types/index.ts` are synced with backend entity models.
- Player roles: `PLAYER`, `ADMIN`.
- Key GraphQL operations:
  - **Queries**: `me`, `cities`, `city(id)`, `resourceTypes`, `productTypes(industry?)`, `rankings`, `myCompanies`, `gameState`, `starterIndustries`
  - **Mutations**: `register(input)`, `login(input)`, `createCompany(input)`, `placeBuilding(input)`, `completeOnboarding(input)`
- The `completeOnboarding` mutation creates a company with $500K starting capital, a factory (4 default units), and a sales shop (3 default units) in the chosen city.

## Server-controlled game state
- Never trust client-provided values for derived or economy-sensitive fields. Activation ticks, upgrade durations, ownership, IDs, server timestamps, levels, prices, balances, and similar progression state must be computed or validated on the backend.
- GraphQL input types should expose only player-editable fields. For building configuration flows, the client may submit layout choices and link booleans, but the backend must own pending-upgrade state such as `appliesAtTick`, `totalTicksRequired`, `isChanged`, and any queued configuration metadata.
- When adding or changing frontend types, distinguish clearly between active state and pending/planned state. Do not let the UI mutate live building state directly when the game rules require a queued upgrade.
- If a field could be abused to bypass ticks, ownership checks, prices, cooldowns, or other game rules, keep it server-controlled and cover the rule with backend tests.

## Authentication
- JWT tokens are obtained via `register` or `login` GraphQL mutations.
- Tokens are stored in `localStorage` under `auth_token` and `auth_expires` keys.
- The GraphQL client automatically attaches the JWT token as a Bearer token in the `Authorization` header.
- The auth store (`src/stores/auth.ts`) provides `initFromStorage()`, `register()`, `login()`, `fetchMe()`, and `logout()`.
- `initFromStorage()` is called in `App.vue`'s `<script setup>` so the token is available to all views.
- Token expiry: 120 minutes. HS256 signing.

## Frontend pages
- **HomeView** (`/`): Hero section, game status cards (tick, tax rate, active players), leaderboard table. CTA changes based on auth state: "Get Started" (unauthenticated) → "Start Your Empire" (no companies) → "Go to Dashboard" (has companies).
- **LoginView** (`/login`): Login/Register toggle form with email, password, optional display name.
- **OnboardingView** (`/onboarding`): 3-step wizard: (1) choose industry, (2) choose city, (3) name company + pick product. Calls `completeOnboarding` mutation. Redirects to dashboard on success.
- **DashboardView** (`/dashboard`): Player info, company cards with buildings list, empty state with link to onboarding.

## Playwright E2E testing

### Structure
- Tests live in `projects/frontend/e2e/`.
- Shared API mock helpers are in `e2e/helpers/mock-api.ts`.
- Active test files: `home.spec.ts` (home page + header nav), `onboarding.spec.ts` (auth, onboarding wizard, dashboard, full journey), `building-detail.spec.ts` (queued building upgrades and unit-link behavior).
- Old events-specific spec files (`auth.spec.ts`, `category.spec.ts`, etc.) contain `test.skip` placeholders.

### Always use the shared mock helper
All tests must set up API mocks **before** calling `page.goto()`. Use `setupMockApi(page, initialState)` from `./helpers/mock-api` to intercept all GraphQL API requests.

```ts
import { setupMockApi, makePlayer } from './helpers/mock-api'

test('shows dashboard', async ({ page }) => {
  const player = makePlayer()
  const state = setupMockApi(page, { players: [player] })
  state.currentUserId = player.id
  state.currentToken = `token-${player.id}`
  await page.addInitScript((token) => {
    localStorage.setItem('auth_token', token)
    localStorage.setItem('auth_expires', new Date(Date.now() + 7200000).toISOString())
  }, `token-${player.id}`)
  await page.goto('/dashboard')
})
```

### Mock API types and factories
- `MockPlayer`, `MockCompany`, `MockBuilding`, `MockBuildingUnit`, `MockCity`, `MockResourceType`, `MockProductType`
- Factory functions: `makePlayer()`, `makeAdminPlayer()`, `makeDefaultCities()`, `makeDefaultResources()`, `makeDefaultProducts()`, `makeChairProduct()`, `makeBratislava()`
- Authentication helper: `loginAs(page, state, player)` — fills login form with English labels

### Authenticated test pattern
For tests that need authentication:
1. Create a player with `makePlayer()`
2. Pass it in `setupMockApi(page, { players: [player] })`
3. Set `state.currentUserId` and `state.currentToken`
4. Use `page.addInitScript()` to set localStorage before page load
5. Navigate with `page.goto()`

### Selectors – prefer accessible locators
Use Playwright's accessible locators in this order of preference:
1. `page.getByRole('button', { name: '…' })` — preferred for interactive elements
2. `page.getByLabel('…')` — preferred for form fields
3. `page.getByRole('heading', { name: '…' })` — preferred for headings
4. `page.locator('.css-class', { hasText: '…' })` — acceptable for component-level checks

**Strict mode**: Playwright runs in strict mode by default. When text can match multiple elements, scope with a container: `page.locator('.company-card').first().getByRole('heading', { name: '…' })`.

### Assertions
- Always `await expect(...)` — never use bare `expect()` in async tests.
- Use `toBeVisible()` to confirm rendered UI, `toContainText()` for partial text.
- Avoid fixed `page.waitForTimeout()` calls; rely on `expect(...).toBeVisible()` or `page.waitForURL()`.

### CI
- Playwright tests run via `.github/workflows/playwright.yml`.
- Only Chromium is used in CI. Run all browsers locally if needed.
- The CI workflow builds the client first then runs `npm run test:e2e`.

### Running tests locally
```bash
cd projects/frontend
npx playwright install --with-deps chromium
npx playwright test --project=chromium
# Specific file
npx playwright test --project=chromium e2e/onboarding.spec.ts
# Debug mode
npx playwright test --debug --project=chromium
```

## Backend testing
- Integration tests in `Api.Tests/GraphQlIntegrationTests.cs` use `WebApplicationFactory` with InMemory SQLite.
- Test factory in `Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`.
- Tests cover: health check, auth (register, login, duplicate email, wrong password), game data queries, company management, building placement, onboarding flow, rankings.
- Run with: `dotnet test projects/Api.Tests`

## Backend documentation
- `Api/docs/game-data-model.md` — Entity relationship diagram, field descriptions, seed data.
- `Api/docs/graphql-api.md` — Full API reference with all queries, mutations, input types, and error codes.

## Internationalization (i18n)
- Frontend uses vue-i18n v11 in Composition API mode (`useI18n()` with `t()` and `locale.value`).
- Locale files: `src/i18n/locales/{en,sk,de}.ts`. Detection chain: localStorage (`app_locale`) → `navigator.languages` → `'en'`.
- All user-visible strings use `t()` calls with keys organized by feature: `common.*`, `nav.*`, `home.*`, `auth.*`, `onboarding.*`, `dashboard.*`.

## PWA and service-worker development
- `vite-plugin-pwa` is configured in `injectManifest` mode with custom service worker at `src/sw.ts`.
- Playwright's config sets `serviceWorkers: 'block'` globally so no SW installs during tests.

## Build commands
```bash
# Frontend
cd projects/frontend
npm run dev          # Dev server on :5173
npm run build:client # Production build with SW
npm run build:ssr    # SSR build with vue-tsc type checking

# Backend
cd projects/Api
dotnet run           # API server
dotnet test ../Api.Tests  # Run integration tests
```

## Validation requirements before reporting completion
- For backend changes, do not stop at Debug-only targeted tests. Always run the workflow-equivalent Release pipeline locally:
  - `cd projects/Api && dotnet restore Api.slnx && dotnet build Api.slnx --configuration Release --no-restore && dotnet test Api.slnx --configuration Release --no-build`
- For frontend changes that affect shipped UI, also run the workflow-equivalent frontend checks:
  - `cd projects/frontend && npm ci && npm run lint && npm run build`
- For Playwright changes or UI flows covered by Playwright, install browsers if needed and run the relevant spec exactly as CI expects:
  - `cd projects/frontend && npx playwright install --with-deps chromium`
  - `cd projects/frontend && npx playwright test --project=chromium e2e/<relevant-spec>.ts`
- If you discover pre-existing failing tests outside the changed area, call them out explicitly in progress/final reporting instead of assuming the repository is green.

## HotChocolate v15 notes
- Non-nullable input fields must be explicitly provided in GraphQL variables even if the C# class has a default.
- Enum values use SCREAMING_SNAKE_CASE strings (e.g., `"FURNITURE"`, `"IN_PERSON"`).
- JWT authentication is configured via `[Authorize]` attribute on mutations.
