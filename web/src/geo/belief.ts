/**
 * Five states, five colours; an unknown value must look wrong rather than healthy. The colour marks a dot
 * beside the label (the label itself stays in text ink), so these only have to be told apart, not read.
 */
export function beliefBadge(state: string): { label: string; color: string } {
  switch (state) {
    case "ok": return { label: "정상", color: "#2fb344" };
    case "degraded": return { label: "저하", color: "#f0a500" };
    case "lost": return { label: "상실", color: "#e5484d" };
    case "backtracking": return { label: "역추적", color: "#9d7cf0" };
    case "stopped": return { label: "정지", color: "#7d8591" };
    default: return { label: "알 수 없음", color: "#a1887f" };
  }
}

/** How much worse than the map promised. Dash when there is nothing to compare against. */
export function fmtRatioOf(actual: number | null | undefined, predicted: number | null | undefined): string {
  if (actual == null || predicted == null || predicted <= 0) return "—";
  return `${(actual / predicted).toFixed(1)}×`;
}
