/** Five states, five colours; an unknown value must look wrong rather than healthy. */
export function beliefBadge(state: string): { label: string; color: string } {
  switch (state) {
    case "ok": return { label: "정상", color: "#2e7d32" };
    case "degraded": return { label: "저하", color: "#f9a825" };
    case "lost": return { label: "상실", color: "#d32f2f" };
    case "backtracking": return { label: "역추적", color: "#6a1b9a" };
    case "stopped": return { label: "정지", color: "#424242" };
    default: return { label: "알 수 없음", color: "#8d6e63" };
  }
}

/** How much worse than the map promised. Dash when there is nothing to compare against. */
export function fmtRatioOf(actual: number | null | undefined, predicted: number | null | undefined): string {
  if (actual == null || predicted == null || predicted <= 0) return "—";
  return `${(actual / predicted).toFixed(1)}×`;
}
