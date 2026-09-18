import { useEffect } from "react";
import { Unity } from "react-unity-webgl";
import { useShipUnity } from "./bridge/useShipUnity";
import { useEditorStore } from "./store/editor";
import { TopBar } from "./components/TopBar";
import { StatusBar } from "./components/StatusBar";
import { DeckTabs } from "./components/DeckTabs";
import { LayerTree } from "./components/LayerTree";
import { PlanDock } from "./components/PlanDock";
import { LoadPanel } from "./components/LoadPanel";
import { PropertyForm } from "./components/PropertyForm";
import { CoveragePanel } from "./components/CoveragePanel";
import { PosePanel } from "./components/PosePanel";
import { DrivePanel } from "./components/DrivePanel";
import "./App.css";

export default function App() {
  const load = useEditorStore((s) => s.load);
  const datasetId = useEditorStore((s) => s.datasetId);
  const mode = useEditorStore((s) => s.mode);
  const { unityProvider, isLoaded, send, reloadScene } = useShipUnity();
  useEffect(() => { void load(datasetId); }, [load, datasetId]);
  return (
    <div className="app">
      <TopBar />
      <aside className="left">
        <DeckTabs />
        <LayerTree />
      </aside>
      <main className="center" onContextMenu={(e) => e.preventDefault()}>
        {!isLoaded && <div className="loading">Unity 로딩 중…</div>}
        <Unity unityProvider={unityProvider} style={{ width: "100%", height: "100%" }} />
      </main>
      <aside className="right">
        {mode === "edit" ? (<><LoadPanel reloadScene={reloadScene} /><CoveragePanel /><PropertyForm send={send} /></>) : <DrivePanel send={send} />}
        <PosePanel />
      </aside>
      <PlanDock />
      <StatusBar unityLoaded={isLoaded} />
    </div>
  );
}
