import { useEffect, useState } from "react";
import { useEditorStore } from "../store/editor";
import { fmtRatio } from "../geo/coverage";
import { pickDeck } from "./MiniMap";

export function CoveragePanel() {
  const s = useEditorStore();
  const [budget, setBudget] = useState(3);
  const [msg, setMsg] = useState<string | null>(null);
  // the same rule MiniMap frames on, so the heatmap can never belong to a different deck than the drawing
  const deck = pickDeck(s.decks, s.deckFilter)?.id;
  useEffect(() => { if (deck) void s.runCoverage(deck); }, [deck]); // eslint-disable-line react-hooks/exhaustive-deps

  const p = s.coverageParams;
  const num = (k: keyof typeof p, label: string, step: number, min: number, max: number) => (
    <div className="row" key={k}>
      <label>{label}</label>
      <input type="range" step={step} min={min} max={max} value={p[k]}
        onChange={(e) => s.setCoverageParams({ [k]: Number(e.target.value) })}
        onMouseUp={() => deck && void s.runCoverage(deck)} onTouchEnd={() => deck && void s.runCoverage(deck)} />
      <span style={{ width: 44, textAlign: "right" }}>{p[k]}</span>
    </div>
  );

  if (!deck) return null;
  return (
    <div className="panel">
      <h4>커버리지{s.coverageBusy ? " …" : ""}</h4>
      <div className="row">
        <button className={`btn${s.coverageMode === "load" ? " primary" : ""}`} onClick={() => s.setCoverageMode("load", deck)}>선적</button>
        <button className={`btn${s.coverageMode === "unload" ? " primary" : ""}`} onClick={() => s.setCoverageMode("unload", deck)}>하역</button>
      </div>
      {s.coverage && (
        <>
          <div className="row"><label>사각지대</label><span><b>{fmtRatio(s.coverage.blind_ratio)}</b> · 반대 모드 {fmtRatio(s.coverage.other_mode.blind_ratio)}</span></div>
          <div className="row"><label>허용오차 미달</label><span>{fmtRatio(s.coverage.weak_ratio)}</span></div>
          {/* the denominator is not the deck: say so, or the number reads as twice the problem it is */}
          <div className="row"><label>대상</label><span>구획·차로 {s.coverage.n_cells.toLocaleString()} 셀 · 갑판 전체 {s.coverage.n_drawn.toLocaleString()} 셀 · {s.coverage.grid_m} m 격자</span></div>
          <div className="row"><label>최악 지점</label><span>{s.coverage.worst ? `x ${s.coverage.worst.x.toFixed(1)} y ${s.coverage.worst.y.toFixed(1)} · σxy ${s.coverage.worst.sigma_xy?.toFixed(2)} m` : "—"}</span></div>
        </>
      )}
      {num("grid_m", "격자 m", 0.5, 0.5, 4)}
      {num("max_dist_m", "인식거리 m", 1, 5, 40)}
      {num("fov_deg", "시야각 °", 5, 30, 180)}
      {num("max_view_angle_deg", "시야한계 °", 5, 20, 89)}
      {num("sigma_r", "σr m", 0.05, 0, 1)}
      {num("sigma_theta", "σθ °", 0.5, 0, 10)}
      {num("sigma_alpha", "σα °", 0.5, 0, 10)}

      <div className="row">
        <label>추천</label>
        <input type="number" min={1} max={10} value={budget} onChange={(e) => setBudget(Number(e.target.value))} style={{ width: 48 }} />
        <button className="btn" onClick={() => void s.runSuggest(deck, budget)} disabled={s.coverageBusy}>실행</button>
      </div>
      {s.suggestions.map((g) => (
        <div className="row" key={g.rank}>
          <span>{g.rank}. x {g.x.toFixed(1)} y {g.y.toFixed(1)} φ {g.phi_deg.toFixed(0)}° · 사각 −{fmtRatio(g.gain)}</span>
          <button className="btn" onClick={() => s.addCandidate({ x: g.x, y: g.y, phi_deg: g.phi_deg, mounted_on: g.mounted_on }, deck)}>담기</button>
        </div>
      ))}

      <div className="row">
        <label>후보 {s.candidates.length} 개</label>
        <button className="btn" onClick={() => s.clearCandidates(deck)} disabled={!s.candidates.length}>비우기</button>
        <button className="btn primary" disabled={!s.candidates.length}
          onClick={() => void s.commitCandidates(deck).then((n) => setMsg(`${n} 개 저장됨`))}>확정 저장</button>
      </div>
      {s.candidates.map((c, i) => (
        <div className="row" key={`c${i}`}>
          <span>x {c.x.toFixed(1)} y {c.y.toFixed(1)} φ {c.phi_deg.toFixed(0)}°</span>
          <button className="btn" onClick={() => s.removeCandidate(i, deck)}>삭제</button>
        </div>
      ))}
      {msg && <div className="row"><span>{msg}</span></div>}
    </div>
  );
}
