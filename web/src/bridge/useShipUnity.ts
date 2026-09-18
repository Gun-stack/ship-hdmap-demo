import { useCallback, useEffect, useRef } from "react";
import { useUnityContext } from "react-unity-webgl";
import { api } from "../api/client";
import { scenarioLine, useEditorStore, type NoiseParams } from "../store/editor";
import { useUiStore, type CamMode, type Tool } from "../store/ui";
import { pickDeck } from "../geo/deck";

export type BridgeName = "Load" | "SetMode" | "SetDeck" | "Select" | "Confirm" | "Delete" | "SetNoise" | "StartScenario" | "SetPose" | "SetTimeScale" | "SetPrediction" | "SetOccluded" | "SetBeliefParams" | "SetTool" | "SetCamMode" | "SetNormal";

/**
 * Everything Unity forgets on a fresh Load or a page reload, as the exact messages to replay.
 * Pure and exported so a test can pin the names and the payload SHAPES without a DOM harness --
 * `scale` and `ids` are wrapped in objects and `noise` is not, and getting one of those wrong
 * is silent: sendMessage takes any JSON and Unity's parser leaves the field at its default.
 */
export function editorStateMessages(
  ui: { tool: Tool; cam: CamMode },
  ed: { noise: NoiseParams; timeScale: number; occluded: string[] },
): [BridgeName, object][] {
  return [
    ["SetTool", { tool: ui.tool }],
    ["SetCamMode", { mode: ui.cam }],
    ["SetNoise", ed.noise],
    ["SetTimeScale", { scale: ed.timeScale }],
    ["SetOccluded", { ids: ed.occluded }],
  ];
}

const URLS = { loaderUrl: "/unity/Build/unity.loader.js", dataUrl: "/unity/Build/unity.data", frameworkUrl: "/unity/Build/unity.framework.js", codeUrl: "/unity/Build/unity.wasm" };

export function useShipUnity() {
  const { unityProvider, isLoaded, sendMessage, addEventListener, removeEventListener } = useUnityContext(URLS);
  const { datasetId, dataset, deckFilter, selectedId, mode, pose, ramp, addDraft, select, setLocalization, moveFeature, onSlotFilled, appendLog, setBelief } = useEditorStore();
  const loadedOnce = useRef(false);
  const loading = useRef(false);
  const fromScene = useRef<string | null>(null);

  const send = useCallback((name: BridgeName, payload?: string | object) => {
    if (!isLoaded) return;
    sendMessage("Map", name, payload === undefined ? "" : typeof payload === "string" ? payload : JSON.stringify(payload));
  }, [isLoaded, sendMessage]);

  /** Pose: hull tilt and sink, quay height, ramp angle (spec §4.1, §4.6); the ramp angle comes from the API's /ramps response the store already holds. */
  const sendPose = useCallback(() => {
    const { pose, ramp, dataset } = useEditorStore.getState();
    if (!pose) return;
    send("SetPose", { draft_fwd_m: pose.draft_fwd_m ?? 8.1, draft_aft_m: pose.draft_aft_m ?? 8.6, heel_deg: pose.heel_deg ?? 0, lpp_m: dataset?.lpp_m ?? 120,
      tide_m: pose.tide_m ?? 0, quay_z_m: pose.quay_z_m ?? 3.5,
      ...(ramp ? { ramp: { id: ramp.id, angle_deg: ramp.angle_deg, state: ramp.state } } : {}) });
  }, [send]);

  /** Everything Unity forgets on a fresh Load or a page reload: the tool, the camera, the sensor and the occlusion set. */
  const sendEditorState = useCallback(() => {
    for (const [name, payload] of editorStateMessages(useUiStore.getState(), useEditorStore.getState())) send(name, payload);
  }, [send]);

  /** Re-sends the whole vehicle-map so Unity rebuilds markers and the overlay (after slot generation or a failed move). */
  const reloadScene = useCallback(async () => {
    const r = await fetch(api.vehicleMapUrl(datasetId)); if (!r.ok) throw new Error("vehicle-map HTTP " + r.status);
    send("Load", await r.text()); send("SetDeck", deckFilter);
    // Load clears the scene selection; re-assert the store's
    send("Select", useEditorStore.getState().selectedId ?? "");
    sendEditorState();
  }, [datasetId, deckFilter, send, sendEditorState]);

  // Unity -> store
  useEffect(() => {
    const onCreated = (json: string) => addDraft(JSON.parse(json));
    const onSelected = (json: string) => { const id = JSON.parse(json).id ?? null; fromScene.current = id; select(id); };
    const onLoc = (json: string) => setLocalization(JSON.parse(json));
    const onBel = (json: string) => setBelief(JSON.parse(json));
    const onMoved = (json: string) => {
      void moveFeature(JSON.parse(json)).then(() => {
        // a normal is worth far more to coverage than a position (M5c/M5d): recompute so the heatmap
        // answers the gizmo drag that just happened
        const ed = useEditorStore.getState();
        const deck = pickDeck(ed.decks, ed.deckFilter)?.id;
        if (deck) void ed.runCoverage(deck);
      }).catch(async (e) => {
        useEditorStore.setState({ error: "move failed: " + (e as Error).message });
        try { await reloadScene(); } catch { /* the banner already says it failed */ }
      });
    };
    const onSlot = (json: string) => { void onSlotFilled(JSON.parse(json)); };
    const onScenario = (json: string) => appendLog(scenarioLine(JSON.parse(json)));
    // Unity moves the camera itself -- Focus on a web-side selection, the probe, leaving the probe, starting a
    // run -- and the toolbar would otherwise keep claiming the old mode. Worse, the fly keys are gated on the
    // store's `cam`, so flight silently stops working after a Focus. No echo guard is needed the way onSelected
    // needs `fromScene`: nothing sends SetCamMode from an effect keyed on `cam`, only the three explicit call
    // sites (the toolbar buttons, the shortcut, and the edit-mode correction, which converges).
    const onCam = (json: string) => { const m = JSON.parse(json).mode; if (m) useUiStore.getState().setCam(m as CamMode); };
    addEventListener("onFeatureCreated", onCreated); addEventListener("onSelected", onSelected); addEventListener("onLocalization", onLoc); addEventListener("onFeatureMoved", onMoved);
    addEventListener("onSlotFilled", onSlot); addEventListener("onScenario", onScenario); addEventListener("onBelief", onBel); addEventListener("onCamMode", onCam);
    return () => {
      removeEventListener("onFeatureCreated", onCreated); removeEventListener("onSelected", onSelected); removeEventListener("onLocalization", onLoc); removeEventListener("onFeatureMoved", onMoved);
      removeEventListener("onSlotFilled", onSlot); removeEventListener("onScenario", onScenario); removeEventListener("onBelief", onBel); removeEventListener("onCamMode", onCam);
    };
  }, [addEventListener, removeEventListener, addDraft, select, setLocalization, moveFeature, reloadScene, onSlotFilled, appendLog, setBelief]);

  // initial Load: the vehicle-map body is exactly the Load payload (spec §10)
  useEffect(() => {
    if (!isLoaded || loadedOnce.current || loading.current || dataset === null) return;
    loading.current = true;
    fetch(api.vehicleMapUrl(datasetId)).then((r) => { if (!r.ok) throw new Error("vehicle-map HTTP " + r.status); return r.text(); }).then((json) => {
      loadedOnce.current = true;
      send("Load", json); send("SetMode", mode); send("SetDeck", deckFilter); sendPose(); sendEditorState();
    }).catch((e) => useEditorStore.setState({ error: "vehicle-map load failed: " + (e as Error).message }))
      .finally(() => { loading.current = false; });
  }, [isLoaded, dataset, datasetId, mode, deckFilter, send, sendPose, sendEditorState]);

  useEffect(() => { if (loadedOnce.current) send("SetDeck", deckFilter); }, [deckFilter, send]);
  useEffect(() => { if (loadedOnce.current) sendPose(); }, [pose, ramp, dataset?.lpp_m, sendPose]);
  useEffect(() => { if (loadedOnce.current) send("SetMode", mode); }, [mode, send]);
  useEffect(() => {
    if (!loadedOnce.current) return;
    if (!selectedId) { fromScene.current = null; send("Select", ""); return; }
    if (fromScene.current === selectedId) { fromScene.current = null; return; } // this change came from the scene; do not echo
    fromScene.current = null;
    send("Select", selectedId);
  }, [selectedId, send]);

  return { unityProvider, isLoaded, send, reloadScene };
}
