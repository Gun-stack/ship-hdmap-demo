import { create } from "zustand";
import { api } from "../api/client";
import type { Dataset, Deck, Feature, FeatureCreatedEvt, FeatureIn, FeatureMovedEvt, GenerateSlotsIn, GenerateSlotsOut, Geometry, Layer, LocalizationEvt, Pose, RampState, ScenarioEvt, ScenarioLine, SlotFilledEvt } from "../api/types";

export type Draft = { tempId: string; layer: Layer; deck_id: string; geometry: Geometry; props: Record<string, unknown> };
export type Mode = "edit" | "drive";

export type EditorState = {
  datasetId: string; dataset: Dataset | null; decks: Deck[]; features: Record<string, Feature>; drafts: Record<string, Draft>;
  selectedId: string | null; deckFilter: string; mode: Mode; pose: Pose | null; ramp: RampState | null; localization: LocalizationEvt | null; error: string | null;
  slotGen: Record<string, { count: number; utilization: number; lashing_coverage: number }>;
  scenarioLog: ScenarioLine[];
  appendLog: (text: string) => void;
  clearLog: () => void;
  onSlotFilled: (e: SlotFilledEvt) => Promise<void>;
  load: (datasetId: string) => Promise<void>;
  select: (id: string | null) => void;
  setDeckFilter: (d: string) => void;
  setMode: (m: Mode) => void;
  addDraft: (e: FeatureCreatedEvt) => void;
  discardDraft: (tempId: string) => void;
  applyDraft: (tempId: string, patch: { kind: string; props?: Record<string, unknown>; deck_id?: string }) => Promise<{ tempId: string; id: string }>;
  updateFeature: (id: string, patch: Partial<FeatureIn>) => Promise<void>;
  moveFeature: (e: FeatureMovedEvt) => Promise<void>;
  removeFeature: (id: string) => Promise<void>;
  savePose: (patch: Partial<Pose>) => Promise<void>;
  setLocalization: (e: LocalizationEvt | null) => void;
  bumpVersion: (v: number) => void;
  unsavedCount: () => number;
  generateSlots: (deck: string, body: GenerateSlotsIn) => Promise<GenerateSlotsOut>;
};

const RAMP_ID = "RAMP-STERN";

/** After any write the server bumped dataset.version; fetch it so the top bar stays honest. Failure is not an error worth showing. */
async function refreshVersion(get: () => EditorState) {
  try { const d = await api.getDataset(get().datasetId); get().bumpVersion(d.version); } catch { /* keep the old value */ }
}

export const useEditorStore = create<EditorState>()((set, get) => ({
  datasetId: "roro-demo-01", dataset: null, decks: [], features: {}, drafts: {}, selectedId: null, deckFilter: "all", mode: "edit",
  pose: null, ramp: null, localization: null, error: null, slotGen: {}, scenarioLog: [],

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
    drafts: { ...s.drafts, [e.tempId]: { tempId: e.tempId, layer: e.layer, deck_id: e.deck, geometry: { type: "Point", coordinates: [e.x, e.y, e.z] }, props: { mounted_on: e.mounted_on ?? "", ...(e.normal ? { normal: e.normal } : {}) } } },
    selectedId: e.tempId,
  })),
  discardDraft: (tempId) => set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, selectedId: s.selectedId === tempId ? null : s.selectedId }; }),
  async applyDraft(tempId, patch) {
    const d = get().drafts[tempId]; if (!d) throw new Error("no draft " + tempId);
    const created = await api.createFeature(get().datasetId, { layer: d.layer, deck_id: patch.deck_id ?? d.deck_id, kind: patch.kind, geometry: d.geometry, props: patch.props ?? d.props });
    set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, features: { ...s.features, [created.id]: created }, selectedId: created.id }; });
    await refreshVersion(get);
    return { tempId, id: created.id };
  },
  async updateFeature(id, patch) {
    const f = await api.updateFeature(get().datasetId, id, patch);
    set((s) => ({ features: { ...s.features, [id]: f } }));
    await refreshVersion(get);
  },
  async moveFeature(e) {
    const d = get().drafts[e.id];
    if (d) { set((s) => ({ drafts: { ...s.drafts, [e.id]: { ...d, deck_id: e.deck, geometry: { type: "Point", coordinates: [e.x, e.y, e.z] }, props: { ...d.props, normal: e.normal, mounted_on: e.mounted_on } } } })); return; }
    const f = get().features[e.id]; if (!f) return;
    await get().updateFeature(e.id, { deck_id: e.deck, geometry: { type: "Point", coordinates: [e.x, e.y, e.z] }, props: { ...f.props, normal: e.normal, mounted_on: e.mounted_on } });
  },
  async removeFeature(id) {
    await api.deleteFeature(get().datasetId, id);
    set((s) => { const features = { ...s.features }; delete features[id]; return { features, selectedId: s.selectedId === id ? null : s.selectedId }; });
    await refreshVersion(get);
  },
  async savePose(patch) {
    const pose = await api.putPose(get().datasetId, patch);
    const ramp = await api.getRamp(get().datasetId, RAMP_ID).catch(() => get().ramp);
    set({ pose, ramp });
  },
  setLocalization: (localization) => set({ localization }),
  appendLog: (text) => set((s) => ({ scenarioLog: [{ t: clock(), text }, ...s.scenarioLog].slice(0, 100) })),
  clearLog: () => set({ scenarioLog: [] }),
  /** Unity judged a slot; persist it (the server bumps version) and log it. No scene reload — Unity already recoloured the fill. */
  async onSlotFilled(e) {
    get().appendLog(slotFilledLine(e));
    try { await api.putSlotStatus(get().datasetId, e.slot_id, e.status); await refreshVersion(get); set({ error: null }); }
    catch (err) { set({ error: "slot status failed: " + (err as Error).message }); }
  },
  bumpVersion: (v) => set((s) => (s.dataset ? { dataset: { ...s.dataset, version: v } } : {})),
  unsavedCount: () => Object.keys(get().drafts).length,
  async generateSlots(deck, body) {
    const out = await api.generateSlots(get().datasetId, deck, body);
    const list = await api.listFeatures(get().datasetId);
    set((s) => ({ features: Object.fromEntries(list.map((f) => [f.id, f])), slotGen: { ...s.slotGen, [deck]: { count: out.count, utilization: out.utilization, lashing_coverage: out.lashing_coverage } },
      selectedId: s.selectedId && !list.some((f) => f.id === s.selectedId) ? null : s.selectedId }));
    get().bumpVersion(out.version);
    return out;
  },
}));

export function visibleFeatures(s: EditorState): Feature[] {
  const all = Object.values(s.features);
  return s.deckFilter === "all" ? all : all.filter((f) => f.deck_id === s.deckFilter);
}

const clock = () => new Date().toTimeString().slice(0, 8);
const signed = (v: number, digits: number) => (v >= 0 ? "+" : "") + v.toFixed(digits);

export function slotFilledLine(e: SlotFilledEvt): string {
  if (e.err_lat === undefined || e.err_lon === undefined || e.err_heading === undefined) return `${e.slot_id} ${e.status}`;
  return `${e.slot_id} ${e.status}  lat ${signed(e.err_lat, 2)} lon ${signed(e.err_lon, 2)} hdg ${signed(e.err_heading, 1)}°`;
}

export function scenarioLine(e: ScenarioEvt): string {
  switch (e.event) {
    case "start": return e.mode === "unload" ? "◀ 하역 시작" : "▶ 선적 시작";
    case "target": return `대상 ${e.slot_id}`;
    case "leave_lane": return `차로 이탈 · ${e.detail ?? ""}`;
    case "frame_switch": return `프레임 전환 · ${e.detail ?? ""}`;
    case "finished": return `종료 (${e.detail ?? ""})`;
    default: return e.event;
  }
}
