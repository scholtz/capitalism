/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_MASTER_GRAPHQL_URL?: string
  readonly VITE_MASTER_WEB_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
