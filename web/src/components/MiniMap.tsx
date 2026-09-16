import { useEditorStore, visibleFeatures } from "../store/editor";
import type { Feature } from "../api/types";

export function bbox(ring: number[][], margin = 0) {
  const xs = ring.map((p) => p[0]), ys = ring.map((p) => -p[1]);
  const x = Math.min(...xs) - margin, y = Math.min(...ys) - margin;
  return { x, y, w: Math.max(...xs) - Math.min(...xs) + 2 * margin, h: Math.max(...ys) - Math.min(...ys) + 2 * margin };
}
/** Ship y (port +) points up on screen, so SVG y = -ship y. */
export function ringPath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" ") + " Z"; }
export function linePath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" "); }

export function MiniMap() {
  const s = useEditorStore();
  const decks = s.deckFilter === "all" ? s.decks : s.decks.filter((d) => d.id === s.deckFilter);
  const area = (d: { outline: number[][] }) => (d.outline.length ? bbox(d.outline).w * bbox(d.outline).h : 0);
  const deck = [...decks, ...s.decks].filter((d) => d.outline.length > 0).sort((a, b) => area(b) - area(a))[0];
  if (!deck) return <div className="panel"><h4>평면도</h4><span>갑판 없음</span></div>;
  const b = bbox(deck.outline, 3);
  const feats = visibleFeatures(s);
  const sel = s.selectedId;
  const mark = (f: Feature) => (f.id === sel ? { stroke: "#1e88e5", strokeWidth: 0.8 } : {});
  return (
    <div className="panel">
      <h4>{deck.name} 평면도</h4>
      <svg viewBox={`${b.x} ${b.y} ${b.w} ${b.h}`} style={{ width: "100%", background: "#f3f6f9" }}>
        {decks.map((d) => <path key={d.id} d={ringPath(d.outline)} fill="none" stroke="#7a8" strokeWidth={0.4} />)}
        {feats.filter((f) => f.layer === "A2").map((f) => <path key={f.id} d={linePath(f.geometry.coordinates as number[][])} fill="none" stroke="#1e88e5" strokeWidth={0.4} strokeDasharray="2 1" onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "B2").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="rgba(30,136,229,.15)" stroke="#1e88e5" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "C" && f.geometry.type === "Polygon").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="#999" stroke="#666" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "LM").map((f) => { const c = f.geometry.coordinates as number[]; return <rect key={f.id} x={c[0] - 0.7} y={-c[1] - 0.7} width={1.4} height={1.4} transform={`rotate(45 ${c[0]} ${-c[1]})`} fill="#e53935" onClick={() => s.select(f.id)} {...mark(f)} />; })}
        {Object.values(s.drafts).map((d) => { const c = d.geometry.coordinates as number[]; return <circle key={d.tempId} cx={c[0]} cy={-c[1]} r={0.9} fill="none" stroke="#ff9800" strokeWidth={0.4} />; })}
      </svg>
    </div>
  );
}
