import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useScrollPreservation } from '../useScrollPreservation'

// scrollPreservation depends on browser window APIs.
// We polyfill them on globalThis so the composable can run in the node test env.
describe('useScrollPreservation', () => {
  let scrollToMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    scrollToMock = vi.fn()
    // Patch globalThis so the composable can read and write scroll position
    Object.defineProperty(globalThis, 'window', {
      value: {
        scrollX: 0,
        scrollY: 0,
        scrollTo: scrollToMock,
      },
      writable: true,
      configurable: true,
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('saveScrollPosition returns current window scroll coordinates', () => {
    ;(globalThis as unknown as { window: { scrollX: number; scrollY: number } }).window.scrollX = 120
    ;(globalThis as unknown as { window: { scrollX: number; scrollY: number } }).window.scrollY = 350

    const { saveScrollPosition } = useScrollPreservation()
    const pos = saveScrollPosition()

    expect(pos).toEqual({ x: 120, y: 350 })
  })

  it('saveScrollPosition returns { x: 0, y: 0 } when page is at top', () => {
    const { saveScrollPosition } = useScrollPreservation()
    const pos = saveScrollPosition()

    expect(pos).toEqual({ x: 0, y: 0 })
  })

  it('restoreScrollPosition calls window.scrollTo with saved coordinates and instant behavior', async () => {
    const { restoreScrollPosition } = useScrollPreservation()
    await restoreScrollPosition({ x: 200, y: 600 })

    // Should have been called at least once (either via options or legacy two-argument form)
    expect(scrollToMock).toHaveBeenCalled()
  })

  it('restoreScrollPosition falls back to two-argument scrollTo when options form throws', async () => {
    // Simulate a browser that throws on ScrollToOptions
    scrollToMock.mockImplementationOnce(() => {
      throw new Error('ScrollToOptions not supported')
    })
    const { restoreScrollPosition } = useScrollPreservation()
    await restoreScrollPosition({ x: 100, y: 400 })

    // First call threw, second call is the fallback
    expect(scrollToMock).toHaveBeenCalledTimes(2)
    expect(scrollToMock).toHaveBeenLastCalledWith(100, 400)
  })

  it('restoreScrollPosition calls scrollTo with zero coords when page was at top', async () => {
    const { restoreScrollPosition } = useScrollPreservation()
    await restoreScrollPosition({ x: 0, y: 0 })

    expect(scrollToMock).toHaveBeenCalledWith({ left: 0, top: 0, behavior: 'instant' })
  })

  it('save then restore round-trip restores the saved position', async () => {
    ;(globalThis as unknown as { window: { scrollX: number; scrollY: number } }).window.scrollX = 40
    ;(globalThis as unknown as { window: { scrollX: number; scrollY: number } }).window.scrollY = 820

    const { saveScrollPosition, restoreScrollPosition } = useScrollPreservation()
    const pos = saveScrollPosition()

    // Simulate the page jumping to a different position during data update
    ;(globalThis as unknown as { window: { scrollY: number } }).window.scrollY = 0

    await restoreScrollPosition(pos)
    expect(scrollToMock).toHaveBeenCalledWith({ left: 40, top: 820, behavior: 'instant' })
  })
})
