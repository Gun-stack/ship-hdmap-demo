import { useState } from "react";
import { ApiError } from "../api/client";
import { slotKpi } from "../geo/kpi";
import { useEditorStore } from "../store/editor";

export function LoadPanel({ reloadScene }: { reloadScene: () => Promise<void> }) {
  const { decks, deckFilter, features, slotGen, generateSlots } = useEditorStore();
  const [deck, setDeck] = useState(deckFilter !== "all" ? deckFilter : "D3");
  const [gapLat, setGapLat] = useState(0.3); const [gapLon, setGapLon] = useState(0.4);
  const [busy, setBusy] = useState(false); const [err, setErr] = useState<string | null>(null);
  const d = decks.find((x) => x.id === deck);
  const kpi = d ? slotKpi(Object.values(features), d) : { count: 0, utilization: 0 };
  const last = slotGen[deck];
  const run = async () => {
    setBusy(true); setErr(null);
    try { await generateSlots(deck, { vehicle_class: "passenger", gap_lat_m: gapLat, gap_lon_m: gapLon }); await reloadScene(); }
    catch (e) { setErr(e instanceof ApiError ? `${e.message}${e.field ? ` (${e.field})` : ""}` : (e as Error).message); }
    finally { setBusy(false); }
  };
  return (
    <div className="panel">
      <h4>적재 계획</h4>
      {err && <div className="err">{err}</div>}
      <div className="row"><label>갑판</label><select value={deck} onChange={(e) => setDeck(e.target.value)}>{decks.map((x) => <option key={x.id} value={x.id}>{x.id}</option>)}</select></div>
      <div className="row"><label>차량</label><span>passenger 4.8 × 1.85 m</span></div>
      <div className="row"><label>측면 간격</label><input type="number" min={0} step={0.05} value={gapLat} onChange={(e) => setGapLat(Number(e.target.value))} /><span>m</span></div>
      <div className="row"><label>전후 간격</label><input type="number" min={0} step={0.05} value={gapLon} onChange={(e) => setGapLon(Number(e.target.value))} /><span>m</span></div>
      <div className="row"><button className="btn primary" disabled={busy || !d} onClick={() => void run()}>{busy ? "생성 중…" : kpi.count ? "재생성" : "생성"}</button></div>
      <div className="row"><label>구획 수</label><b>{kpi.count}</b></div>
      <div className="row"><label>면적 활용률</label><b>{(kpi.utilization * 100).toFixed(1)} %</b></div>
      <div className="row"><label>래싱 4점 매핑</label><b>{last ? `${(last.lashing_coverage * 100).toFixed(0)} %` : "-"}</b></div>
    </div>
  );
}
