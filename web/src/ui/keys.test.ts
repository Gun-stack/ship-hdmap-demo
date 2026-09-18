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
    expect(commandFor(key("]"), FREE)).toEqual({ kind: "tab", dir: 1 });
    expect(commandFor(key("["), FREE)).toEqual({ kind: "tab", dir: -1 });
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
    for (const k of ["v", "a", "p", "c", "0", "1", "f", "]", "?"]) {
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

  /// Tab 은 잡지 않는다 — 버튼에 포커스가 있을 때 삼키면 키보드만 쓰는 사람이
  /// 툴바에서 입력 필드로 영영 못 들어간다. 탭 순환은 `[` `]` 로 옮겼다.
  it("Tab 은 브라우저의 포커스 순회에 맡긴다", () => {
    expect(commandFor(key("Tab"), FREE)).toBeNull();
  });

  /// 도움말과 매핑이 갈라지면 화면이 거짓말을 한다 — 실제로 명령을 내는 키가 표에 전부 있는지 본다.
  it("도움말 표가 실제로 먹는 키를 빠짐없이 덮는다", () => {
    const live = ["v", "a", "p", "c", "1", "2", "3", "[", "]", "f", "0", "Escape", "?"]
      .filter((k) => commandFor(key(k), FREE) !== null);
    const text = HELP_ROWS.map(([k]) => k).join(" ").toLowerCase();
    for (const k of live) {
      const needle = k === "Escape" ? "esc" : k.toLowerCase();
      expect(text, `${k} 가 도움말에 없다`).toContain(needle);
    }
    for (const [k, what] of HELP_ROWS) { expect(k).toBeTruthy(); expect(what).toBeTruthy(); }
  });
});
