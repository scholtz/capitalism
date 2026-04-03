import type { Page } from '@playwright/test'

export interface MockGameServer {
  id: string
  serverKey: string
  displayName: string
  description: string
  region: string
  environment: string
  backendUrl: string
  graphqlUrl: string
  frontendUrl: string
  version: string
  playerCount: number
  companyCount: number
  currentTick: number
  registeredAtUtc: string
  lastHeartbeatAtUtc: string
  isOnline: boolean
}

export interface MockSubscription {
  tier: 'FREE' | 'PRO'
  status: 'NONE' | 'ACTIVE' | 'EXPIRED'
  isActive: boolean
  daysRemaining: number | null
  canProlong: boolean
  expiresAtUtc: string | null
  startsAtUtc: string | null
}

export interface MockPlayer {
  id: string
  email: string
  displayName: string
  createdAtUtc: string
}

export interface MockState {
  servers: MockGameServer[]
  currentToken: string | null
  currentPlayer: MockPlayer | null
  subscription: MockSubscription | null
}

export function makeServer(overrides: Partial<MockGameServer> = {}): MockGameServer {
  return {
    id: 'server-001',
    serverKey: 'capitalism-eu-1',
    displayName: 'Capitalism EU #1',
    description: 'First production economy for EU players',
    region: 'EU',
    environment: 'production',
    backendUrl: 'https://game.example.com',
    graphqlUrl: 'https://game.example.com/graphql',
    frontendUrl: 'https://game.example.com/app',
    version: '1.0.0',
    playerCount: 42,
    companyCount: 128,
    currentTick: 5000,
    registeredAtUtc: '2026-04-01T00:00:00.000Z',
    lastHeartbeatAtUtc: new Date().toISOString(),
    isOnline: true,
    ...overrides,
  }
}

export function makePlayer(overrides: Partial<MockPlayer> = {}): MockPlayer {
  return {
    id: 'player-001',
    email: 'alice@example.com',
    displayName: 'Alice',
    createdAtUtc: '2026-01-01T00:00:00.000Z',
    ...overrides,
  }
}

export function makeSubscription(overrides: Partial<MockSubscription> = {}): MockSubscription {
  const expiresAtUtc = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString()
  return {
    tier: 'PRO',
    status: 'ACTIVE',
    isActive: true,
    daysRemaining: 30,
    canProlong: true,
    expiresAtUtc,
    startsAtUtc: '2026-04-01T00:00:00.000Z',
    ...overrides,
  }
}

export function setupMockApi(page: Page, initialState: Partial<MockState> = {}): MockState {
  const state: MockState = {
    servers: initialState.servers ?? [],
    currentToken: initialState.currentToken ?? null,
    currentPlayer: initialState.currentPlayer ?? null,
    subscription: initialState.subscription ?? null,
  }

  page.route('**/graphql', async (route) => {
    const body = route.request().postDataJSON() as { query: string; variables?: unknown }
    const query = body.query ?? ''

    // Register mutation
    if (query.includes('mutation') && query.includes('register')) {
      const vars = body.variables as { input: { email: string; displayName: string } }
      const player: MockPlayer = {
        id: 'new-player-001',
        email: vars?.input?.email ?? 'test@example.com',
        displayName: vars?.input?.displayName ?? 'Test Player',
        createdAtUtc: new Date().toISOString(),
      }
      state.currentPlayer = player
      state.currentToken = 'mock-token-abc'
      state.subscription = { tier: 'FREE', status: 'NONE', isActive: false, daysRemaining: null, canProlong: true, expiresAtUtc: null, startsAtUtc: null }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            register: {
              token: state.currentToken,
              expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
              player,
            },
          },
        }),
      })
      return
    }

    // Login mutation
    if (query.includes('mutation') && query.includes('login')) {
      if (state.currentPlayer) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            data: {
              login: {
                token: state.currentToken ?? 'mock-token-abc',
                expiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
                player: state.currentPlayer,
              },
            },
          }),
        })
      } else {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            errors: [{ message: 'Invalid email or password.', extensions: { code: 'INVALID_CREDENTIALS' } }],
          }),
        })
      }
      return
    }

    // ProlongSubscription mutation
    if (query.includes('mutation') && query.includes('prolongSubscription')) {
      const newSub: MockSubscription = {
        tier: 'PRO',
        status: 'ACTIVE',
        isActive: true,
        daysRemaining: 30,
        canProlong: true,
        expiresAtUtc: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString(),
        startsAtUtc: new Date().toISOString(),
      }
      state.subscription = newSub
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { prolongSubscription: newSub } }),
      })
      return
    }

    // Me query — must not match gameServers, mySubscription, or prolongSubscription
    if (query.includes('me') && !query.includes('gameServers') && !query.includes('mySubscription') && !query.includes('prolongSubscription')) {
      if (state.currentPlayer) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ data: { me: state.currentPlayer } }),
        })
      } else {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ errors: [{ message: 'Not authenticated.', extensions: { code: 'AUTH_NOT_AUTHENTICATED' } }] }),
        })
      }
      return
    }

    // MySubscription query
    if (query.includes('mySubscription')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          data: {
            mySubscription: state.subscription ?? { tier: 'FREE', status: 'NONE', isActive: false, daysRemaining: null, canProlong: true, expiresAtUtc: null, startsAtUtc: null },
          },
        }),
      })
      return
    }

    // GameServers query
    if (query.includes('gameServers')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ data: { gameServers: state.servers } }),
      })
      return
    }

    // Fallback: pass through or return empty
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ data: {} }),
    })
  })

  return state
}

export async function loginAs(
  page: Page,
  state: MockState,
  player: MockPlayer,
  token = 'mock-token-abc',
) {
  state.currentPlayer = player
  state.currentToken = token
  await page.addInitScript(
    ({ tok, exp }) => {
      localStorage.setItem('master_auth_token', tok)
      localStorage.setItem('master_auth_expires', exp)
    },
    { tok: token, exp: new Date(Date.now() + 7200000).toISOString() },
  )
}
