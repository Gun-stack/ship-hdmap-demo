using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    /// Stands in for the React shell: every message the web sends over the bridge, as a button. Play mode only.
    /// Since M5e the shell also owns the tool, the camera and the virtual viewpoint, so driving the scene from
    /// the editor without it meant typing JSON or editing inspector fields -- the rows below close that gap.
    public class BridgeStubWindow : EditorWindow
    {
        [MenuItem("ShipHdMap/Bridge Stub")] static void Open() => GetWindow<BridgeStubWindow>("Bridge Stub");

        readonly List<string> _log = new(); MapRuntime _rt; float _sr = 0.2f, _st = 1f, _sa = 2f; float _aft = 8.6f, _heel;

        MapRuntime Runtime()
        {
            if (_rt != null) return _rt;
            _rt = FindFirstObjectByType<MapRuntime>();
            if (_rt != null)
            {
                _rt.Emit += (n, j) => { _log.Insert(0, $"{n}: {(j.Length > 160 ? j.Substring(0, 160) + "…" : j)}"); if (_log.Count > 20) _log.RemoveAt(20); Repaint(); };
                _rt.RequestSeed();
            }
            return _rt;
        }

        void OnGUI()
        {
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play mode.", MessageType.Info); _rt = null; return; }
            var rt = Runtime(); if (rt == null) { EditorGUILayout.HelpBox("No MapRuntime in scene (GameObject 'Map').", MessageType.Warning); return; }
            if (GUILayout.Button("Load fixture")) rt.Load(File.ReadAllText(FixturePath()));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Mode edit")) rt.SetMode("edit");
                if (GUILayout.Button("Mode drive")) rt.SetMode("drive");
                if (GUILayout.Button("Start load scenario")) rt.StartScenario("{\"mode\":\"load\"}");
                if (GUILayout.Button("Start unload scenario")) rt.StartScenario("{\"mode\":\"unload\"}");
            }
            using (new EditorGUILayout.HorizontalScope())
                foreach (var k in new[] { 1, 5, 20 }) if (GUILayout.Button($"x{k}")) rt.SetTimeScale($"{{\"scale\":{k}}}");
            using (new EditorGUILayout.HorizontalScope())
                foreach (var d in new[] { "D1", "D2", "D3", "all" }) if (GUILayout.Button(d)) rt.SetDeck(d);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("tool", GUILayout.Width(30));
                foreach (var t in new[] { "select", "place", "probe" }) if (GUILayout.Button(t)) rt.SetTool($"{{\"tool\":\"{t}\"}}");
                EditorGUILayout.LabelField("cam", GUILayout.Width(28));
                foreach (var c in new[] { "orbit", "fly", "driver" }) if (GUILayout.Button(c)) rt.SetCamMode($"{{\"mode\":\"{c}\"}}");
            }
            // Read off the HUD instead of recomputed here. "What is live" kept being the thing two copies
            // disagreed about in M5e; this window would have been the second copy.
            if (rt.Hud)
            {
                EditorGUILayout.LabelField(rt.Hud.StatusText ?? "tool -   cam -", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(rt.Hud.SensorText ?? "seen -   (drive, or drop a probe)", EditorStyles.miniLabel);
            }
            EditorGUI.BeginChangeCheck();
            _sr = EditorGUILayout.Slider("sigma r (m)", _sr, 0, 1); _st = EditorGUILayout.Slider("sigma theta (deg)", _st, 0, 5); _sa = EditorGUILayout.Slider("sigma alpha (deg)", _sa, 0, 10);
            if (EditorGUI.EndChangeCheck()) rt.SetNoise($"{{\"sigma_r\":{_sr},\"sigma_theta\":{_st},\"sigma_alpha\":{_sa},\"sigma_gps\":0.5}}");
            EditorGUI.BeginChangeCheck();
            _aft = EditorGUILayout.Slider("draft aft (m)", _aft, 6, 10); _heel = EditorGUILayout.Slider("heel (deg)", _heel, -3, 3);
            if (EditorGUI.EndChangeCheck()) rt.SetPose($"{{\"draft_fwd_m\":8.1,\"draft_aft_m\":{_aft},\"heel_deg\":{_heel},\"lpp_m\":120}}");
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            foreach (var l in _log) EditorGUILayout.LabelField(l, EditorStyles.miniLabel);
        }

        /// The two rows above are live values; without this the window only redraws on mouse-over.
        void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

        public static string FixturePath() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"));
    }
}
