// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from "vitest";
import { UI_KEY, UI_SCHEMA, useUiStore } from "./ui";

describe("ui store", () => {
  beforeEach(() => { localStorage.clear(); useUiStore.setState(useUiStore.getInitialState(), true); });

  it("기본값으로 시작한다", () => {
    const s = useUiStore.getState();
    expect(s.tool).toBe("select");
    expect(s.cam).toBe("orbit");
    expect(s.tab).toBe("load");
    expect(s.dockOpen).toBe(true);
  });

  /// 선택이 생기면 속성 탭으로 가고, 해제하면 원래 보던 탭으로 돌아온다 — 탭으로 묶으면서
  /// 이걸 빠뜨리면 세로로 쌓였을 때보다 나빠진다 (세로일 땐 속성이 우연히 보였다).
  it("선택하면 속성 탭, 해제하면 직전 탭", () => {
    const s = () => useUiStore.getState();
    s().setTab("coverage");
    s().openPropsFor();
    expect(s().tab).toBe("props");
    s().restoreTab();
    expect(s().tab).toBe("coverage");
  });

  it("속성 탭에서 또 선택해도 직전 탭을 잃지 않는다", () => {
    const s = () => useUiStore.getState();
    s().setTab("pose");
    s().openPropsFor();
    s().openPropsFor();
    s().restoreTab();
    expect(s().tab).toBe("pose");
  });

  it("사용자가 직접 탭을 바꾸면 그게 새 직전 탭이 된다", () => {
    const s = () => useUiStore.getState();
    s().setTab("coverage");
    s().openPropsFor();
    s().setTab("pose");      // 사용자가 속성에서 벗어남
    s().restoreTab();        // 선택 해제
    expect(s().tab).toBe("pose");
  });

  /// 선택 없이 restoreTab 이 불려도(마운트 직후) 아무 일도 없어야 한다.
  it("속성 탭이 아닐 때 restoreTab 은 아무것도 바꾸지 않는다", () => {
    const s = () => useUiStore.getState();
    s().setTab("coverage");
    s().restoreTab();
    expect(s().tab).toBe("coverage");
  });

  it("도구·카메라 모드를 바꾼다", () => {
    const s = () => useUiStore.getState();
    s().setTool("place");
    s().setCam("fly");
    expect(s().tool).toBe("place");
    expect(s().cam).toBe("fly");
  });

  it("평면도 뷰와 트리 펼침을 기억한다", () => {
    const s = () => useUiStore.getState();
    s().setPlanView({ cx: 60, cy: 0, scale: 2 });
    s().toggleTree("LM");
    expect(s().planView.scale).toBe(2);
    expect(s().treeOpen.LM).toBe(false);   // 기본 true 에서 뒤집힘
    s().toggleTree("MEP");
    expect(s().treeOpen.MEP).toBe(true);   // 기본값이 없는 레이어는 닫힘으로 보고 열린다
  });

  /// 새로고침을 흉내낸다: 저장본을 넣고 rehydrate 를 부른다. setState 로 지워 둔 값이
  /// 실제로 저장본에서 되살아나야 통과한다 — 안 그러면 "기본값이 그대로"를 저장 성공으로 착각한다.
  it("저장본을 다시 읽어 복원한다", async () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ state: { tool: "place", tab: "pose", dockTall: true }, version: UI_SCHEMA }));
    await useUiStore.persist.rehydrate();
    const s = useUiStore.getState();
    expect(s.tool).toBe("place");
    expect(s.tab).toBe("pose");
    expect(s.dockTall).toBe(true);
  });

  it("스키마 버전이 다르면 저장본을 버린다", async () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ state: { tool: "place" }, version: UI_SCHEMA + 1 }));
    await useUiStore.persist.rehydrate();
    expect(useUiStore.getState().tool).toBe("select");
  });

  /// 스펙 §6.2: 새로고침 후 어제 고르던 마커가 속성 탭과 함께 열려 있으면 혼란스럽다.
  it("속성 탭은 저장하지 않는다", () => {
    const s = () => useUiStore.getState();
    s().setTab("coverage");
    s().openPropsFor();
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).state.tab).toBe("coverage");
  });

  /// 탭바에서 속성 탭을 직접 눌러도 setTab 이 도는 경로다. prevTab 까지 "props" 로 오염되면
  /// (1) 저장본이 빈 속성 탭을 저장하고 (2) restoreTab 이 되돌아갈 곳을 잃어 갇힌다.
  it("속성 탭을 직접 눌러도 저장되지 않는다", () => {
    const s = () => useUiStore.getState();
    s().setTab("coverage");
    s().setTab("props");
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).state.tab).not.toBe("props");
    s().restoreTab();
    expect(s().tab).toBe("coverage");   // 갇히지 않는다
  });
});
