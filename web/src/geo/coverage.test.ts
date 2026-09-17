import { describe, expect, it } from "vitest";
import { BLIND_COLOR, cellColor, cellOpacity, fmtRatio } from "./coverage";

describe("cellColor", () => {
  it("칠하지 못하는 셀은 빨강", () => {
    expect(cellColor({ x: 0, y: 0, n: 0 })).toBe(BLIND_COLOR);
  });
  it("허용오차를 못 채우면 주황 계열, 채우면 초록 계열", () => {
    const weak = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.3, sigma_psi: 1, stability: 0.5 });
    const ok = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.1, sigma_psi: 0.5, stability: 1.5 });
    expect(weak).not.toBe(ok);
    expect(weak).toMatch(/^#/);
    expect(ok).toMatch(/^#/);
  });
  it("stability 가 오르면 초록에 가까워진다", () => {
    const green = (hex: string) => parseInt(hex.slice(3, 5), 16);
    const low = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.6, sigma_psi: 4, stability: 0.25 });
    const mid = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.15, sigma_psi: 2, stability: 1.0 });
    const high = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.07, sigma_psi: 1, stability: 2.0 });
    expect(green(low)).toBeLessThan(green(mid));
    expect(green(mid)).toBeLessThan(green(high));
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
