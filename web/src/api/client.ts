import type { Dataset, Deck, Feature, FeatureIn, GenerateSlotsIn, GenerateSlotsOut, Pose, RampState } from "./types";

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
  /** M2 has no deck endpoint; decks come from the vehicle map. */
  listDecks: async (ds: string) => (await req<{ decks: Deck[] }>("GET", `/datasets/${ds}/vehicle-map`)).decks,
  listFeatures: (ds: string, f: { deck?: string; layer?: string } = {}) => req<Feature[]>("GET", `/datasets/${ds}/features${q(f)}`),
  createFeature: (ds: string, f: FeatureIn) => req<Feature>("POST", `/datasets/${ds}/features`, f),
  updateFeature: (ds: string, id: string, patch: Partial<FeatureIn>) => req<Feature>("PUT", `/datasets/${ds}/features/${id}`, patch),
  deleteFeature: (ds: string, id: string) => req<void>("DELETE", `/datasets/${ds}/features/${id}`),
  getPose: (ds: string) => req<Pose>("GET", `/datasets/${ds}/pose`),
  putPose: (ds: string, p: Partial<Pose>) => req<Pose>("PUT", `/datasets/${ds}/pose`, p),
  getRamp: (ds: string, rid: string) => req<RampState>("GET", `/datasets/${ds}/ramps/${rid}`),
  generateSlots: (ds: string, deck: string, body: GenerateSlotsIn) => req<GenerateSlotsOut>("POST", `/datasets/${ds}/decks/${deck}/slots/generate`, body),
  vehicleMapUrl: (ds: string) => `${BASE}/datasets/${ds}/vehicle-map`,
  geojsonUrl: (ds: string) => `${BASE}/datasets/${ds}/export.geojson`,
};
