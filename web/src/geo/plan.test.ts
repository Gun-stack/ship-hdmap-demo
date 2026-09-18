import { describe, expect, it } from "vitest";
import { LEGEND, MAX_SCALE, boxOfPoint, fitTo, viewBoxOf, zoomAt } from "./plan";
import { BLIND_COLOR, cellColor } from "./coverage";

// Deck 3 의 실제 모양: 120 x 24 m. SVG 공간이므로 y 는 아래가 +.
const DECK = { x: 0, y: -12, w: 120, h: 24 };

describe("viewBoxOf", () => {
  it("scale 1 이면 갑판 전체", () => {
    expect(viewBoxOf(DECK, { cx: 60, cy: 0, scale: 1 })).toBe("0 -12 120 24");
  });
  it("확대하면 폭이 줄고 중심이 유지된다", () => {
    expect(viewBoxOf(DECK, { cx: 60, cy: 0, scale: 2 })).toBe("30 -6 60 12");
  });
  it("중심을 옮기면 창이 따라간다", () => {
    expect(viewBoxOf(DECK, { cx: 20, cy: 4, scale: 2 })).toBe("-10 -2 60 12");
  });
  /// 배가 뒤집히면 안 된다: cy 를 +로 옮기면 창은 SVG 아래쪽(= 우현, ship -y)으로 간다.
  it("cy 를 한 번만 쓴다 (부호를 뒤집지 않는다)", () => {
    const vb = viewBoxOf(DECK, { cx: 60, cy: 6, scale: 2 }).split(" ").map(Number);
    expect(vb[1]).toBe(0);      // 6 - 12/2
  });
});

describe("fitTo", () => {
  it("한 점에 맞추면 그 점이 중심이고 확대된다", () => {
    const v = fitTo(DECK, boxOfPoint(96, 6.2));
    expect(v.cx).toBeCloseTo(96);
    expect(v.cy).toBeCloseTo(6.2);
    expect(v.scale).toBeGreaterThan(1);
    expect(v.scale).toBeLessThanOrEqual(MAX_SCALE);
  });
  it("갑판 전체에 맞추면 중심은 갑판 중심, 배율은 1 로 묶인다", () => {
    const v = fitTo(DECK, DECK);
    expect(v.cx).toBeCloseTo(60);
    expect(v.cy).toBeCloseTo(0);
    expect(v.scale).toBe(1);
  });
});

describe("zoomAt", () => {
  /// 커서가 가리키던 지점이 확대 후에도 화면의 같은 자리에 있어야 한다.
  /// 안 그러면 마커를 키우려 할 때마다 마커가 화면 밖으로 달아난다.
  it("커서 아래 지점을 붙잡는다", () => {
    const v0 = { cx: 60, cy: 0, scale: 1 };
    const anchor = { x: 100, y: 8 };
    const v1 = zoomAt(v0, anchor, 2);
    const frac = (v: typeof v0, a: number, span: number) => (a - (v.cx - span / v.scale / 2)) / (span / v.scale);
    expect(frac(v1, anchor.x, DECK.w)).toBeCloseTo(frac(v0, anchor.x, DECK.w), 6);
  });
  it("배율을 1..40 으로 묶는다", () => {
    expect(zoomAt({ cx: 60, cy: 0, scale: 1 }, { x: 60, y: 0 }, 0.5).scale).toBe(1);
    expect(zoomAt({ cx: 60, cy: 0, scale: 30 }, { x: 60, y: 0 }, 4).scale).toBe(MAX_SCALE);
  });
});

/// 범례가 히트맵과 다른 색을 쓰면 화면이 거짓말을 한다. cellColor 를 그대로 부르는 것으로는
/// 같은 식을 두 번 쓴 것에 불과하므로, 범례가 실제 셀 모양을 어떤 색으로 칠하는지 값으로 못박는다.
describe("LEGEND", () => {
  it("네 항목이고 blind 는 히트맵과 같은 색이다", () => {
    expect(LEGEND).toHaveLength(4);
    expect(LEGEND[0].color).toBe(BLIND_COLOR);
    expect(LEGEND[0].color).toBe("#d32f2f");   // 값으로 못박는다: cellColor 를 다시 부르면 같은 식을 두 번 쓰는 것뿐
  });
  it("weak 은 주황, ok 는 초록 쪽이다", () => {
    const weak = LEGEND[1].color, ok = LEGEND[2].color;
    expect(weak).not.toBe(ok);
    const g = (hex: string) => parseInt(hex.slice(3, 5), 16);
    expect(g(ok)).toBeGreaterThan(g(weak));      // stability 가 좋을수록 초록이 강해진다
    expect(ok).toBe("#3cc850");                  // cellColor(stability >= 2) 가 saturate 하는 값
  });
});
