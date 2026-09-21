import { create } from "zustand";
import { createJSONStorage, persist, type StateStorage } from "zustand/middleware";
import { api } from "../api/client";
import type { BeliefEvt, BeliefParamsIn, Candidate, CoverageMode, CoverageOut, CoverageSensor, Dataset, Deck, Feature, FeatureCreatedEvt, FeatureIn, FeatureMovedEvt, GenerateSlotsIn, GenerateSlotsOut, Geometry, Layer, LocalizationEvt, Pose, RampState, ScenarioEvt, ScenarioLine, SlotFilledEvt, Suggestion } from "../api/types";
import { SENSOR_DEFAULTS } from "../geo/coverage";

export type Draft = { tempId: string; layer: Layer; deck_id: string; geometry: Geometry; props: Record<string, unknown> };
export type Mode = "edit" | "drive";

/** Sensor noise on the wire: angles in degrees, matching SetNoiseMsg. Lived in DrivePanel until M5e needed it to survive a reload. */
export type NoiseParams = { sigma_r: number; sigma_theta: number; sigma_alpha: number; sigma_gps: number };
export const EDITOR_KEY = "shiphdmap.editor.roro-demo-01";
// 2: M6 moved sigma r/theta/alpha into coverageParams and left only sigma_gps behind, so a v1 blob has
// `noise` and no `sigmaGps`. zustand drops a version mismatch outright (see the persist block's comment),
// which is the wanted behaviour here -- there is nothing in a v1 blob worth migrating.
export const EDITOR_SCHEMA = 2;
export const EDITOR_WRITE_MS = 200;

/**
 * persist writes on EVERY set, and this store takes one at 5 Hz all through a drive
 * (setLocalization and setBelief, MapRuntime's 0.2 s emit) -- at time scale 20 that is a synchronous
 * localStorage write most frames, for fields partialize throws away anyway. Coalesce to one trailing
 * write instead. A write can be lost if the tab closes inside the window; these are view preferences.
 */
function coalescing(ms: number): StateStorage {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let pending: { name: string; value: string } | null = null;
  const flush = () => { timer = undefined; if (pending) localStorage.setItem(pending.name, pending.value); pending = null; };
  return {
    getItem: (name) => localStorage.getItem(name),
    setItem: (name, value) => { pending = { name, value }; timer ??= setTimeout(flush, ms); },
    // The timer has to die with the value -- an armed timer outliving a clear would fire later and
    // resurrect the key from whatever setItem queued moments before. (localStorage.clear() bypasses this
    // adapter entirely, straight to the browser API, so a stray in-flight timer can still win a race
    // against it -- nothing in this file can defend against that path.)
    removeItem: (name) => { pending = null; if (timer !== undefined) { clearTimeout(timer); timer = undefined; } localStorage.removeItem(name); },
  };
}

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
  coverage: CoverageOut | null;
  coverageMode: CoverageMode;
  coverageParams: CoverageSensor & { grid_m: number };
  candidates: Candidate[];
  suggestions: Suggestion[];
  coverageBusy: boolean;
  // Every coverage action takes the deck id instead of picking one: geo/deck.pickDeck owns that rule and
  // importing it here would be a cycle. Two rules would diverge -- all three decks have the same area, so
  // with deckFilter "all" the store and the plan view would land on different decks.
  setCoverageMode: (m: CoverageMode, deck: string) => void;
  setCoverageParams: (p: Partial<CoverageSensor & { grid_m: number }>) => void;
  runCoverage: (deck: string) => Promise<void>;
  addCandidate: (c: Candidate, deck: string) => void;
  removeCandidate: (i: number, deck: string) => void;
  clearCandidates: (deck: string) => void;
  runSuggest: (deck: string, budget: number) => Promise<void>;
  commitCandidates: (deck: string) => Promise<number>;
  belief: BeliefEvt | null;
  beliefParams: BeliefParamsIn;
  occluded: string[];
  setBelief: (e: BeliefEvt | null) => void;
  toggleOccluded: (id: string) => void;
  setBeliefParams: (p: Partial<BeliefParamsIn>) => void;
  setError: (error: string | null) => void;
  sigmaGps: number;
  timeScale: number;
  setSigmaGps: (v: number) => void;
  setTimeScale: (v: number) => void;
};

const RAMP_ID = "RAMP-STERN";

/** After any write the server bumped dataset.version; fetch it so the top bar stays honest. Failure is not an error worth showing. */
async function refreshVersion(get: () => EditorState) {
  try { const d = await api.getDataset(get().datasetId); get().bumpVersion(d.version); } catch { /* keep the old value */ }
}

export const useEditorStore = create<EditorState>()(persist((set, get) => ({
  datasetId: "roro-demo-01", dataset: null, decks: [], features: {}, drafts: {}, selectedId: null, deckFilter: "all", mode: "edit",
  pose: null, ramp: null, localization: null, error: null, slotGen: {}, scenarioLog: [],
  coverage: null, coverageMode: "load",
  // seeded with the API defaults: an empty object would leave the sliders at their minimum while the server
  // silently computed with something else
  coverageParams: { ...SENSOR_DEFAULTS, grid_m: 1.0 },
  candidates: [], suggestions: [], coverageBusy: false,
  belief: null, occluded: [],
  // must match BeliefParams in BeliefMonitor.cs -- the panel showing one number while Unity uses another is the
  // same trap the coverage sliders hit in M5c
  beliefParams: { k: 2.0, frames: 5, drift_rate: 0.05, budget_m: 1.0, max_lost_m: 5.0, trail_m: 20.0 },
  sigmaGps: 0.5, timeScale: 1,
  setBelief: (belief) => set({ belief }),
  setBeliefParams: (p) => set((s) => ({ beliefParams: { ...s.beliefParams, ...p } })),
  setError: (error) => set({ error }),
  setSigmaGps: (v) => set({ sigmaGps: v }),
  setTimeScale: (timeScale) => set({ timeScale }),
  toggleOccluded: (id) => set((s) => ({ occluded: s.occluded.includes(id) ? s.occluded.filter((x) => x !== id) : [...s.occluded, id] })),

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

  setCoverageMode: (coverageMode, deck) => { set({ coverageMode }); void get().runCoverage(deck); },
  setCoverageParams: (p) => set((s) => ({ coverageParams: { ...s.coverageParams, ...p } })),
  async runCoverage(deck) {
    if (!deck) return;
    const s = get();
    set({ coverageBusy: true });
    try {
      const out = await api.coverage(s.datasetId, deck, { ...s.coverageParams, mode: s.coverageMode, extra_landmarks: s.candidates });
      set({ coverage: out, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
    finally { set({ coverageBusy: false }); }
  },
  addCandidate: (c, deck) => { set((s) => ({ candidates: [...s.candidates, c] })); void get().runCoverage(deck); },
  removeCandidate: (i, deck) => { set((s) => ({ candidates: s.candidates.filter((_, k) => k !== i) })); void get().runCoverage(deck); },
  clearCandidates: (deck) => { set({ candidates: [], suggestions: [] }); void get().runCoverage(deck); },
  async runSuggest(deck, budget) {
    if (!deck) return;
    const s = get();
    set({ coverageBusy: true });
    try {
      const out = await api.suggestCoverage(s.datasetId, deck, { ...s.coverageParams, mode: s.coverageMode, extra_landmarks: s.candidates, budget });
      set({ suggestions: out.suggestions, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
    finally { set({ coverageBusy: false }); }
  },
  /** Persist every candidate as a real landmark. Returns how many were written. */
  async commitCandidates(deck) {
    const s = get();
    if (!deck || s.candidates.length === 0) return 0;
    const z = (s.decks.find((d) => d.id === deck)?.z_surface ?? 0) + 1.2;
    let code = Math.max(0, ...Object.values(s.features).filter((f) => f.layer === "LM").map((f) => Number(f.props.code) || 0));
    for (const c of s.candidates) {
      const phi = (c.phi_deg * Math.PI) / 180;
      await api.createFeature(s.datasetId, {
        layer: "LM", deck_id: deck, kind: "apriltag",
        geometry: { type: "Point", coordinates: [c.x, c.y, z] },
        props: { family: "apriltag-36h11", code: ++code, normal: [Math.cos(phi), Math.sin(phi), 0], size_m: 0.3, mounted_on: c.mounted_on ?? "" },
      });
    }
    const n = s.candidates.length;
    set({ candidates: [], suggestions: [] });
    await get().load(s.datasetId);
    await get().runCoverage(deck);
    return n;
  },
}),
  {
    name: EDITOR_KEY,
    version: EDITOR_SCHEMA,
    storage: createJSONStorage(() => coalescing(EDITOR_WRITE_MS)),
    // Only what the viewer set by hand. Map data, coverage results, drafts, the selection and the live
    // localization/belief feeds all come back from the server or from Unity -- persisting a stale copy
    // would put yesterday's numbers next to today's dataset version.
    partialize: (s) => ({
      deckFilter: s.deckFilter, mode: s.mode, coverageMode: s.coverageMode, coverageParams: s.coverageParams,
      beliefParams: s.beliefParams, sigmaGps: s.sigmaGps, timeScale: s.timeScale, occluded: s.occluded,
    }),
    // No migrate: zustand already drops a version mismatch and falls back to the store's own defaults.
    // Nothing here backstops the key list above, though -- partialize's return type is inferred as a
    // plain object either way, so tsc accepts a typo'd or extra key just as happily as a correct one.
    // The exact persisted key set is pinned by a test instead (editor.test.ts, "저장하는 키가 정확히
    // 이것뿐이다") -- that is the only thing that fails if this list drifts.
  },
));

/**
 * The SetNoise payload, assembled. sigma r/theta/alpha live in coverageParams so the heatmap's PREDICTION and
 * the drive's MEASUREMENT cannot be set to different numbers (M6 spec §4); sigma_gps has no coverage
 * counterpart -- the quay leg has no landmarks -- so it stays on its own.
 *
 * Four places send SetNoise: the reload replay, the sigma-GPS slider's release, the scenario start, and the
 * coverage slider effect. They all come through here, or the four drift.
 */
export function noiseMsg(s: Pick<EditorState, "coverageParams" | "sigmaGps">): NoiseParams {
  const { sigma_r, sigma_theta, sigma_alpha } = s.coverageParams;
  return { sigma_r, sigma_theta, sigma_alpha, sigma_gps: s.sigmaGps };
}

/**
 * The SetSensor payload: the same three names the coverage API takes, and nothing else (M6 spec §3.1).
 * Picked field by field on purpose -- coverageParams also carries grid_m and the three sigmas, and spreading
 * it would hand Unity keys SetSensorMsg has no field for, which Newtonsoft drops in silence.
 */
export function sensorMsg(s: Pick<EditorState, "coverageParams">) {
  const { fov_deg, max_dist_m, max_view_angle_deg } = s.coverageParams;
  return { fov_deg, max_dist_m, max_view_angle_deg };
}

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
