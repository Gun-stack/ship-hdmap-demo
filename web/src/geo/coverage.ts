import type { CoverageCell, CoverageSensor } from "../api/types";

export const BLIND_COLOR = "#d32f2f";
/** Must match CoverageAnalyzer.DEFAULTS. Seeding the sliders with these keeps the UI and the server in step. */
export const SENSOR_DEFAULTS: CoverageSensor = {
  fov_deg: 90, max_dist_m: 25, max_view_angle_deg: 70, sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2,
};

/** blind is its own colour; otherwise ramp orange -> green by stability, saturating at 2.0. */
export function cellColor(c: CoverageCell): string {
  if (c.n === 0 || c.stability == null) return BLIND_COLOR;
  const t = Math.min(c.stability, 2) / 2;                  // 0 .. 1
  const r = Math.round(240 - 180 * t), g = Math.round(120 + 80 * t), b = Math.round(40 + 40 * t);
  return `#${[r, g, b].map((v) => v.toString(16).padStart(2, "0")).join("")}`;
}

/** Cells nobody drives through are drawn faintly: visible, but plainly not part of the numbers. */
export function cellOpacity(c: CoverageCell): number { return c.in_scope === false ? 0.18 : 0.55; }

export function fmtRatio(v: number): string { return `${(v * 100).toFixed(1)} %`; }
