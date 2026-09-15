import { useCallback, useEffect, useRef } from "react";
import { useUnityContext } from "react-unity-webgl";
import { api } from "../api/client";
import { useEditorStore } from "../store/editor";

export type BridgeName = "Load" | "SetMode" | "SetDeck" | "Select" | "Confirm" | "Delete" | "SetNoise" | "StartScenario" | "SetPose";

const URLS = { loaderUrl: "/unity/Build/unity.loader.js", dataUrl: "/unity/Build/unity.data", frameworkUrl: "/unity/Build/unity.framework.js", codeUrl: "/unity/Build/unity.wasm" };

export function useShipUnity() {
  const { unityProvider, isLoaded, sendMessage, addEventListener, removeEventListener } = useUnityContext(URLS);
  const { datasetId, features, deckFilter, selectedId, mode, addDraft, select, setLocalization } = useEditorStore();
  const loadedOnce = useRef(false);

  const send = useCallback((name: BridgeName, payload?: string | object) => {
    if (!isLoaded) return;
    sendMessage("Map", name, payload === undefined ? "" : typeof payload === "string" ? payload : JSON.stringify(payload));
  }, [isLoaded, sendMessage]);

  // Unity -> store
  useEffect(() => {
    const onCreated = (json: string) => addDraft(JSON.parse(json));
    const onSelected = (json: string) => select(JSON.parse(json).id ?? null);
    const onLoc = (json: string) => setLocalization(JSON.parse(json));
    addEventListener("onFeatureCreated", onCreated); addEventListener("onSelected", onSelected); addEventListener("onLocalization", onLoc);
    return () => { removeEventListener("onFeatureCreated", onCreated); removeEventListener("onSelected", onSelected); removeEventListener("onLocalization", onLoc); };
  }, [addEventListener, removeEventListener, addDraft, select, setLocalization]);

  // initial Load: the vehicle-map body is exactly the Load payload (spec §10)
  useEffect(() => {
    if (!isLoaded || loadedOnce.current || Object.keys(features).length === 0) return;
    loadedOnce.current = true;
    fetch(api.vehicleMapUrl(datasetId)).then((r) => r.text()).then((json) => { send("Load", json); send("SetMode", mode); send("SetDeck", deckFilter); });
  }, [isLoaded, features, datasetId, mode, deckFilter, send]);

  useEffect(() => { if (loadedOnce.current) send("SetDeck", deckFilter); }, [deckFilter, send]);
  useEffect(() => { if (loadedOnce.current) send("SetMode", mode); }, [mode, send]);
  useEffect(() => { if (loadedOnce.current && selectedId) send("Select", selectedId); }, [selectedId, send]);

  return { unityProvider, isLoaded, send };
}
