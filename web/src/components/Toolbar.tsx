import { useEffect } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type CamMode, type Tool } from "../store/ui";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

const TOOLS: { k: Tool; label: string; key: string; hint: string }[] = [
  { k: "select", label: "선택", key: "V", hint: "마커를 고르고 끌어 옮긴다" },
  { k: "place", label: "배치", key: "A", hint: "구조물 면을 클릭해 마커를 놓는다" },
  { k: "probe", label: "관측", key: "P", hint: "갑판을 클릭해 그 자리에서 보이는 것을 본다" },
];
const CAMS: { k: CamMode; label: string; key: string }[] = [
  { k: "orbit", label: "궤도", key: "C" }, { k: "fly", label: "비행", key: "C" }, { k: "driver", label: "차량 시선", key: "C" },
];

export function Toolbar({ send }: { send: Send }) {
  const mode = useEditorStore((s) => s.mode);
  const { tool, setTool, cam, setCam } = useUiStore();
  const pickTool = (t: Tool) => { setTool(t); send("SetTool", { tool: t }); };
  const pickCam = (c: CamMode) => { setCam(c); send("SetCamMode", { mode: c }); };

  // "driver" outlives the condition that allowed it: leaving drive mode disables the button but
  // does not itself change `cam`, and `cam` is persisted, so a reload would restore a camera the
  // toolbar refuses to let the user pick. Correct it here, in the component that owns the rule
  // (driver requires drive mode), with the same setCam + send pair the buttons use, so the store
  // and Unity never disagree about which camera is live. This settles rather than oscillates:
  // once it runs, cam becomes "orbit" and the condition below is false, so the effect is a no-op
  // on every later render until drive mode is re-entered and driver is picked again.
  useEffect(() => {
    if (mode !== "drive" && cam === "driver") { setCam("orbit"); send("SetCamMode", { mode: "orbit" }); }
  }, [mode, cam, setCam, send]);

  return (
    <div className="toolbar">
      {mode === "edit" && TOOLS.map((t) => (
        <button key={t.k} type="button" className={`btn${tool === t.k ? " primary" : ""}`} title={`${t.hint} (${t.key})`}
          onClick={() => pickTool(t.k)}>{t.label}</button>
      ))}
      {mode === "edit" && <span className="mode-hint">{TOOLS.find((t) => t.k === tool)!.hint}</span>}
      <span className="sep" />
      {CAMS.map((c) => (
        // 차량 시선은 차가 있어야 한다 (스펙 §5.1)
        <button key={c.k} type="button" className={`btn${cam === c.k ? " primary" : ""}`} title={`${c.label} (${c.key} 로 순환)`}
          disabled={c.k === "driver" && mode !== "drive"} onClick={() => pickCam(c.k)}>{c.label}</button>
      ))}
    </div>
  );
}
