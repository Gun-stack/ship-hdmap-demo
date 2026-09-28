/**
 * Colours that MEAN something, shared with Unity. unity/Assets/ShipHdMap/Runtime/Core/Palette.cs carries the
 * same names with the same hex, and palette-sync.test.ts reads that file and fails if any name here is missing
 * there or differs -- the 3D slot fill and the plan-view slot must never say "filled" in two different blues.
 *
 * The slot four and the coverage three were run through a colour-vision validator (deutan/protan/tritan) against
 * both the dark (#15181d) and the light (#fbfbfa) plan surface: every pair inside each set stays apart. One pair
 * ACROSS the sets sits close -- CoverageBlind and SlotNeedsAdjust -- and is tolerated because they never share a
 * mark type: coverage is a cell fill, a slot is an outline, and both carry a text legend.
 * Coverage "ok" is deliberately low-chroma: good ground should recede so that blind and weak ground is what the
 * eye lands on.
 */
export const PALETTE = {
  SlotEmpty: "#199e70",
  SlotFilled: "#3987e5",
  SlotNeedsAdjust: "#d95926",
  SlotUnreachable: "#6b6f78",
  CoverageBlind: "#d03b3b",
  CoverageWeak: "#f0a500",
  CoverageOk: "#8a9aa6",
  Lane: "#e8c547",
  Selection: "#ffb300",
  Seen: "#e040a0",
  SensorCone: "#4cc9f0",
} as const;

export type PaletteName = keyof typeof PALETTE;

/** Plan-view-only marks. Unity has no counterpart for these, so they are not part of the sync check. */
export const PLAN_ONLY = {
  Landmark: "#e5484d",
  Draft: "#ff9800",
  Candidate: "#b07cf0",
} as const;

export const SLOT_COLOR: Record<string, string> = {
  empty: PALETTE.SlotEmpty,
  filled: PALETTE.SlotFilled,
  needs_adjust: PALETTE.SlotNeedsAdjust,
  unreachable: PALETTE.SlotUnreachable,
};

/** Unknown status falls back to empty -- the same default Unity's MapOverlay.FillMat takes. */
export const slotColor = (status: string | undefined): string => SLOT_COLOR[status ?? "empty"] ?? PALETTE.SlotEmpty;
