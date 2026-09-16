import { describe, it, expect } from "vitest";
import { ringArea, slotKpi } from "./kpi";
import type { Deck, Feature } from "../api/types";

const deck: Deck = { id: "D3", name: "Deck 3", z_surface: 10.6, z_clear: 2.2, movable: false, outline: [[0, -12, 10.6], [120, -12, 10.6], [120, 12, 10.6], [0, 12, 10.6], [0, -12, 10.6]] };
const slot = (id: string, x: number, y: number, deckId = "D3"): Feature => ({ id, deck_id: deckId, layer: "B2", kind: "parking_slot", props: {},
  geometry: { type: "Polygon", coordinates: [[[x, y, 10.6], [x + 4.8, y, 10.6], [x + 4.8, y + 1.85, 10.6], [x, y + 1.85, 10.6], [x, y, 10.6]]] } });

describe("slot KPI", () => {
  it("ringArea is the shoelace area regardless of winding", () => {
    expect(ringArea(deck.outline)).toBeCloseTo(2880, 6);
    expect(ringArea([...deck.outline].reverse())).toBeCloseTo(2880, 6);
  });
  it("slotKpi counts only this deck's parking slots", () => {
    const k = slotKpi([slot("PS-D3-001", 100, 2), slot("PS-D3-002", 100, 4.25), slot("PS-D1-001", 10, 2, "D1"), { ...slot("M-1", 0, 0), kind: "arrow" }], deck);
    expect(k.count).toBe(2);
    expect(k.utilization).toBeCloseTo((2 * 4.8 * 1.85) / 2880, 9);
  });
});
