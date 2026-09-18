# M5e 편집기 UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 지금까지 쌓은 기능을 사람이 조작할 수 있는 편집기로 만든다 — 도구 모드, 하단 도크, 오른쪽 탭, 법선 기즈모, 카메라 셋, 가상 관측점, 단축키, 화면 상태 저장.

**Architecture:** 웹 Task 1~6 은 `web/` 만, Unity Task 7~10 은 `unity/` 만 건드리므로 두 트랙이 독립이고 병렬로 돌아간다. Task 11 이 브리지로 잇고 Task 12 가 브라우저에서 검증한다. 화면 상태는 zustand `persist` 로 `localStorage` 에 둔다(런타임 의존성 추가 없음). Unity 쪽 가시 판정은 `LandmarkSensor.VisibleFrom` 한 곳에서만 나오게 뽑아 `Sense`·차량 시선·관측점이 갈라질 수 없게 한다. 법선 기즈모는 **새 이벤트를 만들지 않고** 이미 있는 `onFeatureMoved` 경로(`FeatureMovedEvt.normal`)를 탄다.

**Tech Stack:** React 19 + Zustand 5 + Vite + TS + Vitest, Unity 6.3 / C# (EditMode + NUnit)

**Spec:** `docs/superpowers/specs/2026-09-18-m5e-editor-ux-design.md`

---

## 스펙과 달라지는 점 (먼저 읽는다)

계획을 세우며 코드를 읽어 보니 스펙의 전제 다섯이 사실과 달랐거나(A~C) 값어치보다 비쌌다(D·E). 스펙을 고치지 않고 여기에 적어 둔다 — 실행자는 **이 절이 스펙보다 우선**이다.

| # | 스펙이 말한 것 | 코드의 사실 | 이 계획의 처리 |
|---|---|---|---|
| A | §3.3 "숫자에 분모를 붙인다 — `16.8 % (구획·차로 1,236 셀 중)`" | **이미 되어 있다.** `CoveragePanel.tsx:38` 이 `구획·차로 N 셀 · 갑판 전체 M 셀 · g m 격자` 를 그린다 | 작업 없음. 완료 기준 2 의 "분모" 부분은 이미 충족 |
| B | §4.2 "`F` 로 카메라가 선택으로 간다 — 지금은 하이라이트만 켜지고 화면 밖이면 보이지 않는다" | **웹에서 고른 선택은 이미 카메라가 따라간다.** `MapRuntime.Select`(`MapRuntime.cs:187-191`)가 `Orbit.Focus(…, 12f)` 를 부른다. 씬 클릭 선택만 일부러 안 따라간다(같은 프레임 드래그 레이캐스트 때문 — M3b 함정, `Highlight` 주석) | `F` 는 **새 Unity 코드 없이** 웹이 `send("Select", selectedId)` 를 다시 보내는 것으로 끝난다 (Task 6) |
| C | §3.3 "마커 법선을 항상 그린다 — 평면도와 3D 양쪽" | 3D 에서는 마커 Quad 가 **이미 법선 방향으로 서 있다**(`LandmarkMarker.MoveTo` 의 `LookRotation(-n)`). 위에서 내려다보면 안 보이지만, 선택한 마커에는 기즈모 링이 뜬다 | 3D 에 별도 화살표를 더하지 않는다. 평면도에는 저장된 마커 전부에 법선을 그린다 (Task 4) — 스펙이 실제로 없다고 지적한 쪽이 그쪽이다 |
| D | §4.2 "3D 강조를 윤곽선과 완만한 펄스로 바꾼다" | 지금 Halo(노란 Quad, 1.6 배)가 이미 거리와 무관하게 읽힌다 | **안 바꾼다.** 펄스는 장식이고, 이 마일스톤이 §4.2 에서 실제로 여는 것은 "선택이 화면 밖이면 안 보인다"인데 그건 `F`(Task 6)가 닫는다. 대신 감지 표시(`SetSeen`, 초록 테)를 Halo 와 **겹쳐 보이게** 만들어 둘이 다른 질문에 답하게 한다 (Task 8) |
| E | §6 "3D 카메라 자세" 를 `localStorage` 에 | 카메라 자세는 Unity 안에만 있다. 저장하려면 Unity→웹 카메라 자세 이벤트를 새로 만들고 스로틀링해야 한다 — 매 프레임 채널이 하나 더 생긴다 | **뺀다.** 값어치 대비 비용이 맞지 않는다. §6 의 나머지(탭·트리·갑판·모드·슬라이더 전부·도크·평면도 뷰·가림 집합)는 전부 넣는다. 완료 기준 1 의 "카메라"는 **카메라 모드**(궤도/비행/시선)로 읽는다 — 그건 저장한다 |

또 하나. 스펙 §7.2 의 "선택 모드에서 구조물을 클릭해도 `Created` 가 발화하지 않는다" 는 EditMode 에서 **그대로는 테스트할 수 없다**(`Input.GetMouseButtonDown` 을 가짜로 만들 수 없다). 클릭 판정을 순수 함수 `LandmarkPlacer.Decide(tool, hitMarker, hitStructure)` 로 뽑아 그것을 테스트한다 (Task 7).

---

## Global Constraints

- 웹 들여쓰기 2 칸, C# 4 칸 (기존 파일 그대로)
- **Unity 는 HTTP 를 호출하지 않는다.** API 조회는 웹이 하고 브리지로 넘긴다
- **새 런타임 의존성 없음.** devDependency 는 `jsdom` 하나만 는다 (Task 1 에서 이유를 적는다). `@testing-library/*` 는 쓰지 않는다 — 탭 전환 규칙은 스토어 액션이라 DOM 없이 테스트된다
- 단축키는 **입력 필드에 포커스가 있으면 전부 먹지 않는다**
- 자유 비행 키(`WASD`·방향키·`Q`·`E`·`Shift`)는 **자유 비행 모드에서 3D 캔버스에 포커스가 있을 때만** 읽는다. 웹의 전역 핸들러는 그때 손을 뗀다
- `localStorage` 키는 데이터셋별, 스키마 버전 1. 버전이 다르면 버리고 기본값
- **선택된 객체는 저장하지 않는다** (스펙 §6.2)
- **새 MonoBehaviour 는 씬에 두지 않는다.** `Assets/Scenes/Demo.unity` 에는 `Map`(MapRuntime)과 카메라뿐이고 나머지는 전부 `MapRuntime.InitForTest()` 가 만든다. 씬에 의존하면 WebGL 빌드에서만 사라진다 (M3a 함정)
- 평면도 좌표는 **SVG 공간**(x 선수, **y 아래 = ship −y**)이다. `MiniMap.bbox` 가 이미 `-p[1]` 로 뒤집어 돌려준다. `PlanView.cx/cy` 도 같은 공간이다 — 한 함수 안에서 두 번 뒤집지 않는다
- 커밋 메시지는 한 줄 영문 요약. 자명하지 않은 변경은 본문에 왜 그랬는지 적는다. 본문 끝에:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01PVuA3qih1j2w41MkuPAok4`

**시작 기준선:** Vitest 38 (7 파일), Unity EditMode 134 (`[Test]` 전수, `[TestCase]`·`[UnityTest]` 없음), api 는 이 마일스톤에서 건드리지 않는다.

---

## File Structure

| 파일 | 책임 | 트랙 |
| --- | --- | --- |
| `web/src/store/ui.ts` | **신규.** 화면 상태 + `persist` | 웹 T1 |
| `web/src/store/editor.ts` | `persist` 슬라이스, 노이즈·시간배율을 패널에서 끌어올림 | 웹 T2 |
| `web/src/components/DrivePanel.tsx` | 지역 `useState` 두 개를 스토어로 넘김 | 웹 T2 |
| `web/src/geo/deck.ts` | **신규.** `bbox`·`ringPath`·`linePath`·`pickDeck` 을 컴포넌트 밖으로 | 웹 T3 |
| `web/src/geo/plan.ts` | **신규.** `viewBoxOf`·`fitTo`·`zoomAt`·`screenToPlan`·`LEGEND` | 웹 T3 |
| `web/src/components/PlanDock.tsx` | **신규.** 전폭 평면도 + 범례 + 확대·이동 (MiniMap 흡수) | 웹 T4 |
| `web/src/components/MiniMap.tsx` | **삭제** | 웹 T4 |
| `web/src/components/RightTabs.tsx` | **신규.** 탭 셸, 선택 시 속성 전환 | 웹 T5 |
| `web/src/components/Toolbar.tsx` | **신규.** 도구·카메라 모드 버튼 | 웹 T5 |
| `web/src/components/LayerTree.tsx` | 펼침 상태를 `ui.ts` 로 | 웹 T5 |
| `web/src/App.tsx`, `App.css` | 3칼럼 → 툴바 + 3칼럼 + 하단 도크 | 웹 T5 |
| `web/src/ui/keys.ts` | **신규.** 키 → 명령 순수 매핑 | 웹 T6 |
| `web/src/components/Shortcuts.tsx` | **신규.** 전역 키 배선 + `?` 도움말 | 웹 T6 |
| `unity/…/Landmarks/LandmarkPlacer.cs` | 도구 모드, `Decide`, `ProbeAt` | Unity T7 |
| `unity/…/Localization/LandmarkSensor.cs` | `GeometricMiss`·`VisibleFrom` 추출 | Unity T8 |
| `unity/…/Landmarks/LandmarkMarker.cs` | `SetSeen` (감지 표시) | Unity T8 |
| `unity/…/Vehicle/SensorView.cs` | **신규.** 시야 콘·사거리 호 + 감지 표시 (차량 시선·관측점 공용) | Unity T8 |
| `unity/…/Vehicle/OrbitCamera.cs` | 모드 셋 (궤도·자유 비행·차량 시선) | Unity T9 |
| `unity/…/Landmarks/NormalGizmo.cs` | **신규.** 법선 원형 핸들 | Unity T10 |
| `unity/…/Vehicle/ProbeView.cs` | **신규.** 가상 관측점 | 통합 T11 |
| `unity/…/Bridge/BridgeMessages.cs` | `SetTool`·`SetCamMode`·`SetNormal` | 통합 T11 |
| `unity/…/Bridge/MapRuntime.cs` | 새 메시지 배선, 새 컴포넌트 생성, 빈 선택 emit | 통합 T11 |
| `web/src/bridge/useShipUnity.ts` | 새 메시지 이름, 재로드 후 상태 재전송, 법선 변경 시 커버리지 재계산 | 통합 T11 |

---

### Task 1: 화면 상태 스토어

모든 웹 작업이 여기에 쓰기 때문에 먼저 만든다. `editor.ts` 는 지도·주행 데이터를 갖고, `ui.ts` 는 화면만 갖는다.

`jsdom` 을 devDependency 로 더한다. 이유: `persist` 는 `localStorage` 를 쓰는데 이 저장소의 Vitest 는 `environment: "node"` 이고 **Node 22 에는 전역 `localStorage` 가 없다**(확인함 — `typeof globalThis.localStorage` 가 `"undefined"`). 전역 환경을 바꾸면 순수 함수 테스트 38 개가 공연히 느려지므로 **파일 단위 docblock** 으로만 켠다.

**Files:**
- Create: `web/src/store/ui.ts`
- Modify: `web/package.json` (devDependency `jsdom`)
- Test: `web/src/store/ui.test.ts`

**Interfaces:**
- Produces: `type Tool = "select" | "place" | "probe"`, `type CamMode = "orbit" | "fly" | "driver"`, `type RightTab = "load" | "coverage" | "props" | "pose"`
- Produces: `type PlanView = { cx: number; cy: number; scale: number }` — **SVG 공간**, `cy` 는 아래가 +
- Produces: `UI_SCHEMA = 1`, `UI_KEY = "shiphdmap.ui.roro-demo-01"`
- Produces: `useUiStore` — 상태 `tool`, `cam`, `tab`, `prevTab`, `dockOpen`, `dockTall`, `treeOpen: Record<string, boolean>`, `planView`, `helpOpen`; 액션 `setTool`, `setCam`, `setTab`, `openPropsFor`, `restoreTab`, `toggleTree`, `setPlanView`, `toggleDock`, `toggleDockTall`, `toggleHelp`

- [ ] **Step 1: jsdom 을 더한다**

```bash
cd web && pnpm add -D jsdom
```

- [ ] **Step 2: 실패하는 테스트를 쓴다**

`web/src/store/ui.test.ts`:

```ts
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
});
```

- [ ] **Step 3: 실패를 확인한다**

Run: `cd web && pnpm vitest run ui`
Expected: FAIL — `./ui` 모듈이 없다

- [ ] **Step 4: 스토어를 만든다**

`web/src/store/ui.ts`:

```ts
import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Tool = "select" | "place" | "probe";
export type CamMode = "orbit" | "fly" | "driver";
export type RightTab = "load" | "coverage" | "props" | "pose";

/** Bump when the stored shape changes; a mismatch throws the saved state away rather than migrating it. */
export const UI_SCHEMA = 1;
export const UI_KEY = "shiphdmap.ui.roro-demo-01";

/** Plan-view window, in SVG space: x forward, y DOWN (= ship -y), matching what geo/deck.bbox returns. */
export type PlanView = { cx: number; cy: number; scale: number };

type UiState = {
  tool: Tool; cam: CamMode; tab: RightTab; prevTab: RightTab;
  dockOpen: boolean; dockTall: boolean; helpOpen: boolean;
  treeOpen: Record<string, boolean>;
  planView: PlanView;
  setTool: (t: Tool) => void;
  setCam: (c: CamMode) => void;
  setTab: (t: RightTab) => void;
  /** A selection happened: show 속성, remembering where the user was. */
  openPropsFor: () => void;
  /** Selection cleared: go back to whatever they were looking at. No-op unless 속성 is open. */
  restoreTab: () => void;
  toggleTree: (layer: string) => void;
  setPlanView: (v: PlanView) => void;
  toggleDock: () => void;
  toggleDockTall: () => void;
  toggleHelp: () => void;
};

const DEFAULTS = {
  tool: "select" as Tool, cam: "orbit" as CamMode, tab: "load" as RightTab, prevTab: "load" as RightTab,
  dockOpen: true, dockTall: false, helpOpen: false,
  treeOpen: { LM: true, A2: true, B2: true, C: true } as Record<string, boolean>,
  planView: { cx: 60, cy: 0, scale: 1 } as PlanView,
};

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      ...DEFAULTS,
      setTool: (tool) => set({ tool }),
      setCam: (cam) => set({ cam }),
      // a deliberate tab change also becomes the place we return to after a selection ends
      setTab: (tab) => set({ tab, prevTab: tab }),
      openPropsFor: () => set((s) => (s.tab === "props" ? {} : { tab: "props" as RightTab, prevTab: s.tab })),
      restoreTab: () => set((s) => (s.tab === "props" ? { tab: s.prevTab } : {})),
      toggleTree: (layer) => set((s) => ({ treeOpen: { ...s.treeOpen, [layer]: !(s.treeOpen[layer] ?? false) } })),
      setPlanView: (planView) => set({ planView }),
      toggleDock: () => set((s) => ({ dockOpen: !s.dockOpen })),
      toggleDockTall: () => set((s) => ({ dockTall: !s.dockTall })),
      toggleHelp: () => set((s) => ({ helpOpen: !s.helpOpen })),
    }),
    {
      name: UI_KEY,
      version: UI_SCHEMA,
      // `tab` is stored as prevTab whenever 속성 is open: selection is deliberately NOT persisted (spec §6.2),
      // so restoring straight into an empty 속성 tab would be a panel about nothing. helpOpen is transient too.
      partialize: (s) => ({
        tool: s.tool, cam: s.cam, tab: s.tab === "props" ? s.prevTab : s.tab, prevTab: s.prevTab,
        dockOpen: s.dockOpen, dockTall: s.dockTall, treeOpen: s.treeOpen, planView: s.planView,
      }),
      migrate: () => ({ ...DEFAULTS }),   // no migrations: a shape change means start clean
    },
  ),
);
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit`
Expected: PASS. 38 + 10 = **48**

- [ ] **Step 6: 커밋**

```bash
git add web/src/store/ui.ts web/src/store/ui.test.ts web/package.json web/pnpm-lock.yaml
git commit   # feat: a screen-state store that survives a reload
```

---

### Task 2: 새로고침에도 남는 편집 상태

완료 기준 1 — "새로고침해도 탭·갑판·모드·슬라이더·카메라(모드)·도크 높이가 그대로다" — 는 `ui.ts` 만으로는 못 지킨다. 갑판(`deckFilter`)·모드·커버리지 슬라이더·믿음 슬라이더·가림 집합은 **`editor.ts` 에 있고**, 노이즈 슬라이더 넷과 시간 배율은 **`DrivePanel` 의 지역 `useState` 라서 어디에도 없다.** 먼저 끌어올린 다음 한 번에 저장한다.

**Files:**
- Modify: `web/src/store/editor.ts`, `web/src/components/DrivePanel.tsx:28-33,66-75`
- Test: `web/src/store/editor.test.ts` (docblock + 왕복 테스트 1)

**Interfaces:**
- Produces: `EditorState` 에 `noise: NoiseParams` (`{sigma_r, sigma_theta, sigma_alpha, sigma_gps}`), `timeScale: number`, `setNoise: (p: Partial<NoiseParams>) => void`, `setTimeScale: (v: number) => void`
- Produces: `EDITOR_KEY = "shiphdmap.editor.roro-demo-01"`, `EDITOR_SCHEMA = 1`
- Consumes: 없음 (Task 1 과 독립 — 병렬 가능)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/store/editor.test.ts` 맨 위 1 행에 docblock 을 넣고(`vi.mock` 보다 위), `beforeEach` 에 `localStorage.clear()` 를 더한 뒤 테스트 하나를 붙인다:

```ts
// @vitest-environment jsdom
```

```ts
  beforeEach(() => { localStorage.clear(); useEditorStore.setState(useEditorStore.getInitialState()); });
```

```ts
  /// 완료 기준 1. 검증할 때마다 슬라이더를 다시 맞추던 것이 이 마일스톤에서 없어진다.
  it("갑판·모드·슬라이더·가림 집합이 새로고침을 넘긴다", async () => {
    const s = () => useEditorStore.getState();
    s().setDeckFilter("D2");
    s().setMode("drive");
    s().setCoverageParams({ max_dist_m: 31 });
    s().setBeliefParams({ k: 4.5 });
    s().setNoise({ sigma_r: 0.44 });
    s().setTimeScale(20);
    s().toggleOccluded("LM-0003");

    useEditorStore.setState(useEditorStore.getInitialState());   // 새로고침 흉내: 메모리를 비운다
    expect(s().deckFilter).toBe("all");
    await useEditorStore.persist.rehydrate();

    expect(s().deckFilter).toBe("D2");
    expect(s().mode).toBe("drive");
    expect(s().coverageParams.max_dist_m).toBe(31);
    expect(s().beliefParams.k).toBe(4.5);
    expect(s().noise.sigma_r).toBe(0.44);
    expect(s().timeScale).toBe(20);
    expect(s().occluded).toEqual(["LM-0003"]);
  });

  /// 지도 데이터는 저장하지 않는다 — 서버가 진실이고, 낡은 사본이 되살아나면 버전 표시가 거짓말을 한다.
  it("지도 데이터는 저장하지 않는다", async () => {
    await useEditorStore.getState().load("ds1");
    const saved = JSON.parse(localStorage.getItem(EDITOR_KEY)!).state;
    expect(saved.features).toBeUndefined();
    expect(saved.dataset).toBeUndefined();
    expect(saved.coverage).toBeUndefined();
    expect(saved.selectedId).toBeUndefined();
  });
```

`import { EDITOR_KEY, useEditorStore, visibleFeatures } from "./editor";` 로 import 를 고친다.

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run editor`
Expected: FAIL — `setNoise` 가 없고 `EDITOR_KEY` 를 export 하지 않는다

- [ ] **Step 3: 노이즈·시간 배율을 스토어로 올린다**

`web/src/store/editor.ts`:

```ts
import { persist } from "zustand/middleware";
```

`EditorState` 타입에 더한다:

```ts
  noise: NoiseParams;
  timeScale: number;
  setNoise: (p: Partial<NoiseParams>) => void;
  setTimeScale: (v: number) => void;
```

파일 상단(`Draft` 옆)에:

```ts
/** Sensor noise on the wire: angles in degrees, matching SetNoiseMsg. Lived in DrivePanel until M5e needed it to survive a reload. */
export type NoiseParams = { sigma_r: number; sigma_theta: number; sigma_alpha: number; sigma_gps: number };
export const EDITOR_KEY = "shiphdmap.editor.roro-demo-01";
export const EDITOR_SCHEMA = 1;
```

초기값 블록(`belief: null, occluded: []` 줄 옆)에:

```ts
  noise: { sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2, sigma_gps: 0.5 }, timeScale: 1,
```

액션(`setBeliefParams` 옆)에:

```ts
  setNoise: (p) => set((s) => ({ noise: { ...s.noise, ...p } })),
  setTimeScale: (timeScale) => set({ timeScale }),
```

- [ ] **Step 4: `persist` 로 감싼다**

`create<EditorState>()((set, get) => ({ … }))` 를 `create<EditorState>()(persist((set, get) => ({ … }), { … }))` 로 바꾼다. 본문은 그대로 두고 닫는 괄호 뒤에 옵션만 붙인다:

```ts
  {
    name: EDITOR_KEY,
    version: EDITOR_SCHEMA,
    // Only what the viewer set by hand. Map data, coverage results, drafts, the selection and the live
    // localization/belief feeds all come back from the server or from Unity -- persisting a stale copy
    // would put yesterday's numbers next to today's dataset version.
    partialize: (s) => ({
      deckFilter: s.deckFilter, mode: s.mode, coverageMode: s.coverageMode, coverageParams: s.coverageParams,
      beliefParams: s.beliefParams, noise: s.noise, timeScale: s.timeScale, occluded: s.occluded,
    }),
    migrate: () => ({}),   // a shape change means start from the store's own defaults
  },
```

- [ ] **Step 5: `DrivePanel` 을 스토어에 맞춘다**

`DrivePanel.tsx:30-31` 의 `useState` 두 줄을 지우고 구조분해에 더한다:

```tsx
  const { localization: l, setMode, scenarioLog, clearLog, datasetId, decks, deckFilter, occluded, belief: b,
    beliefParams: bp, setBeliefParams, setError, noise: sig, setNoise, timeScale: scale, setTimeScale } = useEditorStore();
```

슬라이더의 `onChange` 를 `setSig({ ...sig, [s.key]: … })` 에서 `setNoise({ [s.key]: Number(e.target.value) })` 로 바꾸고, 배율 `select` 의 `onChange` 를:

```tsx
        <select value={scale} onChange={(e) => { const v = Number(e.target.value); setTimeScale(v); send("SetTimeScale", { scale: v }); }} style={{ flex: "0 0 auto" }}>
```

나머지(`commit`, `start`)는 `sig`·`scale` 이름을 그대로 읽으므로 바뀌지 않는다.

- [ ] **Step 6: 테스트·빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: PASS. 48 + 2 = **50**

- [ ] **Step 7: 커밋**

```bash
git add web/src/store/editor.ts web/src/store/editor.test.ts web/src/components/DrivePanel.tsx
git commit   # feat: stop making the operator redial every slider after a reload
```

---

### Task 3: 평면도 기하 — `geo/deck.ts` 와 `geo/plan.ts`

`MiniMap.tsx` 안에 순수 함수 넷이 갇혀 있어서 `CoveragePanel`·`DrivePanel` 이 컴포넌트 파일을 import 한다. 밖으로 빼고, 확대·이동 산수를 새로 만든다.

**주의:** `bbox` 는 **이미 y 를 뒤집어** 돌려준다(`ys = ring.map(p => -p[1])`). 그러니 `Box`·`PlanView` 는 전부 SVG 공간이고 `viewBoxOf` 안에서 다시 뒤집으면 안 된다. 계획 초안이 여기서 `-v.cy` 를 써서 배가 위아래로 뒤집혔다.

**Files:**
- Create: `web/src/geo/deck.ts`, `web/src/geo/plan.ts`
- Modify: `web/src/components/MiniMap.tsx` (함수 제거), `web/src/components/MiniMap.test.ts` → `web/src/geo/deck.test.ts` 로 이동, `web/src/components/CoveragePanel.tsx:4`, `web/src/components/DrivePanel.tsx:8`
- Test: `web/src/geo/deck.test.ts` (이동), `web/src/geo/plan.test.ts` (신규)

**Interfaces:**
- Consumes: Task 1 의 `PlanView`
- Produces: `geo/deck.ts` → `bbox(ring: number[][], margin?: number): Box`, `ringPath(pts: number[][]): string`, `linePath(pts: number[][]): string`, `pickDeck<T extends {id: string; outline: number[][]}>(decks: T[], filter: string): T | undefined`, `type Box = { x: number; y: number; w: number; h: number }`
- Produces: `geo/plan.ts` → `viewBoxOf(deck: Box, v: PlanView): string`, `fitTo(deck: Box, target: Box): PlanView`, `zoomAt(deck: Box, v: PlanView, anchor: {x: number; y: number}, k: number): PlanView`, `screenToPlan(pt: {x: number; y: number}, svg: SVGSVGElement): {x: number; y: number}`, `boxOfPoint(x: number, y: number, r?: number): Box`, `LEGEND: {label: string; color: string}[]`, `MIN_SCALE = 1`, `MAX_SCALE = 40`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/geo/plan.test.ts`:

```ts
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
    const v1 = zoomAt(DECK, v0, anchor, 2);
    const frac = (v: typeof v0, a: number, span: number) => (a - (v.cx - span / v.scale / 2)) / (span / v.scale);
    expect(frac(v1, anchor.x, DECK.w)).toBeCloseTo(frac(v0, anchor.x, DECK.w), 6);
  });
  it("배율을 1..40 으로 묶는다", () => {
    expect(zoomAt(DECK, { cx: 60, cy: 0, scale: 1 }, { x: 60, y: 0 }, 0.5).scale).toBe(1);
    expect(zoomAt(DECK, { cx: 60, cy: 0, scale: 30 }, { x: 60, y: 0 }, 4).scale).toBe(MAX_SCALE);
  });
});

/// 범례가 히트맵과 다른 색을 쓰면 화면이 거짓말을 한다. cellColor 를 그대로 부르는 것으로는
/// 같은 식을 두 번 쓴 것에 불과하므로, 범례가 실제 셀 모양을 어떤 색으로 칠하는지 값으로 못박는다.
describe("LEGEND", () => {
  it("네 항목이고 blind 는 히트맵과 같은 색이다", () => {
    expect(LEGEND).toHaveLength(4);
    expect(LEGEND[0].color).toBe(BLIND_COLOR);
    expect(LEGEND[0].color).toBe(cellColor({ x: 0, y: 0, n: 0 }));
  });
  it("weak 은 주황, ok 는 초록 쪽이다", () => {
    const weak = LEGEND[1].color, ok = LEGEND[2].color;
    expect(weak).not.toBe(ok);
    const g = (hex: string) => parseInt(hex.slice(3, 5), 16);
    expect(g(ok)).toBeGreaterThan(g(weak));      // stability 가 좋을수록 초록이 강해진다
    expect(ok).toBe("#3cc850");                  // cellColor(stability >= 2) 가 saturate 하는 값
  });
});
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run plan`
Expected: FAIL — `./plan` 이 없다

- [ ] **Step 3: `geo/deck.ts` 로 옮긴다**

`MiniMap.tsx:5-19` 의 `bbox`·`ringPath`·`linePath`·`pickDeck` 을 `web/src/geo/deck.ts` 로 **이동**한다(복사 아님). 본문은 한 글자도 바꾸지 않고, 파일 맨 위에 타입만 더한다:

```ts
/** A rectangle in plan (SVG) space: x forward, y DOWN. bbox already flips ship y, so nothing downstream flips it again. */
export type Box = { x: number; y: number; w: number; h: number };
```

`MiniMap.tsx` 는 `import { bbox, linePath, pickDeck, ringPath } from "../geo/deck";` 로 받아 쓰게 두고(Task 4 에서 통째로 지운다), `CoveragePanel.tsx:4` 와 `DrivePanel.tsx:8` 의 `from "./MiniMap"` 을 `from "../geo/deck"` 으로 바꾼다.

`web/src/components/MiniMap.test.ts` 를 `web/src/geo/deck.test.ts` 로 `git mv` 하고, import 를 `from "./deck"` 으로, `describe` 이름을 `"deck geometry"` 로 바꾼다. **단언은 한 줄도 건드리지 않는다** — 그게 이동이 옳았다는 확인이다.

`store/editor.ts:38-40` 의 주석에서 `MiniMap.pickDeck` 을 `geo/deck.pickDeck` 으로 고친다. 규칙 자체(갑판 id 는 액션 인자로 받는다)는 그대로다 — `geo/` 로 옮겨도 스토어는 여전히 `pickDeck` 을 import 하지 않는다.

- [ ] **Step 4: `geo/plan.ts` 를 만든다**

```ts
import { cellColor } from "./coverage";
import type { Box } from "./deck";
import type { PlanView } from "../store/ui";

export const MIN_SCALE = 1, MAX_SCALE = 40;
const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v));

/**
 * SVG viewBox for the current pan/zoom. Everything here is already in plan space (y down) because
 * geo/deck.bbox flipped ship y once, at the edge. Flipping again here would stand the ship on its head.
 */
export function viewBoxOf(deck: Box, v: PlanView): string {
  const w = deck.w / v.scale, h = deck.h / v.scale;
  return `${v.cx - w / 2} ${v.cy - h / 2} ${w} ${h}`;
}

/** A tiny box around a plan-space point, so fitTo has something with extent to frame. */
export function boxOfPoint(x: number, y: number, r = 1.5): Box { return { x: x - r, y: y - r, w: 2 * r, h: 2 * r }; }

/** Centre on a target box and zoom until it fills the view, never below the whole deck or above MAX_SCALE. */
export function fitTo(deck: Box, target: Box): PlanView {
  const pad = 6;
  const s = Math.min(deck.w / Math.max(target.w + pad, 1e-6), deck.h / Math.max(target.h + pad, 1e-6));
  return { cx: target.x + target.w / 2, cy: target.y + target.h / 2, scale: clamp(s, MIN_SCALE, MAX_SCALE) };
}

/**
 * Zoom by `k` about `anchor` (plan space), keeping whatever is under the cursor under the cursor.
 * The window half-width scales by s_old/s_new, so the centre moves the same fraction toward the anchor.
 */
export function zoomAt(deck: Box, v: PlanView, anchor: { x: number; y: number }, k: number): PlanView {
  const scale = clamp(v.scale * k, MIN_SCALE, MAX_SCALE);
  const f = v.scale / scale;
  return { cx: anchor.x - (anchor.x - v.cx) * f, cy: anchor.y - (anchor.y - v.cy) * f, scale };
}

/** Screen point -> the SVG's own user units, via its live transform so pan and zoom need no duplicate maths. */
export function screenToPlan(pt: { x: number; y: number }, svg: SVGSVGElement): { x: number; y: number } {
  const p = svg.createSVGPoint(); p.x = pt.x; p.y = pt.y;
  const m = svg.getScreenCTM();
  if (!m) return { x: pt.x, y: pt.y };      // not laid out yet (jsdom, or a hidden dock)
  const u = p.matrixTransform(m.inverse());
  return { x: u.x, y: u.y };
}

/// The legend pulls its colours from cellColor so the key and the heatmap cannot drift apart.
/// The two ratios the panel prints share this vocabulary: blind = the 사각지대 number, weak = 허용오차 미달.
export const LEGEND: { label: string; color: string }[] = [
  { label: "blind · 자세를 잡을 수 없다", color: cellColor({ x: 0, y: 0, n: 0 }) },
  { label: "weak · 허용오차 미달", color: cellColor({ x: 0, y: 0, n: 2, stability: 0.5 }) },
  { label: "ok · 허용오차 충족", color: cellColor({ x: 0, y: 0, n: 3, stability: 2 }) },
  // out-of-scope 셀은 cellOpacity 가 0.18 로 흐리게 칠할 뿐 색은 그 셀의 색 그대로다. 범례의 이 칸만
  // 예외적으로 고정 회색인 이유는 "무슨 색이냐"가 아니라 "왜 흐리냐"를 설명하는 항목이기 때문이다.
  { label: "흐림 · 차가 갈 일이 없어 숫자에서 뺀 곳", color: "#bbb" },
];
```

`BLIND_COLOR` 는 이 파일에서 import 하지 않는다 — `cellColor({n: 0})` 이 그 값을 낸다. 테스트만 둘이 같은지 비교하려고 import 한다.

- [ ] **Step 5: 테스트 통과 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit`
Expected: PASS. 50 + 10 = **60** (기존 `MiniMap.test.ts` 3 개는 파일만 옮겨 갔으므로 총계에 더해지지 않는다)

- [ ] **Step 6: 커밋**

```bash
git add -A web/src/geo web/src/components/MiniMap.tsx web/src/components/MiniMap.test.ts web/src/components/CoveragePanel.tsx web/src/components/DrivePanel.tsx web/src/store/editor.ts
git commit   # refactor: plan geometry out of the component, plus the pan/zoom maths
```

---

### Task 4: 하단 평면도 도크

갑판은 120 × 24 m 로 5:1 가로 그림인데 지금은 좁은 왼쪽 칼럼 맨 아래에 눌려 있다. 전폭으로 내리고, 확대·이동과 범례를 붙이고, **저장된 마커의 법선과 라벨**을 그린다. `MiniMap` 이 그리던 초안 원과 후보 법선 막대는 **그대로 가져간다** — M5c 가 후보 법선을 눈으로 보려고 넣은 것이라 잃으면 퇴보다.

**Files:**
- Create: `web/src/components/PlanDock.tsx`
- Delete: `web/src/components/MiniMap.tsx`
- Modify: `web/src/App.tsx`, `web/src/App.css`
- Test: 없음 (그리기 전용. 산수는 Task 3 이 전부 테스트했다)

**Interfaces:**
- Consumes: Task 1 의 `useUiStore`(`planView`, `setPlanView`, `dockOpen`, `dockTall`, `toggleDock`, `toggleDockTall`), Task 3 의 `geo/deck`·`geo/plan`
- Produces: `PlanDock()`

- [ ] **Step 1: `PlanDock` 을 만든다**

`web/src/components/PlanDock.tsx`:

```tsx
import { useRef } from "react";
import { useEditorStore, visibleFeatures } from "../store/editor";
import { useUiStore } from "../store/ui";
import { bbox, linePath, pickDeck, ringPath } from "../geo/deck";
import { LEGEND, fitTo, screenToPlan, viewBoxOf, zoomAt } from "../geo/plan";
import { cellColor, cellOpacity } from "../geo/coverage";

export function PlanDock() {
  const s = useEditorStore();
  const ui = useUiStore();
  const svgRef = useRef<SVGSVGElement>(null);
  const drag = useRef<{ x: number; y: number } | null>(null);

  if (!ui.dockOpen) return <div className="dock collapsed"><button className="btn" onClick={ui.toggleDock}>평면도 펴기</button></div>;
  const deck = pickDeck(s.decks, s.deckFilter);
  if (!deck) return <div className="dock collapsed"><span style={{ color: "#888" }}>갑판 없음</span></div>;

  const box = bbox(deck.outline, 3);
  const feats = visibleFeatures(s);
  const zoomed = ui.planView.scale >= 4;   // labels only once they would be readable
  const at = (e: { clientX: number; clientY: number }) => screenToPlan({ x: e.clientX, y: e.clientY }, svgRef.current!);

  return (
    <div className={`dock${ui.dockTall ? " tall" : ""}`}>
      <div className="dockbar">
        <span>{deck.name} 평면도 · ×{ui.planView.scale.toFixed(1)}</span>
        <button className="btn" onClick={() => ui.setPlanView(fitTo(box, box))}>전체 (0)</button>
        <button className="btn" onClick={ui.toggleDockTall}>{ui.dockTall ? "낮게" : "크게"}</button>
        <button className="btn" onClick={ui.toggleDock}>접기</button>
        <span className="legend">
          {LEGEND.map((l) => <span key={l.label}><i style={{ background: l.color }} />{l.label}</span>)}
        </span>
      </div>
      <svg ref={svgRef} viewBox={viewBoxOf(box, ui.planView)} className="plan" preserveAspectRatio="xMidYMid meet"
        onWheel={(e) => ui.setPlanView(zoomAt(box, ui.planView, at(e), e.deltaY < 0 ? 1.2 : 1 / 1.2))}
        onPointerDown={(e) => { if (e.button !== 0) return; drag.current = at(e); e.currentTarget.setPointerCapture(e.pointerId); }}
        onPointerMove={(e) => {
          if (!drag.current) return;
          // Grab the plan and pull it: the point under the cursor must not slide, so the window moves the
          // opposite way. `at()` re-reads the live transform, so this stays right at every zoom level.
          const p = at(e);
          ui.setPlanView({ ...ui.planView, cx: ui.planView.cx - (p.x - drag.current.x), cy: ui.planView.cy - (p.y - drag.current.y) });
        }}
        onPointerUp={(e) => { drag.current = null; e.currentTarget.releasePointerCapture(e.pointerId); }}>
        {s.coverage?.cells.map((c, i) => (
          <rect key={i} x={c.x - s.coverage!.grid_m / 2} y={-c.y - s.coverage!.grid_m / 2}
            width={s.coverage!.grid_m} height={s.coverage!.grid_m}
            fill={cellColor(c)} fillOpacity={cellOpacity(c)} stroke="none" pointerEvents="none" />
        ))}
        <path d={ringPath(deck.outline)} fill="none" stroke="#7a8" strokeWidth={0.4} />
        {feats.filter((f) => f.layer === "A2").map((f) => <path key={f.id} d={linePath(f.geometry.coordinates as number[][])} fill="none" stroke="#1e88e5" strokeWidth={0.4} strokeDasharray="2 1" onClick={() => s.select(f.id)} />)}
        {feats.filter((f) => f.layer === "B2").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="rgba(30,136,229,.15)" stroke="#1e88e5" strokeWidth={0.2} onClick={() => s.select(f.id)} />)}
        {feats.filter((f) => f.layer === "C" && f.geometry.type === "Polygon").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="#999" stroke="#666" strokeWidth={0.2} onClick={() => s.select(f.id)} />)}
        {feats.filter((f) => f.layer === "LM").map((f) => {
          const c = f.geometry.coordinates as number[];
          const n = (f.props.normal as number[] | undefined) ?? [1, 0, 0];
          const sel = s.selectedId === f.id;
          const dim = s.occluded.includes(f.id);
          return (
            <g key={f.id} onClick={() => s.select(f.id)} opacity={dim ? 0.35 : 1}>
              <rect x={c[0] - 0.7} y={-c[1] - 0.7} width={1.4} height={1.4} transform={`rotate(45 ${c[0]} ${-c[1]})`}
                fill={sel ? "#ffb300" : "#e53935"} stroke={sel ? "#000" : "none"} strokeWidth={0.2} />
              {/* the normal decides coverage far more than the position does (M5c/M5d), so draw it on saved
                  markers too -- M5c only drew it on candidates, which is why a bad normal was invisible */}
              <line x1={c[0]} y1={-c[1]} x2={c[0] + n[0] * 2} y2={-c[1] - n[1] * 2} stroke={sel ? "#ffb300" : "#e53935"} strokeWidth={0.25} />
              {zoomed && <text x={c[0] + 0.9} y={-c[1] - 0.9} fontSize={1.2} fill="#333" pointerEvents="none">{f.id}</text>}
            </g>
          );
        })}
        {Object.values(s.drafts).map((d) => { const c = d.geometry.coordinates as number[]; return <circle key={d.tempId} cx={c[0]} cy={-c[1]} r={0.9} fill="none" stroke="#ff9800" strokeWidth={0.4} />; })}
        {/* Candidates carry their normal as a stub line: the normal decides heading accuracy but otherwise
            hides inside the props JSON with no way to eyeball it. */}
        {s.candidates.map((c, i) => (
          <g key={`cand-${i}`} pointerEvents="none">
            <circle cx={c.x} cy={-c.y} r={0.8} fill="none" stroke="#7b1fa2" strokeWidth={0.35} />
            <line x1={c.x} y1={-c.y} x2={c.x + Math.cos((c.phi_deg * Math.PI) / 180) * 2}
              y2={-c.y - Math.sin((c.phi_deg * Math.PI) / 180) * 2} stroke="#7b1fa2" strokeWidth={0.25} />
          </g>
        ))}
      </svg>
    </div>
  );
}
```

- [ ] **Step 2: `MiniMap` 을 지우고 `App` 에 도크를 단다**

```bash
git rm web/src/components/MiniMap.tsx
```

`App.tsx`: `import { MiniMap } …` 을 `import { PlanDock } from "./components/PlanDock";` 로 바꾸고, 왼쪽 `<aside>` 에서 `<MiniMap />` 을 지운 뒤 `</aside>`(오른쪽) 다음에 `<PlanDock />` 을 넣는다. (오른쪽 `<aside>` 자체는 Task 5 에서 바뀐다.)

- [ ] **Step 3: CSS 를 더한다**

`App.css:3` 의 그리드에 도크 행을 넣고 — 툴바 행은 Task 5 가 더하므로 지금은 도크만:

```css
.app { height: 100%; display: grid; grid-template-columns: 300px 1fr 340px;
       grid-template-rows: 40px 1fr auto 28px;
       grid-template-areas: "top top top" "left center right" "dock dock dock" "status status status"; }
.dock { grid-area: dock; border-top: 1px solid #ccc; background: #fff; height: 180px; display: flex; flex-direction: column; }
.dock.tall { height: 340px; }
.dock.collapsed { height: 32px; flex-direction: row; align-items: center; gap: 8px; padding: 0 12px; }
.dockbar { display: flex; align-items: center; gap: 8px; padding: 2px 8px; font-size: 12px; border-bottom: 1px solid #eee; }
.dockbar .legend { display: flex; gap: 10px; margin-left: auto; color: #555; }
.dockbar .legend i { display: inline-block; width: 10px; height: 10px; margin-right: 4px; vertical-align: -1px; }
.plan { flex: 1; min-height: 0; width: 100%; background: #f3f6f9; touch-action: none; cursor: grab; }
.plan:active { cursor: grabbing; }
```

`.center` 의 `min-height: 0; overflow: hidden` 과 그 주석은 **그대로 둔다** — Unity 캔버스가 매 프레임 그리드 항목 크기로 백버퍼를 맞추기 때문에 이게 없으면 행이 무한히 자란다 (M3a 함정).

- [ ] **Step 4: 테스트·빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: PASS 60, 빌드 성공

- [ ] **Step 5: 커밋**

```bash
git add -A web/src
git commit   # feat: give the plan view the width its 5:1 shape wants
```

---

### Task 5: 오른쪽 탭, 툴바, 레이아웃

패널 넷이 세로로 쌓여 pose 슬라이더를 보려면 스크롤해야 한다. 한 번에 하나만 연다. 그리고 클릭이 무엇을 하는지 툴바가 말한다.

**Files:**
- Create: `web/src/components/RightTabs.tsx`, `web/src/components/Toolbar.tsx`
- Modify: `web/src/App.tsx`, `web/src/App.css`, `web/src/components/LayerTree.tsx:11,21`, `web/src/bridge/useShipUnity.ts:6`
- Test: `web/src/components/tabs.test.ts`

**Interfaces:**
- Consumes: Task 1 의 `useUiStore`
- Produces: `RightTabs({ send, reloadScene })`, `Toolbar({ send })`
- Produces: `TAB_LABELS: Record<RightTab, string>`, `EDIT_TABS: RightTab[]`, `nextTab(tab: RightTab, dir: number): RightTab` (`Shortcuts` 가 `Tab` 키에 쓴다)
- Produces: `BridgeName` 에 `"SetTool" | "SetCamMode"` 추가 (`"SetNormal"` 은 Task 11)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

컴포넌트가 아니라 **탭 어휘**만 테스트한다. 전환 규칙은 Task 1 이 스토어에서 이미 테스트했으므로 DOM 을 띄울 이유가 없다.

`web/src/components/tabs.test.ts`:

```ts
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
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run tabs`
Expected: FAIL — `./RightTabs` 가 없다

- [ ] **Step 3: `RightTabs` 를 만든다**

`web/src/components/RightTabs.tsx`:

```tsx
import { useEffect } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type RightTab } from "../store/ui";
import type { BridgeName } from "../bridge/useShipUnity";
import { LoadPanel } from "./LoadPanel";
import { CoveragePanel } from "./CoveragePanel";
import { PropertyForm } from "./PropertyForm";
import { PosePanel } from "./PosePanel";
import { DrivePanel } from "./DrivePanel";

type Send = (name: BridgeName, payload?: string | object) => void;

export const TAB_LABELS: Record<RightTab, string> = { load: "적재", coverage: "커버리지", props: "속성", pose: "자세" };
export const EDIT_TABS: RightTab[] = ["load", "coverage", "props", "pose"];

export function nextTab(tab: RightTab, dir: number): RightTab {
  const i = EDIT_TABS.indexOf(tab);
  return EDIT_TABS[(i + dir + EDIT_TABS.length) % EDIT_TABS.length];
}

export function RightTabs({ send, reloadScene }: { send: Send; reloadScene: () => Promise<void> }) {
  const mode = useEditorStore((s) => s.mode);
  const selectedId = useEditorStore((s) => s.selectedId);
  const tab = useUiStore((s) => s.tab);
  const setTab = useUiStore((s) => s.setTab);
  const openPropsFor = useUiStore((s) => s.openPropsFor);
  const restoreTab = useUiStore((s) => s.restoreTab);

  // a selection must not stay hidden behind another tab; clearing it returns the user where they were.
  // restoreTab is a no-op unless 속성 is open, so the mount-time run with no selection changes nothing.
  useEffect(() => { if (selectedId) openPropsFor(); else restoreTab(); }, [selectedId, openPropsFor, restoreTab]);

  if (mode === "drive") return <div className="tabbody"><DrivePanel send={send} /><PosePanel /></div>;

  return (
    <>
      <div className="tabs" role="tablist">
        {EDIT_TABS.map((t) => (
          <button key={t} type="button" role="tab" aria-selected={tab === t} className={`tab${tab === t ? " on" : ""}`}
            onClick={() => setTab(t)}>{TAB_LABELS[t]}</button>
        ))}
      </div>
      <div className="tabbody">
        {tab === "load" && <LoadPanel reloadScene={reloadScene} />}
        {tab === "coverage" && <CoveragePanel />}
        {tab === "props" && <PropertyForm send={send} />}
        {tab === "pose" && <PosePanel />}
      </div>
    </>
  );
}
```

- [ ] **Step 4: `Toolbar` 를 만든다**

`web/src/components/Toolbar.tsx`:

```tsx
import { useEditorStore } from "../store/editor";
import { useUiStore, type CamMode, type Tool } from "../store/ui";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

const TOOLS: { k: Tool; label: string; key: string; hint: string }[] = [
  { k: "select", label: "선택", key: "V", hint: "마커를 고르고 끌어 옮긴다" },
  { k: "place", label: "배치", key: "A", hint: "구조물 면을 클릭해 마커를 놓는다" },
  { k: "probe", label: "관측", key: "P", hint: "갑판을 클릭해 그 자리에서 보이는 것을 본다" },
];
const CAMS: { k: CamMode; label: string; key: string }[] = [
  { k: "orbit", label: "궤도", key: "C" }, { k: "fly", label: "비행", key: "C" }, { k: "driver", label: "차량 시선", key: "C" },
];

export function Toolbar({ send }: { send: Send }) {
  const mode = useEditorStore((s) => s.mode);
  const { tool, setTool, cam, setCam } = useUiStore();
  const pickTool = (t: Tool) => { setTool(t); send("SetTool", { tool: t }); };
  const pickCam = (c: CamMode) => { setCam(c); send("SetCamMode", { mode: c }); };
  return (
    <div className="toolbar">
      {mode === "edit" && TOOLS.map((t) => (
        <button key={t.k} type="button" className={`btn${tool === t.k ? " primary" : ""}`} title={`${t.hint} (${t.key})`}
          onClick={() => pickTool(t.k)}>{t.label}</button>
      ))}
      {mode === "edit" && <span className="mode-hint">{TOOLS.find((t) => t.k === tool)!.hint}</span>}
      <span className="sep" />
      {CAMS.map((c) => (
        // 차량 시선은 차가 있어야 한다 (스펙 §5.1)
        <button key={c.k} type="button" className={`btn${cam === c.k ? " primary" : ""}`}
          disabled={c.k === "driver" && mode !== "drive"} onClick={() => pickCam(c.k)}>{c.label}</button>
      ))}
    </div>
  );
}
```

`web/src/bridge/useShipUnity.ts:6` 의 `BridgeName` 유니온에 `| "SetTool" | "SetCamMode"` 를 더한다.

- [ ] **Step 5: 레이어 트리의 펼침을 `ui.ts` 로 옮긴다**

스펙 §6 이 트리 펼침을 저장 대상으로 잡았는데 지금은 `LayerTree.tsx:11` 의 지역 `useState` 다. 그 줄을 지우고:

```tsx
  const open = useUiStore((s) => s.treeOpen);
  const toggleTree = useUiStore((s) => s.toggleTree);
```

`:21` 의 `onClick={() => setOpen({ ...open, [layer]: !open[layer] })}` 를 `onClick={() => toggleTree(layer)}` 로 바꾼다. `import { useState }` 는 지운다.

- [ ] **Step 6: `App.tsx` 와 CSS 를 고친다**

오른쪽 `<aside>` 안을 `<RightTabs>` 하나로 바꾸고 `TopBar` 아래에 `Toolbar` 를 둔다. `LoadPanel`·`CoveragePanel`·`PropertyForm`·`PosePanel`·`DrivePanel` import 는 `App.tsx` 에서 전부 지운다 (`RightTabs` 가 갖는다):

```tsx
      <TopBar />
      <Toolbar send={send} />
      <aside className="left">
        <DeckTabs />
        <LayerTree />
      </aside>
      <main className="center" onContextMenu={(e) => e.preventDefault()}>…</main>
      <aside className="right"><RightTabs send={send} reloadScene={reloadScene} /></aside>
      <PlanDock />
      <StatusBar unityLoaded={isLoaded} />
```

CSS — 그리드에 툴바 행을 넣고, `.right` 를 세로 flex 로 만든다. **`.right` 가 `overflow: auto` 인 채로 두면 탭 막대가 같이 스크롤돼 사라진다**:

```css
.app { height: 100%; display: grid; grid-template-columns: 300px 1fr 340px;
       grid-template-rows: 40px 32px 1fr auto 28px;
       grid-template-areas: "top top top" "tool tool tool" "left center right" "dock dock dock" "status status status"; }
.toolbar { grid-area: tool; display: flex; align-items: center; gap: 6px; padding: 0 12px; background: #f4f6f8; border-bottom: 1px solid #ddd; }
.toolbar .sep { width: 1px; height: 18px; background: #ccc; margin: 0 6px; }
.toolbar .mode-hint { color: #666; font-size: 12px; }
.right { grid-area: right; display: flex; flex-direction: column; overflow: hidden; border-left: 1px solid #ccc; background: #fff; }
.tabs { display: flex; border-bottom: 1px solid #ddd; }
.tab { flex: 1; padding: 6px 4px; border: 0; background: #f4f6f8; cursor: pointer; border-bottom: 2px solid transparent; font: inherit; }
.tab.on { background: #fff; border-bottom-color: #1e88e5; font-weight: 600; }
.tabbody { flex: 1; min-height: 0; overflow: auto; }
```

(`.right` 의 옛 `overflow: auto` 줄은 위 규칙으로 **대체**한다. `.topbar .tab` 규칙이 먼저 오므로 상단바 버튼은 영향받지 않는다.)

- [ ] **Step 7: 테스트·빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: PASS. 60 + 2 = **62**

- [ ] **Step 8: 커밋**

```bash
git add -A web/src
git commit   # feat: one right-hand panel at a time, and a toolbar that says what a click does
```

---

### Task 6: 단축키

키 매핑은 순수 함수로 두고 컴포넌트는 배선만 한다. 핵심 규칙 둘: **입력 필드에 포커스가 있으면 전부 먹지 않는다**, **자유 비행 중 캔버스에 포커스가 있으면 이동 키에서 손을 뗀다**(Unity 가 직접 읽는다).

**Files:**
- Create: `web/src/ui/keys.ts`, `web/src/components/Shortcuts.tsx`
- Modify: `web/src/App.tsx`, `web/src/App.css`
- Test: `web/src/ui/keys.test.ts`

**Interfaces:**
- Consumes: Task 1 의 `Tool`·`CamMode`, Task 5 의 `nextTab`
- Produces: `type KeyCmd = { kind: "tool"; tool: Tool } | { kind: "cam" } | { kind: "deck"; index: number } | { kind: "tab"; dir: number } | { kind: "fit" } | { kind: "all" } | { kind: "escape" } | { kind: "help" }`
- Produces: `commandFor(e: KeyLike, ctx: { inField: boolean; flyingFocused: boolean }): KeyCmd | null`, `isInField(el: Element | null): boolean`, `FLY_KEYS: Set<string>`, `HELP_ROWS: [string, string][]`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/ui/keys.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { commandFor, HELP_ROWS } from "./keys";

const key = (k: string, mod: Partial<{ ctrlKey: boolean; metaKey: boolean; altKey: boolean }> = {}) =>
  ({ key: k, ctrlKey: false, metaKey: false, altKey: false, ...mod });
const FREE = { inField: false, flyingFocused: false };

describe("commandFor", () => {
  it("도구·카메라·갑판·탭 키를 알아본다", () => {
    expect(commandFor(key("v"), FREE)).toEqual({ kind: "tool", tool: "select" });
    expect(commandFor(key("a"), FREE)).toEqual({ kind: "tool", tool: "place" });
    expect(commandFor(key("p"), FREE)).toEqual({ kind: "tool", tool: "probe" });
    expect(commandFor(key("c"), FREE)).toEqual({ kind: "cam" });
    expect(commandFor(key("2"), FREE)).toEqual({ kind: "deck", index: 1 });
    expect(commandFor(key("Tab"), FREE)).toEqual({ kind: "tab", dir: 1 });
    expect(commandFor(key("f"), FREE)).toEqual({ kind: "fit" });
    expect(commandFor(key("0"), FREE)).toEqual({ kind: "all" });
    expect(commandFor(key("Escape"), FREE)).toEqual({ kind: "escape" });
    expect(commandFor(key("?"), FREE)).toEqual({ kind: "help" });
  });

  it("대문자도 같게 읽는다", () => {
    expect(commandFor(key("V"), FREE)).toEqual({ kind: "tool", tool: "select" });
  });

  /// 속성 패널에서 φ 를 치다가 0 을 누르면 뷰가 리셋되는 일이 없어야 한다 (스펙 §5.4).
  it("입력 필드에 포커스가 있으면 전부 먹지 않는다", () => {
    for (const k of ["v", "a", "p", "c", "0", "1", "f", "Tab", "?"]) {
      expect(commandFor(key(k), { inField: true, flyingFocused: false })).toBeNull();
    }
  });

  /// Esc 만 예외: 입력 중에도 빠져나갈 길은 있어야 한다.
  it("Esc 는 입력 중에도 먹는다", () => {
    expect(commandFor(key("Escape"), { inField: true, flyingFocused: false })).toEqual({ kind: "escape" });
  });

  /// A 가 배치 모드와 자유 비행의 왼쪽 이동에 겹친다. 비행 중 캔버스 포커스면 Unity 가 읽어야 하므로
  /// 웹은 손을 뗀다 — 안 그러면 좌로 날다가 배치 모드로 튀어 다음 클릭이 마커를 낳는다.
  it("자유 비행 + 캔버스 포커스면 이동 키를 Unity 에 넘긴다", () => {
    const fly = { inField: false, flyingFocused: true };
    for (const k of ["w", "a", "s", "d", "q", "e", "Shift", "ArrowLeft", "ArrowUp"]) {
      expect(commandFor(key(k), fly)).toBeNull();
    }
    expect(commandFor(key("v"), fly)).toEqual({ kind: "tool", tool: "select" });   // 비행 키가 아닌 것은 그대로
    expect(commandFor(key("Escape"), fly)).toEqual({ kind: "escape" });
  });

  it("수식 키가 눌리면 브라우저에 넘긴다", () => {
    expect(commandFor(key("0", { metaKey: true }), FREE)).toBeNull();
    expect(commandFor(key("a", { ctrlKey: true }), FREE)).toBeNull();
  });

  it("도움말 표가 모든 키를 설명한다", () => {
    expect(HELP_ROWS.length).toBeGreaterThanOrEqual(8);
    for (const [k, what] of HELP_ROWS) { expect(k).toBeTruthy(); expect(what).toBeTruthy(); }
  });
});
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run keys`
Expected: FAIL — `./keys` 가 없다

- [ ] **Step 3: `ui/keys.ts` 를 만든다**

```ts
import type { Tool } from "../store/ui";

export type KeyCmd =
  | { kind: "tool"; tool: Tool }
  | { kind: "cam" }
  | { kind: "deck"; index: number }
  | { kind: "tab"; dir: number }
  | { kind: "fit" }
  | { kind: "all" }
  | { kind: "escape" }
  | { kind: "help" };

type KeyLike = { key: string; ctrlKey: boolean; metaKey: boolean; altKey: boolean };
type Ctx = { inField: boolean; flyingFocused: boolean };

/** Keys free flight owns. Unity reads them straight off the focused canvas; the web must not also act on them. */
export const FLY_KEYS = new Set(["w", "a", "s", "d", "q", "e", "shift", "arrowleft", "arrowright", "arrowup", "arrowdown"]);

const TOOL_KEYS: Record<string, Tool> = { v: "select", a: "place", p: "probe" };

export function isInField(el: Element | null): boolean {
  if (!el) return false;
  const t = el.tagName;
  return t === "INPUT" || t === "TEXTAREA" || t === "SELECT" || (el as HTMLElement).isContentEditable === true;
}

/**
 * The single place that decides what a keystroke means. Two gates come first:
 * a focused field owns every key but Escape, and free flight with the canvas focused owns the movement keys.
 */
export function commandFor(e: KeyLike, ctx: Ctx): KeyCmd | null {
  if (e.key === "Escape") return { kind: "escape" };      // always a way out, even mid-typing
  if (e.ctrlKey || e.metaKey || e.altKey) return null;    // browser shortcuts stay the browser's
  if (ctx.inField) return null;
  const k = e.key.toLowerCase();
  if (ctx.flyingFocused && FLY_KEYS.has(k)) return null;
  if (k in TOOL_KEYS) return { kind: "tool", tool: TOOL_KEYS[k] };
  if (k === "c") return { kind: "cam" };
  if (k === "1" || k === "2" || k === "3") return { kind: "deck", index: Number(k) - 1 };
  if (e.key === "Tab") return { kind: "tab", dir: 1 };
  if (k === "f") return { kind: "fit" };
  if (k === "0") return { kind: "all" };
  if (e.key === "?") return { kind: "help" };
  return null;
}

/// Shown by `?`. Kept beside the mapping so the two cannot drift.
export const HELP_ROWS: [string, string][] = [
  ["V / A / P", "도구 — 선택 / 배치 / 관측"],
  ["C", "카메라 — 궤도 → 자유 비행 → 차량 시선"],
  ["W A S D · ←↑→↓ · Q E · Shift", "자유 비행 이동 (비행 모드에서 3D 에 포커스가 있을 때)"],
  ["F", "선택에 맞춤 — 활성 뷰포트에만"],
  ["0", "전체 보기 — 평면도"],
  ["Esc", "선택 해제 + 선택 모드로"],
  ["1 / 2 / 3", "갑판 D1 / D2 / D3"],
  ["Tab", "오른쪽 탭 순환"],
  ["?", "이 목록"],
];
```

- [ ] **Step 4: `Shortcuts` 를 만든다**

`web/src/components/Shortcuts.tsx`:

```tsx
import { useEffect } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type CamMode } from "../store/ui";
import { commandFor, isInField, HELP_ROWS } from "../ui/keys";
import { bbox, pickDeck } from "../geo/deck";
import { boxOfPoint, fitTo } from "../geo/plan";
import { nextTab } from "./RightTabs";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;
const CAM_CYCLE: CamMode[] = ["orbit", "fly", "driver"];

export function Shortcuts({ send }: { send: Send }) {
  const helpOpen = useUiStore((s) => s.helpOpen);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const ui = useUiStore.getState();
      const ed = useEditorStore.getState();
      const active = document.activeElement;
      const cmd = commandFor(e, { inField: isInField(active), flyingFocused: ui.cam === "fly" && active?.tagName === "CANVAS" });
      if (!cmd) return;
      e.preventDefault();      // Tab must not walk the focus ring, 0 must not zoom the browser

      switch (cmd.kind) {
        case "tool": ui.setTool(cmd.tool); send("SetTool", { tool: cmd.tool }); break;
        case "cam": {
          // 편집 모드에는 차가 없으므로 차량 시선은 건너뛴다 (스펙 §5.1)
          const pool = ed.mode === "drive" ? CAM_CYCLE : CAM_CYCLE.filter((c) => c !== "driver");
          const next = pool[(pool.indexOf(ui.cam) + 1) % pool.length] ?? "orbit";
          ui.setCam(next); send("SetCamMode", { mode: next });
          break;
        }
        case "deck": { const d = ed.decks[cmd.index]; if (d) ed.setDeckFilter(d.id); break; }
        case "tab": ui.setTab(nextTab(ui.tab, cmd.dir)); break;
        case "fit": {
          // 3D 는 Unity 가 이미 Select 에서 카메라를 옮긴다 — 같은 메시지를 다시 보내면 그게 곧 F 다.
          if (ed.selectedId) send("Select", ed.selectedId);
          const deck = pickDeck(ed.decks, ed.deckFilter);
          const f = ed.selectedId ? ed.features[ed.selectedId] : undefined;
          if (deck && f && f.geometry.type === "Point") {
            const c = f.geometry.coordinates as number[];
            ui.setPlanView(fitTo(bbox(deck.outline, 3), boxOfPoint(c[0], -c[1])));
          }
          break;
        }
        case "all": { const deck = pickDeck(ed.decks, ed.deckFilter); if (deck) { const b = bbox(deck.outline, 3); ui.setPlanView(fitTo(b, b)); } break; }
        case "escape":
          if (ui.helpOpen) { ui.toggleHelp(); break; }
          ed.select(null);
          ui.setTool("select"); send("SetTool", { tool: "select" });
          break;
        case "help": ui.toggleHelp(); break;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [send]);

  if (!helpOpen) return null;
  return (
    <div className="helpcard" onClick={() => useUiStore.getState().toggleHelp()}>
      <table>
        <tbody>{HELP_ROWS.map(([k, what]) => <tr key={k}><th>{k}</th><td>{what}</td></tr>)}</tbody>
      </table>
      <div style={{ color: "#888", marginTop: 6 }}>아무 곳이나 눌러 닫기 · Esc</div>
    </div>
  );
}
```

`App.tsx` 에 `<Shortcuts send={send} />` 를 `<StatusBar …/>` 앞에 넣는다. CSS:

```css
.helpcard { position: fixed; inset: 0; display: grid; place-items: center; background: rgba(0,0,0,.45); z-index: 10; color: #eee; }
.helpcard table { background: #23262b; border-radius: 6px; padding: 14px 18px; border-collapse: collapse; }
.helpcard th { text-align: right; padding: 3px 14px 3px 0; font-family: monospace; font-weight: 600; white-space: nowrap; }
.helpcard td { padding: 3px 0; }
```

- [ ] **Step 5: 테스트·빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: PASS. 62 + 7 = **69**

- [ ] **Step 6: 커밋**

```bash
git add -A web/src
git commit   # feat: keyboard shortcuts, and one place that decides what a key means
```

---

### Task 7: Unity 도구 모드

지금 `LandmarkPlacer` 는 구조물을 클릭하면 **무조건** 마커를 만든다(`LandmarkPlacer.cs:45`). 카메라를 돌리려고 빈 곳을 클릭할 수가 없다. 도구 모드를 넣는다.

`Input.GetMouseButtonDown` 은 EditMode 에서 가짜로 만들 수 없으므로, **클릭이 무엇을 하는지 정하는 부분을 순수 함수로 뽑아** 그것을 테스트한다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkPlacer.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/LandmarkPlacerTests.cs` (신규)

**Interfaces:**
- Produces: `enum PlacerTool { Select, Place, Probe }`, `LandmarkPlacer.tool` (기본 `Select`)
- Produces: `enum ClickAct { None, SelectMarker, DragMarker, Place, Probe, Clear }`
- Produces: `static ClickAct LandmarkPlacer.Decide(PlacerTool tool, bool hitMarker, bool hitStructure)`
- Produces: `event Action<RaycastHit> ProbeAt`, `event Action Cleared`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/LandmarkPlacerTests.cs`:

```csharp
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class LandmarkPlacerTests
    {
        /// The whole point of M5e: in Select mode a click on ship structure must NOT spawn a marker.
        /// Before this, "left click on ship structure spawns a marker" made the 3D view unusable for anything else.
        [Test]
        public void SelectToolNeverPlaces()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, false, true), Is.EqualTo(ClickAct.Clear));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, false, false), Is.EqualTo(ClickAct.Clear));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, true, true), Is.EqualTo(ClickAct.DragMarker));
        }

        [Test]
        public void PlaceToolPlacesOnStructureAndStillSelectsMarkers()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, false, true), Is.EqualTo(ClickAct.Place));
            // a marker under the cursor wins: it is the only way to grab one without leaving the tool
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, true, true), Is.EqualTo(ClickAct.SelectMarker));
            // empty space places nothing and clears nothing -- an accidental miss must not lose the selection
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, false, false), Is.EqualTo(ClickAct.None));
        }

        [Test]
        public void ProbeToolProbesTheSurfaceItHits()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, false, true), Is.EqualTo(ClickAct.Probe));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, true, true), Is.EqualTo(ClickAct.SelectMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, false, false), Is.EqualTo(ClickAct.None));
        }

        /// Only Select drags. Dragging in Place mode would move the marker you just put down while you
        /// were reaching for the next spot.
        [Test]
        public void OnlySelectDrags()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, true, false), Is.EqualTo(ClickAct.DragMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, true, false), Is.EqualTo(ClickAct.SelectMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, true, false), Is.EqualTo(ClickAct.SelectMarker));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Unity 에디터에서 EditMode 테스트를 돌린다 (MCP: `run_tests` mode `EditMode`, 또는 Test Runner 창).
Expected: 컴파일 실패 — `PlacerTool` 이 없다

- [ ] **Step 3: `LandmarkPlacer` 를 고친다**

파일 위쪽 `namespace ShipHdMap {` 바로 안에 더한다:

```csharp
    /// What a left click does. Sticky: placing a row of markers (12 m apart, 18 of them) is the real job,
    /// so the tool does not snap back to Select after one placement -- the toolbar says which mode is on instead.
    public enum PlacerTool { Select, Place, Probe }

    public enum ClickAct { None, SelectMarker, DragMarker, Place, Probe, Clear }
```

클래스 안, 필드 옆:

```csharp
        public PlacerTool tool = PlacerTool.Select;
        public event Action<RaycastHit> ProbeAt; public event Action Cleared;
```

주석(`:7-8`)을 고치고 `Decide` 를 더한다:

```csharp
    /// Play-mode mouse. What a left click does depends on `tool` (M5e): Select picks and drags, Place spawns
    /// on ship structure facing the surface normal, Probe drops the virtual viewpoint. Deletion happens only
    /// from the web form (DB first, then Delete(id)), so there is no right-click delete.
```

```csharp
        /// The click rule, with no Input or Physics in it so an EditMode test can pin it down.
        /// A marker under the cursor always wins -- it is the only handle on a marker in any tool.
        public static ClickAct Decide(PlacerTool tool, bool hitMarker, bool hitStructure)
        {
            if (hitMarker) return tool == PlacerTool.Select ? ClickAct.DragMarker : ClickAct.SelectMarker;
            if (!hitStructure) return tool == PlacerTool.Select ? ClickAct.Clear : ClickAct.None;
            switch (tool)
            {
                case PlacerTool.Place: return ClickAct.Place;
                case PlacerTool.Probe: return ClickAct.Probe;
                default: return ClickAct.Clear;
            }
        }
```

`OnLeftDown` 을 `Decide` 로 다시 쓴다:

```csharp
        void OnLeftDown()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            int lmMask = LayerMask.GetMask("Landmark");
            LandmarkMarker lm = null;
            if (lmMask != 0 && Physics.Raycast(ray, out var mh, 500f, lmMask)) mh.collider.TryGetComponent(out lm);
            bool hitStructure = Physics.Raycast(ray, out var hit, 500f, StructureMask());
            switch (Decide(tool, lm != null, hitStructure))
            {
                case ClickAct.DragMarker: _drag = lm; _dragStart = lm.transform.position; _moved = false; Selected?.Invoke(lm); break;
                case ClickAct.SelectMarker: Selected?.Invoke(lm); break;
                case ClickAct.Place: PlaceAt(hit); break;
                case ClickAct.Probe: ProbeAt?.Invoke(hit); break;
                case ClickAct.Clear: Cleared?.Invoke(); break;
            }
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Unity EditMode 테스트 전체.
Expected: PASS. 134 + 4 = **138**

- [ ] **Step 5: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkPlacer.cs unity/Assets/ShipHdMap/Tests/EditMode/LandmarkPlacerTests.cs
git commit   # feat: a click means what the tool says, not always "spawn a marker"
```

---

### Task 8: 가시 판정을 한 곳으로, 그리고 화면에

스펙 §5.3: **가시 판정은 한 곳에서만 나온다.** `Sense` 에서 가시성 부분을 뽑아 주행 표시·관측점이 같은 구현을 쓰게 한다. 두 구현이 갈라지면 화면이 거짓말을 한다 — M5d 가 `Localizer` 와 `CoverageAnalyzer` 를 같은 야코비안으로 묶어 얻은 성질과 같다.

**두 가지를 반드시 지킨다.**
1. `VisibleFrom` 은 **눈 위치를 인자로 받는다.** `Sense` 는 차량 자신의 `transform.position` 을 쓰지만 관측점은 갑판 아무 데나 설 수 있다. 자세(`Pose2D`)만으로는 가림 레이캐스트의 시작점을 만들 수 없다.
2. **결과는 `List` 로 돌려준다** — `Dictionary` 로 바꾸면 순회 순서가 보장되지 않아 `AddNoise` 의 난수 소비 순서가 흔들리고, 시드 고정에 기대는 `ScenarioRunTests`·`BeliefRunTests` 가 무너진다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs`, `unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkMarker.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/SensorView.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/VisibleFromTests.cs` (신규)

**Interfaces:**
- Produces: `enum Miss { None, Range, Fov, Facing, Occluded, Blocked }`
- Produces: `static Miss LandmarkSensor.GeometricMiss(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)`
- Produces: `List<(string id, Miss miss)> LandmarkSensor.VisibleFrom(Pose2D pose, Vector3 eyeWorld, IDictionary<string, LandmarkRef> map, Func<string, Vector3> posOf)`
- Produces: `LandmarkMarker.SetSeen(bool)`
- Produces: `static Vector3[] SensorView.ConeArcPoints(Pose2D pose, double fovDeg, double maxDist, double zSurface, int arcSegments)`
- Produces: `SensorView.Show(Pose2D pose, double zSurface, IEnumerable<(string id, Miss miss)> why, Func<string, LandmarkMarker> markerOf, double fovDeg, double maxDist)`, `SensorView.Hide()`
- Consumes: 없음 (Task 7 과 독립)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/VisibleFromTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class VisibleFromTests
    {
        const double D = Math.PI / 180.0;
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); }

        static Dictionary<string, LandmarkRef> Map() => new()
        {
            ["near"] = new LandmarkRef { id = "near", mx = 10, my = 0, phiRad = Math.PI },       // faces the vehicle
            ["far"] = new LandmarkRef { id = "far", mx = 30, my = 0, phiRad = Math.PI },
            ["side"] = new LandmarkRef { id = "side", mx = 0.1, my = 10, phiRad = -Math.PI / 2 },// bearing ~89deg, outside the 45deg half-angle
            ["back"] = new LandmarkRef { id = "back", mx = 10, my = 0, phiRad = 0 },             // normal points away
        };

        /// Each of the three geometric conditions has its own answer, so the overlay can say WHY a marker
        /// went dark instead of just that it did (spec §5.2).
        [Test]
        public void GeometricMissNamesTheConditionThatFailed()
        {
            var v = new Pose2D { x = 0, y = 0, psiRad = 0 };
            var m = Map();
            Assert.That(LandmarkSensor.GeometricMiss(v, m["near"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.None));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["far"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Range));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["side"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Fov));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["back"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Facing));
        }

        // `IsVisibleGeometric` 이 `GeometricMiss(...) == Miss.None` 그 자체이므로 둘이 같은지 묻는 테스트는
        // 정의를 정의로 확인할 뿐 어떤 회귀에도 빨개지지 않는다. 그 술어는 기존
        // SensorAndVehicleTests.VisibilityRespectsFovDistanceAndViewAngle 이 실값 5 개로 이미 고정한다.

        /// Spec §7.2.3: the drive and the overlay must not be able to disagree.
        [Test]
        public void VisibleFromAgreesWithSenseWhenThereIsNoNoise()
        {
            go = new GameObject("Map");
            var sensor = go.AddComponent<LandmarkSensor>();
            sensor.noise = new SensorNoise { sigmaR = 0, sigmaThetaRad = 0, sigmaAlphaRad = 0 };
            sensor.occluders = 0;                                   // no colliders in this scene to hide behind
            var map = Map();
            Func<string, Vector3> posOf = id => ShipFrame.ToUnity(map[id].mx, map[id].my, 0);
            var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };

            var sensed = new HashSet<string>();
            foreach (var o in sensor.Sense(truth, map, posOf)) sensed.Add(o.id);

            var seen = new HashSet<string>();
            foreach (var (id, miss) in sensor.VisibleFrom(truth, Vector3.up * sensor.eyeHeight, map, posOf))
                if (miss == Miss.None) seen.Add(id);

            Assert.That(seen, Is.EquivalentTo(sensed));
            Assert.That(seen, Is.EquivalentTo(new[] { "near" }));
        }

        /// Spec §7.2.4. A marker the sensor was told to ignore reads as Occluded, not as an ordinary miss:
        /// the map still promises it, which is exactly what M5d's belief monitor is there to notice.
        [Test]
        public void VisibleFromReportsTheOccludedSetSeparately()
        {
            go = new GameObject("Map");
            var sensor = go.AddComponent<LandmarkSensor>();
            sensor.occluders = 0;
            sensor.occluded.Add("near");
            var map = Map();
            Func<string, Vector3> posOf = id => ShipFrame.ToUnity(map[id].mx, map[id].my, 0);

            var why = new Dictionary<string, Miss>();
            foreach (var (id, miss) in sensor.VisibleFrom(new Pose2D { x = 0, y = 0, psiRad = 0 }, Vector3.up * 1.2f, map, posOf)) why[id] = miss;

            Assert.That(why["near"], Is.EqualTo(Miss.Occluded));
            Assert.That(why["far"], Is.EqualTo(Miss.Range));
            Assert.That(why.Count, Is.EqualTo(4));   // every mapped marker gets an answer, not just the visible ones
        }

        /// The cone edges and the range arc are what turn "it went dark" into "it went dark because".
        [Test]
        public void ConeArcPointsSpanTheFieldOfViewAtTheRangeLimit()
        {
            var pts = SensorView.ConeArcPoints(new Pose2D { x = 0, y = 0, psiRad = 0 }, 90, 25, 10.6, 8);
            Assert.That(pts.Length, Is.EqualTo(11));                       // eye + 9 arc points + back to the eye
            // 2 cm above the deck floor, not on it: coplanar lines z-fight with the deck mesh
            foreach (var p in pts) Assert.That(p.y, Is.EqualTo(10.62f).Within(1e-3f));
            var (ax, ay, _) = ShipFrame.ToShip(pts[1]);
            Assert.That(Math.Sqrt(ax * ax + ay * ay), Is.EqualTo(25).Within(1e-3));
            Assert.That(Math.Atan2(ay, ax) * 180 / Math.PI, Is.EqualTo(45).Within(1e-3));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Unity EditMode.
Expected: 컴파일 실패 — `Miss`·`GeometricMiss`·`VisibleFrom`·`SensorView` 가 없다

- [ ] **Step 3: `LandmarkSensor` 를 나눈다**

`namespace ShipHdMap {` 안, `SensorNoise` 옆:

```csharp
    /// Why a mapped landmark is not an observation. One vocabulary for the drive, the driver's-eye overlay
    /// and the probe, so the three can never tell different stories about the same marker.
    public enum Miss { None, Range, Fov, Facing, Occluded, Blocked }
```

`IsVisibleGeometric` 을 `GeometricMiss` 로 바꾸고 옛 술어는 그 위에 얇게 남긴다:

```csharp
        public static Miss GeometricMiss(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)
        {
            double dx = lm.mx - v.x, dy = lm.my - v.y, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > maxDist || r < 1e-6) return Miss.Range;
            double theta = ShipFrame.WrapRad(Math.Atan2(dy, dx) - v.psiRad);
            if (Math.Abs(theta) > fovRad / 2) return Miss.Fov;
            // angle between marker normal and the direction marker -> vehicle
            double toV = Math.Atan2(-dy, -dx);
            return Math.Abs(ShipFrame.WrapRad(toV - lm.phiRad)) <= maxViewAngleRad ? Miss.None : Miss.Facing;
        }

        public static bool IsVisibleGeometric(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)
            => GeometricMiss(v, lm, fovRad, maxDist, maxViewAngleRad) == Miss.None;
```

`VisibleFrom` 을 더하고 `Sense` 가 그것을 쓰게 한다:

```csharp
        /// Why every mapped landmark is or is not observable from `pose`, with the eye at `eyeWorld`.
        /// The eye is a parameter, not this transform: the probe (spec §5.3) stands wherever the operator
        /// clicked, and a Pose2D alone cannot start an occlusion linecast.
        /// Returns a List, not a Dictionary: Sense draws noise in this order and the seeded runs depend on it.
        public List<(string id, Miss miss)> VisibleFrom(Pose2D pose, Vector3 eyeWorld, IDictionary<string, LandmarkRef> map, Func<string, Vector3> posOf)
        {
            var why = new List<(string, Miss)>(map.Count);
            double fov = fovDeg * Math.PI / 180, mva = maxViewAngleDeg * Math.PI / 180;
            // Keyed by the DICTIONARY KEY, not by lm.id: MapRuntime.Confirm re-keys a draft's entry without
            // rewriting the struct's own id, so after a save the two differ and a re-lookup by lm.id throws.
            foreach (var kv in map)
            {
                string id = kv.Key; var lm = kv.Value;
                if (occluded.Contains(id)) { why.Add((id, Miss.Occluded)); continue; }
                var m = GeometricMiss(pose, lm, fov, maxDist, mva);
                if (m != Miss.None) { why.Add((id, m)); continue; }
                Vector3 target = posOf(id);
                bool blocked = occluders != 0 && Physics.Linecast(eyeWorld, target - (target - eyeWorld).normalized * 0.05f, occluders);
                why.Add((id, blocked ? Miss.Blocked : Miss.None));
            }
            return why;
        }
```

`Sense` 본문을 바꾼다 (난수 소비 순서가 그대로임에 주의):

```csharp
        public List<Observation> Sense(Pose2D truth, IDictionary<string, LandmarkRef> map, Func<string, Vector3> unityPosOf)
        {
            var result = new List<Observation>();
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            foreach (var (id, miss) in VisibleFrom(truth, eye, map, unityPosOf))
            {
                if (miss != Miss.None) continue;
                result.Add(AddNoise(Localizer.Observe(truth, map[id]), noise, _rng ??= new System.Random(noise.seed)));
            }
            return result;
        }
```

- [ ] **Step 4: `LandmarkMarker.SetSeen` 을 더한다**

`_halo` 옆에 `_seenRing` 을 두고 `SetHighlighted` 와 같은 수법을 쓴다. 선택(노랑 halo)과 감지(초록 테)는 겹쳐 보여도 된다 — 다른 질문에 답하기 때문이다.

```csharp
        Transform _seenRing;

        /// Green frame behind the tag, smaller than the selection halo so both can show at once:
        /// the halo answers "which one am I editing", this answers "does the sensor have it right now".
        public void SetSeen(bool on)
        {
            if (_seenRing == null)
            {
                if (!on) return;
                var h = GameObject.CreatePrimitive(PrimitiveType.Quad); h.name = "Seen"; h.layer = gameObject.layer;
                Object.DestroyImmediate(h.GetComponent<Collider>());
                h.transform.SetParent(transform, false);
                h.transform.localPosition = new Vector3(0, 0, 0.006f);
                h.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                h.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 1f, 0.35f) };
                _seenRing = h.transform;
            }
            _seenRing.gameObject.SetActive(on);
        }
```

- [ ] **Step 5: `SensorView` 를 만든다**

`unity/Assets/ShipHdMap/Runtime/Vehicle/SensorView.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Draws what the sensor can see, on the deck floor: the two field-of-view edges and the range arc,
    /// plus a green ring on every marker currently observed. Shared by the driver's-eye camera and the
    /// probe (spec §5.2, §5.3) so both read the same judgement out of LandmarkSensor.VisibleFrom.
    ///
    /// Why draw the cone and the arc and not the other two conditions: a marker that went dark because it
    /// is behind you, or out of range, is explained by the picture. One that is dark INSIDE the cone and
    /// INSIDE the arc is explained by elimination -- its normal turned away, or a pillar is in the way.
    public class SensorView : MonoBehaviour
    {
        public Color coneColor = new(0.3f, 0.8f, 1f, 0.9f);
        LineRenderer _line;
        readonly List<LandmarkMarker> _lit = new();

        /// Eye, one FOV edge, the arc at max range, back to the eye. In the Map root's local space (Ship Frame
        /// mapped by ShipFrame.ToUnity), drawn flat on the deck floor at zSurface + 2 cm.
        public static Vector3[] ConeArcPoints(Pose2D pose, double fovDeg, double maxDist, double zSurface, int arcSegments)
        {
            var pts = new Vector3[arcSegments + 3];       // eye + (arcSegments + 1) arc points + back to the eye
            double half = fovDeg * Math.PI / 360.0, z = zSurface + 0.02;
            pts[0] = ShipFrame.ToUnity(pose.x, pose.y, z);
            for (int i = 0; i <= arcSegments; i++)
            {
                double a = pose.psiRad + half - 2 * half * i / arcSegments;   // +half at i=0, -half at i=arcSegments
                pts[i + 1] = ShipFrame.ToUnity(pose.x + Math.Cos(a) * maxDist, pose.y + Math.Sin(a) * maxDist, z);
            }
            pts[arcSegments + 2] = pts[0];
            return pts;
        }

        /// `why` already carries the occlusion verdict, so the eye position is not a parameter here --
        /// whoever built `why` had it, and passing it twice invites the two copies to disagree.
        public void Show(Pose2D pose, double zSurface, IEnumerable<(string id, Miss miss)> why, Func<string, LandmarkMarker> markerOf, double fovDeg, double maxDist)
        {
            foreach (var m in _lit) if (m) m.SetSeen(false);
            _lit.Clear();
            foreach (var (id, miss) in why)
            {
                var mk = markerOf(id); if (!mk) continue;
                if (miss == Miss.None) { mk.SetSeen(true); _lit.Add(mk); }
            }
            EnsureLine();
            var pts = ConeArcPoints(pose, fovDeg, maxDist, zSurface, 24);
            _line.positionCount = pts.Length; _line.SetPositions(pts);
            _line.enabled = true;
        }

        public void Hide()
        {
            foreach (var m in _lit) if (m) m.SetSeen(false);
            _lit.Clear();
            if (_line) _line.enabled = false;
        }

        void EnsureLine()
        {
            if (_line) return;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;                 // this component lives under the Map root, so local = Ship Frame
            _line.widthMultiplier = 0.12f; _line.numCornerVertices = 0;
            _line.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = coneColor };
        }
    }
}
```

- [ ] **Step 6: 테스트 통과 확인**

Unity EditMode 전체. 기존 `SensorAndVehicleTests` 4 개와 시드 고정 주행 테스트(`ScenarioRunTests` 21, `BeliefRunTests` 4)가 **하나도 안 바뀌고** 통과하는지 특히 본다 — 난수 순서가 유지됐다는 증거다.
Expected: PASS. 138 + 4 = **142**

- [ ] **Step 7: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkMarker.cs unity/Assets/ShipHdMap/Runtime/Vehicle/SensorView.cs unity/Assets/ShipHdMap/Tests/EditMode/VisibleFromTests.cs
git commit   # feat: one visibility judgement, and a picture of why a marker went dark
```

---

### Task 9: 카메라 모드 셋

궤도(지금 것) / 자유 비행 / 차량 시선. `Pose` 와 `AdoptCurrentPose` 는 건드리지 않는다 — `OrbitCameraTests` 2 개가 그대로 통과해야 한다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/OrbitCamera.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/OrbitCameraTests.cs` (추가)

**Interfaces:**
- Produces: `enum CamMode { Orbit, Fly, Driver }`, `OrbitCamera.mode`, `OrbitCamera.driverTarget: Transform`, `OrbitCamera.driverEyeM = 1.2f`
- Produces: `static (Vector3 pos, Quaternion rot) OrbitCamera.FlyStep(Vector3 pos, Quaternion rot, Vector3 axes, float dt, float speed)`
- Consumes: 없음 (Task 7·8 과 독립)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`OrbitCameraTests.cs` 에 더한다:

```csharp
        /// Free flight moves along the camera's own axes: forward is where you are looking, up is world up
        /// (Q/E), so "fly up" never means "fly into the deck" just because the camera was pitched down.
        [Test]
        public void FlyStepMovesAlongTheCameraAxesAndWorldUp()
        {
            var rot = Quaternion.Euler(0, 90, 0);                  // yawed a quarter turn, so "forward" is not world +z
            var (p, r) = OrbitCamera.FlyStep(Vector3.zero, rot, new Vector3(0, 0, 1), 1f, 10f);
            Assert.That(Vector3.Distance(p, rot * Vector3.forward * 10f), Is.LessThan(1e-3f));
            Assert.That(r, Is.EqualTo(rot));                       // FlyStep translates; looking around is the mouse's job

            var (up, _) = OrbitCamera.FlyStep(Vector3.zero, Quaternion.Euler(80, 0, 0), new Vector3(0, 1, 0), 1f, 10f);
            Assert.That(up.y, Is.EqualTo(10f).Within(1e-3f));      // straight up regardless of pitch
            Assert.That(up.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(up.z, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void FlyStepScalesWithTimeAndSpeed()
        {
            var (a, _) = OrbitCamera.FlyStep(Vector3.zero, Quaternion.identity, new Vector3(1, 0, 0), 0.5f, 20f);
            Assert.That(a.x, Is.EqualTo(10f).Within(1e-3f));
            var (z, _) = OrbitCamera.FlyStep(Vector3.one, Quaternion.identity, Vector3.zero, 1f, 20f);
            Assert.That(Vector3.Distance(z, Vector3.one), Is.LessThan(1e-6f));
        }

        /// Driver's eye is the sensor's eye: same height, same heading. That is the whole reason the overlay
        /// in that view is allowed to claim it shows what the sensor sees (spec §5.2).
        [Test]
        public void DriverModeSitsAtTheVehicleEyeAndCopiesItsHeading()
        {
            var car = new GameObject("car"); car.transform.SetPositionAndRotation(new Vector3(30, 10.6f, -4), Quaternion.Euler(0, 35, 0));
            var go = new GameObject("cam"); go.AddComponent<Camera>();
            var orbit = go.AddComponent<OrbitCamera>();
            orbit.mode = CamMode.Driver; orbit.driverTarget = car.transform; orbit.driverEyeM = 1.2f;
            orbit.ApplyDriver();
            Assert.That(go.transform.position.y, Is.EqualTo(10.6f + 1.2f).Within(1e-3f));
            Assert.That(go.transform.position.x, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(Quaternion.Angle(go.transform.rotation, car.transform.rotation), Is.LessThan(1e-3f));
            Object.DestroyImmediate(car); Object.DestroyImmediate(go);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `CamMode`·`FlyStep`·`ApplyDriver` 가 없다

- [ ] **Step 3: `OrbitCamera` 에 모드를 더한다**

`namespace ShipHdMap {` 안:

```csharp
    /// Orbit is the editor's camera, Fly walks the deck, Driver rides the vehicle at sensor eye height.
    public enum CamMode { Orbit, Fly, Driver }
```

클래스 필드:

```csharp
        public CamMode mode = CamMode.Orbit;
        public Transform driverTarget; public float driverEyeM = 1.2f, flySpeed = 12f, flyBoost = 4f;
```

메서드:

```csharp
        /// Pure translation for one free-flight frame. `axes` is (right, worldUp, forward) in -1..1.
        /// Forward and right follow where the camera looks; up is world up, so Q/E always mean up and down.
        public static (Vector3 pos, Quaternion rot) FlyStep(Vector3 pos, Quaternion rot, Vector3 axes, float dt, float speed)
        {
            Vector3 d = rot * Vector3.forward * axes.z + rot * Vector3.right * axes.x + Vector3.up * axes.y;
            return (pos + d * (speed * dt), rot);
        }

        /// The driver's eye is the sensor's eye: same height, same heading (spec §5.2).
        public void ApplyDriver()
        {
            if (!driverTarget) return;
            transform.SetPositionAndRotation(driverTarget.position + Vector3.up * driverEyeM, driverTarget.rotation);
        }
```

`LateUpdate` 를 모드로 가른다:

```csharp
        void LateUpdate()
        {
            if (mode == CamMode.Driver) { ApplyDriver(); return; }
            if (mode == CamMode.Fly) { StepFly(); return; }
            // ... 기존 궤도 본문 그대로 ...
        }

        /// Right-drag looks around, WASD/arrows move, Q/E rise and fall, Shift boosts.
        /// Unity only receives these when its canvas has focus (WebGLInput.captureAllKeyboardInput = false),
        /// which is exactly the condition the web's shortcut handler steps aside for.
        void StepFly()
        {
            if (Input.GetMouseButton(1))
            {
                yawDeg += Input.GetAxis("Mouse X") * orbitSpeed;
                pitchDeg = Mathf.Clamp(pitchDeg - Input.GetAxis("Mouse Y") * orbitSpeed, -89f, 89f);
            }
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0);
            float x = Axis(KeyCode.D, KeyCode.A) + Axis(KeyCode.RightArrow, KeyCode.LeftArrow);
            float z = Axis(KeyCode.W, KeyCode.S) + Axis(KeyCode.UpArrow, KeyCode.DownArrow);
            float y = Axis(KeyCode.E, KeyCode.Q);
            float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? flyBoost : 1f);
            var (p, r) = FlyStep(transform.position, rot, new Vector3(Mathf.Clamp(x, -1, 1), Mathf.Clamp(y, -1, 1), Mathf.Clamp(z, -1, 1)), Time.unscaledDeltaTime, speed);
            transform.SetPositionAndRotation(p, r);
            // leaving fly mode should not snap back to wherever the orbit target was
            target = p + r * Vector3.forward * distance;
        }

        static float Axis(KeyCode plus, KeyCode minus) => (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f);
```

`Focus` 는 궤도로 돌려놓는다 — 비행 중 `F` 를 눌러 선택으로 가면 궤도가 맞다:

```csharp
        public void Focus(Vector3 p, float dist) { mode = CamMode.Orbit; follow = null; target = p; distance = Mathf.Clamp(dist, minDistance, maxDistance); Apply(); }
```

`Time.unscaledDeltaTime` 을 쓰는 이유: 주행 ×20 배율이 `Time.timeScale` 을 건드리므로 그대로 두면 카메라가 스무 배로 날아간다.

- [ ] **Step 4: 테스트 통과 확인**

Expected: PASS. 142 + 3 = **145**. 기존 `OrbitCameraTests` 2 개 그대로 통과

- [ ] **Step 5: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Vehicle/OrbitCamera.cs unity/Assets/ShipHdMap/Tests/EditMode/OrbitCameraTests.cs
git commit   # feat: three cameras -- orbit to edit, fly to walk the deck, driver to be the sensor
```

---

### Task 10: 법선 기즈모

M5c·M5d 가 **법선이 위치보다 결정적**이라고 반복해 밝혔는데 지금 법선을 바꾸려면 `props` JSON 을 손으로 고쳐야 한다. 기즈모를 넣어 그 고리를 닫는다.

**핵심 절약:** 기즈모는 **새 이벤트를 만들지 않는다.** 놓는 순간 `Placer.Moved` 를 그대로 발화시키면 `MapRuntime.OnMarkerMoved` → `onFeatureMoved` → `store.moveFeature` 가 이미 `normal` 을 실어 나른다 (`BridgeMessages.cs:13`, `editor.ts:109-114`). 마커 드래그 이동이 이미 쓰는 길이다.

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Landmarks/NormalGizmo.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/NormalGizmoTests.cs` (신규)

**Interfaces:**
- Consumes: Task 9 의 `OrbitCamera`
- Produces: `static double NormalGizmo.PhiAt(Vector3 centerUnity, Vector3 pointUnity)` — Ship Frame 각, `(-π, π]`
- Produces: `NormalGizmo.Attach(LandmarkMarker)`, `NormalGizmo.Detach()`, `event Action<LandmarkMarker> Rotated`, 필드 `cam`·`root`·`orbit`·`radiusM`

**가상 관측점(`ProbeView`)은 Task 11 에 있다** — `MapRuntime.MarkerOf`/`MarkerPos` 없이는 컴파일되지 않아서, 여기 두면 이 Task 의 커밋이 빌드되지 않는다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/NormalGizmoTests.cs`:

```csharp
using System;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class NormalGizmoTests
    {
        /// The handle reads an angle in Ship Frame, never in Unity's -- the marker's stored normal is Ship Frame
        /// and a sign slip here would mirror every rotation about the centreline.
        [Test]
        public void PhiAtReadsShipFrameAngles()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(55, 0, 11)), Is.EqualTo(0).Within(1e-6));
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(50, 5, 11)), Is.EqualTo(Math.PI / 2).Within(1e-6));
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(50, -5, 11)), Is.EqualTo(-Math.PI / 2).Within(1e-6));
        }

        /// M5c §5.1.1's trap: atan2(-0.0, -1) is -pi, and an unwrapped -pi is a different number from pi
        /// for every downstream comparison. WrapRad's half-open interval is the fix; this pins it.
        [Test]
        public void PhiAtWrapsToTheHalfOpenInterval()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            double a = NormalGizmo.PhiAt(c, ShipFrame.ToUnity(45, -0.0, 11));
            Assert.That(a, Is.EqualTo(Math.PI).Within(1e-9));
            Assert.That(a, Is.GreaterThan(-Math.PI));
        }

        /// Height must not leak into the angle: the handle is a circle on the deck plane, and dragging the
        /// mouse a little high should not swing the normal.
        [Test]
        public void PhiAtIgnoresHeight()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(55, 0, 14)), Is.EqualTo(0).Within(1e-6));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `NormalGizmo` 가 없다

- [ ] **Step 3: `NormalGizmo` 를 만든다**

```csharp
using System;
using UnityEngine;

namespace ShipHdMap
{
    /// A circular handle around the selected marker. Dragging it turns the marker's normal in the deck plane;
    /// letting go raises Rotated, which MapRuntime feeds into the SAME onFeatureMoved path a drag-move uses --
    /// the web already persists `normal` from that event, so no new message exists for this.
    ///
    /// Why it matters: M5c and M5d both found the normal decides coverage far more than the position does,
    /// and until now the only way to change one was to hand-edit props JSON.
    public class NormalGizmo : MonoBehaviour
    {
        public Camera cam; public Transform root; public OrbitCamera orbit; public float radiusM = 1.6f;
        public event Action<LandmarkMarker> Rotated;
        LandmarkMarker _marker; LineRenderer _ring; bool _dragging;

        public LandmarkMarker Target => _marker;

        /// Angle from `centerUnity` to `pointUnity` in Ship Frame, wrapped to (-pi, pi]. Height is dropped:
        /// the handle is a circle on the deck plane.
        public static double PhiAt(Vector3 centerUnity, Vector3 pointUnity)
        {
            var (cx, cy, _) = ShipFrame.ToShip(centerUnity);
            var (px, py, _) = ShipFrame.ToShip(pointUnity);
            return ShipFrame.WrapRad(Math.Atan2(py - cy, px - cx));
        }

        public void Attach(LandmarkMarker m) { _marker = m; _dragging = false; DrawRing(); }
        public void Detach() { if (_dragging && orbit) orbit.enabled = true; _marker = null; _dragging = false; if (_ring) _ring.enabled = false; }

        void Update()
        {
            if (_marker == null || cam == null) { if (_dragging) EndDrag(); return; }
            // Right button, so the gizmo never competes with LandmarkPlacer's left-click tools. That means it
            // shares a button with the orbit camera, so a drag that starts on the ring switches the camera off
            // for its duration -- otherwise turning a normal also spins the view. The ring is a thin annulus,
            // so every other right-drag still orbits.
            if (Input.GetMouseButtonDown(1) && OnRing()) { _dragging = true; if (orbit) orbit.enabled = false; }
            else if (_dragging && Input.GetMouseButton(1)) DragTo();
            if (_dragging && Input.GetMouseButtonUp(1)) EndDrag();
        }

        bool OnRing()
        {
            if (!PlanePoint(out var p)) return false;
            return Mathf.Abs(Vector3.Distance(p, _marker.transform.position) - radiusM) < radiusM * 0.5f;
        }

        void DragTo()
        {
            if (!PlanePoint(out var p)) return;
            // Both points go through `root` first: PhiAt reads Ship Frame, and the marker's world transform is
            // the Ship Frame rotated by trim and heel. Reading world coordinates as if they were Ship Frame
            // pollutes x with the marker's height (11.8 m at 2 deg trim is 0.4 m) -- degrees of error on a 1.6 m ring.
            double phi = PhiAt(ToRoot(_marker.transform.position), ToRoot(p));
            var nLocal = ShipFrame.ToUnity(Math.Cos(phi), Math.Sin(phi), 0);
            // MoveTo takes WORLD; keep the mounting point where it is and turn only the facing
            var n = root ? root.TransformDirection(nLocal) : nLocal;
            _marker.MoveTo(_marker.transform.position - _marker.NormalUnity * 0.01f, n, _marker.deckId, _marker.mountedOn);
            DrawRing();
        }

        Vector3 ToRoot(Vector3 world) => root ? root.InverseTransformPoint(world) : world;

        void EndDrag() { bool was = _dragging; _dragging = false; if (was && orbit) orbit.enabled = true; if (was && _marker) Rotated?.Invoke(_marker); }

        /// Mouse ray against the horizontal plane through the marker.
        bool PlanePoint(out Vector3 p)
        {
            p = default;
            var plane = new Plane(Vector3.up, _marker.transform.position);
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray, out float t)) return false;
            p = ray.GetPoint(t); return true;
        }

        void DrawRing()
        {
            if (_ring == null)
            {
                _ring = gameObject.AddComponent<LineRenderer>();
                _ring.useWorldSpace = true; _ring.widthMultiplier = 0.05f; _ring.loop = true;
                _ring.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.72f, 0f) };
            }
            var c = _marker.transform.position;
            var pts = new Vector3[36];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = c + new Vector3(Mathf.Cos(a) * radiusM, 0, Mathf.Sin(a) * radiusM);
            }
            _ring.positionCount = pts.Length; _ring.SetPositions(pts);
            _ring.enabled = true;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Unity EditMode 전체.
Expected: PASS. 145 + 3 = **148**

- [ ] **Step 5: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Landmarks/NormalGizmo.cs unity/Assets/ShipHdMap/Tests/EditMode/NormalGizmoTests.cs
git commit   # feat: turn a marker's normal by hand -- the loop M5c and M5d left open
```

---

### Task 11: 브리지 배선

웹의 도구·카메라·법선을 Unity 에 잇고, 새 컴포넌트를 `InitForTest` 에서 만들고, 법선이 바뀌면 커버리지를 다시 돌린다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`, `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/ProbeView.cs`
- Modify: `web/src/bridge/useShipUnity.ts`, `web/src/components/PropertyForm.tsx`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs` (추가)

**Interfaces:**
- Consumes: Task 7~10 전부, Task 5 의 `BridgeName`
- Produces: `BridgeMessages.SetTool`·`SetCamMode`·`SetNormal`
- Produces: `class SetToolMsg { public string tool; }`, `class SetCamModeMsg { public string mode; }`, `class SetNormalMsg { public string id; public double[] normal; }`
- Produces: `ProbeView.PlaceAt(Vector3 worldPoint, double zSurface)`, `ProbeView.Clear()`, `ProbeView.Active`, `ProbeView.Aim()`
- Produces: `MapRuntime.MarkerOf(string): LandmarkMarker`, `MapRuntime.MarkerPos(string): Vector3`, `MapRuntime.SetTool(string)`, `MapRuntime.SetCamMode(string)`, `MapRuntime.SetNormal(string)`
- Produces: `BridgeName` 에 `"SetNormal"` 추가

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`MapRuntimeTests.cs` 에 더한다:

```csharp
        /// 완료 기준 5: 편집 모드에서 3D 를 마음대로 클릭해도 마커가 생기지 않는다.
        /// 도구는 웹이 정하고, Load 가 그것을 지워서는 안 된다 — 지도를 다시 받았다고 배치 모드가 풀리면
        /// 마커를 줄지어 놓던 사람이 매번 다시 켜야 한다.
        [Test]
        public void SetToolReachesThePlacerAndSurvivesAReload()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Select));      // default: clicking must be safe
            rt.SetTool("{\"tool\":\"place\"}");
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Place));
            rt.Load(Fixture());
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Place));
        }

        /// The web saved the normal already; Unity just has to turn the quad and update the map reference,
        /// or the coverage the web is about to recompute will disagree with what the drive senses.
        [Test]
        public void SetNormalTurnsTheMarkerAndTheMapReference()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest(); rt.Load(Fixture());
            var before = rt.MapRefs["LM-0001"].phiRad;
            Assert.That(before, Is.EqualTo(System.Math.PI / 2).Within(1e-6));
            rt.SetNormal("{\"id\":\"LM-0001\",\"normal\":[1,0,0]}");
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(0).Within(1e-6));
            Assert.That(rt.MarkerOf("LM-0001").ToModel().normal[0], Is.EqualTo(1).Within(1e-3));
        }

        /// Two halves, because the early return is the whole risk: without an Orbit it must not throw, and
        /// WITH one it must actually set the mode. Asserting only the first half passes even if the body is dead.
        [Test]
        public void SetCamModeGuardsAMissingCameraAndOtherwiseSetsTheMode()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            Assert.DoesNotThrow(() => rt.SetCamMode("{\"mode\":\"fly\"}"));   // EditMode has no Orbit; must not NRE

            var camGo = new GameObject("cam"); camGo.AddComponent<Camera>();
            rt.Orbit = camGo.AddComponent<OrbitCamera>();
            rt.SetCamMode("{\"mode\":\"fly\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Fly));
            rt.SetCamMode("{\"mode\":\"driver\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Driver));
            Assert.That(rt.Orbit.driverTarget, Is.EqualTo(rt.Vehicle.transform));
            rt.SetCamMode("{\"mode\":\"orbit\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Orbit));
            Assert.That(rt.Orbit.driverTarget, Is.Null);
            Object.DestroyImmediate(camGo);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `SetTool`·`SetNormal`·`MarkerOf` 가 없다

- [ ] **Step 3: `ProbeView` 를 만든다**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// The virtual viewpoint (spec §5.3). Click the deck in Probe mode and the camera stands there at eye
    /// height, showing exactly what LandmarkSensor.VisibleFrom says a sensor there would see -- same call the
    /// drive makes, so the editor cannot promise a view the drive will not deliver.
    ///
    /// Never persisted: it is a question the operator is asking, not a fact about the ship.
    public class ProbeView : MonoBehaviour
    {
        public LandmarkSensor sensor; public SensorView view; public OrbitCamera orbit; public Transform root;
        public float eyeHeight = 1.2f, turnDegPerSec = 60f;
        public bool Active { get; private set; }
        Pose2D _pose; double _zSurface;

        public void PlaceAt(Vector3 worldPoint, double zSurface)
        {
            var local = root ? root.InverseTransformPoint(worldPoint) : worldPoint;
            var (x, y, _) = ShipFrame.ToShip(local);
            _pose = new Pose2D { x = x, y = y, psiRad = _pose.psiRad };   // keep the heading across re-clicks
            _zSurface = zSurface; Active = true;
            Aim();
        }

        public void Clear() { Active = false; if (view) view.Hide(); }

        void Update()
        {
            if (!Active) return;
            float turn = (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f);
            if (turn != 0f) { _pose.psiRad = ShipFrame.WrapRad(_pose.psiRad + turn * turnDegPerSec * Mathf.Deg2Rad * Time.unscaledDeltaTime); Aim(); }
        }

        /// Recompute the overlay and park the camera at the probe's eye.
        public void Aim()
        {
            if (sensor == null || view == null) return;
            var runtime = GetComponent<MapRuntime>();
            Vector3 eyeLocal = ShipFrame.ToUnity(_pose.x, _pose.y, _zSurface + eyeHeight);
            Vector3 eyeWorld = root ? root.TransformPoint(eyeLocal) : eyeLocal;
            var why = sensor.VisibleFrom(_pose, eyeWorld, runtime.MapRefs, id => runtime.MarkerPos(id));
            view.Show(_pose, _zSurface, why, id => runtime.MarkerOf(id), sensor.fovDeg, sensor.maxDist);
            if (orbit)
            {
                orbit.mode = CamMode.Driver; orbit.driverTarget = null;
                orbit.transform.SetPositionAndRotation(eyeWorld, Quaternion.Euler(0, ShipFrame.UnityYawDeg(_pose.psiRad * 180 / Math.PI), 0));
            }
        }
    }
}
```

`orbit.driverTarget` 이 `null` 이면 `ApplyDriver` 가 곧장 돌아오므로(위 Task 9) 관측점이 직접 놓은 자세가 유지된다.

`MarkerOf`·`MarkerPos` 는 이 Task 의 Step 5 가 `MapRuntime` 에 더한다. 그래서 `ProbeView` 가 Task 10 이 아니라 여기 있다 — 두 파일이 한 커밋에서 같이 컴파일된다.

- [ ] **Step 4: 메시지를 더한다**

`BridgeMessages.cs`:

```csharp
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario", SetTimeScale = "SetTimeScale", SetPrediction = "SetPrediction", SetOccluded = "SetOccluded",
            SetBeliefParams = "SetBeliefParams", SetTool = "SetTool", SetCamMode = "SetCamMode", SetNormal = "SetNormal";
```

```csharp
    public class SetToolMsg { public string tool; }          // select | place | probe
    public class SetCamModeMsg { public string mode; }       // orbit | fly | driver
    /// The web already wrote the new normal to the DB; this only turns the quad and the map reference.
    public class SetNormalMsg { public string id; public double[] normal; }
```

- [ ] **Step 5: `MapRuntime` 을 배선한다**

`InitForTest` 끝에 새 컴포넌트를 만든다. **씬에 두지 않는 이유는 Global Constraints 참조** — 씬에는 `Map` 과 카메라뿐이다.

```csharp
            // On the MAP ROOT, not on Vehicle: EnsureLine sets useWorldSpace = false, so the cone's points are
            // read as the parent's local space. VehicleController.Apply() overwrites Vehicle's transform every
            // frame (and the quay leg unparents it entirely), which would apply the vehicle pose a second time.
            View = gameObject.AddComponent<SensorView>();
            Gizmo = gameObject.AddComponent<NormalGizmo>(); Gizmo.root = LandmarksRoot;
            Probe = gameObject.AddComponent<ProbeView>(); Probe.sensor = Sensor; Probe.view = View; Probe.root = transform;
```

프로퍼티:

```csharp
        public SensorView View { get; private set; }
        public NormalGizmo Gizmo { get; private set; }
        public ProbeView Probe { get; private set; }

        public LandmarkMarker MarkerOf(string id) => _markers.TryGetValue(id, out var m) ? m : null;
        public Vector3 MarkerPos(string id) => _markers[id].transform.position;
```

`Awake` 의 카메라 줄은 `MapRuntime.cs:56` **한 줄**이다. 그 줄만 아래로 바꾼다 — 바로 위 `:55` 의 `if (cam == null) cam = Camera.main;` 는 그대로 둔다:

```csharp
            if (cam)
            {
                Placer.cam = cam; Hud.cam = cam; Gizmo.cam = cam;
                Orbit = cam.GetComponent<OrbitCamera>(); if (!Orbit) { Orbit = cam.gameObject.AddComponent<OrbitCamera>(); Orbit.AdoptCurrentPose(); }
                Probe.orbit = Orbit; Gizmo.orbit = Orbit;     // after Orbit exists, or both get null
            }
```

`Awake` 의 이벤트 구독에 셋을 더한다:

```csharp
            Placer.Cleared += () => { Highlight(null); Send(BridgeMessages.OnSelected, "{\"id\":null}"); };
            Placer.ProbeAt += hit => { var d = CurrentMap?.decks?.Find(x => x.id == Placer.DeckIdForHeight(LandmarksRoot.InverseTransformPoint(hit.point).y)); Probe.PlaceAt(hit.point, d?.z_surface ?? 0); };
            Gizmo.Rotated += OnMarkerMoved;   // the same path a drag-move takes: onFeatureMoved carries `normal`
```

메시지 핸들러:

```csharp
        public void SetTool(string json)
        {
            var t = MapJson.Parse<SetToolMsg>(json)?.tool;
            Placer.tool = t == "place" ? PlacerTool.Place : t == "probe" ? PlacerTool.Probe : PlacerTool.Select;
            if (Placer.tool == PlacerTool.Probe) return;
            Probe.Clear();
            // The probe drove the camera to its own eye with no driverTarget; leaving the tool must give the
            // camera back, or the toolbar offers no way out of a first-person view of nothing.
            if (Orbit && Orbit.mode == CamMode.Driver && Orbit.driverTarget == null) Orbit.mode = CamMode.Orbit;
        }

        public void SetCamMode(string json)
        {
            var m = MapJson.Parse<SetCamModeMsg>(json)?.mode;
            if (Orbit == null) return;                 // EditMode and the pre-camera frames have no orbit yet
            Orbit.mode = m == "fly" ? CamMode.Fly : m == "driver" ? CamMode.Driver : CamMode.Orbit;
            Orbit.driverTarget = Orbit.mode == CamMode.Driver ? Vehicle.transform : null;
            Orbit.follow = Orbit.mode == CamMode.Orbit && _mode == "drive" ? Vehicle.transform : null;
            if (Orbit.mode != CamMode.Driver) View.Hide();
        }

        /// The web edited the normal (gizmo release or the property form) and already persisted it.
        /// Turning the quad here keeps MapRefs -- and therefore the next drive -- in step with the DB.
        public void SetNormal(string json)
        {
            var m = MapJson.Parse<SetNormalMsg>(json);
            if (m?.id == null || m.normal == null || m.normal.Length < 3 || !_markers.TryGetValue(m.id, out var mk)) return;
            var n = LandmarksRoot.TransformDirection(ShipFrame.ToUnity(m.normal[0], m.normal[1], m.normal[2]));
            mk.MoveTo(mk.transform.position - mk.NormalUnity * 0.01f, n, mk.deckId, mk.mountedOn);
            MapRefs[m.id] = RefOf(mk.ToModel());
        }
```

`StartScenario`(`MapRuntime.cs:282`)의 `if (Orbit)` 블록에 한 줄을 더한다. 모드 게이팅이 생기면서 `follow` 는 **궤도 모드에서만** 동작하므로, 자유 비행 중에 주행을 시작하면 카메라가 차를 안 따라간다:

```csharp
            if (Orbit) { Orbit.follow = Vehicle.transform; Orbit.distance = 25f; Orbit.pitchDeg = 35f;
                // free flight follows nothing; driver's eye is a valid way to watch a run, so keep that one
                if (Orbit.mode != CamMode.Driver) Orbit.mode = CamMode.Orbit; }
```

`Load` 안에서 기즈모·관측점을 떼어 놓는다 (마커를 전부 지우므로). `MapRuntime.cs:126` 의 `ScenarioPhase = Phase.Idle; …` 줄 **바로 뒤**에 넣는다:

```csharp
            Gizmo.Detach(); Probe.Clear(); View.Hide();
```

`Highlight` 끝에 기즈모를 붙인다 — 선택된 마커에만 핸들이 뜬다:

```csharp
            if (_selected != null && _mode == "edit") Gizmo.Attach(_markers[_selected]); else Gizmo.Detach();
```

`Localize()` 끝에 차량 시선 표시를 붙인다. `_seen` 은 **믿음 판정용으로 그대로 두고**(스펙 §5.3) 표시는 `VisibleFrom` 이 낸 이유까지 쓴다:

```csharp
            // Vehicle.Z, not _targetDeck.z_surface: the target deck is where the car is GOING, and on the quay
            // and the ramp it is nowhere near that height. Vehicle.Z is the current path point, right every frame.
            // (_targetDeck would never be null here either -- Step returns early unless a run is under way.)
            if (Orbit != null && Orbit.mode == CamMode.Driver)
            {
                Vector3 eye = Sensor.transform.position + Vector3.up * Sensor.eyeHeight;
                View.Show(truth, Vehicle.Z, Sensor.VisibleFrom(truth, eye, MapRefs, MarkerPos), MarkerOf, Sensor.fovDeg, Sensor.maxDist);
            }
```

- [ ] **Step 6: 웹 쪽을 잇는다**

`useShipUnity.ts:6` 의 `BridgeName` 에 `| "SetNormal"` 을 더한다 (`SetTool`·`SetCamMode` 는 Task 5 에서 더했다).

`reloadScene` 과 초기 `Load` 뒤에 **사람이 맞춰 둔 상태를 다시 보낸다.** M4 에서 "재로드 후 Select 재전송"을 빠뜨려 데었던 자리인데, M5e 는 저장까지 하므로 새로고침 직후에도 필요하다:

```ts
  /** Everything Unity forgets on a fresh Load or a page reload: the tool, the camera, the sensor and the occlusion set. */
  const sendEditorState = useCallback(() => {
    const ed = useEditorStore.getState();
    const ui = useUiStore.getState();
    send("SetTool", { tool: ui.tool });
    send("SetCamMode", { mode: ui.cam });
    send("SetNoise", ed.noise);
    send("SetTimeScale", { scale: ed.timeScale });
    send("SetOccluded", { ids: ed.occluded });
  }, [send]);
```

초기 `Load` 의 `.then` 안에서 `sendPose();` 다음에, 그리고 `reloadScene` 의 `send("Select", …)` 다음에 `sendEditorState();` 를 부른다. `useUiStore` import 를 더한다.

법선이 바뀌면 커버리지를 다시 돌린다 — 완료 기준 4 의 나머지 반쪽이다. `onMoved` 를 고친다:

```ts
    const onMoved = (json: string) => {
      void moveFeature(JSON.parse(json)).then(() => {
        // a normal is worth far more to coverage than a position (M5c/M5d): recompute so the heatmap
        // answers the gizmo drag that just happened
        const ed = useEditorStore.getState();
        const deck = pickDeck(ed.decks, ed.deckFilter)?.id;
        if (deck) void ed.runCoverage(deck);
      }).catch(async (e) => { … 기존 그대로 … });
    };
```

`import { pickDeck } from "../geo/deck";` 를 더한다.

`PropertyForm.tsx:40` 의 "적용" 버튼이 법선을 고쳤을 수 있으므로 저장 뒤 Unity 에 알린다:

```tsx
          <button className="btn primary" onClick={() => run(async () => {
            const props = parseProps();
            await s.updateFeature(id, { kind, deck_id: deck || undefined, props });
            // Unity holds its own copy of the normal; without this the quad and MapRefs keep the old facing
            if (Array.isArray(props.normal)) send("SetNormal", { id, normal: props.normal });
            const d = pickDeck(s.decks, s.deckFilter)?.id; if (d) void s.runCoverage(d);
          })}>적용</button>
```

`import { pickDeck } from "../geo/deck";` 를 더한다.

- [ ] **Step 7: 테스트·빌드 확인**

Run (웹): `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: PASS 69, 빌드 성공

Unity EditMode 전체.
Expected: PASS. 148 + 3 = **151**

- [ ] **Step 8: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime web/src/bridge/useShipUnity.ts web/src/components/PropertyForm.tsx unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs
git commit   # feat: wire the tools, the cameras and the normal edit across the bridge
```

---

### Task 12: WebGL 빌드와 브라우저 검증

단위 테스트는 규칙을 지키지만 **이 마일스톤이 약속한 것은 화면**이다. M5a 는 결함 셋을, M5d 는 둘을 손으로만 잡았다. 여기서 못 보면 스펙에 거짓이 남는다.

**Files:**
- Modify: 검증에서 나온 결함만
- Test: 브라우저

- [ ] **Step 1: 빌드하고 띄운다**

```bash
cd unity && /Applications/Unity/Hub/Editor/6000.3*/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -executeMethod ShipHdMap.Editor.WebGLBuild.Build -logFile -
cd ../api && ./gradlew bootRun &
cd ../web && pnpm dev
```

- [ ] **Step 2: 여덟 항목을 순서대로 확인한다 (스펙 §7.3)**

각 항목의 결과를 `docs/learning/10-m5e-review.md` 에 그 자리에서 적는다.

1. **편집 모드에서 3D 를 회전·클릭해도 마커가 생기지 않는다.** 빈 곳·구조물·기둥을 각각 클릭한다. 툴바에서 `배치` 로 바꾸면 같은 클릭이 마커를 만든다. `A` 와 `V` 로도 같은지 본다
2. **트리에서 마커를 고르면 3D 가 그리로 가고 속성 탭이 뜬다.** 커버리지 탭을 연 채로 고른다 — 속성으로 튀어야 한다. 해제하면 커버리지로 돌아온다. 속성의 삭제 버튼으로 지워진다
3. **기즈모로 법선을 90° 돌리면 히트맵과 `blind_ratio` 가 바뀐다.** 돌리기 전 값을 적고, 돌린 뒤 값을 적는다. **평면도의 그 마커 화살표도 같이 돈다.** 새로고침 후에도 돈 채로 남아 있다 (DB 에 갔다는 뜻)
4. **새로고침 후 탭·갑판·모드·슬라이더 전부·카메라 모드·도크 높이·평면도 확대가 그대로다.** 슬라이더는 커버리지 7 개, 노이즈 4 개, 믿음 6 개, 시간 배율을 각각 기본값이 아닌 값으로 두고 확인한다
5. **자유 비행으로 갑판을 돌아다닌다.** 3D 를 클릭해 포커스를 준 뒤 `WASD`·`Q`·`E`·`Shift`. 포커스를 오른쪽 패널로 옮기면 키가 안 먹고, `A` 가 배치 모드로 가지 **않는지** 본다(포커스가 3D 에 있을 때). 속성 패널 `props` 칸에서 `0`·`1`·`F` 를 쳐도 뷰가 안 바뀌는지 본다
6. **하역 주행을 차량 시선으로 본다.** `C` 로 차량 시선까지 순환한다. 선미로 갈수록 마커의 초록 테가 하나씩 꺼지고, **마지막이 꺼지는 프레임에 믿음 배지가 `상실` 로 바뀌는지** 본다. 콘과 호가 바닥에 그려져 있어 "왜 꺼졌나"가 읽히는지 본다
7. **관측 모드로 바깥 열 구획에 관측점을 찍으면 보이는 마커가 적고, 차로에 찍으면 많다.** 각각 몇 개인지 센다. 화살표 키로 `ψ` 를 돌리면 집합이 바뀐다
8. **평면도를 확대해도 3D 는 그대로다.** 휠로 확대하고 드래그로 옮긴다. 마커가 커서 아래에 붙어 있는지, `0` 으로 전체가 돌아오는지, 배율 4 를 넘으면 `LM-0003` 라벨이 뜨는지 본다

- [ ] **Step 3: 나온 결함을 고친다**

각 수정은 자기 테스트를 데려온다. 브라우저에서만 보이는 결함이면 **왜 단위 테스트가 못 봤는지**를 리뷰 문서에 적는다 (M5d 교훈 둘).

- [ ] **Step 4: 전체 확인**

```bash
cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build && pnpm lint
cd ../api && ./gradlew test
```
Unity EditMode 전체.
Expected: web 69, Unity 151, api 는 시작할 때 센 값 그대로 (이 마일스톤은 `api/` 를 건드리지 않는다)

- [ ] **Step 5: 리뷰 문서와 커밋**

`docs/learning/10-m5e-review.md` 를 이전 리뷰들과 같은 형식으로 쓴다: 한 줄 요약, 구성, 핵심 개념, **계획서·스펙이 틀렸고 무엇이 잡았나** 표, 교훈, 파킹 항목.

```bash
git add -A
git commit   # docs: what the browser said about M5e
```

---

## 완료 기준 대조 (스펙 §1.4)

| # | 기준 | 어디서 |
|---|---|---|
| 1 | 새로고침해도 탭·갑판·모드·슬라이더·카메라(모드)·도크가 그대로 | T1, T2 / 검증 T12-2.4 |
| 2 | 히트맵을 확대 없이 읽고 범례가 색과 분모를 설명 | T3(LEGEND), T4(전폭 도크). 분모는 이미 있음 — 위 "스펙과 달라지는 점" A |
| 3 | 트리에서 고르면 3D 카메라가 가고 세 곳이 같은 선택을 보인다 | 이미 있음(B) + T4(평면도 선택 강조), T5(속성 탭 전환) / 검증 T12-2.2 |
| 4 | 3D 에서 법선을 돌려 저장하면 커버리지 숫자가 바뀐다 | T10(기즈모) + T11(`onFeatureMoved` 재사용 + `runCoverage`) / 검증 T12-2.3 |
| 5 | 편집 모드에서 클릭·회전해도 마커가 안 생긴다 | T7(`Decide`) + T11(`SetTool`) / 검증 T12-2.1 |
| 6 | 차량 시선에서 마커가 하나씩 꺼지고 마지막이 `상실` 과 같은 시점 | T8(`VisibleFrom`·`SensorView`) + T9(Driver) + T11(`Localize` 배선) / 검증 T12-2.6 |
| 7 | 관측점에서 세 조건이 각각 다른 이유로 읽힌다 | T8(`Miss`) + T11(`ProbeView`) / 검증 T12-2.7 |

## 테스트 총계

| | 시작 | 끝 | 늘어나는 곳 |
|---|---|---|---|
| Vitest | 38 | **69** | T1 +10, T2 +2, T3 +10, T5 +2, T6 +7 |
| Unity EditMode | 134 | **151** | T7 +4, T8 +4, T9 +3, T10 +3, T11 +3 |
| api (Gradle) | 그대로 | 그대로 | 이 마일스톤은 `api/` 를 건드리지 않는다 |
