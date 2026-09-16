import type { Deck, Feature } from "../api/types";

/** Shoelace area of a closed ring's x-y projection, always positive. */
export function ringArea(ring: number[][]): number {
  let a = 0;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) a += ring[j][0] * ring[i][1] - ring[i][0] * ring[j][1];
  return Math.abs(a) / 2;
}

/** Spec §5.3: utilization = sum of slot polygon areas / deck outline area, for one deck's parking slots. */
export function slotKpi(features: Feature[], deck: Deck): { count: number; utilization: number } {
  const slots = features.filter((f) => f.deck_id === deck.id && f.layer === "B2" && f.kind === "parking_slot");
  const deckArea = deck.outline.length ? ringArea(deck.outline) : 0;
  const area = slots.reduce((s, f) => s + ringArea((f.geometry.coordinates as number[][][])[0]), 0);
  return { count: slots.length, utilization: deckArea > 0 ? area / deckArea : 0 };
}
