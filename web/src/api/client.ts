import type { CoverageIn, CoverageOut, Dataset, Deck, Feature, FeatureIn, GenerateSlotsIn, GenerateSlotsOut, Pose, RampState, Route, InnerRamp, SlotStatus, SuggestOut } from "./types";

const BASE = "/api";

export class ApiError extends Error {
  status: number;
  field?: string;
  constructor(status: number, message: string, field?: string) {
    super(message);
    this.status = status;
    this.field = field;
  }
}

async function req<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(BASE + path, { method, headers: body === undefined ? {} : { "content-type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new ApiError(res.status, json?.message ?? res.statusText, json?.field);
  return json as T;
}

const q = (params: Record<string, string | undefined>) => {
  const s = new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined) as [string, string][]).toString();
  return s ? "?" + s : "";
};

export const api = {
  getDataset: (ds: string) => req<Dataset>("GET", `/datasets/${ds}`),
  /** M2 has no deck endpoint; decks come from the vehicle map. So do slot statuses, which live in parking_slot, not on the B2 feature. */
  mapMeta: async (ds: string) => {
    const m = await req<{ decks: Deck[]; parking_slots?: { id: string; status?: SlotStatus }[]; ramps?: (InnerRamp & { type?: string })[]; routes?: Route[] }>("GET", `/datasets/${ds}/vehicle-map`);
    return {
      decks: m.decks,
      slotStatus: Object.fromEntries((m.parking_slots ?? []).map((p) => [p.id, p.status ?? "empty"])) as Record<string, SlotStatus>,
      innerRamps: (m.ramps ?? []).filter((r) => r.type === "internal_hoistable").map(({ id, lower_deck, upper_deck, hinge, toe }) => ({ id, lower_deck, upper_deck, hinge, toe })),
      routes: m.routes ?? [],
    };
  },
  listFeatures: (ds: string, f: { deck?: string; layer?: string } = {}) => req<Feature[]>("GET", `/datasets/${ds}/features${q(f)}`),
  createFeature: (ds: string, f: FeatureIn) => req<Feature>("POST", `/datasets/${ds}/features`, f),
  updateFeature: (ds: string, id: string, patch: Partial<FeatureIn>) => req<Feature>("PUT", `/datasets/${ds}/features/${id}`, patch),
  deleteFeature: (ds: string, id: string) => req<void>("DELETE", `/datasets/${ds}/features/${id}`),
  getPose: (ds: string) => req<Pose>("GET", `/datasets/${ds}/pose`),
  putPose: (ds: string, p: Partial<Pose>) => req<Pose>("PUT", `/datasets/${ds}/pose`, p),
  getRamp: (ds: string, rid: string) => req<RampState>("GET", `/datasets/${ds}/ramps/${rid}`),
  generateSlots: (ds: string, deck: string, body: GenerateSlotsIn) => req<GenerateSlotsOut>("POST", `/datasets/${ds}/decks/${deck}/slots/generate`, body),
  coverage: (ds: string, deck: string, body: CoverageIn) => req<CoverageOut>("POST", `/datasets/${ds}/decks/${deck}/coverage`, body),
  suggestCoverage: (ds: string, deck: string, body: CoverageIn) => req<SuggestOut>("POST", `/datasets/${ds}/decks/${deck}/coverage/suggest`, body),
  putSlotStatus: (ds: string, sid: string, status: SlotStatus) => req<{ id: string; status: SlotStatus }>("PUT", `/datasets/${ds}/slots/${sid}/status`, { status }),
  vehicleMapUrl: (ds: string) => `${BASE}/datasets/${ds}/vehicle-map`,
  geojsonUrl: (ds: string) => `${BASE}/datasets/${ds}/export.geojson`,
};
