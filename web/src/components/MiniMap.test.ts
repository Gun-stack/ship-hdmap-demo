import { describe, it, expect } from "vitest";
import { bbox, ringPath } from "./MiniMap";

describe("minimap geometry", () => {
  it("bbox of a closed ring with margin", () => {
    const b = bbox([[0, -12, 10.6], [120, -12, 10.6], [120, 12, 10.6], [0, 12, 10.6], [0, -12, 10.6]], 2);
    expect(b).toEqual({ x: -2, y: -14, w: 124, h: 28 });
  });
  it("ring path flips y", () => {
    expect(ringPath([[0, 0, 1], [10, 0, 1], [10, 5, 1]])).toBe("M0,0 L10,0 L10,-5 Z");
  });
});
