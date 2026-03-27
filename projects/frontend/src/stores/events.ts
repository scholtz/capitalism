import type { LocationQuery, LocationQueryValue } from 'vue-router'
import type { EventFilters, SavedSearch } from '@/types'

type DiscoveryFilterInput = {
  searchText?: string
  domainSlug?: string
  locationText?: string
  startsFromUtc?: string
  startsToUtc?: string
  isFree?: boolean
  priceMin?: number
  priceMax?: number
  sortBy: 'UPCOMING' | 'NEWEST' | 'RELEVANCE'
  attendanceMode?: 'IN_PERSON' | 'ONLINE' | 'HYBRID'
  language?: string
  timezone?: string
}

const DEFAULT_EVENT_FILTERS: Required<
  Pick<
    EventFilters,
    | 'search'
    | 'domain'
    | 'dateFrom'
    | 'dateTo'
    | 'location'
    | 'priceType'
    | 'priceMin'
    | 'priceMax'
    | 'sortBy'
    | 'attendanceMode'
    | 'language'
    | 'timezone'
  >
> = {
  search: '',
  domain: '',
  dateFrom: '',
  dateTo: '',
  location: '',
  priceType: 'ALL',
  priceMin: '',
  priceMax: '',
  sortBy: 'UPCOMING',
  attendanceMode: '',
  language: '',
  timezone: '',
}

function firstQueryValue(value: LocationQueryValue | LocationQueryValue[] | undefined): string {
  if (Array.isArray(value)) {
    return value[0] ?? ''
  }

  return value ?? ''
}

function parsePositiveNumber(value: string): number | undefined {
  const trimmed = value.trim()
  if (!trimmed) {
    return undefined
  }

  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

function toDateStartUtc(value: string): string | undefined {
  const trimmed = value.trim()
  return trimmed ? `${trimmed}T00:00:00.000Z` : undefined
}

function toDateEndUtc(value: string): string | undefined {
  const trimmed = value.trim()
  return trimmed ? `${trimmed}T23:59:59.999Z` : undefined
}

function isoDateToYyyyMmDd(value: string | null): string {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return date.toISOString().slice(0, 10)
}

function parseAttendanceMode(value: string): '' | 'IN_PERSON' | 'ONLINE' | 'HYBRID' {
  switch (value) {
    case 'in-person':
      return 'IN_PERSON'
    case 'online':
      return 'ONLINE'
    case 'hybrid':
      return 'HYBRID'
    default:
      return ''
  }
}

export function createDefaultEventFilters(): EventFilters {
  return { ...DEFAULT_EVENT_FILTERS }
}

export function areEventFiltersEqual(left: EventFilters, right: EventFilters): boolean {
  return left.search === right.search
    && left.domain === right.domain
    && left.dateFrom === right.dateFrom
    && left.dateTo === right.dateTo
    && left.location === right.location
    && left.priceType === right.priceType
    && left.priceMin === right.priceMin
    && left.priceMax === right.priceMax
    && left.sortBy === right.sortBy
    && left.attendanceMode === right.attendanceMode
    && left.language === right.language
    && left.timezone === right.timezone
}

export function eventFiltersToQuery(filters: EventFilters): Record<string, string> {
  const search = filters.search?.trim() ?? ''
  const domain = filters.domain?.trim() ?? ''
  const dateFrom = filters.dateFrom?.trim() ?? ''
  const dateTo = filters.dateTo?.trim() ?? ''
  const location = filters.location?.trim() ?? ''
  const priceMin = filters.priceMin?.trim() ?? ''
  const priceMax = filters.priceMax?.trim() ?? ''
  const language = filters.language?.trim().toLowerCase() ?? ''
  const timezone = filters.timezone?.trim() ?? ''

  const query: Record<string, string> = {}

  if (search) query.q = search
  if (domain) query.domain = domain
  if (dateFrom) query.from = dateFrom
  if (dateTo) query.to = dateTo
  if (location) query.location = location
  if (filters.priceType === 'FREE') query.price = 'free'
  if (filters.priceType === 'PAID') query.price = 'paid'
  if (priceMin) query.minPrice = priceMin
  if (priceMax) query.maxPrice = priceMax
  if (filters.sortBy === 'NEWEST') query.sort = 'newest'
  if (filters.sortBy === 'RELEVANCE') query.sort = 'relevance'
  if (filters.attendanceMode === 'IN_PERSON') query.mode = 'in-person'
  if (filters.attendanceMode === 'ONLINE') query.mode = 'online'
  if (filters.attendanceMode === 'HYBRID') query.mode = 'hybrid'
  if (language) query.lang = language
  if (timezone) query.tz = timezone

  return query
}

export function eventFiltersFromQuery(query: LocationQuery): EventFilters {
  const price = firstQueryValue(query.price)
  const sort = firstQueryValue(query.sort)
  const mode = firstQueryValue(query.mode)

  return {
    ...createDefaultEventFilters(),
    search: firstQueryValue(query.q).trim(),
    domain: firstQueryValue(query.domain).trim(),
    dateFrom: firstQueryValue(query.from).trim(),
    dateTo: firstQueryValue(query.to).trim(),
    location: firstQueryValue(query.location).trim(),
    priceType: price === 'free' ? 'FREE' : price === 'paid' ? 'PAID' : 'ALL',
    priceMin: firstQueryValue(query.minPrice).trim(),
    priceMax: firstQueryValue(query.maxPrice).trim(),
    sortBy: sort === 'newest' ? 'NEWEST' : sort === 'relevance' ? 'RELEVANCE' : 'UPCOMING',
    attendanceMode: parseAttendanceMode(mode),
    language: firstQueryValue(query.lang).trim().toLowerCase(),
    timezone: firstQueryValue(query.tz).trim(),
  }
}

export function buildDiscoveryFilterInput(filters: EventFilters): DiscoveryFilterInput {
  const search = filters.search?.trim() ?? ''
  const domain = filters.domain?.trim() ?? ''
  const location = filters.location?.trim() ?? ''
  const language = filters.language?.trim().toLowerCase() ?? ''
  const timezone = filters.timezone?.trim() ?? ''

  const input: DiscoveryFilterInput = {
    sortBy: filters.sortBy ?? 'UPCOMING',
  }

  if (search) input.searchText = search
  if (domain) input.domainSlug = domain
  if (location) input.locationText = location

  const startsFromUtc = toDateStartUtc(filters.dateFrom ?? '')
  if (startsFromUtc) input.startsFromUtc = startsFromUtc

  const startsToUtc = toDateEndUtc(filters.dateTo ?? '')
  if (startsToUtc) input.startsToUtc = startsToUtc

  if (filters.priceType === 'FREE') input.isFree = true
  if (filters.priceType === 'PAID') input.isFree = false

  const priceMin = parsePositiveNumber(filters.priceMin ?? '')
  if (priceMin !== undefined) input.priceMin = priceMin

  const priceMax = parsePositiveNumber(filters.priceMax ?? '')
  if (priceMax !== undefined) input.priceMax = priceMax

  if (filters.attendanceMode) input.attendanceMode = filters.attendanceMode
  if (language) input.language = language
  if (timezone) input.timezone = timezone

  return input
}

export function savedSearchToFilters(savedSearch: SavedSearch): EventFilters {
  return {
    ...createDefaultEventFilters(),
    search: savedSearch.searchText ?? '',
    domain: savedSearch.domainSlug ?? '',
    location: savedSearch.locationText ?? '',
    dateFrom: isoDateToYyyyMmDd(savedSearch.startsFromUtc),
    dateTo: isoDateToYyyyMmDd(savedSearch.startsToUtc),
    priceType: savedSearch.isFree === true ? 'FREE' : savedSearch.isFree === false ? 'PAID' : 'ALL',
    priceMin: savedSearch.priceMin === null ? '' : String(savedSearch.priceMin),
    priceMax: savedSearch.priceMax === null ? '' : String(savedSearch.priceMax),
    sortBy: savedSearch.sortBy,
    attendanceMode: savedSearch.attendanceMode ?? '',
    language: savedSearch.language ?? '',
    timezone: savedSearch.timezone ?? '',
  }
}

export function formatEventPrice(input: {
  isFree: boolean
  priceAmount: number | null
  currencyCode: string
}): string {
  // Keep zero-priced entries user-friendly even if upstream data forgot to set
  // `isFree`, because discovery cards should still render them as free events.
  if (input.isFree || input.priceAmount === 0) {
    return 'Free'
  }

  if (input.priceAmount === null) {
    return 'Paid'
  }

  const formatter = new Intl.NumberFormat('en', {
    style: 'currency',
    currency: input.currencyCode || 'EUR',
    minimumFractionDigits: Number.isInteger(input.priceAmount) ? 0 : 2,
    maximumFractionDigits: Number.isInteger(input.priceAmount) ? 0 : 2,
  })

  return formatter.format(input.priceAmount)
}
