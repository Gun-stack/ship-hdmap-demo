import { useEffect, useRef } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type CamMode } from "../store/ui";
import { commandFor, isInField, HELP_ROWS } from "../ui/keys";
import { bbox, pickDeck } from "../geo/deck";
import { boxOfPoint, fitTo } from "../geo/plan";
import { nextTab } from "./RightTabs";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;
const CAM_CYCLE: CamMode[] = ["orbit", "fly", "driver"];

export function Shortcuts({ send, canvas }: { send: Send; canvas: HTMLCanvasElement | null }) {
  const helpOpen = useUiStore((s) => s.helpOpen);
  const cam = useUiStore((s) => s.cam);

  // Reacts to the state, not the keystroke -- so it covers every way "fly" gets set: the C key,
  // the toolbar button (which never touches focus itself), and a reload that restores
  // cam === "fly" from persisted state before Unity has even mounted its canvas. That last one
  // needs `canvas` to be React state (App.tsx holds it via a callback ref), not a plain ref object
  // -- a ref's mutation is invisible to React, so an effect keyed on it would never re-run once
  // the canvas actually arrives, and mounting already in fly mode would silently focus nothing.
  useEffect(() => {
    if (cam === "fly") canvas?.focus();
  }, [cam, canvas]);

  /// timeStamp of the last Escape that arrived mid-composition; see the pairing rule in ui/keys.ts.
  const composingEscapeAt = useRef<number | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const ui = useUiStore.getState();
      const ed = useEditorStore.getState();
      const active = document.activeElement;
      // e is a real KeyboardEvent, so it already carries isComposing and timeStamp -- KeyLike needs nothing extra.
      // Remember the composition-cancelling Escape BEFORE asking commandFor, because the plain Escape that
      // follows it shares this timeStamp and that equality is the only thing distinguishing it from a real one.
      if (e.key === "Escape" && e.isComposing) composingEscapeAt.current = e.timeStamp;
      const cmd = commandFor(e, {
        inField: isInField(active),
        flyingFocused: ui.cam === "fly" && active?.tagName === "CANVAS",
        composingEscapeAt: composingEscapeAt.current,
      });
      if (!cmd) return;
      // Escape keeps its native behaviour -- it is what closes an open <select> dropdown, and the panels have several.
      // Unconditional otherwise, even for "tool" in drive mode where it ends up a no-op: plain
      // letters and brackets have no browser default worth preserving, so gating this per case
      // that happens to be a no-op would add a branch for nothing observable.
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
          // 캔버스는 일부러 포커스는 되지만 탭으로는 못 간다(tabIndex=-1) -- Unity 가 Tab 을 삼키는지는
          // wasm 안이라 알 수 없으므로, 마우스 없이도 빠져나갈 길을 여기서 보장한다. blur() 는 캔버스가
          // 포커스 상태가 아니면 그냥 아무 일도 안 한다.
          // 대가: 비행 중에 Escape 를 누르면(선택 해제가 목적이었더라도) flyingFocused 가 꺼져서
          // WASD 가 도구 단축키로 되돌아간다 -- 다시 3D 뷰를 클릭할 때까지. 일부러 그렇게 뒀다: Escape
          // 가 가장 필요한 사람은 바로 지금 비행 중인, 키보드만 쓰는 사용자다. 여기서 blur 를 건너뛰면
          // 그 사람에게 탈출구가 없어지므로 이 함정을 막으려던 목적 자체가 무너진다.
          canvas?.blur();
          break;
        case "help": ui.toggleHelp(); break;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [send, canvas]);

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
