import { useEffect } from "react";
import { Unity } from "react-unity-webgl";
import { useShipUnity } from "./bridge/useShipUnity";
import { useEditorStore } from "./store/editor";
import { TopBar } from "./components/TopBar";
import { StatusBar } from "./components/StatusBar";
import "./App.css";

export default function App() {
  const load = useEditorStore((s) => s.load);
  const datasetId = useEditorStore((s) => s.datasetId);
  const { unityProvider, isLoaded, send } = useShipUnity();
  useEffect(() => { void load(datasetId); }, [load, datasetId]);
  return (
    <div className="app">
      <TopBar />
      <aside className="left">left panel (Task 6)</aside>
      <main className="center">
        {!isLoaded && <div className="loading">Unity 로딩 중…</div>}
        <Unity unityProvider={unityProvider} style={{ width: "100%", height: "100%" }} />
      </main>
      <aside className="right">right panel (Task 7–8)</aside>
      <StatusBar unityLoaded={isLoaded} />
      {/* send is threaded to panels in later tasks */}
      <span hidden>{typeof send}</span>
    </div>
  );
}
