import { useEffect, useRef } from "react";
import { Unity } from "react-unity-webgl";
import { useShipUnity } from "./bridge/useShipUnity";
import { useEditorStore } from "./store/editor";
import { TopBar } from "./components/TopBar";
import { Toolbar } from "./components/Toolbar";
import { StatusBar } from "./components/StatusBar";
import { DeckTabs } from "./components/DeckTabs";
import { LayerTree } from "./components/LayerTree";
import { PlanDock } from "./components/PlanDock";
import { RightTabs } from "./components/RightTabs";
import { Shortcuts } from "./components/Shortcuts";
import "./App.css";

export default function App() {
  const load = useEditorStore((s) => s.load);
  const datasetId = useEditorStore((s) => s.datasetId);
  const { unityProvider, isLoaded, send, reloadScene } = useShipUnity();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => { void load(datasetId); }, [load, datasetId]);
  return (
    <div className="app">
      <TopBar />
      <Toolbar send={send} />
      <aside className="left">
        <DeckTabs />
        <LayerTree />
      </aside>
      {/* tabIndex=-1, not 0: the canvas must be focusable so Unity gets the flight keys, but it
          is deliberately kept OUT of the Tab order. Emscripten's keydown handler runs
          preventDefault() before we ever see whether it consumed the key, so if it swallows Tab
          a keyboard user who tabbed onto the canvas would have no way to tab back off -- a trap
          we cannot rule out by reading wasm. Keeping it -1 means Tab never lands here in the
          first place; the canvas still gets focus by a click here or by Shortcuts.tsx focusing
          it the moment C cycles the camera into fly mode. */}
      <main className="center" onContextMenu={(e) => e.preventDefault()} onPointerDown={() => canvasRef.current?.focus()}>
        {!isLoaded && <div className="loading">Unity 로딩 중…</div>}
        <Unity ref={canvasRef} unityProvider={unityProvider} tabIndex={-1} style={{ width: "100%", height: "100%" }} />
      </main>
      <aside className="right"><RightTabs send={send} reloadScene={reloadScene} /></aside>
      <PlanDock />
      <Shortcuts send={send} canvasRef={canvasRef} />
      <StatusBar unityLoaded={isLoaded} />
    </div>
  );
}
