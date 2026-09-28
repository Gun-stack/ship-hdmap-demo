import type { BeliefParamsIn } from "../api/types";
import { beliefBadge, fmtRatioOf } from "../geo/belief";
import { wrapDeg } from "../geo/shipFrame";
import { noiseMsg, useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";
import { dot } from "../theme/theme";

type Send = (name: BridgeName, payload?: string | object) => void;
const SIGMAS = [
  { key: "sigma_gps", label: "σ GPS (m)", max: 3, step: 0.1, digits: 1 },   // quay leg only: decides how squarely the vehicle enters the ramp
] as const;
const BELIEF_SLIDERS: { key: keyof BeliefParamsIn; label: string; min: number; max: number; step: number; digits: number }[] = [
  { key: "k", label: "허용 배수", min: 1, max: 6, step: 0.5, digits: 1 },
  { key: "frames", label: "연속 프레임", min: 1, max: 20, step: 1, digits: 0 },
  { key: "drift_rate", label: "오도메트리 drift (/m)", min: 0, max: 0.5, step: 0.05, digits: 2 },
  { key: "budget_m", label: "예산 σ (m)", min: 0.2, max: 3, step: 0.1, digits: 1 },
  { key: "max_lost_m", label: "최대 상실 (m)", min: 1, max: 20, step: 1, digits: 0 },
  { key: "trail_m", label: "자취 (m)", min: 5, max: 50, step: 5, digits: 0 },
];

export function DrivePanel({ send }: { send: Send }) {
  const { localization: l, scenarioLog, belief: b, beliefParams: bp, setBeliefParams, sigmaGps, setSigmaGps, coverageParams } = useEditorStore();
  const commit = () => send("SetNoise", noiseMsg(useEditorStore.getState())); // on release only — Unity's SetNoise is cheap but the bridge is not a slider event bus
  const commitBelief = () => send("SetBeliefParams", bp);
  const err = l ? Math.hypot(l.est_x - l.true_x, l.est_y - l.true_y) : null;
  return (
    <div className="panel">
      <h4>주행 시뮬레이션</h4>
      <span className="muted">선적·일시정지·정지는 3D 뷰 위 막대에서</span>
      <div className="row">
        <label>σ</label>
        <span>거리 {coverageParams.sigma_r.toFixed(2)} m · 방위 {coverageParams.sigma_theta.toFixed(1)}° · 방향각 {coverageParams.sigma_alpha.toFixed(1)}°
          {" "}<span className="muted">(커버리지 탭에서 조정)</span></span>
      </div>
      {SIGMAS.map((s) => (
        <div className="row" key={s.key}><label>{s.label}</label>
          <input type="range" min={0} max={s.max} step={s.step} value={sigmaGps} onChange={(e) => setSigmaGps(Number(e.target.value))}
            onMouseUp={commit} onKeyUp={commit} onTouchEnd={commit} />
          <span className="num">{sigmaGps.toFixed(s.digits)}</span></div>
      ))}
      <h4 className="sub">위치 추정</h4>
      {!l ? <span className="muted">추정 없음</span> : (
        <div className="readout">
          <div>frame {l.frame} · N {l.n_obs} · RMS {l.residual_rms.toFixed(3)}</div>
          <div>est  x {l.est_x.toFixed(2)} y {l.est_y.toFixed(2)} ψ {l.est_psi.toFixed(1)}</div>
          <div>true x {l.true_x.toFixed(2)} y {l.true_y.toFixed(2)} ψ {l.true_psi.toFixed(1)}</div>
          <div>err {err!.toFixed(2)} m · {wrapDeg(l.est_psi - l.true_psi).toFixed(1)}°</div>
        </div>
      )}
      <h4 className="sub">믿음 상태</h4>
      {b && (
        <>
          <div className="row"><label>상태</label>
            <span className="badge" style={dot(beliefBadge(b.state).color)}>{beliefBadge(b.state).label}</span>
            <span>마커 {b.n_obs} 개</span></div>
          <div className="row"><label>σxy</label>
            <span>실측 {b.sigma_xy?.toFixed(2) ?? "—"} m · 예측 {b.predicted_sigma_xy?.toFixed(2) ?? "—"} m · {fmtRatioOf(b.sigma_xy, b.predicted_sigma_xy)}</span></div>
          <div className="row"><label>상실</label>
            <span>{b.lost_m.toFixed(1)} m · 오도 σ {b.sigma_odo.toFixed(2)} m · 자취 {b.trail_m.toFixed(1)} m</span></div>
        </>
      )}
      {BELIEF_SLIDERS.map((s) => (
        <div className="row" key={s.key}><label>{s.label}</label>
          <input type="range" min={s.min} max={s.max} step={s.step} value={bp[s.key]}
            onChange={(e) => setBeliefParams({ [s.key]: Number(e.target.value) })}
            onMouseUp={commitBelief} onKeyUp={commitBelief} onTouchEnd={commitBelief} />
          <span className="num">{bp[s.key].toFixed(s.digits)}</span></div>
      ))}
      <h4 className="sub">시나리오 로그</h4>
      <div className="log">
        {scenarioLog.length === 0 ? <span className="muted">없음</span> : scenarioLog.map((line, i) => <div key={i}>{line.t} {line.text}</div>)}
      </div>
    </div>
  );
}
