import { useEffect } from "react";
import { useEditorStore } from "../store/editor";
import { useUiStore, type RightTab } from "../store/ui";
import type { BridgeName } from "../bridge/useShipUnity";
import { LoadPanel } from "./LoadPanel";
import { CoveragePanel } from "./CoveragePanel";
import { PropertyForm } from "./PropertyForm";
import { PosePanel } from "./PosePanel";
import { DrivePanel } from "./DrivePanel";

type Send = (name: BridgeName, payload?: string | object) => void;

export const TAB_LABELS: Record<RightTab, string> = { load: "적재", coverage: "커버리지", props: "속성", pose: "자세" };
export const EDIT_TABS: RightTab[] = ["load", "coverage", "props", "pose"];

export function nextTab(tab: RightTab, dir: number): RightTab {
  const i = EDIT_TABS.indexOf(tab);
  return EDIT_TABS[(i + dir + EDIT_TABS.length) % EDIT_TABS.length];
}

export function RightTabs({ send, reloadScene }: { send: Send; reloadScene: () => Promise<void> }) {
  const mode = useEditorStore((s) => s.mode);
  const selectedId = useEditorStore((s) => s.selectedId);
  const tab = useUiStore((s) => s.tab);
  const setTab = useUiStore((s) => s.setTab);
  const openPropsFor = useUiStore((s) => s.openPropsFor);
  const restoreTab = useUiStore((s) => s.restoreTab);

  // a selection must not stay hidden behind another tab; clearing it returns the user where they were.
  // restoreTab is a no-op unless 속성 is open, so the mount-time run with no selection changes nothing.
  useEffect(() => { if (selectedId) openPropsFor(); else restoreTab(); }, [selectedId, openPropsFor, restoreTab]);

  if (mode === "drive") return <div className="tabbody"><DrivePanel send={send} /><PosePanel /></div>;

  return (
    <>
      <div className="tabs" role="tablist">
        {EDIT_TABS.map((t) => (
          <button key={t} type="button" role="tab" aria-selected={tab === t} className={`tab${tab === t ? " on" : ""}`}
            onClick={() => setTab(t)}>{TAB_LABELS[t]}</button>
        ))}
      </div>
      <div className="tabbody">
        {tab === "load" && <LoadPanel reloadScene={reloadScene} />}
        {tab === "coverage" && <CoveragePanel />}
        {tab === "props" && <PropertyForm send={send} />}
        {tab === "pose" && <PosePanel />}
      </div>
    </>
  );
}
