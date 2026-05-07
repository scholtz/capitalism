<script setup lang="ts">
/**
 * UnitLinkConnector – a single SVG component that renders a directional link
 * indicator for all eight supported link states in the 4×4 building unit grid.
 *
 * Replaces the old two-element approach (separate `link-line` span + `link-arrow`
 * Unicode character, or `diag-line` rotated rectangle + `diag-arrow` glyph) which
 * produced duplicate visual elements and rounded CSS artifacts on diagonal links.
 *
 * A single SVG path per link button is drawn:
 *   – a track line (muted or primary-coloured depending on active state)
 *   – zero, one, or two arrowhead polylines showing directionality
 *
 * Directionality meaning:
 *   forward  = resource flows from the source cell toward the neighbor
 *   backward = resource flows back toward the source cell
 *   both     = legacy bidirectional state; arrowheads at both ends
 *   none     = link slot exists but no active connection
 *
 * The SVG for diagonal links is sized at 36×36 px and uses `overflow: visible`
 * so that it can cover the full diagonal intersection square even though each
 * button is only 18 px wide.  The secondary (/) button's SVG is positioned with
 * `right: 0` inside its button, which causes the left edge to extend leftward
 * into the primary button's area – giving the full 36×36 coverage required for
 * a clean diagonal line.
 */

export type LinkConnectorState = 'none' | 'forward' | 'backward' | 'both'
export type LinkConnectorType = 'horizontal' | 'vertical' | 'diag-primary' | 'diag-secondary'

interface Props {
  /** Which link slot this connector represents. */
  type: LinkConnectorType
  /** Current directional state of the link. */
  state: LinkConnectorState
}

const props = defineProps<Props>()
</script>

<template>
  <!-- ── Horizontal (32 × 14) ──────────────────────────────────────────── -->
  <svg
    v-if="props.type === 'horizontal'"
    class="link-connector-svg"
    :class="{ 'is-active': props.state !== 'none' }"
    viewBox="0 0 32 14"
    width="100%"
    height="100%"
    aria-hidden="true"
    :data-connector-type="props.type"
    :data-connector-state="props.state"
  >
    <!-- track line -->
    <line class="connector-track" x1="3" y1="7" x2="29" y2="7" />
    <!-- forward arrowhead → at right end -->
    <polyline
      v-if="props.state === 'forward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="forward"
      points="24,4 29,7 24,10"
    />
    <!-- backward arrowhead ← at left end -->
    <polyline
      v-if="props.state === 'backward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="backward"
      points="8,4 3,7 8,10"
    />
  </svg>

  <!-- ── Vertical (14 × 32) ─────────────────────────────────────────────── -->
  <svg
    v-else-if="props.type === 'vertical'"
    class="link-connector-svg"
    :class="{ 'is-active': props.state !== 'none' }"
    viewBox="0 0 14 32"
    width="100%"
    height="100%"
    aria-hidden="true"
    :data-connector-type="props.type"
    :data-connector-state="props.state"
  >
    <line class="connector-track" x1="7" y1="3" x2="7" y2="29" />
    <!-- forward arrowhead ↓ at bottom end -->
    <polyline
      v-if="props.state === 'forward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="forward"
      points="4,24 7,29 10,24"
    />
    <!-- backward arrowhead ↑ at top end -->
    <polyline
      v-if="props.state === 'backward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="backward"
      points="4,8 7,3 10,8"
    />
  </svg>

  <!-- ── Diagonal primary \ ─────────────────────────────────────────────── -->
  <!--
    SVG is 36×36 and positioned absolutely at left:0 of its button.
    overflow:visible lets it extend rightward to cover the full intersection.
    pointer-events:none keeps click handling on the containing button.
  -->
  <svg
    v-else-if="props.type === 'diag-primary'"
    class="link-connector-svg link-connector-svg--diag-primary"
    :class="{ 'is-active': props.state !== 'none' }"
    viewBox="0 0 36 36"
    width="36"
    height="36"
    aria-hidden="true"
    :data-connector-type="props.type"
    :data-connector-state="props.state"
  >
    <!-- \ track line top-left → bottom-right -->
    <line class="connector-track" x1="4" y1="4" x2="32" y2="32" />
    <!-- forward arrowhead ↘ – corner bracket at bottom-right -->
    <polyline
      v-if="props.state === 'forward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="forward"
      points="24,32 32,32 32,24"
    />
    <!-- backward arrowhead ↖ – corner bracket at top-left -->
    <polyline
      v-if="props.state === 'backward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="backward"
      points="12,4 4,4 4,12"
    />
  </svg>

  <!-- ── Diagonal secondary / ──────────────────────────────────────────── -->
  <!--
    SVG is 36×36 positioned at right:0 of its button (which itself is at right:0
    of the 36×36 group).  right:0 in button-local coordinates means the SVG's
    right edge aligns with the group's right edge, so the SVG's left edge is at
    group-x = 0 – covering the full intersection area leftward.
  -->
  <svg
    v-else-if="props.type === 'diag-secondary'"
    class="link-connector-svg link-connector-svg--diag-secondary"
    :class="{ 'is-active': props.state !== 'none' }"
    viewBox="0 0 36 36"
    width="36"
    height="36"
    aria-hidden="true"
    :data-connector-type="props.type"
    :data-connector-state="props.state"
  >
    <!-- / track line top-right → bottom-left -->
    <line class="connector-track" x1="32" y1="4" x2="4" y2="32" />
    <!-- forward arrowhead ↙ – corner bracket at bottom-left -->
    <polyline
      v-if="props.state === 'forward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="forward"
      points="4,24 4,32 12,32"
    />
    <!-- backward arrowhead ↗ – corner bracket at top-right -->
    <polyline
      v-if="props.state === 'backward' || props.state === 'both'"
      class="connector-arrow"
      data-arrowhead="backward"
      points="32,12 32,4 24,4"
    />
  </svg>
</template>

<style scoped>
/* All SVGs should not capture pointer events – the containing button handles clicks */
.link-connector-svg {
  display: block;
  overflow: visible;
  pointer-events: none;
}

/* ── Track line (always visible) ───────────────────────────────────────── */
.connector-track {
  stroke: color-mix(in srgb, var(--color-border) 80%, transparent);
  stroke-width: 2.5;
  stroke-linecap: round;
  fill: none;
}

.is-active .connector-track {
  stroke: var(--color-primary);
}

/* ── Arrowhead polylines (only rendered when state is not 'none') ────── */
.connector-arrow {
  stroke: var(--color-primary);
  stroke-width: 2.5;
  stroke-linecap: round;
  stroke-linejoin: round;
  fill: none;
}

/* ── Diagonal SVG positioning ──────────────────────────────────────────── */
/* Primary \ button is at left:0 in the 36px group; SVG left edge = group-x 0 */
.link-connector-svg--diag-primary {
  position: absolute;
  top: 0;
  left: 0;
}

/* Secondary / button is at right:0 in the group; right:0 within the button
   means the SVG right edge = group right edge = group-x 36.
   SVG is 36px wide, so SVG left edge = group-x 0. Covers the full area. */
.link-connector-svg--diag-secondary {
  position: absolute;
  top: 0;
  right: 0;
}
</style>
