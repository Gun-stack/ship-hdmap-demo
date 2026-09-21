// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from "vitest";
import { EDITOR_KEY, EDITOR_SCHEMA, EDITOR_WRITE_MS, useEditorStore, visibleFeatures } from "./editor";
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
    generateSlots: vi.fn(async (_ds: string, deck: string) => ({ deck, count: 2, utilization: 0.4, lashing_coverage: 1, version: 9, slots: [] })),
    putSlotStatus: vi.fn(async (_ds: string, id: string, status: string) => ({ id, status })),
  },
}));

describe("editor store", () => {
  beforeEach(() => { localStorage.clear(); useEditorStore.setState(useEditorStore.getInitialState()); });

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

  it("moveFeature PUTs geometry, deck and normal for a saved feature", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().moveFeature({ id: "LM-0001", x: 30, y: 6, z: 11.8, normal: [0, -1, 0], deck: "D3", mounted_on: "C-PILLAR-D3-009" });
    expect(api.updateFeature).toHaveBeenCalledWith("ds1", "LM-0001", { deck_id: "D3", geometry: { type: "Point", coordinates: [30, 6, 11.8] }, props: { code: 1, normal: [0, -1, 0], mounted_on: "C-PILLAR-D3-009" } });
    expect(useEditorStore.getState().features["LM-0001"].geometry.coordinates).toEqual([30, 6, 11.8]);
  });

  it("moveFeature on a draft only updates the draft", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().addDraft({ tempId: "LM-0002", layer: "LM", x: 84, y: -6.2, z: 11.8, deck: "D3", mounted_on: "C-PILLAR-D3-001" });
    vi.mocked(api.updateFeature).mockClear();
    await useEditorStore.getState().moveFeature({ id: "LM-0002", x: 85, y: -6.2, z: 11.8, normal: [0, 1, 0], deck: "D3", mounted_on: "C-PILLAR-D3-002" });
    expect(api.updateFeature).not.toHaveBeenCalled();
    const d = useEditorStore.getState().drafts["LM-0002"];
    expect(d.geometry.coordinates).toEqual([85, -6.2, 11.8]);
    expect(d.props.mounted_on).toBe("C-PILLAR-D3-002");
  });

  it("writes refresh the dataset version", async () => {
    await useEditorStore.getState().load("ds1");
    expect(useEditorStore.getState().dataset?.version).toBe(3);
    vi.mocked(api.getDataset).mockResolvedValueOnce({ id: "ds1", name: "d", version: 4, ap_lat: 0, ap_lon: 0, heading_deg: 0, lpp_m: 120 });
    await useEditorStore.getState().updateFeature("LM-0001", { props: { code: 9 } });
    expect(useEditorStore.getState().dataset?.version).toBe(4);
  });

  it("generateSlots refreshes features, version and KPI", async () => {
    await useEditorStore.getState().load("ds1");
    vi.mocked(api.listFeatures).mockResolvedValueOnce([{ id: "PS-D3-001", deck_id: "D3", layer: "B2", kind: "parking_slot", geometry: { type: "Polygon", coordinates: [[[100, 2, 10.6], [104.8, 2, 10.6], [104.8, 3.85, 10.6], [100, 3.85, 10.6], [100, 2, 10.6]]] }, props: {} }]);
    const out = await useEditorStore.getState().generateSlots("D3", { gap_lat_m: 0.3 });
    expect(api.generateSlots).toHaveBeenCalledWith("ds1", "D3", { gap_lat_m: 0.3 });
    expect(out.count).toBe(2);
    expect(useEditorStore.getState().dataset?.version).toBe(9);
    expect(useEditorStore.getState().slotGen.D3.lashing_coverage).toBe(1);
    expect(Object.keys(useEditorStore.getState().features)).toEqual(["PS-D3-001"]);
  });

  /// 완료 기준 1. 검증할 때마다 슬라이더를 다시 맞추던 것이 이 마일스톤에서 없어진다.
  it("갑판·모드·슬라이더·가림 집합이 새로고침을 넘긴다", async () => {
    const s = () => useEditorStore.getState();
    s().setDeckFilter("D2");
    s().setMode("drive");
    s().setCoverageParams({ max_dist_m: 31 });
    s().setBeliefParams({ k: 4.5 });
    s().setSigmaGps(0.44);
    s().setTimeScale(20);
    s().toggleOccluded("LM-0003");

    // 쓰기는 디바운스된다(아래 EDITOR_WRITE_MS) — 붙잡기 전에 실제로 나갈 때까지 기다린다
    await new Promise((r) => setTimeout(r, EDITOR_WRITE_MS + 80));
    // persist 는 *모든* setState 를 storage 에 쓴다. 그래서 메모리를 비우는 그 동작이 저장본까지
    // 기본값으로 덮어쓴다 — 붙잡았다가 되돌려 놓지 않으면 어떤 구현으로도 통과할 수 없는 테스트가 된다.
    const saved = localStorage.getItem(EDITOR_KEY)!;
    useEditorStore.setState(useEditorStore.getInitialState());   // 새로고침 흉내: 메모리를 비운다
    expect(s().deckFilter).toBe("all");
    localStorage.setItem(EDITOR_KEY, saved);
    await useEditorStore.persist.rehydrate();

    expect(s().deckFilter).toBe("D2");
    expect(s().mode).toBe("drive");
    expect(s().coverageParams.max_dist_m).toBe(31);
    expect(s().beliefParams.k).toBe(4.5);
    expect(s().sigmaGps).toBe(0.44);
    expect(s().timeScale).toBe(20);
    expect(s().occluded).toEqual(["LM-0003"]);
  });

  /// 지도 데이터는 저장하지 않는다 — 서버가 진실이고, 낡은 사본이 되살아나면 버전 표시가 거짓말을 한다.
  it("지도 데이터는 저장하지 않는다", async () => {
    await useEditorStore.getState().load("ds1");
    await new Promise((r) => setTimeout(r, EDITOR_WRITE_MS + 80));
    const saved = JSON.parse(localStorage.getItem(EDITOR_KEY)!).state;
    expect(saved.features).toBeUndefined();
    expect(saved.dataset).toBeUndefined();
    expect(saved.coverage).toBeUndefined();
    expect(saved.selectedId).toBeUndefined();
  });

  /// partialize 는 타입도 컴파일러도 지켜 주지 않는다 — 지도 데이터가 한 번 새면 낡은 사본이
  /// 오늘의 dataset 버전 옆에 되살아난다. 키 집합 자체를 못박는다.
  it("저장하는 키가 정확히 이것뿐이다", async () => {
    useEditorStore.getState().setDeckFilter("D2");
    await new Promise((r) => setTimeout(r, EDITOR_WRITE_MS + 80));
    expect(Object.keys(JSON.parse(localStorage.getItem(EDITOR_KEY)!).state).sort()).toEqual(
      ["beliefParams", "coverageMode", "coverageParams", "deckFilter", "mode", "occluded", "sigmaGps", "timeScale"],
    );
  });
});

import { scenarioLine, slotFilledLine } from "./editor";

describe("scenario log and slot status", () => {
  beforeEach(() => useEditorStore.setState(useEditorStore.getInitialState()));

  it("onSlotFilled PUTs the status, logs a line and refreshes the version", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-012", status: "filled", err_lat: 0.04, err_lon: -0.11, err_heading: 0.6 });
    expect(api.putSlotStatus).toHaveBeenCalledWith("ds1", "PS-D3-012", "filled");
    const s = useEditorStore.getState();
    expect(s.scenarioLog[0].text).toBe("PS-D3-012 filled  lat +0.04 lon -0.11 hdg +0.6°");
    expect(s.scenarioLog[0].t).toMatch(/^\d\d:\d\d:\d\d$/);
    expect(s.error).toBeNull();
  });

  it("unload lines carry no errors and a failed PUT shows the banner", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-012", status: "empty" });
    expect(useEditorStore.getState().scenarioLog[0].text).toBe("PS-D3-012 empty");
    vi.mocked(api.putSlotStatus).mockRejectedValueOnce(new Error("boom"));
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-013", status: "filled", err_lat: 0, err_lon: 0, err_heading: 0 });
    expect(useEditorStore.getState().error).toContain("boom");
  });

  it("log keeps the newest 100 lines, newest first, and clearLog empties it", () => {
    for (let i = 0; i < 105; i++) useEditorStore.getState().appendLog("line " + i);
    const log = useEditorStore.getState().scenarioLog;
    expect(log).toHaveLength(100);
    expect(log[0].text).toBe("line 104");
    expect(log[99].text).toBe("line 5");
    useEditorStore.getState().clearLog();
    expect(useEditorStore.getState().scenarioLog).toHaveLength(0);
  });

  it("formats scenario events", () => {
    expect(scenarioLine({ event: "start", mode: "load" })).toBe("▶ 선적 시작");
    expect(scenarioLine({ event: "start", mode: "unload" })).toBe("◀ 하역 시작");
    expect(scenarioLine({ event: "target", slot_id: "PS-D3-001" })).toBe("대상 PS-D3-001");
    expect(scenarioLine({ event: "leave_lane", slot_id: "PS-D3-001", detail: "est x 97.48 y 0.02 psi 0.1" })).toBe("차로 이탈 · est x 97.48 y 0.02 psi 0.1");
    expect(scenarioLine({ event: "finished", detail: "no_empty_slot" })).toBe("종료 (no_empty_slot)");
    expect(slotFilledLine({ slot_id: "PS-D3-002", status: "needs_adjust", err_lat: -0.2, err_lon: 0.05, err_heading: -2.5 })).toBe("PS-D3-002 needs_adjust  lat -0.20 lon +0.05 hdg -2.5°");
  });

  it("frame_switch 를 사람이 읽는 줄로 만든다", () => {
    expect(scenarioLine({ event: "frame_switch", detail: "est x 1.20 y -0.30 psi 0.4" })).toBe("프레임 전환 · est x 1.20 y -0.30 psi 0.4");
    expect(scenarioLine({ event: "finished", detail: "ramp_blocked" })).toBe("종료 (ramp_blocked)");
  });
});

import { noiseMsg, sensorMsg } from "./editor";

describe("bridge payload assembly", () => {
  const s = {
    coverageParams: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40, sigma_r: 0.4, sigma_theta: 2, sigma_alpha: 3, grid_m: 2 },
    sigmaGps: 0.7,
  };

  it("SetNoise takes its three sigmas from coverageParams and sigma_gps from the store", () => {
    expect(noiseMsg(s)).toEqual({ sigma_r: 0.4, sigma_theta: 2, sigma_alpha: 3, sigma_gps: 0.7 });
  });

  it("SetSensor carries the API's three names and nothing else", () => {
    // grid_m and the sigmas must not ride along: SetSensorMsg has no field for them and Newtonsoft drops
    // unknown keys without a word, so a spread would look fine and quietly send a shape nobody reads.
    expect(sensorMsg(s)).toEqual({ fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40 });
  });
});

describe("persisted schema", () => {
  it("drops a v1 blob instead of restoring it without sigmaGps", async () => {
    // A v1 blob has `noise` and no `sigmaGps`. Restoring it would leave sigmaGps undefined, and the first
    // noiseMsg() would put `sigma_gps: undefined` on the wire -- Newtonsoft then leaves the field at
    // SetNoiseMsg's default and the quay leg silently drives on a sigma nobody chose.
    localStorage.setItem(EDITOR_KEY, JSON.stringify({ version: 1, state: { noise: { sigma_r: 9, sigma_theta: 9, sigma_alpha: 9, sigma_gps: 9 }, deckFilter: "D2" } }));
    await useEditorStore.persist.rehydrate();
    expect(useEditorStore.getState().sigmaGps).toBe(0.5);
    expect(useEditorStore.getState().deckFilter).not.toBe("D2");
  });

  it("is on version 2", () => { expect(EDITOR_SCHEMA).toBe(2); });
});
