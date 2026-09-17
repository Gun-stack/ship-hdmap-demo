import { useState } from "react";
import { wrapDeg } from "../geo/shipFrame";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;
type Sig = { sigma_r: number; sigma_theta: number; sigma_alpha: number; sigma_gps: number };
const SLIDERS: { key: keyof Sig; label: string; max: number; step: number; digits: number }[] = [
  { key: "sigma_r", label: "σ 거리 (m)", max: 1, step: 0.05, digits: 2 },
  { key: "sigma_theta", label: "σ 방위 (°)", max: 5, step: 0.5, digits: 1 },
  { key: "sigma_alpha", label: "σ 방향각 (°)", max: 10, step: 0.5, digits: 1 },
  { key: "sigma_gps", label: "σ GPS (m)", max: 3, step: 0.1, digits: 1 },   // quay leg only: decides how squarely the vehicle enters the ramp
];
const SCALES = [1, 5, 20];

export function DrivePanel({ send }: { send: Send }) {
  const { localization: l, setMode, scenarioLog, clearLog } = useEditorStore();
  const [sig, setSig] = useState<Sig>({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2, sigma_gps: 0.5 });
  const [scale, setScale] = useState(1);
  const commit = () => send("SetNoise", sig); // on release only — Unity's SetNoise is cheap but the bridge is not a slider event bus
  const start = (mode: "load" | "unload") => { clearLog(); send("SetTimeScale", { scale }); send("StartScenario", { mode }); };
  const err = l ? Math.hypot(l.est_x - l.true_x, l.est_y - l.true_y) : null;
  return (
    <div className="panel">
      <h4>주행 시뮬레이션</h4>
      <div className="row">
        <button className="btn primary" onClick={() => start("load")}>▶ 선적</button>
        <button className="btn" onClick={() => start("unload")}>◀ 하역</button>
        <button className="btn" onClick={() => setMode("edit")}>정지</button>
        <select value={scale} onChange={(e) => { const v = Number(e.target.value); setScale(v); send("SetTimeScale", { scale: v }); }} style={{ flex: "0 0 auto" }}>
          {SCALES.map((k) => <option key={k} value={k}>×{k}</option>)}
        </select>
      </div>
      {SLIDERS.map((s) => (
        <div className="row" key={s.key}><label>{s.label}</label>
          <input type="range" min={0} max={s.max} step={s.step} value={sig[s.key]} onChange={(e) => setSig({ ...sig, [s.key]: Number(e.target.value) })}
            onMouseUp={commit} onKeyUp={commit} onTouchEnd={commit} />
          <span>{sig[s.key].toFixed(s.digits)}</span></div>
      ))}
      <h4 style={{ marginTop: 8 }}>위치 추정</h4>
      {!l ? <span style={{ color: "#888" }}>추정 없음</span> : (
        <div style={{ fontFamily: "monospace", fontSize: 12 }}>
          <div>frame {l.frame} · N {l.n_obs} · RMS {l.residual_rms.toFixed(3)}</div>
          <div>est  x {l.est_x.toFixed(2)} y {l.est_y.toFixed(2)} ψ {l.est_psi.toFixed(1)}</div>
          <div>true x {l.true_x.toFixed(2)} y {l.true_y.toFixed(2)} ψ {l.true_psi.toFixed(1)}</div>
          <div>err {err!.toFixed(2)} m · {wrapDeg(l.est_psi - l.true_psi).toFixed(1)}°</div>
        </div>
      )}
      <h4 style={{ marginTop: 8 }}>시나리오 로그</h4>
      <div style={{ fontFamily: "monospace", fontSize: 11, maxHeight: 220, overflowY: "auto", whiteSpace: "pre" }}>
        {scenarioLog.length === 0 ? <span style={{ color: "#888" }}>없음</span> : scenarioLog.map((line, i) => <div key={i}>{line.t} {line.text}</div>)}
      </div>
    </div>
  );
}
