import { api } from "../api/client";
import { noiseMsg, predictionMsg, useEditorStore, type Run } from "../store/editor";
import { useUiStore } from "../store/ui";
import type { BridgeName } from "../bridge/useShipUnity";
import { dot } from "../theme/theme";

type Send = (name: BridgeName, payload?: string | object) => void;
const SCALES = [1, 5, 20];
const RUN_BADGE: Record<Run, { label: string; color: string }> = {
  idle: { label: "대기", color: "#7d8591" },
  running: { label: "주행 중", color: "#2fb344" },
  paused: { label: "일시정지", color: "#f0a500" },
  done: { label: "완료", color: "#4c8dff" },
};

/** Pause/resume, shared by the button and the Space key: the store flips, then Unity is told the matching scale. */
export function togglePause(send: Send) {
  const ed = useEditorStore.getState();
  if (ed.run !== "running" && ed.run !== "paused") return;
  send("SetTimeScale", { scale: ed.run === "running" ? 0 : ed.timeScale });
  ed.togglePause();
}

/** The run controls, over the 3D view so they survive immersive mode (every panel hidden). */
export function RunBar({ send }: { send: Send }) {
  const { mode, run, runDetail, decks, datasetId, occluded, beliefParams: bp, timeScale: scale, setTimeScale, clearLog, setError } = useEditorStore();
  const { immersive, toggleImmersive } = useUiStore();
  const start = async (m: "load" | "unload") => {
    clearLog();
    // M8: a run can go to any deck, and each car is judged against its own deck's promise -- so every deck's, tagged.
    const empty = { grid_m: 1, bbox: [0, 0, 0, 0], cells: [] };
    await Promise.all(decks.map(async (d) => {
      try {
        const cov = await api.coverage(datasetId, d.id, predictionMsg(useEditorStore.getState(), m));
        // trim to what the vehicle needs: 2880 cells of {x, y, s} instead of the full response
        send("SetPrediction", { deck_id: d.id, grid_m: cov.grid_m, bbox: cov.bbox, cells: cov.cells.map((c) => ({ x: c.x, y: c.y, s: c.sigma_xy ?? null })) });
      } catch (e) {
        // no prediction is still a valid drive: BeliefMonitor skips the Degraded check when predictedSigmaXy
        // is null and Lost/backtracking/stopped still work off observation count alone — start anyway. But
        // Unity must actually drop whatever prediction it was holding for that deck, or a stale one (another
        // occlusion set's) keeps judging Degraded against a promise this run never made.
        send("SetPrediction", { deck_id: d.id, ...empty });
        setError("coverage prediction unavailable: " + (e as Error).message);
      }
    }));
    if (decks.length === 0) send("SetPrediction", empty);
    send("SetOccluded", { ids: occluded });
    send("SetBeliefParams", bp);
    send("SetNoise", noiseMsg(useEditorStore.getState())); // reload can restore a saved value while Unity still holds SetNoiseMsg's default -- resend it every start
    send("SetTimeScale", { scale });   // also what un-pauses a paused run that is started over
    send("StartScenario", { mode: m });
  };
  if (mode !== "drive" && !immersive) return null;
  const live = run === "running" || run === "paused";
  const badge = RUN_BADGE[run];
  return (
    <div className="runbar">
      {mode === "drive" && (
        <>
          <span className="badge" style={dot(badge.color)}>{badge.label}{run === "done" && runDetail ? ` (${runDetail})` : ""}</span>
          <button className="btn primary" onClick={() => start("load")}>▶ 선적</button>
          <button className="btn" onClick={() => start("unload")}>◀ 하역</button>
          <button className="btn" disabled={!live} title="일시정지 / 재개 (Space)" onClick={() => togglePause(send)}>
            {run === "paused" ? "▶ 재개" : "⏸ 일시정지"}</button>
          <button className="btn" disabled={!live} title="이 주행을 끝내고 화면은 그대로 둔다" onClick={() => send("StopScenario", {})}>■ 정지</button>
          {/* while paused only the store changes; resume sends the new value */}
          <select value={scale} onChange={(e) => { const v = Number(e.target.value); setTimeScale(v); if (run !== "paused") send("SetTimeScale", { scale: v }); }}>
            {SCALES.map((k) => <option key={k} value={k}>×{k}</option>)}
          </select>
        </>
      )}
      {immersive && <button className="btn" title="패널 다시 보기 (I / Esc)" onClick={toggleImmersive}>⤢ 나가기</button>}
    </div>
  );
}
