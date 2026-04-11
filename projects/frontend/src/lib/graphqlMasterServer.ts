const GRAPHQL_URL = import.meta.env.VITE_MASTER_GRAPHQL_URL || 'http://localhost:44364/graphql'

export interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: Record<string, unknown> }>
}

/**
 * Structured error thrown by gqlRequest when the API returns a GraphQL error.
 * Carries the machine-readable `code` from `extensions.code` (e.g. `LOT_ALREADY_OWNED`)
 * so callers can distinguish specific failure types from generic API errors.
 */
export class GraphQLError extends Error {
  constructor(
    message: string,
    public readonly code?: string,
  ) {
    super(message)
    this.name = 'GraphQLError'
  }
}

/**
 * Lightweight GraphQL client that sends requests to the Master API.
 * Automatically attaches the JWT bearer token from localStorage when available.
 * Throws `GraphQLError` (with `.code` from `extensions.code`) on API errors.
 */
export async function gqlRequest<T>(query: string, variables?: Record<string, unknown>): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }

  const token = typeof localStorage !== 'undefined' ? localStorage.getItem('auth_token') : null
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const res = await fetch(GRAPHQL_URL, {
    method: 'POST',
    headers,
    body: JSON.stringify({ query, variables }),
  })

  const json: GraphQLResponse<T> = await res.json()

  if (json.errors && json.errors.length > 0) {
    const firstError = json.errors[0]!
    const code = firstError.extensions?.code as string | undefined
    throw new GraphQLError(firstError.message, code)
  }

  if (!json.data) {
    throw new GraphQLError('No data returned from GraphQL API')
  }

  return json.data
}
