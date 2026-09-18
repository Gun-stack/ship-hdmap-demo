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
 * Takes no deck box: the anchor is already absolute, and tsconfig sets noUnusedParameters.
 */
export function zoomAt(v: PlanView, anchor: { x: number; y: number }, k: number): PlanView {
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
