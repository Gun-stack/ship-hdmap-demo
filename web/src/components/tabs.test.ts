import { describe, expect, it } from "vitest";
import { EDIT_TABS, TAB_LABELS, nextTab } from "./RightTabs";

describe("right-hand tabs", () => {
  it("네 탭의 라벨이 모두 다르다", () => {
    expect(EDIT_TABS).toHaveLength(4);
    expect(new Set(Object.values(TAB_LABELS)).size).toBe(4);
    for (const t of EDIT_TABS) expect(TAB_LABELS[t]).toBeTruthy();
  });

  /// Tab 키는 끝에서 처음으로 돌아온다 — 안 돌면 마지막 탭에서 키가 죽은 것처럼 보인다.
  it("Tab 은 순환한다", () => {
    expect(nextTab("load", 1)).toBe("coverage");
    expect(nextTab("pose", 1)).toBe("load");
    expect(nextTab("load", -1)).toBe("pose");
  });
});
