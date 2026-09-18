import { useEffect } from "react";
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
  useEffect(() => { void load(datasetId); }, [load, datasetId]);
  return (
    <div className="app">
      <TopBar />
      <Toolbar send={send} />
      <aside className="left">
        <DeckTabs />
        <LayerTree />
      </aside>
      <main className="center" onContextMenu={(e) => e.preventDefault()}>
        {!isLoaded && <div className="loading">Unity 로딩 중…</div>}
        {/* tabIndex=0, not -1: a click must focus the canvas (flyingFocused in Shortcuts checks
            document.activeElement === this canvas), and a keyboard user reaching for free flight
            needs to Tab onto it too. 0 keeps it in normal DOM order rather than jumping the queue
            ahead of the panels around it -- a positive index would; -1 would drop it from Tab entirely. */}
        <Unity unityProvider={unityProvider} tabIndex={0} style={{ width: "100%", height: "100%" }} />
      </main>
      <aside className="right"><RightTabs send={send} reloadScene={reloadScene} /></aside>
      <PlanDock />
      <Shortcuts send={send} />
      <StatusBar unityLoaded={isLoaded} />
    </div>
  );
}
