import { describe, expect, it } from "vitest";
import { editorStateMessages } from "./useShipUnity";
import { useUiStore } from "../store/ui";
import { useEditorStore } from "../store/editor";

describe("editorStateMessages", () => {
  const ui = { tool: "place" as const, cam: "fly" as const };
  const ed = { noise: { sigma_r: 0.3, sigma_theta: 2, sigma_alpha: 3, sigma_gps: 0.6 }, timeScale: 4, occluded: ["LM-0001"] };

  it("replays every piece of state Unity forgets on a Load", () => {
    // If one of these ever drops off the list the symptom is silent: the scene simply behaves as if
    // the user had never set it, and only after a reload.
    expect(editorStateMessages(ui, ed).map(([name]) => name)).toEqual(["SetTool", "SetCamMode", "SetNoise", "SetTimeScale", "SetOccluded"]);
  });

  it("wraps each payload the way its Unity message class reads it", () => {
    // SetTimeScaleMsg has `scale` and SetOccludedMsg has `ids`, but SetNoiseMsg IS the noise object.
    // Send a bare number or a bare array and Newtonsoft leaves the field at its default, in silence.
    expect(Object.fromEntries(editorStateMessages(ui, ed))).toEqual({
      SetTool: { tool: "place" },
      SetCamMode: { mode: "fly" },
      SetNoise: ed.noise,
      SetTimeScale: { scale: 4 },
      SetOccluded: { ids: ["LM-0001"] },
    });
  });

  it("accepts the live stores' own defaults, so the first replay after a reload is well formed", () => {
    // The two stores are the real arguments; this is what catches a field being renamed on one side only.
    const msgs = Object.fromEntries(editorStateMessages(useUiStore.getState(), useEditorStore.getState()));
    expect(msgs.SetTool).toEqual({ tool: "select" });
    expect(msgs.SetCamMode).toEqual({ mode: "orbit" });
    expect(msgs.SetOccluded).toEqual({ ids: [] });
    expect(msgs.SetTimeScale).toEqual({ scale: 1 });
  });
});
