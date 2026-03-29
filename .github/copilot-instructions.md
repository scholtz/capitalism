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
  - **Mutations**: `register(input)`, `login(input)`, `createCompany(input)`, `placeBuilding(input)`, `completeOnboarding(input)`, `startOnboardingCompany(input)`, `finishOnboarding(input)`, `purchaseLot(input)`
- The staged onboarding flow now uses `startOnboardingCompany` to create the first company and purchase the first factory lot, then `finishOnboarding` to select the starter product, purchase the first sales shop lot, configure both buildings, and complete onboarding.

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
- **NEVER push with known failing tests.** If a test fails because of your change, you MUST fix it before pushing — even if the test appears "pre-existing" or "unrelated". An existing test that breaks under your new validation is evidence that the test data was invalid under the new rule; fix the test data, not the validation.
- For frontend changes that affect shipped UI, also run the workflow-equivalent frontend checks:
  - `cd projects/frontend && npm ci && npm run lint && npm run test:unit && npm run build`
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
- `useTickCountdown` composable (`src/composables/useTickCountdown.ts`) is the single source of truth for next-tick countdown display. Do not inline countdown logic in views.
- `computeCountdownTimeStr(remainingMs)` is an exported pure helper from `useTickCountdown.ts` — use it when you need the raw time string, and test it in `src/composables/__tests__/useTickCountdown.test.ts`.
- `PendingActionsTimeline` component (`src/components/dashboard/PendingActionsTimeline.vue`) renders the player's scheduled building upgrades. It accepts `pendingActions: ScheduledActionSummary[]` and `loading: boolean` props.
- `DashboardView` fetches `gameState` and `myPendingActions` in parallel on mount. The tick clock widget and timeline are both driven by these responses.
- Backend `myPendingActions` query (authenticated) returns `ScheduledActionSummary[]` ordered by `appliesAtTick` asc. Fields: `id`, `actionType`, `buildingId`, `buildingName`, `buildingType`, `submittedAtUtc`, `submittedAtTick`, `appliesAtTick`, `ticksRemaining`, `totalTicksRequired`.
- When testing `myPendingActions` in E2E, set `state.pendingActions` in the mock-api helper before `page.goto()`.

## City map conventions
- `CityMapView.vue` lives at route `/city/:id`. It uses the `leaflet` package with OpenStreetMap tiles and `L.divIcon` for color-coded lot markers (green = available, blue = yours, gray = other owner).
- `BuildingLot` entity (`projects/Api/Data/Entities/BuildingLot.cs`) stores purchasable locations with `Latitude`, `Longitude`, `District`, `Price`, and comma-separated `SuitableTypes`. 14 lots are seeded for Bratislava in `AppDbInitializer.cs`.
- GraphQL: `cityLots(cityId)` is a **public query** (no auth required) so unauthenticated visitors can browse. `lot(id)` is also public. `purchaseLot` mutation requires auth and uses optimistic concurrency via `ConcurrencyToken`.
- A building placed via `purchaseLot` inherits the lot's `Latitude`/`Longitude` exactly.
- All city map UI strings use the `cityMap.*` i18n namespace, with district names under `cityMap.districts.*`. All three locales (en/sk/de) must have these keys.
- `makeDefaultBuildingLots()` factory in `e2e/helpers/mock-api.ts` provides 4 mock lots (2 factory, 1 commercial, 1 residential) for E2E tests. Use `state.buildingLots` to customize lot ownership in tests.
- When adding new city map E2E tests, always include `city-map.spec.ts` in the run; it is the canonical spec for the `/city/:id` route.
- Bratislava coordinates (for validation): lat 47.8–48.4°N, lon 16.8–17.5°E.
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
