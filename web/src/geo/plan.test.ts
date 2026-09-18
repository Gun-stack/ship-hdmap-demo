// @vitest-environment jsdom
// (screenToPlan needs a real SVGSVGElement to prove its jsdom fallback doesn't throw)
import { describe, expect, it } from "vitest";
import { LEGEND, MAX_SCALE, boxOfPoint, clampView, fitTo, screenToPlan, viewBoxOf, zoomAt } from "./plan";
import { BLIND_COLOR } from "./coverage";

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
  it("한 점에 맞추면 그 점이 중심이고, 배율은 그 마커 크기에 비례한 값으로 맞춰진다", () => {
    const v = fitTo(DECK, boxOfPoint(96, 6.2));
    expect(v.cx).toBeCloseTo(96);
    expect(v.cy).toBeCloseTo(6.2);
    // pad = max(3,3)*0.5 = 1.5 → 높이가 좁은 쪽을 잡는다: 24 / (3+1.5) = 16/3
    expect(v.scale).toBeCloseTo(16 / 3, 6);
  });
  it("갑판 전체에 맞추면 중심은 갑판 중심, 배율은 1 로 묶인다", () => {
    const v = fitTo(DECK, DECK);
    expect(v.cx).toBeCloseTo(60);
    expect(v.cy).toBeCloseTo(0);
    expect(v.scale).toBe(1);
  });
  /// fitTo 는 스스로 clampView 를 거친다: 호출자가 매번 감싸는 규칙이면 언젠가 한 번은 잊혀지고,
  /// 그때 배가 화면 밖으로 나가 영구 저장된다 (Task 6 의 "선택에 맞추기" 가 다음 호출자다).
  /// 이물 훨씬 밖(x=200)을 목표로 잡아, 클램프가 없으면 중심이 갑판 밖(120 초과)으로 나가는 경우로 검증한다.
  it("목표 중심이 갑판 밖이면 갑판 가장자리로 클램프된다", () => {
    const v = fitTo(DECK, boxOfPoint(200, 0));   // 갑판은 x 0..120, 200 은 한참 밖
    expect(v.cx).toBe(120);
    expect(v.cy).toBe(0);
  });
});

describe("zoomAt", () => {
  /// 커서가 가리키던 지점이 확대 후에도 화면의 같은 자리에 있어야 한다.
  /// 안 그러면 마커를 키우려 할 때마다 마커가 화면 밖으로 달아난다.
  it("커서 아래 지점을 붙잡는다 (x, y 둘 다)", () => {
    const v0 = { cx: 60, cy: 0, scale: 1 };
    const anchor = { x: 100, y: 8 };
    const v1 = zoomAt(v0, anchor, 2);
    const fracOf = (key: "cx" | "cy") => (v: typeof v0, a: number, span: number) => (a - (v[key] - span / v.scale / 2)) / (span / v.scale);
    expect(fracOf("cx")(v1, anchor.x, DECK.w)).toBeCloseTo(fracOf("cx")(v0, anchor.x, DECK.w), 6);
    // y 는 이 코드가 이미 한 번 부호를 잘못 넣어 배를 뒤집었던 축이다 -- x 만 검사하면 그 부호 오류를
    // 못 잡으므로 (cx 는 사용하지 않는 식이라 그대로 통과한다) 같은 불변식을 y 로도 검사한다.
    expect(fracOf("cy")(v1, anchor.y, DECK.h)).toBeCloseTo(fracOf("cy")(v0, anchor.y, DECK.h), 6);
  });
  /// 위 불변식 검사와 별개로, cy 식의 부호를 뒤집어도(`cy: anchor.y + (anchor.y - v.cy) * f`) 여전히
  /// 통과하지 않도록 결과값을 직접 못박는다: v0={cx:60,cy:0,scale:1}, anchor.y=8, k=2 → f=0.5 →
  /// cy = 8 - (8-0)*0.5 = 4. 부호가 뒤집히면 12 가 나온다.
  it("y 축 부호를 뒤집으면 이 값이 어긋난다 (cy 를 직접 못박는다)", () => {
    expect(zoomAt({ cx: 60, cy: 0, scale: 1 }, { x: 60, y: 8 }, 2).cy).toBeCloseTo(4, 6);
  });
  it("배율을 1..40 으로 묶는다", () => {
    expect(zoomAt({ cx: 60, cy: 0, scale: 1 }, { x: 60, y: 0 }, 0.5).scale).toBe(1);
    expect(zoomAt({ cx: 60, cy: 0, scale: 30 }, { x: 60, y: 0 }, 4).scale).toBe(MAX_SCALE);
  });
});

describe("clampView", () => {
  it("이물을 한참 지나친 창은 갑판 가장자리로 돌아온다", () => {
    expect(clampView(DECK, { cx: 500, cy: 0, scale: 2 })).toEqual({ cx: 120, cy: 0, scale: 2 });
  });
  it("갑판 안쪽 창은 건드리지 않는다", () => {
    const v = { cx: 60, cy: 4, scale: 5 };
    expect(clampView(DECK, v)).toEqual(v);
  });
  it("배율은 클램프의 영향을 받지 않는다", () => {
    expect(clampView(DECK, { cx: -999, cy: 999, scale: 17 }).scale).toBe(17);
  });
});

describe("screenToPlan", () => {
  it("SVG 좌표 API 가 없으면 (jsdom) 화면 좌표를 그대로 돌려준다", () => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg") as unknown as SVGSVGElement;
    expect(screenToPlan({ x: 12, y: 34 }, svg)).toEqual({ x: 12, y: 34 });
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
