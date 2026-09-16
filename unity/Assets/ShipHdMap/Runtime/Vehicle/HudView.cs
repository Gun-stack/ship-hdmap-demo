using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShipHdMap
{
    /// UI Toolkit HUD: bottom-left status box (deck, selection, cursor in Ship Frame, localization while driving) and
    /// floating deck labels projected from world points. Every element ignores picking so scene clicks pass through.
    public class HudView : MonoBehaviour
    {
        public Camera cam;
        UIDocument _doc; Label _info; VisualElement _labelLayer;
        readonly List<(Label label, Vector3 world)> _deckLabels = new();
        LocalizerResult _last; Pose2D _truth; string _frame = "SHIP_AP", _deck = "all", _selected;

        public void Set(LocalizerResult r, Pose2D truth, string frame) { _last = r; _truth = truth; _frame = frame; }
        public void SetContext(string deck, string selected) { _deck = deck ?? "all"; _selected = selected; }

        public void SetDeckLabels(IEnumerable<(string text, Vector3 world)> items)
        {
            foreach (var (label, _) in _deckLabels) label.RemoveFromHierarchy();
            _deckLabels.Clear();
            if (_labelLayer == null) return;
            foreach (var (text, world) in items)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                Style(l.style, 12); l.style.position = Position.Absolute;
                _labelLayer.Add(l); _deckLabels.Add((l, world));
            }
        }

        public static string[] Lines(string deck, string selected, (double x, double y, double z)? cursor, LocalizerResult r, Pose2D truth, string frame)
        {
            const double R2D = 180 / Math.PI;
            var lines = new List<string>
            {
                $"deck {deck}   sel {selected ?? "-"}",
                cursor.HasValue ? $"cursor x {cursor.Value.x,7:F2}  y {cursor.Value.y,7:F2}  z {cursor.Value.z,6:F2}" : "cursor -",
            };
            if (r != null)
            {
                var e = r.pose; double err = Math.Sqrt((e.x - truth.x) * (e.x - truth.x) + (e.y - truth.y) * (e.y - truth.y));
                lines.Add($"frame {frame}   N {r.nObs}   RMS {r.residualRms:F3}   iter {r.iterations}{(r.ok ? "" : "   (holding previous)")}");
                lines.Add($"est  x {e.x,7:F2}  y {e.y,7:F2}  psi {e.psiRad * R2D,7:F1}");
                lines.Add($"true x {truth.x,7:F2}  y {truth.y,7:F2}  psi {truth.psiRad * R2D,7:F1}");
                lines.Add($"err  {err:F2} m   {ShipFrame.WrapDeg((e.psiRad - truth.psiRad) * R2D):F1} deg");
            }
            return lines.ToArray();
        }

        void OnEnable()
        {
            var ps = Resources.Load<PanelSettings>("HudPanelSettings");
            if (ps == null) { Debug.LogWarning("HudView: Resources/HudPanelSettings missing — run menu ShipHdMap/Create HUD Assets"); enabled = false; return; }
            _doc = GetComponent<UIDocument>(); if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = ps;
            var root = _doc.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer.style.position = Position.Absolute; _labelLayer.style.left = 0; _labelLayer.style.top = 0; _labelLayer.style.right = 0; _labelLayer.style.bottom = 0;
            root.Add(_labelLayer);
            _info = new Label { pickingMode = PickingMode.Ignore };
            Style(_info.style, 12); _info.style.position = Position.Absolute; _info.style.left = 10; _info.style.bottom = 10; _info.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_info);
        }

        static void Style(IStyle s, int fontSize)
        {
            s.backgroundColor = new Color(0, 0, 0, 0.55f); s.color = Color.white; s.fontSize = fontSize;
            s.paddingLeft = 6; s.paddingRight = 6; s.paddingTop = 3; s.paddingBottom = 3;
        }

        void LateUpdate()
        {
            if (_info == null) return;
            _info.text = string.Join("\n", Lines(_deck, _selected, CursorShip(), _last, _truth, _frame));
            if (cam == null) return;
            var panel = _doc.rootVisualElement.panel;
            foreach (var (label, world) in _deckLabels)
            {
                var vp = cam.WorldToViewportPoint(world);
                label.visible = vp.z > 0;
                if (!label.visible) continue;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
                label.style.left = p.x; label.style.top = p.y;
            }
        }

        (double x, double y, double z)? CursorShip()
        {
            if (cam == null || !Application.isPlaying) return null;
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f, LandmarkPlacer.StructureMask())) return null;
            return ShipFrame.ToShip(hit.point);
        }
    }
}
