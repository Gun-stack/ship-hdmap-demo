import { useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

export function PropertyForm({ send }: { send: Send }) {
  const s = useEditorStore();
  const id = s.selectedId;
  const draft = id ? s.drafts[id] : undefined;
  const feat = id ? s.features[id] : undefined;
  const [deck, setDeck] = useState(""); const [kind, setKind] = useState(""); const [propsText, setPropsText] = useState("{}"); const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    setErr(null);
    if (draft) { setDeck(draft.deck_id); setKind("apriltag"); const y = (draft.geometry.coordinates as number[])[1];
      setPropsText(JSON.stringify({ family: "apriltag-36h11", code: nextCode(Object.values(s.features)), normal: (draft.props.normal as number[]) ?? (y < 0 ? [0, 1, 0] : [0, -1, 0]), size_m: 0.3, mounted_on: (draft.props.mounted_on as string) ?? "" }, null, 1)); }
    else if (feat) { setDeck(feat.deck_id ?? ""); setKind(feat.kind); setPropsText(JSON.stringify(feat.props, null, 1)); }
  }, [id, draft, feat]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!id) return <div className="panel"><h4>속성</h4><span style={{ color: "#888" }}>객체를 선택하거나 3D 에서 기둥 면을 클릭해 마커를 놓으세요</span></div>;

  const parseProps = () => { try { return JSON.parse(propsText) as Record<string, unknown>; } catch { throw new Error("props 는 JSON 이어야 합니다"); } };
  const run = async (fn: () => Promise<void>) => { try { setErr(null); await fn(); } catch (e) { setErr(e instanceof ApiError ? `${e.message}${e.field ? ` (${e.field})` : ""}` : (e as Error).message); } };

  return (
    <div className="panel">
      <h4>속성 — {id}{draft ? " (초안)" : ""}</h4>
      {err && <div className="err">{err}</div>}
      <div className="row"><label>갑판</label><select value={deck} onChange={(e) => setDeck(e.target.value)}><option value="">(없음)</option>{s.decks.map((d) => <option key={d.id} value={d.id}>{d.id}</option>)}</select></div>
      <div className="row"><label>kind</label><input value={kind} onChange={(e) => setKind(e.target.value)} /></div>
      <div className="row"><label>위치</label><span>{JSON.stringify((draft ?? feat)!.geometry.coordinates)}</span></div>
      <div className="row"><label>props</label><textarea rows={7} style={{ flex: 1, fontFamily: "monospace" }} value={propsText} onChange={(e) => setPropsText(e.target.value)} /></div>
      <div className="row">
        {draft ? (<>
          <button className="btn primary" onClick={() => run(async () => { const m = await s.applyDraft(id, { kind, deck_id: deck || undefined, props: parseProps() }); send("Confirm", m); })}>적용(저장)</button>
          <button className="btn" onClick={() => { s.discardDraft(id); send("Delete", id); }}>취소</button>
        </>) : (<>
          <button className="btn primary" onClick={() => run(() => s.updateFeature(id, { kind, deck_id: deck || undefined, props: parseProps() }))}>적용</button>
          <button className="btn" onClick={() => run(async () => { await s.removeFeature(id); send("Delete", id); })}>삭제</button>
        </>)}
      </div>
    </div>
  );
}

function nextCode(features: { layer: string; props: Record<string, unknown> }[]): number {
  const used = new Set(features.filter((f) => f.layer === "LM").map((f) => Number(f.props.code)));
  for (let c = 1; c < 20; c++) if (!used.has(c)) return c;
  return 0;
}
