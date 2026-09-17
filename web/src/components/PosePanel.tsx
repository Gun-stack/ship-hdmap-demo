import { useEffect, useRef, useState } from "react";
import { ApiError } from "../api/client";
import { useEditorStore } from "../store/editor";
import type { Pose } from "../api/types";

const FIELDS: { key: keyof Pose; label: string; min: number; max: number; step: number; unit: string }[] = [
  { key: "draft_fwd_m", label: "흘수 선수", min: 6, max: 10, step: 0.1, unit: "m" },
  { key: "draft_aft_m", label: "흘수 선미", min: 6, max: 10, step: 0.1, unit: "m" },
  { key: "heel_deg", label: "횡경사", min: -3, max: 3, step: 0.1, unit: "°" },
  { key: "tide_m", label: "조위", min: -1, max: 3, step: 0.1, unit: "m" },
  { key: "quay_z_m", label: "부두 높이", min: 0, max: 8, step: 0.1, unit: "m" },
];

export function PosePanel() {
  const { pose, ramp, savePose } = useEditorStore();
  const [local, setLocal] = useState<Pose>({});
  const [err, setErr] = useState<string | null>(null);
  const dragging = useRef(false);
  useEffect(() => { if (pose && !dragging.current) setLocal(pose); }, [pose]);
  if (!pose) return null;
  const commit = (key: keyof Pose) => {
    dragging.current = false;
    const v = local[key];
    if (typeof v === "number" && v !== pose[key]) {
      setErr(null);
      savePose({ [key]: v }).catch((e) => setErr(e instanceof ApiError ? e.message : (e as Error).message));
    }
  };
  return (
    <div className="panel">
      <h4>선박 자세 (pose)</h4>
      {err && <div className="err">{err}</div>}
      {FIELDS.map((f) => (
        <div className="row" key={f.key}>
          <label>{f.label}</label>
          <input type="range" min={f.min} max={f.max} step={f.step} value={(local[f.key] as number) ?? f.min}
            onChange={(e) => setLocal({ ...local, [f.key]: Number(e.target.value) })} onMouseUp={() => commit(f.key)} onKeyUp={() => commit(f.key)} onTouchEnd={() => commit(f.key)}
            onMouseDown={() => { dragging.current = true; }} onTouchStart={() => { dragging.current = true; }} />
          <span style={{ width: 52, textAlign: "right" }}>{(local[f.key] as number)?.toFixed(1)} {f.unit}</span>
        </div>
      ))}
      <div className="row"><label>트림</label><span>{pose.trim_deg?.toFixed(2)}°</span></div>
      <div className="row"><label>램프</label>
        {ramp ? <span>{ramp.angle_deg.toFixed(2)}° <b style={{ color: ramp.state === "deployed" ? "#2a2" : "#c62828" }}>{ramp.state}</b></span> : <span>-</span>}
      </div>
    </div>
  );
}
