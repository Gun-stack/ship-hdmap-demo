import { describe, expect, it } from "vitest";
import { beliefBadge, fmtRatioOf } from "./belief";

describe("beliefBadge", () => {
  it("다섯 상태가 서로 다른 라벨과 색을 갖는다", () => {
    const states = ["ok", "degraded", "lost", "backtracking", "stopped"] as const;
    const badges = states.map(beliefBadge);
    expect(new Set(badges.map((b) => b.label)).size).toBe(5);
    expect(new Set(badges.map((b) => b.color)).size).toBe(5);
    badges.forEach((b) => expect(b.color).toMatch(/^#/));
  });
  it("모르는 값은 정상으로 떨어지지 않는다", () => {
    expect(beliefBadge("wat").label).not.toBe(beliefBadge("ok").label);
  });
});

describe("fmtRatioOf", () => {
  it("예측 대비 배수를 낸다", () => {
    expect(fmtRatioOf(0.6, 0.3)).toBe("2.0×");
  });
  it("예측이 없으면 대시", () => {
    expect(fmtRatioOf(0.6, null)).toBe("—");
    expect(fmtRatioOf(null, 0.3)).toBe("—");
  });
  it("예측이 0 이면 나눗셈을 하지 않는다", () => {
    expect(fmtRatioOf(0.6, 0)).toBe("—");
  });
});
