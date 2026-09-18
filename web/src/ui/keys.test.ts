// @vitest-environment jsdom
// isInField below needs a real DOM (document.createElement); the rest of the suite runs in the
// faster default "node" environment, so this file opts in on its own rather than switching everyone.
import { describe, expect, it } from "vitest";
import { commandFor, isInField, HELP_ROWS } from "./keys";

/// `code` is derived the way a US-layout QWERTY board would report it, so every case below reads as one
/// keystroke. A Korean-mode keystroke is the one place key and code DISAGREE -- see `ko()` further down.
const codeOf = (k: string) =>
  /^[a-z]$/i.test(k) ? "Key" + k.toUpperCase()
  : /^[0-9]$/.test(k) ? "Digit" + k
  : k === "[" ? "BracketLeft" : k === "]" ? "BracketRight" : k === "?" ? "Slash"
  : k === "Shift" ? "ShiftLeft" : k;

const key = (k: string, mod: Partial<{ ctrlKey: boolean; metaKey: boolean; altKey: boolean; isComposing: boolean }> = {}) =>
  ({ key: k, code: codeOf(k), timeStamp: 0, ctrlKey: false, metaKey: false, altKey: false, isComposing: false, ...mod });
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

  /// 한글 입력 모드에서 글자 키는 `e.key` 가 자모로 온다. Chrome/macOS 2-벌식, 포커스가 <body> 일 때 실측:
  /// KeyV → key "ㅍ", keyCode 86, code "KeyV", isComposing false (조합이 아니므로 isComposing 게이트도 못 막는다).
  /// `e.key` 를 읽으면 한국어 UI 에서 글자 단축키 **전체**가 죽는다. 숫자·괄호·화살표·Escape 는 실측상 그대로다.
  it("한글 입력 모드에서도 글자 단축키가 먹는다", () => {
    const ko = (code: string, jamo: string) =>
      ({ key: jamo, code, timeStamp: 0, ctrlKey: false, metaKey: false, altKey: false, isComposing: false });
    expect(commandFor(ko("KeyV", "ㅍ"), FREE)).toEqual({ kind: "tool", tool: "select" });
    expect(commandFor(ko("KeyA", "ㅁ"), FREE)).toEqual({ kind: "tool", tool: "place" });
    expect(commandFor(ko("KeyP", "ㅔ"), FREE)).toEqual({ kind: "tool", tool: "probe" });
    expect(commandFor(ko("KeyC", "ㅊ"), FREE)).toEqual({ kind: "cam" });
    expect(commandFor(ko("KeyF", "ㄹ"), FREE)).toEqual({ kind: "fit" });
    // 비행 게이트도 자모로 와야 막힌다 -- 안 막으면 한글 모드로 좌로 날다가 ㅁ(KeyA) 이 배치 모드로 튄다.
    const fly = { inField: false, flyingFocused: true };
    for (const [c, j] of [["KeyW", "ㅈ"], ["KeyA", "ㅁ"], ["KeyS", "ㄴ"], ["KeyD", "ㅇ"], ["KeyQ", "ㅂ"], ["KeyE", "ㄷ"]]) {
      expect(commandFor(ko(c, j), fly), `${c} 가 비행 중에 새 나간다`).toBeNull();
    }
  });

  it("수식 키가 눌리면 브라우저에 넘긴다", () => {
    expect(commandFor(key("0", { metaKey: true }), FREE)).toBeNull();
    expect(commandFor(key("a", { ctrlKey: true }), FREE)).toBeNull();
  });

  /// 한글 입력 중에는 Escape 도 IME 의 것이다 -- 조합을 취소하는 게 그 Escape 이지,
  /// 선택 해제가 아니다. 조합 중에 명령이 새 나가면 반쯤 쓴 글자 밑에서 패널이 사라진다.
  it("조합 중이면 Escape 도 먹지 않는다", () => {
    expect(commandFor(key("Escape", { isComposing: true }), FREE)).toBeNull();
    expect(commandFor(key("v", { isComposing: true }), FREE)).toBeNull();
  });

  /// 한 번 누른 Escape 가 한글 조합 중에는 **두 번** 온다 (Chrome/macOS 2벌식, 속성 폼 textarea 에서 실측):
  ///   keydown Escape kc 229 isComposing true  → 위의 조합 게이트가 막는다
  ///   compositionend "ㄱ"
  ///   keydown Escape kc  27 isComposing false → 게이트를 통과해 선택을 풀어 버렸다
  /// 둘은 같은 키 누름이라 `timeStamp` 가 같다. 그 동일성으로만 짝지을 수 있고, 타이머도 허용 오차도 필요 없다.
  it("조합을 취소한 Escape 는 뒤따르는 평문 Escape 까지 삼킨다", () => {
    const at = 313779;
    const esc = (isComposing: boolean) =>
      ({ key: "Escape", code: "Escape", timeStamp: at, ctrlKey: false, metaKey: false, altKey: false, isComposing });
    const field = { inField: true, flyingFocused: false };
    // 1) 조합 중의 Escape — 조합 게이트가 막는다. 핸들러는 이 timeStamp 를 기억한다.
    expect(commandFor(esc(true), field)).toBeNull();
    // 2) 같은 누름에서 나온 평문 Escape — 기억한 timeStamp 와 같으므로 삼킨다.
    expect(commandFor(esc(false), { ...field, composingEscapeAt: at })).toBeNull();
    // 3) 그 뒤 사람이 진짜로 다시 누른 Escape 는 다른 누름이라 timeStamp 가 다르다 — 반드시 먹어야 한다.
    expect(commandFor({ ...esc(false), timeStamp: at + 120 }, { ...field, composingEscapeAt: at }))
      .toEqual({ kind: "escape" });
    // 4) 조합이 없었으면 기억한 값도 없다 — 평범한 Escape 는 그대로 먹는다.
    expect(commandFor(esc(false), field)).toEqual({ kind: "escape" });
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
      // 단어 경계로 찾는다 -- 순수 substring 이면 "c" 가 "esc" 안에서 걸려, C 행을 지워도 이 테스트가 못 잡는다.
      const found = /^[a-z0-9]+$/.test(needle) ? new RegExp(`\\b${needle}\\b`).test(text) : text.includes(needle);
      expect(found, `${k} 가 도움말에 없다`).toBe(true);
    }
    for (const [k, what] of HELP_ROWS) { expect(k).toBeTruthy(); expect(what).toBeTruthy(); }
  });
});

describe("isInField", () => {
  const tag = (name: string, opts: { editable?: boolean } = {}) => {
    const el = document.createElement(name);
    if (opts.editable) Object.defineProperty(el, "isContentEditable", { value: true });
    return el;
  };

  it("INPUT · TEXTAREA · SELECT · contentEditable 는 필드다", () => {
    expect(isInField(tag("input"))).toBe(true);
    expect(isInField(tag("textarea"))).toBe(true);
    expect(isInField(tag("select"))).toBe(true);
    expect(isInField(tag("div", { editable: true }))).toBe(true);
  });

  /// 버튼은 필드가 아니다 -- 그게 아니면 탭 순환을 `[` `]` 로 옮긴 이유가 없다.
  it("버튼은 필드가 아니다", () => {
    expect(isInField(tag("button"))).toBe(false);
  });

  it("포커스가 없으면 필드가 아니다", () => {
    expect(isInField(null)).toBe(false);
  });
});
