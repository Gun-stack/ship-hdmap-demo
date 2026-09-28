import type { BridgeName } from "../bridge/useShipUnity";
import { useEditorStore } from "../store/editor";
import { PLAN_ONLY } from "../theme/palette";
import { dot } from "../theme/theme";

type Send = (name: BridgeName, payload?: string | object) => void;

/**
 * M8: the hoistable internal ramps. The scene owns their state (a run raises and lowers them as decks fill and
 * empty) and reports every change; this panel mirrors it, and while no run is on it can ask the scene for a
 * change -- the scene answers with the state it actually took (never down onto a parked car).
 */
export function RampPanel({ send }: { send: Send }) {
  const { innerRamps, rampStates, mode } = useEditorStore();
  if (innerRamps.length === 0) return null;
  const locked = mode === "drive";
  return (
    <div className="panel">
      <h4>내부 램프</h4>
      {innerRamps.map((r) => {
        const state = rampStates[r.id] ?? "deployed";
        const down = state === "deployed";
        return (
          <div className="row" key={r.id}>
            <label>{r.upper_deck} ↔ {r.lower_deck}</label>
            <span className="badge" style={dot(down ? PLAN_ONLY.RampDeployed : PLAN_ONLY.RampStowed)}>{down ? "전개" : "수납"}</span>
            <span className="spacer" style={{ flex: 1 }} />
            <button className="btn" disabled={locked} title={locked ? "주행 중에는 시나리오가 램프를 움직인다" : undefined}
              onClick={() => send("SetRampState", { id: r.id, state: down ? "stowed" : "deployed" })}>{down ? "수납" : "전개"}</button>
          </div>
        );
      })}
      <div className="muted" style={{ fontSize: 12, marginTop: 4 }}>수납하면 너머 갑판에 갈 수 없고, 가까운 쪽 갑판의 램프 자리가 바닥이 된다.</div>
    </div>
  );
}
