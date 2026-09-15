import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { wrapDeg, wrapRad, toUnity, unityYawDeg, toWgs84 } from "./shipFrame";

const vectors = JSON.parse(readFileSync(resolve(__dirname, "../../../docs/test-vectors/ship-frame.json"), "utf8"));

describe("shipFrame (shared vectors)", () => {
  it("points map to unity", () => {
    for (const p of vectors.points) expect(toUnity(p.ship[0], p.ship[1], p.ship[2])).toEqual(p.unity);
  });
  it("headings", () => {
    for (const h of vectors.headings) expect(unityYawDeg(h.heading_deg)).toBe(h.unity_yaw_deg);
  });
  it("wrap", () => {
    for (const w of vectors.wrap_deg) expect(wrapDeg(w.in)).toBeCloseTo(w.out, 9);
    expect(wrapRad(3 * Math.PI)).toBeCloseTo(Math.PI, 12);
    expect(wrapRad(-Math.PI)).toBeCloseTo(Math.PI, 12);
  });
  it("wgs84 planar approximation", () => {
    for (const c of vectors.wgs84) {
      const { lat, lon } = toWgs84(c.georef, c.ship[0], c.ship[1]);
      expect(lat).toBeCloseTo(c.expect.lat, 7);
      expect(lon).toBeCloseTo(c.expect.lon, 7);
    }
  });
});
