import { describe, it, expect } from "vitest";
import { bbox, ringPath, pickDeck } from "./deck";

describe("deck geometry", () => {
  it("bbox of a closed ring with margin", () => {
    const b = bbox([[0, -12, 10.6], [120, -12, 10.6], [120, 12, 10.6], [0, 12, 10.6], [0, -12, 10.6]], 2);
    expect(b).toEqual({ x: -2, y: -14, w: 124, h: 28 });
  });
  it("ring path flips y", () => {
    expect(ringPath([[0, 0, 1], [10, 0, 1], [10, 5, 1]])).toBe("M0,0 L10,0 L10,-5 Z");
  });
  it("pickDeck honours the filter and skips decks without an outline", () => {
    const small = { id: "D1", outline: [[0, -5, 1], [50, -5, 1], [50, 5, 1], [0, 5, 1], [0, -5, 1]] };
    const big = { id: "D2", outline: [[0, -12, 2], [120, -12, 2], [120, 12, 2], [0, 12, 2], [0, -12, 2]] };
    const empty = { id: "D3", outline: [] as number[][] };
    expect(pickDeck([small, big, empty], "all")?.id).toBe("D2");
    expect(pickDeck([small, big, empty], "D1")?.id).toBe("D1");
    expect(pickDeck([small, big, empty], "D3")).toBeUndefined();
    expect(pickDeck([], "all")).toBeUndefined();
  });
});
