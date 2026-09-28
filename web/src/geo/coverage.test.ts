import { describe, expect, it } from "vitest";
import { BLIND_COLOR, cellColor, cellOpacity, fmtRatio } from "./coverage";
import { PALETTE } from "../theme/palette";

describe("cellColor", () => {
  it("칠하지 못하는 셀은 blind 색", () => {
    expect(cellColor({ x: 0, y: 0, n: 0 })).toBe(BLIND_COLOR);
  });
  it("허용오차를 못 채우면 weak 쪽, 채우면 ok 쪽", () => {
    const weak = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.3, sigma_psi: 1, stability: 0.5 });
    const ok = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.1, sigma_psi: 0.5, stability: 1.5 });
    expect(weak).not.toBe(ok);
    expect(weak).toMatch(/^#/);
    expect(ok).toMatch(/^#/);
  });
  it("stability 가 오르면 ok 색에 가까워지고 2.0 에서 ok 색 그대로다", () => {
    const rgb = (hex: string) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
    const dist = (a: string, b: string) => Math.hypot(...rgb(a).map((v, i) => v - rgb(b)[i]));
    const low = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.6, sigma_psi: 4, stability: 0.25 });
    const mid = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.15, sigma_psi: 2, stability: 1.0 });
    const high = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.07, sigma_psi: 1, stability: 2.0 });
    expect(dist(low, PALETTE.CoverageOk)).toBeGreaterThan(dist(mid, PALETTE.CoverageOk));
    expect(high).toBe(PALETTE.CoverageOk);
    expect(cellColor({ x: 0, y: 0, n: 2, stability: 0 })).toBe(PALETTE.CoverageWeak);
    expect(low).not.toBe(BLIND_COLOR);
  });
});

describe("cellOpacity", () => {
  it("차가 갈 수 있는 셀은 진하게", () => {
    expect(cellOpacity({ x: 0, y: 0, n: 2, stability: 1 })).toBeGreaterThan(0.4);
  });
  it("in_scope 가 false 면 흐리게 — 지표에서 빠졌다는 표시", () => {
    const out = cellOpacity({ x: 0, y: 0, n: 0, in_scope: false });
    expect(out).toBeLessThan(0.25);
    expect(out).toBeGreaterThan(0);
  });
});

describe("fmtRatio", () => {
  it("백분율 한 자리", () => {
    expect(fmtRatio(0.0213)).toBe("2.1 %");
    expect(fmtRatio(0)).toBe("0.0 %");
  });
});
