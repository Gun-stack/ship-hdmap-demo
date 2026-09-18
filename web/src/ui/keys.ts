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

type KeyLike = { key: string; code: string; ctrlKey: boolean; metaKey: boolean; altKey: boolean; isComposing: boolean };
type Ctx = { inField: boolean; flyingFocused: boolean };

/**
 * Keys free flight owns, named by PHYSICAL key. Unity reads them straight off the focused canvas;
 * the web must not also act on them. See the `e.code` note on commandFor for why not `e.key`.
 */
export const FLY_KEYS = new Set(["KeyW", "KeyA", "KeyS", "KeyD", "KeyQ", "KeyE", "ShiftLeft", "ShiftRight", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"]);

const TOOL_KEYS: Record<string, Tool> = { KeyV: "select", KeyA: "place", KeyP: "probe" };

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
  // Composing (한글 입력 중) owns every key, Escape included -- that Escape is what cancels the
  // composition itself. Not also checking legacy keyCode === 229: it only ever attaches to
  // character keys during composition, never to the Escape that ends one (that arrives as 27),
  // so it could not have guarded this case anyway -- and the character keys it does cover are
  // already stopped by the inField gate below.
  if (e.isComposing) return null;
  if (e.key === "Escape") return { kind: "escape" };      // always a way out, even mid-typing
  if (e.ctrlKey || e.metaKey || e.altKey) return null;    // browser shortcuts stay the browser's
  if (ctx.inField) return null;
  // LETTERS GO BY e.code, NOT e.key. Measured in Chrome/macOS with the 2-Set Korean IME on and focus
  // on <body>: KeyV arrives as key "ㅍ" (and W/A/S/D/Q/E/C/F/P likewise), keyCode still 86, code still
  // "KeyV", isComposing false. Reading e.key would kill every letter shortcut for a user typing Korean --
  // which is every user of this UI. Digits, brackets, arrows and Escape were measured UNCHANGED by the
  // IME, so they stay on e.key: that keeps the numpad and non-QWERTY digit rows working.
  if (ctx.flyingFocused && FLY_KEYS.has(e.code)) return null;
  if (e.code in TOOL_KEYS) return { kind: "tool", tool: TOOL_KEYS[e.code] };
  if (e.code === "KeyC") return { kind: "cam" };
  const k = e.key.toLowerCase();
  if (k === "1" || k === "2" || k === "3") return { kind: "deck", index: Number(k) - 1 };
  // Not Tab: swallowing it on a focused button strands keyboard users in the toolbar. Brackets are free.
  if (k === "]") return { kind: "tab", dir: 1 };
  if (k === "[") return { kind: "tab", dir: -1 };
  if (e.code === "KeyF") return { kind: "fit" };
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
  ["[ / ]", "오른쪽 탭 순환 (Tab 은 브라우저 포커스 순회에 남겨 둔다)"],
  ["?", "이 목록"],
];
