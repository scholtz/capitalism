import type { ProductType } from '@/types'

type ProductAccessFields = Pick<ProductType, 'isProOnly' | 'isUnlockedForCurrentPlayer'>

export type ProductAccessState = 'free' | 'pro-locked' | 'pro-unlocked'

export function getProductAccessState(product: ProductAccessFields): ProductAccessState {
  if (!product.isProOnly) {
    return 'free'
  }

  return product.isUnlockedForCurrentPlayer ? 'pro-unlocked' : 'pro-locked'
}

export function isProductLocked(product: ProductAccessFields): boolean {
  return getProductAccessState(product) === 'pro-locked'
}
