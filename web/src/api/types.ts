export type Layer = "A1" | "A2" | "B2" | "C" | "LM" | "LP" | "MEP";
export type Geometry = { type: "Point" | "LineString" | "Polygon"; coordinates: number[] | number[][] | number[][][] };
export type Feature = { id: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props: Record<string, unknown>; created_at?: string; updated_at?: string };
export type FeatureIn = { id?: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props?: Record<string, unknown> };
export type Deck = { id: string; name: string; z_surface: number; z_clear: number; movable: boolean; outline: number[][] };
export type Dataset = { id: string; name: string; ship_name?: string; version: number; ap_lat: number; ap_lon: number; heading_deg: number; lpp_m: number };
export type Pose = { draft_fwd_m?: number; draft_aft_m?: number; heel_deg?: number; heading_deg?: number; tide_m?: number; quay_z_m?: number; ap_lat?: number; ap_lon?: number; measured_at?: string; trim_deg?: number };
export type RampState = { id: string; length_m: number; width_m?: number; angle_deg: number; state: "deployed" | "blocked"; connects_lane?: string };
/** Unity -> React events (spec §10). */
export type FeatureCreatedEvt = { tempId: string; layer: Layer; x: number; y: number; z: number; deck: string };
export type LocalizationEvt = { est_x: number; est_y: number; est_psi: number; true_x: number; true_y: number; true_psi: number; residual_rms: number; n_obs: number; frame: string };
