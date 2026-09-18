import { useRef, useState } from "react";
import { useEditorStore, visibleFeatures } from "../store/editor";
import { useUiStore, type PlanView } from "../store/ui";
import { bbox, linePath, pickDeck, ringPath } from "../geo/deck";
import { LEGEND, clampView, fitTo, screenToPlan, viewBoxOf, zoomAt } from "../geo/plan";
import { cellColor, cellOpacity } from "../geo/coverage";

// A pointerdown that never moves this many screen px is a click, not a drag -- small enough that any
// real drag crosses it almost at once, big enough to absorb the jitter a plain click always has.
const DRAG_PX = 4;
// One store write per wheel *gesture*, not per tick: a burst of ticks keeps pushing this out.
const WHEEL_COMMIT_MS = 250;

export function PlanDock() {
  const s = useEditorStore();
  const ui = useUiStore();
  const svgRef = useRef<SVGSVGElement>(null);
  // Anchor plus the screen point pointerdown happened at, so pointermove can tell a click from a drag
  // before committing to either.
  const drag = useRef<{ x: number; y: number; sx: number; sy: number } | null>(null);
  const dragging = useRef(false);   // crossed DRAG_PX -- only then does this pointerdown own the gesture
  const wheelTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cancelWheelCommit = () => { if (wheelTimer.current) { clearTimeout(wheelTimer.current); wheelTimer.current = null; } };
  // The store is persisted, and zustand's persist serialises the whole slice synchronously on EVERY set.
  // A pan is one set per pointermove, so the drag lives in local state and only the release reaches the store.
  const [live, setLive] = useState<PlanView | null>(null);
  const view = live ?? ui.planView;

  if (!ui.dockOpen) return <div className="dock collapsed"><button className="btn" onClick={ui.toggleDock}>평면도 펴기</button></div>;
  const deck = pickDeck(s.decks, s.deckFilter);
  if (!deck) return (
    <div className={`dock${ui.dockTall ? " tall" : ""}`}>
      <div className="dockbar">
        <span style={{ color: "#888" }}>갑판 없음</span>
        <button className="btn" onClick={ui.toggleDockTall}>{ui.dockTall ? "낮게" : "크게"}</button>
        <button className="btn" onClick={ui.toggleDock}>접기</button>
      </div>
    </div>
  );

  const box = bbox(deck.outline, 3);
  const feats = visibleFeatures(s);
  const mark = (f: (typeof feats)[number]) => (s.selectedId === f.id ? { stroke: "#1e88e5", strokeWidth: 0.8 } : {});
  const zoomed = view.scale >= 4;   // `view` is what's on screen right now; the store only catches up 250ms after a wheel gesture ends
  const at = (e: { clientX: number; clientY: number }) => screenToPlan({ x: e.clientX, y: e.clientY }, svgRef.current!);

  return (
    <div className={`dock${ui.dockTall ? " tall" : ""}`}>
      <div className="dockbar">
        <span>{deck.name} 평면도 · ×{view.scale.toFixed(1)}</span>
        <button className="btn" onClick={() => ui.setPlanView(fitTo(box, box))}>전체 (0)</button>
        <button className="btn" onClick={ui.toggleDockTall}>{ui.dockTall ? "낮게" : "크게"}</button>
        <button className="btn" onClick={ui.toggleDock}>접기</button>
        <span className="legend">
          {LEGEND.map((l) => <span key={l.label}><i style={{ background: l.color }} />{l.label}</span>)}
        </span>
      </div>
      <svg ref={svgRef} viewBox={viewBoxOf(box, view)} className="plan" preserveAspectRatio="xMidYMid meet"
        onWheel={(e) => {
          // Zoom lives in the same local `live` state the pan uses, so the two can never disagree about
          // which view is current -- and the write to the persisted store is debounced to one per burst
          // of ticks, not one per tick.
          const next = clampView(box, zoomAt(view, at(e), e.deltaY < 0 ? 1.2 : 1 / 1.2));
          setLive(next);
          cancelWheelCommit();
          wheelTimer.current = setTimeout(() => { ui.setPlanView(next); wheelTimer.current = null; setLive(null); }, WHEEL_COMMIT_MS);
        }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          // A pending wheel-zoom is a real part of the current view that just hasn't reached the store
          // yet -- flush it now instead of merely cancelling the debounce, or a click's local-state
          // reset (below) and a drag reseeding itself would each throw it away right after.
          if (wheelTimer.current && live) ui.setPlanView(live);
          cancelWheelCommit();
          // Record the anchor and where the gesture started, but do NOT capture yet: capturing here
          // retargets the pointerup/click to this <svg>, so the onClick handlers below never see it and
          // clicking a marker or a slot in the plan view stops working entirely.
          drag.current = { ...at(e), sx: e.clientX, sy: e.clientY };
          dragging.current = false;
        }}
        onPointerMove={(e) => {
          if (!drag.current) return;
          if (e.buttons === 0) {
            // Deferring capture until the drag threshold (below) is what makes this possible: a release
            // that lands outside this <svg> before that threshold is crossed never reaches onPointerUp
            // (there is no capture yet to retarget it here), so drag.current can be left set from a
            // gesture that already ended. A later button-less hover must not resume it as a pan.
            drag.current = null;
            dragging.current = false;
            return;
          }
          if (!dragging.current) {
            const dx = e.clientX - drag.current.sx, dy = e.clientY - drag.current.sy;
            if (dx * dx + dy * dy < DRAG_PX * DRAG_PX) return;   // still just a click until this crosses
            dragging.current = true;
            // A pending zoom (wheel, not yet committed) is part of the current view too -- adopt it as
            // the drag's baseline instead of dropping back to whatever the store last had.
            setLive((v) => v ?? ui.planView);
            e.currentTarget.setPointerCapture(e.pointerId);   // now it's really a drag -- own the gesture
          }
          // Grab the plan and pull it: the point under the cursor must not slide, so the window moves the
          // opposite way. `at()` re-reads the live transform, so this stays right at every zoom level.
          // The anchor stays the plan point that was grabbed. at() re-reads the LIVE transform, so once the
          // pan is right this delta is zero and nothing more moves -- do NOT re-anchor, that chases its own tail.
          const p = at(e), a = drag.current;
          setLive((v) => { const b = v ?? ui.planView; return { ...b, cx: b.cx - (p.x - a.x), cy: b.cy - (p.y - a.y) }; });
          cancelWheelCommit();   // an active drag owns `live`; a stale wheel commit must not stomp it later
        }}
        onPointerUp={(e) => {
          if (dragging.current) {
            if (live) ui.setPlanView(clampView(box, live));   // one persisted write per drag, not one per frame
            e.currentTarget.releasePointerCapture(e.pointerId);
          }
          drag.current = null;
          dragging.current = false;
          setLive(null);
        }}
        onPointerCancel={() => {
          // An interrupted gesture (capture stolen, touch cancelled, ...): discard whatever pan was in
          // flight rather than committing or leaving it half-applied on screen.
          drag.current = null;
          dragging.current = false;
          setLive(null);
        }}>
        {s.coverage?.cells.map((c, i) => (
          <rect key={i} x={c.x - s.coverage!.grid_m / 2} y={-c.y - s.coverage!.grid_m / 2}
            width={s.coverage!.grid_m} height={s.coverage!.grid_m}
            fill={cellColor(c)} fillOpacity={cellOpacity(c)} stroke="none" pointerEvents="none" />
        ))}
        <path d={ringPath(deck.outline)} fill="none" stroke="#7a8" strokeWidth={0.4} />
        {feats.filter((f) => f.layer === "A2").map((f) => <path key={f.id} d={linePath(f.geometry.coordinates as number[][])} fill="none" stroke="#1e88e5" strokeWidth={0.4} strokeDasharray="2 1" onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "B2").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="rgba(30,136,229,.15)" stroke="#1e88e5" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "C" && f.geometry.type === "Polygon").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="#999" stroke="#666" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "LM").map((f) => {
          const c = f.geometry.coordinates as number[];
          // 없는 법선을 API 와 같은 쪽으로 본다 — CoverageController.java:138 이
          // `n == null ? Math.PI : atan2(...)` 로 선미를 향한다고 친다. [1,0,0] 이면 화면이 커버리지와 180° 어긋난다.
          const n = (f.props.normal as number[] | undefined) ?? [-1, 0, 0];
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
