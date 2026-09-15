using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    /// Stands in for the React shell while there is no web/ yet. Play mode only.
    public class BridgeStubWindow : EditorWindow
    {
        [MenuItem("ShipHdMap/Bridge Stub")] static void Open() => GetWindow<BridgeStubWindow>("Bridge Stub");

        readonly List<string> _log = new(); MapRuntime _rt; float _sr = 0.2f, _st = 1f, _sa = 2f;

        MapRuntime Runtime()
        {
            if (_rt != null) return _rt;
            _rt = FindFirstObjectByType<MapRuntime>();
            if (_rt != null) _rt.Emit += (n, j) => { _log.Insert(0, $"{n}: {(j.Length > 160 ? j.Substring(0, 160) + "…" : j)}"); if (_log.Count > 20) _log.RemoveAt(20); Repaint(); };
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
            }
            using (new EditorGUILayout.HorizontalScope())
                foreach (var d in new[] { "D1", "D2", "D3", "all" }) if (GUILayout.Button(d)) rt.SetDeck(d);
            EditorGUI.BeginChangeCheck();
            _sr = EditorGUILayout.Slider("sigma r (m)", _sr, 0, 1); _st = EditorGUILayout.Slider("sigma theta (deg)", _st, 0, 5); _sa = EditorGUILayout.Slider("sigma alpha (deg)", _sa, 0, 10);
            if (EditorGUI.EndChangeCheck()) rt.SetNoise($"{{\"sigma_r\":{_sr},\"sigma_theta\":{_st},\"sigma_alpha\":{_sa},\"sigma_gps\":0.5}}");
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            foreach (var l in _log) EditorGUILayout.LabelField(l, EditorStyles.miniLabel);
        }

        public static string FixturePath() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"));
    }
}
