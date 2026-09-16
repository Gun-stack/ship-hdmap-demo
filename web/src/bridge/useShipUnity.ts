import { useCallback, useEffect, useRef } from "react";
import { useUnityContext } from "react-unity-webgl";
import { api } from "../api/client";
import { useEditorStore } from "../store/editor";

export type BridgeName = "Load" | "SetMode" | "SetDeck" | "Select" | "Confirm" | "Delete" | "SetNoise" | "StartScenario" | "SetPose";

const URLS = { loaderUrl: "/unity/Build/unity.loader.js", dataUrl: "/unity/Build/unity.data", frameworkUrl: "/unity/Build/unity.framework.js", codeUrl: "/unity/Build/unity.wasm" };

export function useShipUnity() {
  const { unityProvider, isLoaded, sendMessage, addEventListener, removeEventListener } = useUnityContext(URLS);
  const { datasetId, dataset, deckFilter, selectedId, mode, addDraft, select, setLocalization, moveFeature } = useEditorStore();
  const loadedOnce = useRef(false);
  const loading = useRef(false);
  const fromScene = useRef<string | null>(null);

  const send = useCallback((name: BridgeName, payload?: string | object) => {
    if (!isLoaded) return;
    sendMessage("Map", name, payload === undefined ? "" : typeof payload === "string" ? payload : JSON.stringify(payload));
  }, [isLoaded, sendMessage]);

  /** Re-sends the whole vehicle-map so Unity rebuilds markers and the overlay (after slot generation or a failed move). */
  const reloadScene = useCallback(async () => {
    const r = await fetch(api.vehicleMapUrl(datasetId)); if (!r.ok) throw new Error("vehicle-map HTTP " + r.status);
    send("Load", await r.text()); send("SetDeck", deckFilter);
  }, [datasetId, deckFilter, send]);

  // Unity -> store
  useEffect(() => {
    const onCreated = (json: string) => addDraft(JSON.parse(json));
    const onSelected = (json: string) => { const id = JSON.parse(json).id ?? null; fromScene.current = id; select(id); };
    const onLoc = (json: string) => setLocalization(JSON.parse(json));
    const onMoved = (json: string) => {
      void moveFeature(JSON.parse(json)).catch(async (e) => {
        useEditorStore.setState({ error: "move failed: " + (e as Error).message });
        try { await reloadScene(); } catch { /* the banner already says it failed */ }
      });
    };
    addEventListener("onFeatureCreated", onCreated); addEventListener("onSelected", onSelected); addEventListener("onLocalization", onLoc); addEventListener("onFeatureMoved", onMoved);
    return () => { removeEventListener("onFeatureCreated", onCreated); removeEventListener("onSelected", onSelected); removeEventListener("onLocalization", onLoc); removeEventListener("onFeatureMoved", onMoved); };
  }, [addEventListener, removeEventListener, addDraft, select, setLocalization, moveFeature, reloadScene]);

  // initial Load: the vehicle-map body is exactly the Load payload (spec §10)
  useEffect(() => {
    if (!isLoaded || loadedOnce.current || loading.current || dataset === null) return;
    loading.current = true;
    fetch(api.vehicleMapUrl(datasetId)).then((r) => { if (!r.ok) throw new Error("vehicle-map HTTP " + r.status); return r.text(); }).then((json) => {
      loadedOnce.current = true;
      send("Load", json); send("SetMode", mode); send("SetDeck", deckFilter);
    }).catch((e) => useEditorStore.setState({ error: "vehicle-map load failed: " + (e as Error).message }))
      .finally(() => { loading.current = false; });
  }, [isLoaded, dataset, datasetId, mode, deckFilter, send]);

  useEffect(() => { if (loadedOnce.current) send("SetDeck", deckFilter); }, [deckFilter, send]);
  useEffect(() => { if (loadedOnce.current) send("SetMode", mode); }, [mode, send]);
  useEffect(() => {
    if (!loadedOnce.current || !selectedId) return;
    if (fromScene.current === selectedId) { fromScene.current = null; return; } // this change came from the scene; do not echo
    fromScene.current = null;
    send("Select", selectedId);
  }, [selectedId, send]);

  return { unityProvider, isLoaded, send, reloadScene };
}
