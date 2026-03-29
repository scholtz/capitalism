export type OnboardingAnalyticsEvent =
  | 'onboarding_start'
  | 'industry_selected'
  | 'city_selected'
  | 'factory_configured'
  | 'shop_configured'
  | 'save_prompt_shown'
  | 'first_profit_shown'
  | 'completed'

/**
 * Lightweight analytics hook for onboarding step events.
 * Dispatches a browser custom event so future instrumentation can subscribe
 * without coupling this flow to a specific analytics vendor.
 */
export function trackOnboardingEvent(
  eventName: OnboardingAnalyticsEvent,
  detail: Record<string, unknown> = {},
): void {
  if (typeof window === 'undefined') {
    return
  }

  window.dispatchEvent(
    new CustomEvent('capitalism:onboarding', {
      detail: {
        eventName,
        timestamp: new Date().toISOString(),
        ...detail,
      },
    }),
  )
}

/**
 * Compute a simplified projected profit for the guest "first profit" preview.
 * Uses the product's base price and output quantity to estimate one tick's revenue,
 * then subtracts a rough material cost estimate to produce a net profit figure.
 *
 * @param basePrice      Product base market price per unit
 * @param outputQuantity Units produced per craft cycle
 * @param recipeCost     Total raw-material cost estimate (optional; defaults to 40% of revenue)
 */
export function computeSimulatedProfit(
  basePrice: number,
  outputQuantity: number,
  recipeCost?: number,
): { revenue: number; cost: number; profit: number } {
  const revenue = Math.round(basePrice * outputQuantity)
  const cost =
    recipeCost !== undefined ? Math.round(recipeCost) : Math.round(revenue * 0.4)
  const profit = revenue - cost
  return { revenue, cost, profit }
}
