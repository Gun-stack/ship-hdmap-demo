/** A rectangle in plan (SVG) space: x forward, y DOWN. bbox already flips ship y, so nothing downstream flips it again. */
export type Box = { x: number; y: number; w: number; h: number };

export function bbox(ring: number[][], margin = 0) {
  const xs = ring.map((p) => p[0]), ys = ring.map((p) => -p[1]);
  const x = Math.min(...xs) - margin, y = Math.min(...ys) - margin;
  return { x, y, w: Math.max(...xs) - Math.min(...xs) + 2 * margin, h: Math.max(...ys) - Math.min(...ys) + 2 * margin };
}
/** Ship y (port +) points up on screen, so SVG y = -ship y. */
export function ringPath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" ") + " Z"; }
export function linePath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" "); }

/** Deck to frame the minimap on: the filtered deck when a filter is active, else the largest deck that has an outline. undefined → nothing to draw. */
export function pickDeck<T extends { id: string; outline: number[][] }>(decks: T[], filter: string): T | undefined {
  const area = (d: T) => (d.outline.length ? bbox(d.outline).w * bbox(d.outline).h : 0);
  const pool = (filter === "all" ? decks : decks.filter((d) => d.id === filter)).filter((d) => d.outline.length > 0);
  return [...pool].sort((a, b) => area(b) - area(a))[0];
}
