import { useState } from "react";
import { wrapDeg } from "../geo/shipFrame";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

export function DrivePanel({ send }: { send: Send }) {
  const { localization: l, setMode } = useEditorStore();
  const [sig, setSig] = useState({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2 });
  const noise = (patch: Partial<typeof sig>) => { const n = { ...sig, ...patch }; setSig(n); send("SetNoise", { ...n, sigma_gps: 0.5 }); };
  const err = l ? Math.hypot(l.est_x - l.true_x, l.est_y - l.true_y) : null;
  return (
    <div className="panel">
      <h4>주행 시뮬레이션</h4>
      <div className="row">
        <button className="btn primary" onClick={() => send("StartScenario", { mode: "load" })}>▶ 선적 시나리오</button>
        <button className="btn" onClick={() => { send("SetMode", "edit"); setMode("edit"); }}>정지</button>
      </div>
      <div className="row"><label>σ 거리 (m)</label><input type="range" min={0} max={1} step={0.05} value={sig.sigma_r} onChange={(e) => noise({ sigma_r: Number(e.target.value) })} /><span>{sig.sigma_r.toFixed(2)}</span></div>
      <div className="row"><label>σ 방위 (°)</label><input type="range" min={0} max={5} step={0.5} value={sig.sigma_theta} onChange={(e) => noise({ sigma_theta: Number(e.target.value) })} /><span>{sig.sigma_theta}</span></div>
      <div className="row"><label>σ 방향각 (°)</label><input type="range" min={0} max={10} step={0.5} value={sig.sigma_alpha} onChange={(e) => noise({ sigma_alpha: Number(e.target.value) })} /><span>{sig.sigma_alpha}</span></div>
      <h4 style={{ marginTop: 8 }}>위치 추정</h4>
      {!l ? <span style={{ color: "#888" }}>추정 없음</span> : (
        <div style={{ fontFamily: "monospace", fontSize: 12 }}>
          <div>frame {l.frame} · N {l.n_obs} · RMS {l.residual_rms.toFixed(3)}</div>
          <div>est  x {l.est_x.toFixed(2)} y {l.est_y.toFixed(2)} ψ {l.est_psi.toFixed(1)}</div>
          <div>true x {l.true_x.toFixed(2)} y {l.true_y.toFixed(2)} ψ {l.true_psi.toFixed(1)}</div>
          <div>err {err!.toFixed(2)} m · {wrapDeg(l.est_psi - l.true_psi).toFixed(1)}°</div>
        </div>
      )}
    </div>
  );
}
