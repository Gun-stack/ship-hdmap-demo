import { useEffect } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type CamMode } from "../store/ui";
import { commandFor, isInField, HELP_ROWS } from "../ui/keys";
import { bbox, pickDeck } from "../geo/deck";
import { boxOfPoint, fitTo } from "../geo/plan";
import { nextTab } from "./RightTabs";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;
const CAM_CYCLE: CamMode[] = ["orbit", "fly", "driver"];

export function Shortcuts({ send }: { send: Send }) {
  const helpOpen = useUiStore((s) => s.helpOpen);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const ui = useUiStore.getState();
      const ed = useEditorStore.getState();
      const active = document.activeElement;
      // e is a real KeyboardEvent, so it already carries isComposing -- KeyLike needs nothing extra here.
      const cmd = commandFor(e, { inField: isInField(active), flyingFocused: ui.cam === "fly" && active?.tagName === "CANVAS" });
      if (!cmd) return;
      // Escape keeps its native behaviour -- it is what closes an open <select> dropdown, and the panels have several.
      if (cmd.kind !== "escape") e.preventDefault();

      switch (cmd.kind) {
        // 드라이브 모드에는 도구 UI 자체가 없다 (Toolbar 는 mode === "edit" 에서만 그린다) -- 조용히 무시.
        case "tool": if (ed.mode !== "drive") { ui.setTool(cmd.tool); send("SetTool", { tool: cmd.tool }); } break;
        case "cam": {
          // 편집 모드에는 차가 없으므로 차량 시선은 건너뛴다 (스펙 §5.1)
          const pool = ed.mode === "drive" ? CAM_CYCLE : CAM_CYCLE.filter((c) => c !== "driver");
          const next = pool[(pool.indexOf(ui.cam) + 1) % pool.length] ?? "orbit";
          ui.setCam(next); send("SetCamMode", { mode: next });
          break;
        }
        case "deck": { const d = ed.decks[cmd.index]; if (d) ed.setDeckFilter(d.id); break; }
        case "tab": ui.setTab(nextTab(ui.tab, cmd.dir)); break;
        case "fit": {
          // 3D 는 Unity 가 이미 Select 에서 카메라를 옮긴다 — 같은 메시지를 다시 보내면 그게 곧 F 다.
          if (ed.selectedId) send("Select", ed.selectedId);
          const deck = pickDeck(ed.decks, ed.deckFilter);
          // drafts too: the moment you most want F is right after placing a marker, and a draft is not in features yet
          const f = ed.selectedId ? (ed.features[ed.selectedId] ?? ed.drafts[ed.selectedId]) : undefined;
          if (deck && f && f.geometry.type === "Point") {
            const c = f.geometry.coordinates as number[];
            ui.setPlanView(fitTo(bbox(deck.outline, 3), boxOfPoint(c[0], -c[1])));
          }
          break;
        }
        case "all": { const deck = pickDeck(ed.decks, ed.deckFilter); if (deck) { const b = bbox(deck.outline, 3); ui.setPlanView(fitTo(b, b)); } break; }
        case "escape":
          if (ui.helpOpen) { ui.toggleHelp(); break; }
          ed.select(null);
          ui.setTool("select"); send("SetTool", { tool: "select" });
          break;
        case "help": ui.toggleHelp(); break;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [send]);

  if (!helpOpen) return null;
  return (
    <div className="helpcard" onClick={() => useUiStore.getState().toggleHelp()}>
      <table>
        <tbody>{HELP_ROWS.map(([k, what]) => <tr key={k}><th>{k}</th><td>{what}</td></tr>)}</tbody>
      </table>
      <div style={{ color: "#888", marginTop: 6 }}>아무 곳이나 눌러 닫기 · Esc</div>
    </div>
  );
}
