import { describe, it, expect, vi, beforeEach } from "vitest";
import { useEditorStore, visibleFeatures } from "./editor";
import { api } from "../api/client";

vi.mock("../api/client", () => ({
  api: {
    getDataset: vi.fn(async () => ({ id: "ds1", name: "d", version: 3, ap_lat: 0, ap_lon: 0, heading_deg: 0, lpp_m: 120 })),
    listDecks: vi.fn(async () => [{ id: "D3", name: "Deck 3", z_surface: 10.6, z_clear: 2.2, movable: false, outline: [] }]),
    listFeatures: vi.fn(async () => [
      { id: "LM-0001", deck_id: "D3", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [12, -6.2, 11.8] }, props: { code: 1 } },
      { id: "A2-D1-0001", deck_id: "D1", layer: "A2", kind: "centerline", geometry: { type: "LineString", coordinates: [[2, 0, 5.4], [118, 0, 5.4]] }, props: {} },
    ]),
    getPose: vi.fn(async () => ({ draft_fwd_m: 8.1, draft_aft_m: 8.6, tide_m: 0, trim_deg: 0.24 })),
    getRamp: vi.fn(async () => ({ id: "RAMP-STERN", length_m: 30, angle_deg: 2.9, state: "deployed" })),
    createFeature: vi.fn(async (_ds: string, f: { layer: string }) => ({ id: "LM-0020", deck_id: "D3", layer: f.layer, kind: "apriltag", geometry: { type: "Point", coordinates: [84, -6.2, 11.8] }, props: { code: 7 } })),
    updateFeature: vi.fn(async (_ds: string, id: string, patch: object) => ({ id, deck_id: "D3", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [12, -6.2, 11.8] }, props: { code: 9 }, ...patch })),
    deleteFeature: vi.fn(async () => undefined),
    putPose: vi.fn(async (_ds: string, p: object) => ({ draft_fwd_m: 8.1, draft_aft_m: 8.6, tide_m: 1.2, trim_deg: 0.24, ...p })),
  },
}));

describe("editor store", () => {
  beforeEach(() => useEditorStore.setState(useEditorStore.getInitialState()));

  it("load fills dataset, decks, features, pose, ramp", async () => {
    await useEditorStore.getState().load("ds1");
    const s = useEditorStore.getState();
    expect(s.dataset?.version).toBe(3);
    expect(s.decks.map((d) => d.id)).toEqual(["D3"]);
    expect(Object.keys(s.features)).toEqual(["LM-0001", "A2-D1-0001"]);
    expect(s.pose?.tide_m).toBe(0);
    expect(s.ramp?.state).toBe("deployed");
  });

  it("deck filter selects visible features", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().setDeckFilter("D3");
    expect(visibleFeatures(useEditorStore.getState()).map((f) => f.id)).toEqual(["LM-0001"]);
    useEditorStore.getState().setDeckFilter("all");
    expect(visibleFeatures(useEditorStore.getState())).toHaveLength(2);
  });

  it("draft -> apply creates a feature and returns the id mapping", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().addDraft({ tempId: "LM-0002", layer: "LM", x: 84, y: -6.2, z: 11.8, deck: "D3" });
    expect(useEditorStore.getState().unsavedCount()).toBe(1);
    expect(useEditorStore.getState().selectedId).toBe("LM-0002");
    const map = await useEditorStore.getState().applyDraft("LM-0002", { kind: "apriltag", props: { code: 7 } });
    expect(map).toEqual({ tempId: "LM-0002", id: "LM-0020" });
    expect(useEditorStore.getState().features["LM-0020"].props.code).toBe(7);
    expect(useEditorStore.getState().unsavedCount()).toBe(0);
    expect(useEditorStore.getState().selectedId).toBe("LM-0020");
    expect(api.createFeature).toHaveBeenCalledWith("ds1", expect.objectContaining({ layer: "LM", deck_id: "D3", geometry: { type: "Point", coordinates: [84, -6.2, 11.8] } }));
  });

  it("update and remove", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().updateFeature("LM-0001", { props: { code: 9 } });
    expect(useEditorStore.getState().features["LM-0001"].props.code).toBe(9);
    useEditorStore.getState().select("LM-0001");
    await useEditorStore.getState().removeFeature("LM-0001");
    expect(useEditorStore.getState().features["LM-0001"]).toBeUndefined();
    expect(useEditorStore.getState().selectedId).toBeNull();
  });

  it("discardDraft removes the draft and clears selection", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().addDraft({ tempId: "LM-0002", layer: "LM", x: 84, y: -6.2, z: 11.8, deck: "D3" });
    useEditorStore.getState().discardDraft("LM-0002");
    expect(useEditorStore.getState().unsavedCount()).toBe(0);
    expect(useEditorStore.getState().selectedId).toBeNull();
  });

  it("savePose merges the response", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().savePose({ tide_m: 1.2 });
    expect(useEditorStore.getState().pose?.tide_m).toBe(1.2);
    expect(api.putPose).toHaveBeenCalledWith("ds1", { tide_m: 1.2 });
  });

  it("writes refresh the dataset version", async () => {
    await useEditorStore.getState().load("ds1");
    expect(useEditorStore.getState().dataset?.version).toBe(3);
    vi.mocked(api.getDataset).mockResolvedValueOnce({ id: "ds1", name: "d", version: 4, ap_lat: 0, ap_lon: 0, heading_deg: 0, lpp_m: 120 });
    await useEditorStore.getState().updateFeature("LM-0001", { props: { code: 9 } });
    expect(useEditorStore.getState().dataset?.version).toBe(4);
  });
});
