import { api } from "../api/client";
import { useEditorStore } from "../store/editor";

export function TopBar() {
  const { dataset, datasetId, mode, setMode } = useEditorStore();
  return (
    <header className="topbar">
      <strong>Ship HD Map Editor</strong>
      <span className={"tab" + (mode === "edit" ? " on" : "")} onClick={() => setMode("edit")}>편집</span>
      <span className={"tab" + (mode === "drive" ? " on" : "")} onClick={() => setMode("drive")}>주행</span>
      <span className="spacer" />
      <span>{datasetId} · v{dataset?.version ?? "-"}</span>
      <a href={api.geojsonUrl(datasetId)} target="_blank" rel="noreferrer">GeoJSON</a>
      <a href={api.vehicleMapUrl(datasetId)} target="_blank" rel="noreferrer">차량지도</a>
    </header>
  );
}
