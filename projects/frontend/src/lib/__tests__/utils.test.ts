import { describe, it, expect } from 'vitest'
import { deepEqual } from '../utils'

describe('deepEqual', () => {
  it('returns true for identical primitives', () => {
    expect(deepEqual(1, 1)).toBe(true)
    expect(deepEqual('hello', 'hello')).toBe(true)
    expect(deepEqual(true, true)).toBe(true)
    expect(deepEqual(null, null)).toBe(true)
    expect(deepEqual(undefined, undefined)).toBe(true)
  })

  it('returns false for different primitives', () => {
    expect(deepEqual(1, 2)).toBe(false)
    expect(deepEqual('hello', 'world')).toBe(false)
    expect(deepEqual(true, false)).toBe(false)
    expect(deepEqual(null, undefined)).toBe(false)
  })

  it('returns true for identical arrays', () => {
    expect(deepEqual([1, 2, 3], [1, 2, 3])).toBe(true)
    expect(deepEqual([], [])).toBe(true)
  })

  it('returns false for different arrays', () => {
    expect(deepEqual([1, 2, 3], [1, 2, 4])).toBe(false)
    expect(deepEqual([1, 2], [1, 2, 3])).toBe(false)
    expect(deepEqual([1, 2, 3], [3, 2, 1])).toBe(false)
  })

  it('returns true for identical objects', () => {
    expect(deepEqual({ a: 1, b: 2 }, { a: 1, b: 2 })).toBe(true)
    expect(deepEqual({}, {})).toBe(true)
  })

  it('returns false for different objects', () => {
    expect(deepEqual({ a: 1, b: 2 }, { a: 1, b: 3 })).toBe(false)
    expect(deepEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false)
    expect(deepEqual({ a: 1, b: 2 }, { b: 2, a: 1 })).toBe(true) // order doesn't matter
  })

  it('handles nested structures', () => {
    expect(deepEqual({ a: [1, 2], b: { c: 3 } }, { a: [1, 2], b: { c: 3 } })).toBe(true)
    expect(deepEqual({ a: [1, 2], b: { c: 3 } }, { a: [1, 2], b: { c: 4 } })).toBe(false)
  })

  it('handles different types', () => {
    expect(deepEqual({}, [])).toBe(false)
    expect(deepEqual([], {})).toBe(false)
    expect(deepEqual(1, '1')).toBe(false)
  })
})
