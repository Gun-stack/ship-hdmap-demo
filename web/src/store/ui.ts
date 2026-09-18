import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Tool = "select" | "place" | "probe";
export type CamMode = "orbit" | "fly" | "driver";
export type RightTab = "load" | "coverage" | "props" | "pose";

/** Bump when the stored shape changes; a mismatch throws the saved state away rather than migrating it. */
export const UI_SCHEMA = 1;
export const UI_KEY = "shiphdmap.ui.roro-demo-01";

/** Plan-view window, in SVG space: x forward, y DOWN (= ship -y), matching what geo/deck.bbox returns. */
export type PlanView = { cx: number; cy: number; scale: number };

type UiState = {
  tool: Tool; cam: CamMode; tab: RightTab; prevTab: RightTab;
  dockOpen: boolean; dockTall: boolean; helpOpen: boolean;
  treeOpen: Record<string, boolean>;
  planView: PlanView;
  setTool: (t: Tool) => void;
  setCam: (c: CamMode) => void;
  setTab: (t: RightTab) => void;
  /** A selection happened: show 속성, remembering where the user was. */
  openPropsFor: () => void;
  /** Selection cleared: go back to whatever they were looking at. No-op unless 속성 is open. */
  restoreTab: () => void;
  toggleTree: (layer: string) => void;
  setPlanView: (v: PlanView) => void;
  toggleDock: () => void;
  toggleDockTall: () => void;
  toggleHelp: () => void;
};

const DEFAULTS = {
  tool: "select" as Tool, cam: "orbit" as CamMode, tab: "load" as RightTab, prevTab: "load" as RightTab,
  dockOpen: true, dockTall: false, helpOpen: false,
  treeOpen: { LM: true, A2: true, B2: true, C: true } as Record<string, boolean>,
  planView: { cx: 60, cy: 0, scale: 1 } as PlanView,
};

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      ...DEFAULTS,
      setTool: (tool) => set({ tool }),
      setCam: (cam) => set({ cam }),
      // a deliberate tab change also becomes the place we return to after a selection ends
      setTab: (tab) => set({ tab, prevTab: tab }),
      openPropsFor: () => set((s) => (s.tab === "props" ? {} : { tab: "props" as RightTab, prevTab: s.tab })),
      restoreTab: () => set((s) => (s.tab === "props" ? { tab: s.prevTab } : {})),
      toggleTree: (layer) => set((s) => ({ treeOpen: { ...s.treeOpen, [layer]: !(s.treeOpen[layer] ?? false) } })),
      setPlanView: (planView) => set({ planView }),
      toggleDock: () => set((s) => ({ dockOpen: !s.dockOpen })),
      toggleDockTall: () => set((s) => ({ dockTall: !s.dockTall })),
      toggleHelp: () => set((s) => ({ helpOpen: !s.helpOpen })),
    }),
    {
      name: UI_KEY,
      version: UI_SCHEMA,
      // `tab` is stored as prevTab whenever 속성 is open: selection is deliberately NOT persisted (spec §6.2),
      // so restoring straight into an empty 속성 tab would be a panel about nothing. helpOpen is transient too.
      partialize: (s) => ({
        tool: s.tool, cam: s.cam, tab: s.tab === "props" ? s.prevTab : s.tab, prevTab: s.prevTab,
        dockOpen: s.dockOpen, dockTall: s.dockTall, treeOpen: s.treeOpen, planView: s.planView,
      }),
      migrate: () => ({ ...DEFAULTS }),   // no migrations: a shape change means start clean
    },
  ),
);
