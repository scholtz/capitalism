const GRAPHQL_URL = import.meta.env.VITE_GRAPHQL_URL || 'https://localhost:44364/graphql'

export interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: Record<string, unknown> }>
}

export class GraphQLError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'GraphQLError'
  }
}

export async function gqlRequest<T>(
  query: string,
  variables?: Record<string, unknown>,
  token?: string | null,
): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const res = await fetch(GRAPHQL_URL, {
    method: 'POST',
    headers,
    body: JSON.stringify({ query, variables }),
  })

  const json: GraphQLResponse<T> = await res.json()

  if (json.errors?.length) {
    throw new GraphQLError(json.errors[0]?.message || 'Master API error')
  }

  if (!json.data) {
    throw new GraphQLError('No data returned from GraphQL API')
  }

  return json.data
}