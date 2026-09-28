import { useState } from "react";
import { api } from "../api/client";
import { useEditorStore } from "../store/editor";
import { applyTheme, loadTheme, type Theme } from "../theme/theme";

export function TopBar() {
  const { dataset, datasetId, mode, setMode } = useEditorStore();
  const [theme, setTheme] = useState<Theme>(loadTheme);
  const flip = () => { const t: Theme = theme === "dark" ? "light" : "dark"; applyTheme(t); setTheme(t); };
  return (
    <header className="topbar">
      <strong>Ship HD Map Editor</strong>
      <span className="seg">
        <button type="button" className={"tab" + (mode === "edit" ? " on" : "")} onClick={() => setMode("edit")}>편집</button>
        <button type="button" className={"tab" + (mode === "drive" ? " on" : "")} onClick={() => setMode("drive")}>주행</button>
      </span>
      <span className="spacer" />
      <span className="ds">{datasetId} · v{dataset?.version ?? "-"}</span>
      <a href={api.geojsonUrl(datasetId)} target="_blank" rel="noreferrer">GeoJSON</a>
      <a href={api.vehicleMapUrl(datasetId)} target="_blank" rel="noreferrer">차량지도</a>
      <button type="button" className="theme" onClick={flip} title="화면 테마 전환">{theme === "dark" ? "☀ 라이트" : "☾ 다크"}</button>
    </header>
  );
}
