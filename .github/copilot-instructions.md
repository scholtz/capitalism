# Copilot instructions for `scholtz/capitalism`

## Repository structure
- Root repository contains both frontend and backend.
- Game backend API lives at `projects/Api` (ASP.NET Core).
- Master backend API lives at `projects/MasterApi` (ASP.NET Core).
- Backend tests live at `projects/Api.Tests`.
- Game frontend Vue app lives at `projects/frontend`.
- Master frontend Vue app lives at `projects/master-frontend`.
- The deployed backend is available at `https://capitalism.de-4.biatec.io/graphql`.
- Main game frontend source files are in `projects/frontend/src`.
- Main master frontend source files are in `projects/master-frontend/src`.
- Views (page-level components) are in `src/views/`.
- Reusable components are in `src/components/`, organized by feature (e.g., `layout/`).
- TypeScript type definitions are in `src/types/`.
- Pinia stores are in `src/stores/`.
- Game frontend GraphQL client helper is in `projects/frontend/src/lib/graphql.ts`.
- Master frontend GraphQL client helper is in `projects/master-frontend/src/lib/graphql.ts`.
- Global CSS styles and design tokens are in `src/assets/styles/main.css`.
- Game frontend router configuration is in `projects/frontend/src/router/index.ts`.
- Master frontend router configuration is in `projects/master-frontend/src/router/index.ts`.

## Technology and conventions
- Frontends use Vue 3 + TypeScript + Vite.
- Game backend uses ASP.NET Core 10, Hot Chocolate GraphQL v15, Entity Framework Core (SQLite), and JWT bearer authentication.
- Master backend uses ASP.NET Core 10, Hot Chocolate GraphQL v15, and Entity Framework Core (SQLite locally) to store the live game-server registry.
- Frontends communicate with their backend exclusively via GraphQL using lightweight fetch-based clients.
- The game frontend GraphQL endpoint URL is configured via `VITE_GRAPHQL_URL` environment variable (defaults to `https://capitalism.de-4.biatec.io/graphql`).
- The master frontend GraphQL endpoint URL is configured via `VITE_GRAPHQL_URL` environment variable (defaults to `https://localhost:44364/graphql`).
- State management uses Pinia with the Composition API (`defineStore` with `setup` function syntax).
- Routing uses Vue Router 5 with lazy-loaded route components (except the home page).
- Follow existing formatting conventions from `projects/frontend/.prettierrc.json`:
  - `semi: false`
  - `singleQuote: true`
  - `printWidth: 100`
- Follow the same formatting conventions for `projects/master-frontend/.prettierrc.json`.
- Keep changes minimal and scoped to the issue being solved.
- For any UI change or new UI behavior, add or update Playwright end-to-end tests that cover the user-visible flow.
- Use `<script setup lang="ts">` in all Vue single-file components.
- Use scoped styles (`<style scoped>`) in Vue components.
- Use CSS custom properties (variables) defined in `main.css` for theming/colors.
- Prefer semantic HTML elements and accessibility attributes.
- Use `@/` path alias for imports from the `src` directory.
- Default local development ports are: game frontend `5173`, master frontend `5174`, game API `5095`, master API `44364`.

## Multiple game servers infrastructure
- The master website is the discovery and product-pitch surface. It lists active game servers and links players to the correct game frontend.
- The master registry lives in `projects/MasterApi`. Core files are `Program.cs`, `Data/MasterDbContext.cs`, `Types/Query.cs`, and `Types/Mutation.cs`.
- The master frontend landing page lives in `projects/master-frontend/src/views/HomeView.vue` and lists the `gameServers` query result from `MasterApi`.
- Game servers register themselves to `MasterApi` through the `registerGameServer` GraphQL mutation.
- Game-server registration is implemented in `projects/Api/Utilities/MasterServerRegistrationHostedService.cs` and configured through the `MasterServer` section in `projects/Api/appsettings*.json`.
- The registration contract uses `projects/MasterApi/Types/RegisterGameServerInput` fields: `registrationKey`, `serverKey`, `displayName`, `description`, `region`, `environment`, `backendUrl`, `graphqlUrl`, `frontendUrl`, `version`, `playerCount`, `companyCount`, and `currentTick`.
- The registration key in `projects/Api/appsettings*.json` must match `projects/MasterApi/appsettings*.json` for the game server to appear in the master frontend.
- Treat `frontendUrl`, `backendUrl`, and `graphqlUrl` as server-owned infrastructure metadata. Do not let the master frontend invent or mutate them client-side.

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
  - **Mutations**: `register(input)`, `login(input)`, `createCompany(input)`, `placeBuilding(input)`, `completeOnboarding(input)`, `startOnboardingCompany(input)`, `finishOnboarding(input)`, `purchaseLot(input)`
- The staged onboarding flow now uses `startOnboardingCompany` to create the first company and purchase the first factory lot, then `finishOnboarding` to select the starter product, purchase the first sales shop lot, configure both buildings, and complete onboarding.
- Master-server GraphQL operations are `gameServers` and `registerGameServer(input)`.

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
- **HomeView** (`/`): Hero section, game status cards (tick, tax rate, active players), leaderboard table. CTA changes based on auth state: "Get Started" (unauthenticated) → "Start Your Empire" (authenticated but onboarding not completed) → "Go to Dashboard" (onboarding completed).
- **LoginView** (`/login`): Login/Register toggle form with email, password, optional display name.
- **OnboardingView** (`/onboarding`): guided staged onboarding flow: (1) choose industry, (2) choose city, (3) name company + purchase the first factory lot on the city map, (4) choose starter product + purchase the first sales shop lot, then completion. If onboarding is interrupted after the factory purchase, the player resumes directly into the shop step using backend-owned onboarding state.
- **DashboardView** (`/dashboard`): Player info, company cards with buildings list, empty state with link to onboarding.

## Playwright E2E testing

### Structure
- Tests live in `projects/frontend/e2e/`.
- Shared API mock helpers are in `e2e/helpers/mock-api.ts`.
- Active test files: `home.spec.ts` (home page + header nav), `onboarding.spec.ts` (auth, onboarding wizard, dashboard, full journey), `building-detail.spec.ts` (queued building upgrades and unit-link behavior), `city-map.spec.ts` (city map rendering, lot selection, purchase flow, dashboard→map navigation).
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
- If you change onboarding, auth routing, home CTA behavior, or dashboard redirect/resume behavior, run the broader affected specs locally instead of only a single targeted onboarding spec. At minimum include `e2e/onboarding.spec.ts` and `e2e/home.spec.ts`, and run `npm run test:e2e` if the change touches shared routing or mocked auth behavior.

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
- Integration tests in `Api.Tests/GraphQlIntegrationTests.cs` use `WebApplicationFactory` with a unique SQLite database per test factory instance.
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
- **vue-i18n v11 JIT compiler special characters**: The following characters are SPECIAL in vue-i18n message strings and must be escaped with `{'char'}` syntax when used as literal text:
  - `{` and `}` — used for interpolation; a bare `}` causes `SyntaxError: 10` (UNEXPECTED_CLOSE_BRACE / Invalid linked format)
  - `@` — begins a linked message reference (`@:key`); an email address like `user@example.com` MUST be written as `user{'@'}example.com`
  - `|` — used for plural rules
  - `$` — historical: `${param}` was NEVER valid vue-i18n syntax (use `{param}` instead); the `$` prefix is only added in the template caller, not in the message
- **Always validate locale files with the `@intlify/message-compiler` parser after adding or editing messages.** The SyntaxError fires at runtime (JIT) not at build time, and silently causes the entire component to render as `<!---->` in Vue 3's error boundary — making root-cause diagnosis very difficult.

## PWA and service-worker development
- `vite-plugin-pwa` is configured in `injectManifest` mode with custom service worker at `src/sw.ts`.
- Playwright's config sets `serviceWorkers: 'block'` globally so no SW installs during tests.

## Build commands
```bash
# Frontend
cd projects/frontend
npm run dev          # Dev server on :5173
npm run build:client # Production build with SW (no type checking)
npm run build:ssr    # SSR build via Vite only (no type checking)
npm run build        # Full build: runs vue-tsc type-check + build:client + build:ssr (USE THIS for CI-equivalent validation)

# Backend
cd projects/Api
dotnet run           # API server
dotnet test ../Api.Tests  # Run integration tests

# Master frontend
cd projects/master-frontend
npm install
npm run dev          # Dev server on :5174
npm run lint
npm run test:unit
npm run build

# Master backend
cd projects/MasterApi
dotnet run           # API server on :44364
dotnet build
```

## Validation requirements before reporting completion
- For backend changes, do not stop at Debug-only targeted tests. Always run the workflow-equivalent Release pipeline locally:
  - `cd projects/Api && dotnet restore Api.slnx && dotnet build Api.slnx --configuration Release --no-restore && dotnet test Api.slnx --configuration Release --no-build`
- For master-backend changes, run at least `cd projects/MasterApi && dotnet build` and, if you add tests later, run those too before reporting completion.
- **NEVER push with known failing tests.** If a test fails because of your change, you MUST fix it before pushing — even if the test appears "pre-existing" or "unrelated". An existing test that breaks under your new validation is evidence that the test data was invalid under the new rule; fix the test data, not the validation.
- For frontend changes that affect shipped UI, also run the workflow-equivalent frontend checks:
  - `cd projects/frontend && npm ci && npm run lint && npm run test:unit && npm run build`
- For master-frontend changes, run `cd projects/master-frontend && npm install && npm run lint && npm run test:unit && npm run build` before reporting completion.
  - **Lint must exit 0 (no errors) before pushing.** Run `npm run lint` explicitly after every frontend code change, not just at the very end. Common lint errors include unused destructured variables (e.g., `const { a, unusedB } = composable()`) and unreachable imports.
- For Playwright changes or UI flows covered by Playwright, install browsers if needed and run the relevant spec exactly as CI expects:
  - `cd projects/frontend && npx playwright install --with-deps chromium`
  - `cd projects/frontend && npx playwright test --project=chromium e2e/<relevant-spec>.ts`
- For onboarding/auth/routing changes, do not stop at a single happy-path spec. Run the broader Playwright surface that CI depends on (`npm run test:e2e`) or, if you are narrowing scope, explicitly include both onboarding and home/dashboard coverage so CTA and redirect regressions are caught before pushing.
- If you discover tests failing after your changes, root-cause them before assuming they are pre-existing. A test that was passing before your change and fails after is your responsibility.

## Lint quality gate
- **Always run `npm run lint` immediately after finishing code edits** and before `report_progress`. Do not defer lint to "final validation" — catch unused variables, missing imports, and type narrowing issues while edits are fresh.
- When destructuring a composable (e.g., `const { a, b, c } = useComposable()`), only destructure values you actually reference in the template or script. Unused destructured bindings produce ESLint `@typescript-eslint/no-unused-vars` errors that fail CI.
- When a composable handles its own cleanup internally via `onUnmounted`, callers do not need to destructure `stop*` / `cleanup*` functions unless they need to invoke cleanup manually before unmount.

## Unit test coverage requirements
- **All composables and `src/lib/` helpers with pure business logic must have unit tests.** Extract pure functions (time formatting, cost calculations, link-state logic) from Vue components into `src/lib/` or `src/composables/` so they can be tested without Vue or i18n context.
- Place composable unit tests in `src/composables/__tests__/` and lib unit tests in `src/lib/__tests__/`. Tests use Vitest with `environment: 'node'` — no browser APIs available.
- When you add a module with pure helpers, also add a `__tests__/<moduleName>.test.ts` covering: happy path, edge cases (zero, negative, empty), and format/boundary cases.
- The full unit test command is `cd projects/frontend && npm run test:unit`.

## HotChocolate v15 notes
- Non-nullable input fields must be explicitly provided in GraphQL variables even if the C# class has a default.
- Enum values use SCREAMING_SNAKE_CASE strings (e.g., `"FURNITURE"`, `"IN_PERSON"`).
- JWT authentication is configured via `[Authorize]` attribute on mutations.

## Backend integration test data quality — unit link flags
- **Every active link flag in test `StoreBuildingConfiguration` inputs must point to an occupied cell in the same submitted unit list.** The server validates this and returns `LINK_TARGET_MISSING` otherwise. A flag `linkRight=true` on a unit at `gridX=1` requires a unit at `gridX=2` in the same input array.
- **Link flags pointing outside the 4×4 grid are rejected with `LINK_OUT_OF_BOUNDS`.** Example: `linkRight=true` on a unit at `gridX=3` (max column) is always invalid.
- **Links are directional and asymmetric.** Setting `linkRight=true` on unit A does NOT require `linkLeft=true` on unit B. The engine treats each flag independently (source pushes resources in that direction). Test data can be asymmetric by design.
- Root-cause of quality failure (March 2026, PR #40): test data for `StoreBuildingConfiguration_ExpiredProCanKeepExistingLockedProductInPlace` had `linkRight=true` on a MANUFACTURING unit at x=1 with no unit at x=2 — the new validation correctly rejected it, causing CI failure. Fix: remove the orphan link flag from the test data.

## Playwright E2E test quality requirements
- **Never push code with known failing tests.** If tests fail locally, fix them before pushing. Do not assume CI will behave differently. If there is a legitimate build-cache discrepancy, document it and investigate before pushing.
- **Strict mode: always scope `getByText` when text may appear in multiple elements.** Use `{ exact: true }` when matching a heading/label string that also appears as a substring in another element (e.g., inside paragraph hints). Alternatively use `page.locator('.step-card').getByText(...)` to scope the locator.
- **URL assertions must account for router query params.** If the component sets a `?step=...` query param on mount (e.g., via `router.replace({ query: { step: 'complete' } })`), assertions like `toHaveURL('/onboarding')` will fail because the actual URL is `/onboarding?step=complete`. Check the component's routing logic and assert the full URL including query params, or use a regex: `await expect(page).toHaveURL(/\/onboarding/)`.
- **Always run the full `npm run test:e2e` suite before reporting completion for any change that touches OnboardingView, auth routing, or the mock-api helper.** Targeted single-spec runs can miss cross-spec regressions.
- **Root-cause failures before bypassing them.** If a test is failing because the implementation behaves unexpectedly, fix the implementation or the test — do not change the assertion to match a broken behavior.

## Startup pack and monetization conventions
- StartupPackOffer entity is in `projects/Api/Data/Entities/StartupPackOffer.cs` with states: `PENDING` (pre-onboarding), `ELIGIBLE` (offer active), `SHOWN`, `DISMISSED`, `CLAIMED`, `EXPIRED`.
- StartupPackService is in `projects/Api/Utilities/StartupPackService.cs`. It handles activation (idempotent), expiry calculation, and claim with cash grant + pro entitlement.
- The offer is activated in `completeOnboarding` and `finishOnboarding` mutations when `OnboardingCompletedAtUtc` is set.
- Backend GraphQL exposes `startupPackOffer` query on the authenticated player and mutations `markStartupPackOfferShown`, `dismissStartupPackOffer`, `claimStartupPack`.
- Frontend analytics hooks are in `src/lib/startupPackAnalytics.ts`. Always call these when displaying, dismissing, or claiming the offer.
- The frontend timer is derived from `expiresAtUtc` (authoritative backend timestamp) – never use `setTimeout` alone for expiry display.
- Offer banner appears in `DashboardView.vue` for ELIGIBLE/SHOWN/DISMISSED states; offer UI also appears in `OnboardingView.vue` completion step.

## Tick countdown and pending actions conventions
- `useGameStateStore` (`src/stores/gameState.ts`) is the single source of truth for authoritative simulation time on the frontend. It is started in `App.vue`, polls around the next tick boundary, and exposes `currentTick`, `currentGameYear`, `currentGameTimeUtc`, `nextTaxTick`, `nextTaxGameTimeUtc`, and `nextTaxGameYear`.
- Current in-game time in the navbar must come from the shared game-state store via `formatInGameTime()` from `src/lib/gameTime.ts`. Do not add separate page-local game-clock timers.
- Any view that shows tick-sensitive simulation data must use `useTickRefresh` (`src/composables/useTickRefresh.ts`) to refetch its page-specific data when the authoritative tick changes. This includes dashboards, leaderboards, live building views, onboarding completion state, and ledger/tax summaries.
- When refreshing a view on tick changes, preserve unsaved local draft state if the page supports editing. Do not wipe pending building-layout edits or similar client-side work-in-progress just because the authoritative tick advanced.
- `useTickCountdown` composable (`src/composables/useTickCountdown.ts`) is the single source of truth for next-tick countdown display. Do not inline countdown logic in views.
- `computeCountdownTimeStr(remainingMs)` is an exported pure helper from `useTickCountdown.ts` — use it when you need the raw time string, and test it in `src/composables/__tests__/useTickCountdown.test.ts`.
- `PendingActionsTimeline` component (`src/components/dashboard/PendingActionsTimeline.vue`) renders the player's scheduled building upgrades. It accepts `pendingActions: ScheduledActionSummary[]` and `loading: boolean` props.
- `DashboardView` uses the shared `useGameStateStore` for the tick clock and in-game time, and refetches dashboard-specific data (`myCompanies`, `myPendingActions`) on tick changes.
- Backend `myPendingActions` query (authenticated) returns `ScheduledActionSummary[]` ordered by `appliesAtTick` asc. Fields: `id`, `actionType`, `buildingId`, `buildingName`, `buildingType`, `submittedAtUtc`, `submittedAtTick`, `appliesAtTick`, `ticksRemaining`, `totalTicksRequired`.
- When testing `myPendingActions` in E2E, set `state.pendingActions` in the mock-api helper before `page.goto()`.
- Ledger summaries are tax-year scoped. Use `companyLedger(companyId, gameYear?)` for the selected year, show `history` for prior years, and keep `ledgerDrillDown(companyId, category, gameYear?)` aligned to the same selected year. The current year resets at the annual tax boundary, but historical years remain browsable in the ledger history UI.

## City map conventions
- `CityMapView.vue` lives at route `/city/:id`. It uses the `leaflet` package with OpenStreetMap tiles and `L.divIcon` for color-coded lot markers (green = available, blue = yours, gray = other owner).
- `BuildingLot` entity (`projects/Api/Data/Entities/BuildingLot.cs`) stores purchasable locations with `Latitude`, `Longitude`, `District`, `Price`, `BasePrice`, and comma-separated `SuitableTypes`. Also has `ResourceTypeId`, `MaterialQuality`, `MaterialQuantity` for raw material deposits. 14 lots are seeded for Bratislava in `AppDbInitializer.cs`.
- GraphQL: `cityLots(cityId)` is a **public query** (no auth required) so unauthenticated visitors can browse. `lot(id)` is also public. `purchaseLot` mutation requires auth and uses optimistic concurrency via `ConcurrencyToken`.
- A building placed via `purchaseLot` inherits the lot's `Latitude`/`Longitude` exactly.
- All city map UI strings use the `cityMap.*` i18n namespace, with district names under `cityMap.districts.*`. All three locales (en/sk/de) must have these keys.
- `makeDefaultBuildingLots()` factory in `e2e/helpers/mock-api.ts` provides 4 mock lots (2 factory, 1 commercial, 1 residential) for E2E tests. Use `state.buildingLots` to customize lot ownership in tests.
- When adding new city map E2E tests, always include `city-map.spec.ts` in the run; it is the canonical spec for the `/city/:id` route.
- Bratislava coordinates (for validation): lat 47.8–48.4°N, lon 16.8–17.5°E.
- The lot detail panel shows both `appraisedValue` (basePrice) and `price` (asking price) separately. When a lot has a raw material deposit and `price > basePrice`, a "resource premium" badge is shown next to the asking price. This implements the ROADMAP requirement: "The price to purchase the land includes also the base price for the raw material."
- A PR titled `[WIP]` must not be left in that state. Drive every PR to a production-ready, fully-tested state before reporting complete.
- Always confirm CI would pass by running the full local validation pipeline (backend Release build + tests, frontend lint + unit tests + build, full Playwright suite) before reporting completion.
- Remove `[WIP]` from the PR title when all acceptance criteria are met, all tests pass, and the code has been reviewed.

## PR description accuracy — preventing feature-claim / diff-only mismatch

**Before writing a PR description, always run `git diff FETCH_HEAD..HEAD --stat` (or `git diff origin/main..HEAD --stat` if that ref is available) to confirm what files are actually changed by this branch.** Write the PR description based only on what the diff shows, not on what is present in the repository as a whole (files that were already on main before the branch was cut must NOT be listed as "delivered" by this PR).

Root-cause of a past quality failure (March 2026, PR #24):
- The branch was cut from a main commit that already contained the full city-map implementation (CityMapView.vue, BuildingLot.cs, purchaseLot mutation, etc.) delivered in a previous session.
- The agent wrote a PR description claiming full feature delivery without verifying the actual diff vs main.
- The real diff only contained test additions; the PR description was misleading.

To prevent this from repeating:
1. Run `git diff FETCH_HEAD..HEAD --stat` at the start of every session to see what this branch actually contributes vs main.
2. If the diff is smaller than expected (e.g., implementation files are absent), investigate whether they were already merged to main in a previous PR.
3. Only claim features in the PR description that appear as **additions** in the diff.
4. If the implementation is already on main and the branch only adds tests or follow-up work, say so explicitly in the PR description.
5. Never let the presence of files in the working tree mislead you — files can already be on main.

## Empty-PR quality failure — the "initial plan only" anti-pattern

Root-cause of a past quality failure (March 2026, PR #46):
- The agent was assigned to implement the onboarding wizard. The implementation was already delivered on `main` in a prior session (by a different agent run) before this branch was cut.
- When the new agent session started, the diff was empty (only an "Initial plan" commit with no file changes). The agent responded by explaining the work was done — but never verified, fixed, or improved anything.
- The product owner reviewed the PR, saw no meaningful diff and no indication of quality improvements, and concluded the delivery was unfinished.

**When the diff is empty and the PR title implies work to be done:**
1. **Do not simply reply "it's already done."** Always investigate whether there are quality gaps, test coverage holes, or product-definition misalignments even when the core implementation exists.
2. Run the full test suite (`dotnet test` + `npm run test:unit` + `npx playwright test`) and fix any failures.
3. Check the product definition (ROADMAP.md and the linked issue) against the existing implementation and identify missing coverage.
4. Add missing backend tests (validation errors, duplicate prevention, all industry paths, unauthenticated calls).
5. Add missing E2E tests (skip-path, edge cases named in the issue acceptance criteria).
6. Update the copilot instructions with root-cause lessons so the failure does not repeat.
7. Only report completion after meaningful improvements are committed and all tests pass.

**Minimum quality bar for any PR that implements a user-facing feature:**
- Backend: validation error tests, unauthenticated tests, duplicate-prevention tests for every mutation.
- E2E: golden path + at least one interruption/resume test + at least one skip-path or error-recovery test.
- All tests must pass locally before pushing; never push with known failures.

## Onboarding product-proof quality — green CI is not enough

Root-cause of a quality failure (March 2026, PR #51 onboarding follow-up):
- The branch had green backend, unit, and Playwright suites, but the completion-step UX still did not *prove* the business launch outcome clearly enough for product review.
- The onboarding guide used generic copy for pricing/time instead of explicit business context (for example the numeric starter price target and the current simulation tick).
- Review feedback was therefore about product-definition alignment and proof quality, not just red CI.

**When a first-session or guided-flow feature is "green but not ready":**
1. Re-read the relevant ROADMAP acceptance text and inspect the actual rendered UI, not just the tests.
2. Verify the completion state exposes the concrete business context a player needs (cash, numeric price target, current tick/time, next action).
3. Add or tighten E2E assertions for those concrete values so future regressions are caught.
4. Add backend assertions for authoritative starter configuration values when the guided flow depends on them (for example min/max prices, product/resource bindings, and unit links).
5. Do not respond to product-review feedback with "tests already pass" alone — prove the player-visible outcome and then update the PR description accordingly.

## Frontend CI parity — avoid local/CI type-check drift

Root-cause of a quality failure (March 2026, PR #52 onboarding follow-up):
- `frontend-ci-cd` failed in CI with a `vue-tsc` error even though the agent had previously seen a local `npm run build` succeed.
- The missing type surface (`ProductType.imageUrl`) was still referenced by the app, but the local check did not expose it before the branch was pushed.

**When frontend CI reports a type-check failure but local build looked green:**
1. Pull the failing GitHub Actions job logs and identify the exact file/line before assuming the failure is transient.
2. Re-run the frontend pipeline from a clean dependency state (`npm ci`, then `npm run lint`, `npm run test:unit`, `npm run build`) so CI and local validation match.
3. If the failure is a missing property on a shared frontend GraphQL type, update the canonical type definition in `src/types/index.ts` and then rerun the affected build/tests.

## Frontend merge/rebase hygiene — prevent duplicate type members

Root-cause of a quality failure (March 2026, PR #52 after rebasing with `main`):
- A merge from `main` changed the shared frontend type surface and left `ProductType.imageUrl` declared twice in `src/types/index.ts`.
- ESLint stayed green, but `vue-tsc` in `frontend-ci-cd` correctly failed with `TS2300 Duplicate identifier`.

**When your branch is rebased or merged with `main`:**
1. Re-open `src/types/index.ts` (and any other shared type files touched by the merge) and scan for duplicated properties or merge leftovers before assuming the branch is still green.
2. Re-run the clean frontend pipeline (`npm ci`, `npm run lint`, `npm run test:unit`, `npm run build`) after the merge/rebase, even if the branch was green before.
3. Treat any new CI type-check failure after a merge/rebase as a real regression in the merged branch head and fix that head state directly.

## Frontend merge parity — main can advance between sessions

Root-cause of a recurring CI failure (March 2026, PRs #89):
- A new commit landed on `main` while the branch was in review (`0bd706a` added `src/lib/utils.ts` with `deepEqual(a: any, b: any)`). This violated the ESLint `@typescript-eslint/no-explicit-any` rule.
- The branch appeared locally clean but CI ran the PR against the HEAD of `main` and caught the violation.
- Fixing the lint error by replacing `any` with `unknown` then caused `vue-tsc` to fail because `unknown` does not support array indexing or string key access without explicit type narrowing.

**Correct fix for `unknown`-typed generic deep-equality functions:**
- Use `Array.isArray(a) && Array.isArray(b)` (both must be narrowed) before accessing array indices.
- Cast to `Record<string, unknown>` after confirming the type is a non-array object before accessing string keys.
- Never use `a[i]` or `a[key]` on a value typed as `unknown` — TypeScript will reject it at compile time even when ESLint passes.

**At the start of every session:**
1. Run `git fetch origin main:refs/remotes/origin/main && git merge refs/remotes/origin/main` before ANY local lint/build validation. Note: `git fetch origin main` then `git merge refs/remotes/origin/main` is the reliable two-step approach because `FETCH_HEAD` points to the fetched commit which may be the same as HEAD if the branch is already up-to-date.
2. Run `npm run build` (NOT `npm run build:ssr`) to exercise `vue-tsc` type checking. The `build:ssr` command only invokes Vite's SSR bundler and does NOT run `vue-tsc`, so TypeScript errors are missed. `npm run build` runs `type-check` (vue-tsc --build) AND `build-only` in parallel — both must pass.
3. Use `npm run build` as the canonical type-check step. The `build:client` command does NOT fail on type errors; only `npm run build` (which includes the `type-check` script) catches them.

**When a merge conflict occurs in `utils.ts` or shared utility files:**
- Both branches may have valid changes. If the logic is functionally equivalent, prefer the main version to minimize divergence.
- After resolving conflicts, `git add` the file and run `GIT_EDITOR=true git merge --continue` to complete the merge without opening an editor.
- Always re-run lint + `npm run build` + tests after any merge to confirm no regressions.

## E2E test quality — preventing selector failures

Root-cause of a quality failure (March 2026, PR #48 / global exchange):
- Two E2E tests were pushed with known failures ("ran out of time") because selectors were wrong.
- Test 1: `getByText(/Exchange:/)` matched multiple elements (one per city offer) — strict mode violation.
- Test 2: Used non-existent button text "Edit Layout" (real text: "Edit Building"), wrong section heading "New Configuration" (real: "Planned Upgrade"), and `getByLabel` on labels without `for` attributes.

Root-cause of a CI failure (March 2026, PR #82 / power grid — second attempt):
- Two encyclopedia tests failed in CI with `getByLabel('Language').selectOption('sk')` / `page.locator('#language-select').selectOption('sk')` — the element either resolved to the wrong type or timed out.
- Root cause: interacting with the LanguageSwitcher UI element in the header is unreliable in CI (different build artifacts, stale caches, strict mode issues).
- **Correct fix: use `page.addInitScript(() => localStorage.setItem('app_locale', 'sk'))` BEFORE `page.goto()`.** The i18n module reads `app_locale` from localStorage on startup (see `src/i18n/index.ts:detectLocale()`), so setting it before navigation gives a fully-localized page without any UI interaction.

**How to prevent these failures:**
1. **Never push E2E tests without running them first.** If Playwright tests can't run due to missing browser, install it: `npx playwright install --with-deps chromium`.
2. **Verify all button and heading text against the actual i18n keys.** Check `src/i18n/locales/en.ts` for exact English strings before writing `getByRole('button', { name: '...' })` or `getByRole('heading', { name: '...' })`.
3. **For `getByText` that may match multiple elements, always scope or use `.first()`.** Exchange offer items repeat per city; use `.locator('.exchange-offers-list').getByText(...)` or `.first()`.
4. **Labels without `for` attributes cannot be found with `getByLabel`.** Scope using `.locator('.config-field').filter({ has: page.getByText('Label Text') }).locator('input')` instead.
5. **To test locale/language changes, use `page.addInitScript(() => localStorage.setItem('app_locale', 'sk'))` before `page.goto()`.** Do NOT use `page.locator('#language-select').selectOption(...)` — the UI element can be unreliable across CI build variants.
6. **After placing a unit via the picker (`placeUnit`), `selectedCell` is reset to null.** The cell must be clicked again before the config panel is visible.
7. **Always run the targeted spec before `report_progress` with `CI=true`:** `CI=true npx playwright test --project=chromium e2e/<spec>.ts`. Only then run the full suite. Running without `CI=true` uses dev server which may behave differently from the production build used in CI.
8. **When you replace an existing UI workflow (for example inline selector to full-page dialog, or removing a field like `Lock to Vendor`), update every existing Playwright assertion that references the old UI in the same session.** Do not leave legacy expectations in the suite.
9. **Add a regression test for the new workflow itself, not just the old test rewritten.** Example: if purchase configuration moves to a full-page selector, add a test that opens the selector, chooses the item/vendor, saves, and verifies the persisted state.
10. **Do not assert on dialog fields after the dialog is closed.** If the user clicks `Done` in a full-page selector, assert against the persisted summary/state in the sidebar instead of a removed search input or closed overlay element.
11. **For mini-chart UIs, assert rendered data presence rather than per-bar visibility heuristics.** Prefer checking that the chart label is visible and that bars exist (count/title/style), because narrow bar divs inside compact charts may be reported as `hidden` by Playwright even when the chart rendered correctly.

## Minimal-change PR quality — prove the gap, don't just fix the symptom

Root-cause of a quality failure (March 2026, PR #63 onboarding routing fix):
- The actual fix was correct (route "Get Started" to /onboarding instead of /login) and all tests passed.
- However, the PR did not include E2E tests that explicitly exercised the new routing path from the home page (starting at `/`, clicking "Get Started", landing on `/onboarding` as an unauthenticated guest).
- The product owner saw a minimal diff without proof of the guest journey working end-to-end, and concluded the implementation was incomplete.

**When fixing a single gap in a fully-implemented feature:**
1. **Always add a test that specifically exercises the fixed behavior.** If you route "Get Started" to `/onboarding`, add an E2E test that starts at `/`, clicks "Get Started", and verifies `/onboarding` is reached without auth.
2. **Add tests that prove the end-to-end user journey works from the entry point you changed.** The home page CTA test should continue through the onboarding wizard steps, not just assert the URL.
3. **Include at least one golden-path E2E test that covers the full flow affected by the fix.** For this routing fix: home → guest onboarding → register → empire launched.
4. **Explain in the PR description what the gap was and how the test proves it is fixed.** Link to the acceptance criterion it satisfies.
5. **Do not consider a routing-only fix "done" without E2E proof.** Routing changes are easy to regress; tests are the safety net.

## Leaderboard / multi-query UI resilience — do not blank a healthy tab

Root-cause of a quality gap (April 2026, PR #239 / leaderboard split):
- `LeaderboardView` requested `rankings` and `companyRankings` in one combined GraphQL query.
- If `companyRankings` failed (for example due to backend/schema mismatch or mocked API drift), the whole page fell into a global error state and even the working player-rankings tab showed "Failed to fetch".
- Existing E2E coverage only exercised the player tab happy path, so the company tab and partial-failure behavior were not proven.

**Rules to prevent recurrence:**
1. **When a page has multiple independently-usable tabs backed by different GraphQL fields, fetch them independently.** A failure in one tab must not blank the content of another healthy tab.
2. **Do not use a single page-level error/loading state for independent tab datasets.** Keep per-tab loading/error state so the active working tab can still render.
3. **When adding a new GraphQL field to a page that already works, add E2E coverage for both the new happy path and a partial-failure fallback.** For leaderboard, that means proving the companies tab renders and proving players still render when `companyRankings` fails.
4. **Update the shared mock API helper for every new query field used by shipped UI.** Do not rely on partial mock payloads when the real page depends on the new field.

## PR draft state and CI triggering — do not leave PRs in draft

Root-cause of a quality failure (March 2026, PR #76 guest onboarding):
- The PR was opened in draft state. Because it was draft, CI workflows did not trigger, so the product owner saw "no checks reported."
- The agent had passing tests locally but the PR description only reflected the initial plan with one small backend test addition — not the full completed scope.
- Product owner rejected the PR with: "no reported CI checks," "missing proof of completed implementation," "missing automated coverage."

**How to prevent this:**
1. **Never leave a PR in draft state when the implementation is complete.** A PR should be marked "ready for review" as part of delivery, not left for someone else to un-draft.
2. **Pushing a non-empty commit triggers CI.** If CI is not running, verify the branch has pushed commits. Every `report_progress` call pushes, so CI should trigger automatically after the first code commit.
3. **If CI fails with infrastructure errors (e.g., Docker registry "Username and password required"), that is not a code failure** — it is a secrets/credentials issue in the repository settings. Focus on fixing code failures; infrastructure credential failures are the repository owner's responsibility.
4. **The PR description must explicitly link to the issue it resolves** using GitHub's `Fixes #N` or `Closes #N` syntax so reviewers can trace the PR back to the product requirement.
5. **Always demonstrate the full scope of delivery in the PR description**, not just the last incremental change. Reviewers need to see what was already on main vs what this branch contributes — make both clear.
6. **Respond to product-owner review comments by adding concrete proof** (test names, passing counts, screenshots) — never by just asserting "it works."

## Guest onboarding temporary-state guarantee — clearProgress after migration

Root-cause of a bug (March 2026, PR #76 guest onboarding follow-up):
- `saveGuestProgress()` called `finishOnboarding` successfully, set `completionResult.value`, and showed the authenticated completion screen.
- But no `clearProgress()` was called after the successful migration, so the reactive `saveProgress()` watch re-wrote the stale guest choices (step=5, lots, industry, city, etc.) back into localStorage.
- A test asserting `localStorage.getItem('onboarding_progress') === null` after migration correctly caught this bug.

**Rule: always call `clearProgress()` after a successful guest-to-authenticated migration** so the localStorage sandbox state is cleaned up. The watch-based `saveProgress()` will still fire on reactive updates, but the key insight is that after `clearProgress()` the authenticated code path will NOT re-write guest state because `isGuestMode` becomes false (the player is now authenticated).

## Guest onboarding quality — test all industries end-to-end, check MaxPrice vs exchange price

Root-cause of a silent production bug (March 2026, PR #93):
- `ConfigureStarterFactory` set `MaxPrice = product.BasePrice` on the raw-material purchase unit.
- For Food Processing (Bread), `BasePrice = 3m` but Grain's global exchange price in Bratislava is `~6m`.
- The purchasing phase silently skips the exchange when `exchangePrice > MaxPrice`, so the factory could never buy any Grain. The entire Food Processing supply chain was permanently broken at launch.
- This was not caught because supply chain end-to-end tests only covered Furniture. Healthcare and Furniture were unaffected because their product base prices exceeded their raw material exchange prices.

**Rules to prevent recurrence:**
1. **When setting `MaxPrice` on any purchase unit, compare it against the actual global exchange price for that resource in the target city.** Do NOT use `product.BasePrice` as a purchase cap unless you have verified it exceeds the exchange price for that industry's raw material.
2. **Always run supply chain tick tests for ALL starter industries, not just Furniture.** After calling `FinishOnboarding`, process at least 4 ticks and assert that shop purchase unit inventory fills and `PublicSalesRecords` are created for every seeded industry (FURNITURE, FOOD_PROCESSING, HEALTHCARE).
3. **When a feature covers multiple industries, add backend integration tests for each industry's full tick cycle** — not just a single "all three industries produce valid state" happy-path test.
4. **null MaxPrice means no cap (decimal.MaxValue in the purchasing phase)** — this is the safe default for starter factory purchase units and allows the unit to buy at market rate regardless of the product price.

## Starter industry card UX quality — ROADMAP requires fantasy, first product, and why-choose

Root-cause of a UX gap (March 2026, PR #93):
- Industry card descriptions were a single bland sentence: "Craft wooden furniture from harvested timber."
- The ROADMAP explicitly requires: "Each option should explain the fantasy, likely first product, and why a player might choose it."
- No tests verified the content quality of the industry cards.

**Rules to prevent recurrence:**
1. **When implementing any wizard selection step (industry, city, product), match the ROADMAP's content requirements exactly.** For industry cards this means: fantasy description, first product name + price, and a "why choose" tagline.
2. **Industry descriptions must use i18n keys** (not hardcoded English strings in the component) so all three locales (en, sk, de) are kept in sync.
3. **Add E2E tests that assert the specific content values** (product names, key description words, why tags) are visible on the cards — not just that the cards are rendered.
4. **The `.card-first-product` badge is the canonical selector** for industry first-product hints; `.card-why` is the canonical selector for the tagline. Tests must use these selectors when verifying card content.

## "Already on main" quality mandate — investigating before claiming done

Root-cause of a quality failure (March 2026, PR #93 initial session):
- The branch diff vs main was empty. The agent found the onboarding wizard already implemented and reported it as complete without investigating bugs or gaps.
- A silent supply chain bug (MaxPrice blocking Food Processing) and poor UX copy (1-sentence industry descriptions not matching ROADMAP) both went undetected.

**When the diff is empty and the feature appears "already implemented":**
1. Run the full supply chain end-to-end for **every variant** defined in the ROADMAP, not just the first happy path.
2. Compare every visible UI string against the ROADMAP's content requirements for that screen. Short descriptions that don't explain fantasy/product/why are UX gaps.
3. Run `dotnet test` with a filter targeting the specific feature area to find failures the happy-path tests mask.
4. Screenshot every wizard step and compare against the ROADMAP description before declaring done.
5. A feature is only "done" when ALL of: CI passes, product copy matches ROADMAP, all industries work end-to-end, and tests cover every defined variant.

## TypeScript build — always use `npm run build` not `npm run build:ssr` for type checking

Root-cause of a CI failure (March 2026, PR #115 global exchange):
- `GlobalExchangeView.vue` had `cities[0].id` on a `City[]` array — TypeScript's `noUncheckedIndexedAccess` or strict mode flags this as `Object is possibly 'undefined'` even when preceded by a `.length > 0` check.
- The agent ran `npm run build:ssr` locally and saw it pass. `build:ssr` only invokes Vite SSR bundling — it does NOT run `vue-tsc`. The TypeScript error was only caught by `vue-tsc --build` which runs as `type-check` inside `npm run build`.
- `frontend-ci-cd` failed with: `error TS2532: Object is possibly 'undefined'`.

**Rules to prevent recurrence:**
1. **Always use `npm run build` as the canonical local type-check step.** Never rely on `build:ssr` or `build:client` alone — neither runs `vue-tsc`.
2. **When accessing an array by index (`arr[0]`), always narrow to a variable first:** `const first = arr[0]; if (first) { ... }`. TypeScript strict mode does not consider `.length > 0` sufficient to narrow array subscript access.
3. **The correct CI-equivalent command is: `cd projects/frontend && npm ci && npm run lint && npm run test:unit && npm run build`** — where `npm run build` bundles BOTH type-check and build-only together. All three must exit 0 before pushing.

## CI infrastructure failures vs code failures — always distinguish before reporting

Root-cause of a quality failure (March 2026, PR #107):
- The product owner asked "Fix build and fix tests" after seeing `frontend-ci-cd` failures on `main`.
- The failures were Docker registry credential errors (`Username and password required` on `docker/login-action`) — a repository secrets/infrastructure issue, not a code failure.
- The agent had already confirmed all code tests pass (235 backend, 494 unit, 238 E2E) but did not explicitly distinguish infra failures from code failures in its reply, causing continued concern.

**Rules to prevent recurrence:**
1. **When CI is failing, always check the job logs first.** If the failure is in a Docker push/login step and says "Username and password required", it is an infrastructure credentials issue — not a code failure. Report it explicitly as such.
2. **Before claiming "CI passes", list exactly which workflows pass and which fail, and why each failure category is not a code regression.**
3. **Infrastructure CI failures (Docker credentials, registry unavailable, missing secrets) are the repository owner's responsibility.** Do NOT try to fix them by changing code. Report them clearly and proceed to address any code-level gaps.
4. **When a PR has a small diff (e.g., only regression tests), explicitly prove the broader implementation is working:** run all test suites locally, provide test counts, and include screenshots of the live UI flow.
5. **The "Addressing comment on PR" agent run always has CI triggered for the branch.** If the branch CI passes but `main` CI fails, that is a main-branch infrastructure issue and not related to the PR.

## Test coverage quality — all industries must be covered at every layer

Root-cause of a gap (March 2026, PR #107):
- The configure-guide benchmark price test (asserting "$45" for Furniture) existed, but identical coverage for Food Processing ($3) and Healthcare ($50) was missing.
- This left a regression vector where the `configureGuideBasePrice` computed property or the `FinishOnboarding` GraphQL result could silently stop returning `basePrice` for non-Furniture industries without any test catching it.

**Rules to prevent recurrence:**
1. **When adding a test for one industry variant, always add equivalent tests for all other starter industries (FURNITURE, FOOD_PROCESSING, HEALTHCARE).** Do not stop at the first happy-path industry.
2. **When the configure-guide or wizard teaches a price/margin concept, assert the concrete numeric value for each industry** — not just that "some price" is shown.
3. **Backend `FinishOnboarding` result must include `selectedProduct.basePrice`** so the frontend configure-guide can show the industry-specific benchmark. Test this with a dedicated backend test covering all 3 industries.
4. **For any ROADMAP teaching moment** (price configuration, tick explanation, cash display), add both a backend test validating the data is returned and an E2E test validating the data is displayed.

## Encyclopedia / discovery-layer quality — cover all industry chains end-to-end

Root-cause of a quality failure (March 2026, PR #109):
- The manufacturing encyclopedia feature was already implemented on `main`. The agent verified all 240 E2E tests and 236 backend tests passed but did not add missing cross-industry coverage.
- Existing backend tests covered only Wood (Furniture) and Silicon (Electronics) chains. No tests for Grain→Bread (Food Processing) or Chemical Minerals→Basic Medicine (Healthcare) chains existed.
- Existing E2E tests covered only the Wood/Furniture chain for the "discovery journey" and lacked Food Processing and Healthcare journey tests and mobile viewport coverage.
- The ROADMAP explicitly requires ALL production combinations to be visible. "All combinations" means every industry chain, not just the first one tested.

**Rules to prevent recurrence:**
1. **For any encyclopedia, catalog, or discovery feature: add backend and E2E tests for EVERY industry chain**, not just the first one you verify. Specifically for the starter industries: FURNITURE (Wood→Wooden Chair), FOOD_PROCESSING (Grain→Bread), and HEALTHCARE (Chemical Minerals→Basic Medicine).
2. **Always add an E2E test for mobile viewport** (375px wide) when the ROADMAP or issue specifies that mobile/tablet layouts must be supported. The test should navigate to the key screen, interact with search/filter, and confirm relationship data is visible without horizontal overflow.
3. **When a backend test uses `slug: "wood"` as the only example, it is not sufficient for ROADMAP alignment.** Add parallel tests for `grain` and `chemical-minerals` slugs to prove all starter industry chains are queryable.
4. **The quality bar for "done" on a discovery feature is**: all starter industry chains are navigable (resource detail → downstream product detail) in both E2E and backend tests, mobile viewport is covered, and all 8 resource slugs are verified as present.

## Exchange / market feature quality — cross-industry tick-engine coverage required

Root-cause of a quality gap (March 2026, PR #115 city global exchange):
- The exchange feature was already implemented on `main`. The initial agent session pushed only an "Initial plan" commit with NO code changes.
- The agent correctly identified the implementation existed but failed to add meaningful new coverage: per-industry tick-engine tests for Grain (Food Processing) and Chemical Minerals (Healthcare) exchange purchasing were missing.
- Backend tests only verified Wood purchasing from exchange; the other two starter industries had no tick-engine exchange tests.
- E2E tests lacked: authenticated post-onboarding player discovery, error state visibility, abundance percentage display, quality-vs-abundance correlation, and best-offer selection by delivered price.

**Rules to prevent recurrence:**
1. **For any market or sourcing feature, add tick-engine tests for ALL three starter industries** (Wood/Furniture, Grain/Food Processing, Chemical Minerals/Healthcare). A single `resourceSlug: "wood"` test does not prove Grain or ChemMinerals work.
2. **Always add E2E tests for**: authenticated player discovery (post-onboarding access), error state when API fails, data quality display (abundance/quality percentages), and city-level differentiation proofs.
3. **Verify all 8 seed resource slugs appear in exchange listings** via a dedicated backend test — not just the 3 starter industry inputs.
4. **Exchange quality must be tested against seed abundance data** — higher abundance resources (Wood at 0.7) must produce higher quality than lower abundance resources (ChemMinerals at 0.3) in the same city.
5. **When the diff vs main is empty, do not report "done".** Instead, run targeted tests by feature area to find gaps, add tests for every missing variant, and only report done after new tests are committed and pass.

## Guided wizard — always show auto-configured layouts to the player

Root-cause of a ROADMAP alignment gap (March 2026, PR #125 guest onboarding):
- The onboarding wizard successfully auto-configured the factory layout on the backend via `ConfigureStarterFactory` (PURCHASE → MANUFACTURING → STORAGE → B2BSales) and the shop via `AddStarterShop` (PURCHASE → PUBLIC_SALES).
- But the completion screen (step 5) only showed the factory and shop names — it did NOT show the configured unit layout to the player.
- The ROADMAP explicitly says: "This will set the factory layout for them. Wizard will show them important areas on the screen."
- Fix: updated the `finishOnboarding` GraphQL query to request `units { id unitType gridX gridY level linkRight }` from both factory and salesShop, then added a `factory-layout-panel` with a `unit-chain` visual display showing each unit type with an icon and an arrow between them.

**Rules to prevent recurrence:**
1. **When a wizard step says it will "set" or "configure" something, the wizard must also SHOW what was configured** — not just confirm it happened. "Factory configured and ready to produce" is insufficient; the player needs to see the unit chain.
2. **Read ROADMAP phrases like "Wizard will show them important areas on the screen" as concrete UI requirements**, not just aspirational copy. They specify that the wizard must display the configured layout before the player is sent elsewhere.
3. **After any auto-configuration step (factory setup, shop setup, etc.), add the configured layout to the completion/summary screen.** For building units: show the unit type chain with icons and arrows sorted by gridX position.
4. **GraphQL queries for completion results must request enough data to display what was configured.** If the backend configures units, the mutation result must include `units { id unitType gridX gridY level linkRight }` so the frontend can render the chain.
5. **Add E2E tests that assert the configured unit types are VISIBLE on the completion screen** — `expect(page.locator('[aria-label="Factory layout"] .unit-chain-label', { hasText: 'Manufacturing' })).toBeVisible()` — not just that "factory was set up" text is present.
6. **Add backend tests that request units in the mutation response** and verify the count and types are correct for each supported industry.

## Ledger page quality — prevent flickering on tick updates

Root-cause of flickering (current issue):
- `useTickRefresh` calls `fetchLedger()` which sets `loading.value = true`, causing the loading spinner to appear briefly on every tick update, leading to app flickering.
- The loading state is intended for initial load, not for background refreshes.

**Rules to prevent recurrence:**
1. **When using `useTickRefresh`, modify fetch functions to accept an `isRefresh` parameter.** Set `loading` only when `!isRefresh`.
2. **Call fetch functions with `isRefresh: true` in `useTickRefresh` callbacks** to avoid showing loading states during automatic refreshes.
3. **Ensure ledger data is tax-year scoped and drill-downs align with the selected year.** Use `ledgerDrillDown(companyId, category, gameYear?)` with the same `gameYear` as the ledger query.
4. **Add drill-downs for all major statement items as per ROADMAP:** revenue (sales items), costs (purchases, labor, energy, marketing), assets (building list), etc.
5. **For ledger improvements, always verify against ROADMAP requirements:** income statement, cash flow, balance sheet, drillable details, tax info, history.

## City coverage — always test all three seeded cities

Root-cause of a gap (April 2026, PR #151 onboarding sandbox):
- The full onboarding flow was implemented and tested for all three starter industries (Furniture, Food Processing, Healthcare) but only for the default city (Bratislava).
- Prague was covered by one E2E test. Vienna (the third seeded city) had no dedicated E2E test and no backend integration test.
- The ROADMAP states "The game will start in single city and later other cities will be added" — all three seeded cities must be exercisable from day one.

**Rules to prevent recurrence:**
1. **When adding backend onboarding tests that use `GetCityIdByNameAsync()`, add at least one variant that uses Vienna** to prove the third city path works end-to-end.
2. **For any feature that uses city selection (onboarding, lot purchase, exchange), add at least one E2E test that explicitly selects Vienna** — not just Bratislava or Prague.
3. **`makeDefaultCities()` already includes Vienna (`city-vi`)**. When extending lot fixtures for city-specific tests, always add Vienna lots alongside Prague lots so the wizard is fully exercisable for all three cities.
4. **Asserting `.city-card` is rendered for all three cities on step 2** is a minimal sanity check that must exist as its own test, not just as a side effect of a longer flow test.

## Previous session quality failure — "already done" anti-pattern with no added value

Root-cause of a quality failure (April 2026, PR #151):
- The branch diff vs main was a single "Initial plan" commit with no code changes beyond cleanup.
- The agent verified all tests passed and declared the feature "already done" without investigating what improvements could still be made.
- This violated the "Empty-PR quality failure" and "already on main quality mandate" lessons documented above.
- The product owner's comment "increase test coverage" was a signal that the existing tests were insufficient — not that they were broken.

**When a PR comment says "increase test coverage":**
1. **Treat it as a concrete gap, not a vague ask.** Identify which variants, cities, industries, or edge cases are not covered, then add them.
2. **Run a gap analysis**: for each seeded entity (city, industry, product), check if there is a dedicated backend test AND a dedicated E2E test. If not, add them.
3. **Always add tests for the third option** (Vienna as the third city, Healthcare as the third industry, etc.) since tests tend to cover the first two and miss the third.
4. **Do not stop at confirming existing tests pass.** The ask is for more tests, not for confirmation that the current ones work.
5. **Commit the new tests before replying** — the reply should reference the commit hash and list what was specifically added.

## Master portal mock-api — gameServers query contains "me" as substring

Root-cause of a quality failure (April 2026, PR #180 master portal):
- The master-frontend E2E mock-api helper intercepted `me` queries by checking `query.includes('me')`.
- The GraphQL query for game servers is named `gameServers` — which contains "me" as a substring (ga**me**Servers).
- This caused the `me` handler to fire instead of the `gameServers` handler, returning an auth error and showing `"Not authenticated."` as the server list error.
- This is the same root-cause pattern documented in the game-frontend memory about `isStandaloneMeQuery`, but in the master-frontend mock it wasn't guarded.
- All 11 server-list E2E tests failed silently until the debug approach of printing `state-message` content revealed the error.

**Rules to prevent recurrence:**
1. **In any `me` query handler in a mock-api helper, ALWAYS exclude other query names that contain "me" as a substring.** The guard must be:
   ```ts
   if (query.includes('me') && !query.includes('gameServers') && !query.includes('mySubscription') && !query.includes('prolongSubscription'))
   ```
2. **After writing a new mock-api helper, verify it with a debug test that prints the `.state-message` content** to confirm the correct handler is firing, not a false-positive substring match.
3. **GraphQL field/query names containing "me" as a substring** (e.g., `gameServers`, `mySubscription`, `performance`, `schema`, `comment`, `rename`) must be explicitly excluded from the `me` query handler.
4. **Apply the same pattern as the game-frontend `isStandaloneMeQuery()` helper** to master-frontend and any future portal mock-api helpers.

## EF Core Cartesian explosion — always use AsSplitQuery for multiple collection Includes

Root-cause of a CI failure (April 2026, PR #233 public-sales analytics):
- `TickProcessor.BuildContextAsync` loaded buildings with three collection Includes:
  `.Include(b => b.Units).Include(b => b.PendingConfiguration).ThenInclude(p => p.Units).Include(b => b.PendingConfiguration).ThenInclude(p => p.Removals)`
- Without `AsSplitQuery()`, EF Core generates a single SQL with Cartesian product: `BuildingUnits × PlanUnits × PlanRemovals`. Each `BuildingUnit` appears multiple times in the result.
- EF Core's identity map *should* deduplicate, but fails in certain SQLite + multiple-collection-Include combinations. The navigation property `b.Units` received duplicate entries (same `(GridX, GridY)` position) at runtime.
- The same issue in `BuildingConfigurationService.ApplyDuePlansAsync`: `plan.Building.Units` could appear empty or duplicated, causing `ApplyDuePlansAsync` to insert **new** `BuildingUnit` rows at positions already occupied, corrupting the database state for all subsequent tick engine calls.
- CI failures were ordering-dependent: tests that called `StoreBuildingConfiguration` (leaving a pending plan) caused the Cartesian explosion in any later test that called `ProcessTicksAsync`. Locally, test ordering happened to avoid this; CI ordering exposed it consistently.

**Rules to prevent recurrence:**
1. **Any EF Core query with two or more `Include`/`ThenInclude` paths that each traverse a collection navigation property MUST use `.AsSplitQuery()`.** EF Core warns about Cartesian explosion for a reason — the warning should be treated as a required fix, not an advisory.
2. **The canonical pattern for multi-collection loading:**
   ```csharp
   var buildings = await db.Buildings
       .Include(b => b.Units)
       .Include(b => b.PendingConfiguration)!.ThenInclude(p => p!.Units)
       .Include(b => b.PendingConfiguration)!.ThenInclude(p => p!.Removals)
       .AsSplitQuery()
       .ToListAsync(ct);
   ```
3. **After applying `AsSplitQuery()`, the query is split into separate round-trips per Include path.** This avoids the Cartesian product and guarantees navigation collections are populated correctly.
4. **CI ordering vs local ordering is not deterministic.** If tests share a database (via `IClassFixture`), any test that creates an entity with pending child collections (e.g., `BuildingConfigurationPlan`) but does NOT apply/delete it leaves state that can corrupt subsequent tests. Both the query fix (`AsSplitQuery`) AND the test isolation matter.
5. **When CI fails with "An item with the same key has already been added" in a `ToDictionary` on a navigation collection, immediately suspect a Cartesian explosion in an EF Core Include chain.** Add `AsSplitQuery()` and re-run; do not try to work around with `DistinctBy` as that would hide the underlying data corruption.

## Tick-refresh flicker prevention — always verify selectors against actual i18n labels before pushing E2E tests

Root-cause of a CI failure (April 2026, PR #261 flicker prevention):
- A new E2E test asserted `getByRole('button', { name: 'Save' })` after entering building edit mode.
- The actual button text is **"Store Upgrade"** (`t('buildingDetail.storeConfiguration')`), not "Save".
- The test passed lint and `npm run build` locally (since these don't execute Playwright), but failed in CI on every retry.

**Rules to prevent recurrence:**
1. **Always cross-check every `getByRole('button', { name: '...' })` against the actual i18n keys in `src/i18n/locales/en.ts` before writing the assertion.** Do not guess button labels; look them up.
2. **When verifying "edit mode is active", prefer the always-visible indicator.** For BuildingDetailView's planning section: the `getByRole('button', { name: 'Cancel Editing' })` and `getByRole('heading', { name: 'Planned Upgrade' })` are always visible when `isEditing = true`, whereas "Store Upgrade" is disabled when there are no draft changes (though still visible).
3. **Run `CI=true npx playwright test --project=chromium e2e/<spec>.ts` with the production build** to catch selector mismatches before `report_progress`. Running without `CI=true` uses the dev server which may behave differently.

## Mock-API `rankings` vs `companyRankings` substring collision

Root-cause of a latent mock bug (April 2026, PR #261):
- The mock-api `rankings` handler used `query.includes('rankings')` which also matched `companyRankings` queries because `'companyRankings'.includes('rankings') === true`.
- This caused all `companyRankings` GraphQL requests to be handled by the `rankings` handler which returned `{ data: { rankings: [...] } }` — the company rankings tab silently received wrong data.
- The existing company-rankings E2E test appeared to pass because a second `page.route()` with higher priority intercepted the request before the mock-api handler.

**Rules to prevent recurrence:**
1. **Whenever a query name contains another query name as a substring, the longer name MUST be excluded from the shorter check:** `query.includes('rankings') && !query.includes('companyRankings')`.
2. **Follow the same guard pattern as `isStandaloneMeQuery`** (already documented above for `me`/`gameServers`). Apply it to any pair where one query name contains another as a substring.
3. **After adding a new GraphQL query handler to mock-api, check whether the query name is a substring of any other query name** already in the mock. If so, add the exclusion guard immediately.
4. **Known pairs requiring exclusion guards:** `rankings` must exclude `companyRankings`; `me` must exclude `gameServers`, `mySubscription`, `prolongSubscription`; `rank` must exclude `rankings`; `company` must exclude `companyRankings`.
