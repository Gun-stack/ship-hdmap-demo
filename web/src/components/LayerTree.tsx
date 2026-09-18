import { useEditorStore, visibleFeatures } from "../store/editor";
import { useUiStore } from "../store/ui";
import type { Layer } from "../api/types";

const ORDER: Layer[] = ["A2", "A1", "B2", "LP", "C", "LM", "MEP"];
const LABEL: Record<Layer, string> = { A2: "A2 차로중심선", A1: "A1 차선", B2: "B2 노면표시·구획", LP: "LP 래싱 포인트", C: "C 시설물", LM: "LM 랜드마크", MEP: "MEP" };

export function LayerTree() {
  const s = useEditorStore();
  const feats = visibleFeatures(s);
  const open = useUiStore((s) => s.treeOpen);
  const toggleTree = useUiStore((s) => s.toggleTree);
  return (
    <div className="panel tree">
      <h4>레이어 / 객체</h4>
      {ORDER.map((layer) => {
        const items = feats.filter((f) => f.layer === layer).sort((a, b) => a.id.localeCompare(b.id));
        const drafts = layer === "LM" ? Object.values(s.drafts) : [];
        if (items.length === 0 && drafts.length === 0) return null;
        return (
          <ul key={layer}>
            <li onClick={() => toggleTree(layer)}>{open[layer] ? "▾" : "▸"} {LABEL[layer]} ({items.length}{drafts.length ? ` +${drafts.length} 초안` : ""})</li>
            {open[layer] && (
              <ul>
                {drafts.map((d) => <li key={d.tempId} className={s.selectedId === d.tempId ? "sel" : ""} onClick={() => s.select(d.tempId)}>{d.tempId} (초안)</li>)}
                {items.map((f) => (
                  <li key={f.id} className={s.selectedId === f.id ? "sel" : ""} onClick={() => s.select(f.id)} style={layer === "LM" && s.occluded.includes(f.id) ? { opacity: 0.45 } : undefined}>
                    {layer === "LM" && (
                      <input type="checkbox" checked={s.occluded.includes(f.id)} onClick={(e) => e.stopPropagation()} onChange={() => s.toggleOccluded(f.id)} title="가림" />
                    )}
                    {" "}{f.id} <small style={{ color: "#888" }}>{f.kind}{f.deck_id ? ` · ${f.deck_id}` : ""}</small>
                  </li>
                ))}
              </ul>
            )}
          </ul>
        );
      })}
    </div>
  );
}
