import { describe, expect, it } from "vitest";
import { editorStateMessages } from "./useShipUnity";
import { useUiStore } from "../store/ui";
import { useEditorStore } from "../store/editor";

describe("editorStateMessages", () => {
  const ui = { tool: "place" as const, cam: "fly" as const };
  const ed = {
    coverageParams: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40, sigma_r: 0.3, sigma_theta: 2, sigma_alpha: 3, grid_m: 1 },
    sigmaGps: 0.6, timeScale: 4, occluded: ["LM-0001"],
  };

  it("replays every piece of state Unity forgets on a Load", () => {
    // If one of these ever drops off the list the symptom is silent: the scene simply behaves as if
    // the user had never set it, and only after a reload.
    expect(editorStateMessages(ui, ed).map(([name]) => name)).toEqual(
      ["SetTool", "SetCamMode", "SetSensor", "SetNoise", "SetTimeScale", "SetOccluded"],
    );
  });

  it("wraps each payload the way its Unity message class reads it", () => {
    // SetTimeScaleMsg has `scale` and SetOccludedMsg has `ids`, but SetNoiseMsg IS the noise object.
    // Send a bare number or a bare array and Newtonsoft leaves the field at its default, in silence.
    expect(Object.fromEntries(editorStateMessages(ui, ed))).toEqual({
      SetTool: { tool: "place" },
      SetCamMode: { mode: "fly" },
      SetSensor: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40 },
      SetNoise: { sigma_r: 0.3, sigma_theta: 2, sigma_alpha: 3, sigma_gps: 0.6 },
      SetTimeScale: { scale: 4 },
      SetOccluded: { ids: ["LM-0001"] },
    });
  });

  it("sends the sensor Unity will actually run on, not the panel's spare fields", () => {
    // grid_m and the sigmas share coverageParams with the three geometry names; SetSensorMsg has no field
    // for them. This is the reload path, where a wrong shape is hardest to notice.
    expect(Object.fromEntries(editorStateMessages(ui, ed)).SetSensor).not.toHaveProperty("grid_m");
    expect(Object.fromEntries(editorStateMessages(ui, ed)).SetSensor).not.toHaveProperty("sigma_r");
  });

  it("accepts the live stores' own defaults, so the first replay after a reload is well formed", () => {
    // The two stores are the real arguments; this is what catches a field being renamed on one side only.
    const msgs = Object.fromEntries(editorStateMessages(useUiStore.getState(), useEditorStore.getState()));
    expect(msgs.SetTool).toEqual({ tool: "select" });
    expect(msgs.SetCamMode).toEqual({ mode: "orbit" });
    expect(msgs.SetOccluded).toEqual({ ids: [] });
    expect(msgs.SetTimeScale).toEqual({ scale: 1 });
    expect(msgs.SetSensor).toEqual({ fov_deg: 90, max_dist_m: 25, max_view_angle_deg: 70 });
    expect(msgs.SetNoise).toEqual({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2, sigma_gps: 0.5 });
  });
});
