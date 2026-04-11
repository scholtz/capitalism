/**
 * Master Portal layout API client.
 *
 * Provides authenticated access to the BuildingLayoutTemplate endpoints on the
 * Master API using the shared Capitalism session token. Falls back silently to
 * localStorage when no authenticated session is active so the building editor
 * always has a working save/load path.
 */

const MASTER_GRAPHQL_URL =
  import.meta.env.VITE_MASTER_GRAPHQL_URL || 'http://localhost:44364/graphql'

const MASTER_TOKEN_KEY = 'auth_token'
const MASTER_EXPIRES_KEY = 'auth_expires'

// ── Local-storage layout key (fallback) ──────────────────────────────────────
const LOCAL_LAYOUT_KEY = 'capitalism_building_layouts'

// ── Types ────────────────────────────────────────────────────────────────────

export interface LayoutUnit {
  unitType: string
  gridX: number
  gridY: number
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
  lockedCityId?: string | null
}

export interface BuildingLayoutTemplate {
  /** Server-assigned ID (UUID) — undefined for local-only entries */
  id?: string
  name: string
  description?: string | null
  buildingType: string
  units: LayoutUnit[]
  updatedAtUtc?: string
  /** true when stored only in localStorage, not yet synced to master API */
  isLocal?: boolean
}

// ── Master-API session helpers ────────────────────────────────────────────────

export function getMasterToken(): string | null {
  if (typeof localStorage === 'undefined') return null
  const token = localStorage.getItem(MASTER_TOKEN_KEY)
  const expires = localStorage.getItem(MASTER_EXPIRES_KEY)
  if (!token || !expires) return null
  if (new Date(expires) <= new Date()) {
    clearMasterSession()
    return null
  }
  return token
}

export function isMasterConnected(): boolean {
  return getMasterToken() !== null
}

export function clearMasterSession(): void {
  if (typeof localStorage === 'undefined') return
  localStorage.removeItem(MASTER_TOKEN_KEY)
  localStorage.removeItem(MASTER_EXPIRES_KEY)
}

// ── Low-level GraphQL request to master API ──────────────────────────────────

async function masterGql<T>(
  query: string,
  variables?: Record<string, unknown>,
): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  const tok = getMasterToken()
  if (tok) headers['Authorization'] = `Bearer ${tok}`

  const res = await fetch(MASTER_GRAPHQL_URL, {
    method: 'POST',
    headers,
    body: JSON.stringify({ query, variables }),
  })

  const json: { data?: T; errors?: Array<{ message: string }> } = await res.json()

  if (json.errors?.length) {
    throw new Error(json.errors[0]?.message ?? 'Master API error')
  }
  if (!json.data) throw new Error('No data returned from Master API')
  return json.data
}

// ── Layout CRUD via master API ────────────────────────────────────────────────

const MY_LAYOUTS_QUERY = `
  query MyBuildingLayouts {
    myBuildingLayouts {
      id
      name
      description
      buildingType
      unitsJson
      updatedAtUtc
    }
  }
`

const SAVE_LAYOUT_MUTATION = `
  mutation SaveBuildingLayout($input: SaveBuildingLayoutInput!) {
    saveBuildingLayout(input: $input) {
      id
      name
      description
      buildingType
      unitsJson
      updatedAtUtc
    }
  }
`

const DELETE_LAYOUT_MUTATION = `
  mutation DeleteBuildingLayout($input: DeleteBuildingLayoutInput!) {
    deleteBuildingLayout(input: $input)
  }
`

interface RawLayoutInfo {
  id: string
  name: string
  description: string | null
  buildingType: string
  unitsJson: string
  updatedAtUtc: string
}

function parseRawLayout(raw: RawLayoutInfo): BuildingLayoutTemplate {
  let units: LayoutUnit[] = []
  try {
    units = JSON.parse(raw.unitsJson) as LayoutUnit[]
  } catch {
    units = []
  }
  return {
    id: raw.id,
    name: raw.name,
    description: raw.description,
    buildingType: raw.buildingType,
    units,
    updatedAtUtc: raw.updatedAtUtc,
    isLocal: false,
  }
}

/** Fetch all layout templates from the master API for the authenticated user. */
export async function fetchMasterLayouts(): Promise<BuildingLayoutTemplate[]> {
  const data = await masterGql<{ myBuildingLayouts: RawLayoutInfo[] }>(MY_LAYOUTS_QUERY)
  return data.myBuildingLayouts.map(parseRawLayout)
}

/** Save a layout to the master API.  Returns the persisted template. */
export async function saveMasterLayout(
  name: string,
  description: string | null,
  buildingType: string,
  units: LayoutUnit[],
  existingId?: string,
): Promise<BuildingLayoutTemplate> {
  const unitsJson = JSON.stringify(units)
  const data = await masterGql<{ saveBuildingLayout: RawLayoutInfo }>(SAVE_LAYOUT_MUTATION, {
    input: {
      name,
      description: description || null,
      buildingType,
      unitsJson,
      existingId: existingId ?? null,
    },
  })
  return parseRawLayout(data.saveBuildingLayout)
}

/** Delete a layout from the master API by its server ID. */
export async function deleteMasterLayout(id: string): Promise<void> {
  await masterGql<{ deleteBuildingLayout: boolean }>(DELETE_LAYOUT_MUTATION, { input: { id } })
}

// ── localStorage fallback CRUD ────────────────────────────────────────────────

function getLocalLayouts(): BuildingLayoutTemplate[] {
  try {
    const raw = localStorage.getItem(LOCAL_LAYOUT_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as Array<{
      name: string
      buildingType: string
      units: LayoutUnit[]
      description?: string | null
    }>
    return parsed.map((l, i) => ({
      id: `local-${i}`,
      name: l.name,
      description: l.description ?? null,
      buildingType: l.buildingType,
      units: l.units,
      isLocal: true,
    }))
  } catch {
    return []
  }
}

function setLocalLayouts(layouts: Array<{ name: string; buildingType: string; units: LayoutUnit[]; description?: string | null }>): void {
  localStorage.setItem(LOCAL_LAYOUT_KEY, JSON.stringify(layouts))
}

export function saveLocalLayout(
  name: string,
  description: string | null,
  buildingType: string,
  units: LayoutUnit[],
): void {
  const raw = getLocalLayouts()
  const existing = raw.findIndex((l) => l.name === name && l.buildingType === buildingType)
  const entry = { name, description, buildingType, units }
  if (existing >= 0) {
    raw.splice(existing, 1, entry)
  } else {
    raw.push(entry)
  }
  setLocalLayouts(raw.map((l) => ({ name: l.name, buildingType: l.buildingType, units: l.units, description: l.description ?? null })))
}

export function deleteLocalLayout(name: string, buildingType: string): void {
  const raw = getLocalLayouts()
  const filtered = raw.filter((l) => !(l.name === name && l.buildingType === buildingType))
  setLocalLayouts(filtered.map((l) => ({ name: l.name, buildingType: l.buildingType, units: l.units, description: l.description ?? null })))
}

/** Returns local-only layouts filtered to a specific building type. */
export function getLocalLayoutsForType(buildingType: string): BuildingLayoutTemplate[] {
  return getLocalLayouts().filter((l) => l.buildingType === buildingType)
}
