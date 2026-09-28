using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShipHdMap
{
    /// How a HUD row is coloured. The row's WORDS never change with it -- the web, the docs and the capture script
    /// quote those strings -- only the ink does.
    public enum HudTone { Text, Dim, Accent, Warn }

    /// UI Toolkit HUD: bottom-left status card (deck, selection, cursor in Ship Frame, localization while driving) and
    /// floating deck labels projected from world points. Every element ignores picking so scene clicks pass through.
    public class HudView : MonoBehaviour
    {
        public Camera cam;
        UIDocument _doc; VisualElement _card, _labelLayer; string _ramp;
        readonly List<Label> _rows = new();
        string _flash; float _flashUntil;

        /// What tool and camera are live, and what the visible eye currently sees. Public so the editor's
        /// Bridge Stub window can show the same two lines without recomputing them from a second source.
        public string StatusText { get; private set; }
        public string SensorConfigText { get; private set; }
        public string SensorText { get; private set; }
        readonly List<(Label label, Vector3 local)> _deckLabels = new();
        LocalizerResult _last; Pose2D _truth; string _frame = "SHIP_AP", _deck = "all", _selected;

        public void Set(LocalizerResult r, Pose2D truth, string frame) { _last = r; _truth = truth; _frame = frame; }
        public void SetContext(string deck, string selected) { _deck = deck ?? "all"; _selected = selected; }
        public void SetRamp(string line) { _ramp = line; }
        public void SetStatus(string line) { StatusText = line; }
        /// Named SetSensorCounts, never SetSensor: GameObject.SendMessage("Map", "SetSensor", json) invokes
        /// EVERY component on the Map root whose method is named SetSensor, not just one -- and MapRuntime.SetSensor
        /// is the intended target for that wire message. A method here called SetSensor would silently also fire
        /// on every web SetSensor call, clobbering this line with the raw sensor-geometry JSON (M6 found this live).
        /// Do not rename this back to SetSensor.
        public void SetSensorCounts(string line) { SensorText = line; }
        public void SetSensorConfig(string line) { SensorConfigText = line; }
        /// Never name this SetHud: that is MapRuntime's wire message, and SendMessage would fire both (see SetSensorCounts).
        public void SetVisible(bool on) { if (_doc) _doc.rootVisualElement.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        /// A click that did nothing has to say so. LandmarkPlacer.Decide returns ClickAct.None when Place or
        /// Probe misses the ship entirely, and the cursor readout keeps updating from a DIFFERENT raycast --
        /// so a swallowed click reads as a frozen app rather than a miss (M5e review 6.2).
        public void Flash(string msg, float seconds = 2.5f) { _flash = msg; _flashUntil = Time.unscaledTime + seconds; }

        /// Which tool and which camera, in the HUD's own words.
        ///
        /// ASCII only, and not by oversight: the HUD draws with JetBrains Mono (Resources/Fonts),
        /// which carries no Hangul, so Korean here would come out as blank boxes in the WebGL build. The web
        /// toolbar is where the Korean labels live; these are the same three tools and three cameras.
        ///
        /// `probeActive` is why this is not just `cam`: the probe parks the camera in Driver mode without
        /// telling the web (MapRuntime.PollCamMode explains why it must not), so the toolbar keeps showing
        /// 궤도 while the operator is standing at a virtual viewpoint. This line is the one place that admits it.
        public static string StatusLine(PlacerTool tool, CamMode cam, bool probeActive)
        {
            string t = tool == PlacerTool.Place ? "place" : tool == PlacerTool.Probe ? "probe" : "select";
            string c = probeActive ? "probe-eye" : cam == CamMode.Fly ? "fly" : cam == CamMode.Driver ? "driver" : "orbit";
            string hint = probeActive ? "   [left/right] turn the eye"
                : tool == PlacerTool.Probe ? "   click a deck to stand there"
                : cam == CamMode.Fly ? "   [WASD] move  [QE] up/down  [shift] faster" : "";
            return $"tool {t}   cam {c}{hint}";
        }

        /// What the eye is SET TO, as opposed to what it found. It sits directly above SensorLine so the counts
        /// are read next to the numbers that produced them -- "seen 1 / 23" means nothing without "fov 55".
        ///
        /// ASCII only, for the reason StatusLine gives. "0.#" rather than "F0" because the coverage sliders step
        /// in whole units today but nothing stops a future one from landing on 55.5, and a rounded line that
        /// disagrees with the panel beside it would be worse than no line.
        public static string SensorConfigLine(double fovDeg, double maxDist, double maxViewAngleDeg)
            => $"sensor  fov {fovDeg:0.#}  range {maxDist:0.#} m  view {maxViewAngleDeg:0.#}";

        /// How many mapped markers this eye actually sees, and why the rest are dark.
        ///
        /// The cone and the magenta rings answer "which ones"; nothing on screen answers "how many", and
        /// counting by eye is always a lower bound because the camera's field of view (about 60 deg) is
        /// narrower than the sensor's (90). VisibleFrom already carries every verdict -- this just adds them up.
        public static string SensorLine(IEnumerable<(string id, Miss miss)> why)
        {
            if (why == null) return null;
            var n = new int[Enum.GetValues(typeof(Miss)).Length];   // sized off the enum so a new Miss cannot overflow it
            int total = 0;
            foreach (var (_, m) in why) { n[(int)m]++; total++; }
            return $"seen {n[(int)Miss.None]} / {total}   fov {n[(int)Miss.Fov]}  range {n[(int)Miss.Range]}  facing {n[(int)Miss.Facing]}  hidden {n[(int)Miss.Occluded]}  blocked {n[(int)Miss.Blocked]}";
        }

        /// items carry root-local Unity points (Ship Frame); LateUpdate converts each to world via the Map root before projecting.
        public void SetDeckLabels(IEnumerable<(string text, Vector3 local)> items)
        {
            foreach (var (label, _) in _deckLabels) label.RemoveFromHierarchy();
            _deckLabels.Clear();
            if (_labelLayer == null) return;
            foreach (var (text, local) in items)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                Style(l.style, 12); l.style.position = Position.Absolute;
                _labelLayer.Add(l); _deckLabels.Add((l, local));
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
            // Monospace so the columns in est/true/err line up; the NL cut has no ligatures, so "--" stays two dashes.
            var font = Resources.Load<Font>("Fonts/JetBrainsMonoNL-Regular");
            if (font) root.style.unityFontDefinition = FontDefinition.FromFont(font);
            _card = new VisualElement { pickingMode = PickingMode.Ignore };
            Card(_card.style, 0.82f);
            _card.style.position = Position.Absolute; _card.style.left = 10; _card.style.bottom = 10;
            _card.style.paddingLeft = 10; _card.style.paddingRight = 10; _card.style.paddingTop = 6; _card.style.paddingBottom = 7;
            root.Add(_card);
        }

        /// The card's rows, in the order the old single label printed them, each with its ink and its group. A new
        /// group starts a hairline, so the four things the HUD says -- where, how well, what the scene is doing,
        /// what the eye sees -- read as four blocks instead of one wall of monospace.
        public static List<(string text, HudTone tone, int group)> Rows(string[] lines, string ramp, string status, string sensorConfig, string sensorCounts, string flash)
        {
            var rows = new List<(string, HudTone, int)>();
            foreach (var l in lines ?? Array.Empty<string>())
            {
                if (l.StartsWith("cursor")) rows.Add((l, HudTone.Dim, 0));
                else if (l.StartsWith("frame")) rows.Add((l, l.Contains("(holding previous)") ? HudTone.Warn : HudTone.Accent, 1));
                else if (l.StartsWith("est") || l.StartsWith("true") || l.StartsWith("err")) rows.Add((l, HudTone.Text, 1));
                else rows.Add((l, HudTone.Text, 0));
            }
            if (ramp != null) rows.Add((ramp, HudTone.Dim, 2));
            if (status != null) rows.Add((status, HudTone.Dim, 2));
            if (sensorConfig != null) rows.Add((sensorConfig, HudTone.Accent, 3));
            if (sensorCounts != null) rows.Add((sensorCounts, HudTone.Accent, 3));
            if (flash != null) rows.Add((flash, HudTone.Warn, 4));
            return rows;
        }

        static Color Ink(HudTone t) => Palette.C(t switch { HudTone.Dim => Palette.HudDim, HudTone.Accent => Palette.HudAccent, HudTone.Warn => Palette.HudWarn, _ => Palette.HudText });

        static void Card(IStyle s, float alpha)
        {
            s.backgroundColor = Palette.C(Palette.HudPanel, alpha); s.color = Palette.C(Palette.HudText);
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 6;
            var edge = new Color(1, 1, 1, 0.09f);
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = edge;
        }

        static void Style(IStyle s, int fontSize)
        {
            Card(s, 0.78f); s.fontSize = fontSize;
            s.paddingLeft = 7; s.paddingRight = 7; s.paddingTop = 2; s.paddingBottom = 3;
        }

        void LateUpdate()
        {
            if (_card == null) return;
            if (_flash != null && Time.unscaledTime > _flashUntil) _flash = null;
            var rows = Rows(Lines(_deck, _selected, CursorShip(), _last, _truth, _frame), _ramp, StatusText, SensorConfigText, SensorText, _flash);
            while (_rows.Count < rows.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.style.fontSize = 12; l.style.paddingLeft = l.style.paddingRight = 0; l.style.paddingTop = l.style.paddingBottom = 0;
                l.style.borderTopColor = new Color(1, 1, 1, 0.08f);
                _card.Add(l); _rows.Add(l);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var l = _rows[i]; bool on = i < rows.Count;
                l.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                var (text, tone, group) = rows[i];
                bool opens = i > 0 && group != rows[i - 1].group;
                l.text = text; l.style.color = Ink(tone);
                l.style.unityFontStyleAndWeight = tone == HudTone.Warn ? FontStyle.Bold : FontStyle.Normal;
                l.style.marginTop = opens ? 4 : 0; l.style.paddingTop = opens ? 4 : 0; l.style.borderTopWidth = opens ? 1 : 0;
            }
            if (cam == null) return;
            var panel = _doc.rootVisualElement.panel;
            foreach (var (label, local) in _deckLabels)
            {
                var world = transform.TransformPoint(local);
                var vp = cam.WorldToViewportPoint(world);
                label.visible = vp.z > 0;
                if (!label.visible) continue;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
                // kept on the canvas: the aft deck corner projects left of the view in the default orbit, and the
                // label's text (the deck name) is exactly the part that would be cut off
                label.style.left = Mathf.Max(4f, p.x); label.style.top = p.y;
            }
        }

        (double x, double y, double z)? CursorShip()
        {
            if (cam == null || !Application.isPlaying) return null;
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f, LandmarkPlacer.StructureMask())) return null;
            return ShipFrame.ToShip(transform.InverseTransformPoint(hit.point));
        }
    }
}
