export type Layer = "A1" | "A2" | "B2" | "C" | "LM" | "LP" | "MEP";
export type Geometry = { type: "Point" | "LineString" | "Polygon"; coordinates: number[] | number[][] | number[][][] };
export type Feature = { id: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props: Record<string, unknown>; created_at?: string; updated_at?: string };
export type FeatureIn = { id?: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props?: Record<string, unknown> };
export type Deck = { id: string; name: string; z_surface: number; z_clear: number; movable: boolean; outline: number[][] };
export type Dataset = { id: string; name: string; ship_name?: string; version: number; ap_lat: number; ap_lon: number; heading_deg: number; lpp_m: number };
export type Pose = { draft_fwd_m?: number; draft_aft_m?: number; heel_deg?: number; heading_deg?: number; tide_m?: number; quay_z_m?: number; ap_lat?: number; ap_lon?: number; measured_at?: string; trim_deg?: number };
export type RampState = { id: string; length_m: number; width_m?: number; angle_deg: number; state: "deployed" | "blocked"; connects_lane?: string };
/** Unity -> React events (spec §10). */
export type FeatureCreatedEvt = { tempId: string; layer: Layer; x: number; y: number; z: number; deck: string; mounted_on?: string; normal?: number[] };
export type FeatureMovedEvt = { id: string; x: number; y: number; z: number; normal: number[]; deck: string; mounted_on: string };
export type LocalizationEvt = { est_x: number; est_y: number; est_psi: number; true_x: number; true_y: number; true_psi: number; residual_rms: number; n_obs: number; frame: string };
export type GenerateSlotsIn = { vehicle_class?: string; gap_lat_m?: number; gap_lon_m?: number; lashing_pitch_m?: number };
export type GenerateSlotsOut = { deck: string; count: number; utilization: number; lashing_coverage: number; version: number; slots: unknown[] };
export type SlotStatus = "empty" | "filled" | "needs_adjust" | "unreachable";
/** Unity -> React: parking judgement (load) or an emptied slot (unload: status "empty", no errors). */
export type SlotFilledEvt = { slot_id: string; status: SlotStatus; err_lat?: number; err_lon?: number; err_heading?: number };
export type ScenarioEvt = { event: "start" | "target" | "leave_lane" | "frame_switch" | "finished"; mode?: "load" | "unload"; slot_id?: string; detail?: string };
export type ScenarioLine = { t: string; text: string };
/** in_scope is absent for in-scope cells: the API only writes it when false. */
export type CoverageCell = { x: number; y: number; n: number; sigma_xy?: number; sigma_psi?: number; stability?: number; in_scope?: boolean };
export type CoverageMode = "load" | "unload";
export type CoverageSensor = { fov_deg: number; max_dist_m: number; max_view_angle_deg: number; sigma_r: number; sigma_theta: number; sigma_alpha: number };
export type Candidate = { x: number; y: number; phi_deg: number; mounted_on?: string };
export type CoverageIn = Partial<CoverageSensor> & { mode?: CoverageMode; grid_m?: number; extra_landmarks?: Candidate[]; omit?: string[]; budget?: number };
export type CoverageOut = {
  deck: string; mode: CoverageMode; grid_m: number; bbox: number[];
  n_cells: number;        // in-scope cells; the denominator of both ratios
  n_drawn: number;        // cells.length, the whole deck
  blind_ratio: number; weak_ratio: number; worst?: CoverageCell;
  other_mode: { mode: CoverageMode; blind_ratio: number; weak_ratio: number };
  cells: CoverageCell[];
};
export type Suggestion = { rank: number; x: number; y: number; phi_deg: number; mounted_on: string; blind_after: number; weak_after: number; gain: number };
export type SuggestOut = {
  deck: string; mode: CoverageMode; budget: number; grid_m: number;
  before: { blind_ratio: number; weak_ratio: number }; after: { blind_ratio: number; weak_ratio: number };
  suggestions: Suggestion[];
};
