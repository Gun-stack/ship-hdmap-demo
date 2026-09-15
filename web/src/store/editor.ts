import { create } from "zustand";
import { api } from "../api/client";
import type { Dataset, Deck, Feature, FeatureCreatedEvt, FeatureIn, Geometry, Layer, LocalizationEvt, Pose, RampState } from "../api/types";

export type Draft = { tempId: string; layer: Layer; deck_id: string; geometry: Geometry; props: Record<string, unknown> };
export type Mode = "edit" | "drive";

export type EditorState = {
  datasetId: string; dataset: Dataset | null; decks: Deck[]; features: Record<string, Feature>; drafts: Record<string, Draft>;
  selectedId: string | null; deckFilter: string; mode: Mode; pose: Pose | null; ramp: RampState | null; localization: LocalizationEvt | null; error: string | null;
  load: (datasetId: string) => Promise<void>;
  select: (id: string | null) => void;
  setDeckFilter: (d: string) => void;
  setMode: (m: Mode) => void;
  addDraft: (e: FeatureCreatedEvt) => void;
  discardDraft: (tempId: string) => void;
  applyDraft: (tempId: string, patch: { kind: string; props?: Record<string, unknown>; deck_id?: string }) => Promise<{ tempId: string; id: string }>;
  updateFeature: (id: string, patch: Partial<FeatureIn>) => Promise<void>;
  removeFeature: (id: string) => Promise<void>;
  savePose: (patch: Partial<Pose>) => Promise<void>;
  setLocalization: (e: LocalizationEvt | null) => void;
  bumpVersion: (v: number) => void;
  unsavedCount: () => number;
};

const RAMP_ID = "RAMP-STERN";

export const useEditorStore = create<EditorState>()((set, get) => ({
  datasetId: "roro-demo-01", dataset: null, decks: [], features: {}, drafts: {}, selectedId: null, deckFilter: "all", mode: "edit",
  pose: null, ramp: null, localization: null, error: null,

  async load(datasetId) {
    try {
      const [dataset, decks, list, pose] = await Promise.all([api.getDataset(datasetId), api.listDecks(datasetId), api.listFeatures(datasetId), api.getPose(datasetId)]);
      const ramp = await api.getRamp(datasetId, RAMP_ID).catch(() => null);
      set({ datasetId, dataset, decks, features: Object.fromEntries(list.map((f) => [f.id, f])), pose, ramp, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
  },
  select: (id) => set({ selectedId: id }),
  setDeckFilter: (deckFilter) => set({ deckFilter }),
  setMode: (mode) => set({ mode }),
  addDraft: (e) => set((s) => ({
    drafts: { ...s.drafts, [e.tempId]: { tempId: e.tempId, layer: e.layer, deck_id: e.deck, geometry: { type: "Point", coordinates: [e.x, e.y, e.z] }, props: {} } },
    selectedId: e.tempId,
  })),
  discardDraft: (tempId) => set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, selectedId: s.selectedId === tempId ? null : s.selectedId }; }),
  async applyDraft(tempId, patch) {
    const d = get().drafts[tempId]; if (!d) throw new Error("no draft " + tempId);
    const created = await api.createFeature(get().datasetId, { layer: d.layer, deck_id: patch.deck_id ?? d.deck_id, kind: patch.kind, geometry: d.geometry, props: patch.props ?? d.props });
    set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, features: { ...s.features, [created.id]: created }, selectedId: created.id }; });
    return { tempId, id: created.id };
  },
  async updateFeature(id, patch) {
    const f = await api.updateFeature(get().datasetId, id, patch);
    set((s) => ({ features: { ...s.features, [id]: f } }));
  },
  async removeFeature(id) {
    await api.deleteFeature(get().datasetId, id);
    set((s) => { const features = { ...s.features }; delete features[id]; return { features, selectedId: s.selectedId === id ? null : s.selectedId }; });
  },
  async savePose(patch) {
    const pose = await api.putPose(get().datasetId, patch);
    const ramp = await api.getRamp(get().datasetId, RAMP_ID).catch(() => get().ramp);
    set({ pose, ramp });
  },
  setLocalization: (localization) => set({ localization }),
  bumpVersion: (v) => set((s) => (s.dataset ? { dataset: { ...s.dataset, version: v } } : {})),
  unsavedCount: () => Object.keys(get().drafts).length,
}));

export function visibleFeatures(s: EditorState): Feature[] {
  const all = Object.values(s.features);
  return s.deckFilter === "all" ? all : all.filter((f) => f.deck_id === s.deckFilter);
}
