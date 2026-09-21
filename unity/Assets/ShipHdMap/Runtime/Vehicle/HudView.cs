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
        UIDocument _doc; Label _info; VisualElement _labelLayer; string _ramp;
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

        /// A click that did nothing has to say so. LandmarkPlacer.Decide returns ClickAct.None when Place or
        /// Probe misses the ship entirely, and the cursor readout keeps updating from a DIFFERENT raycast --
        /// so a swallowed click reads as a frozen app rather than a miss (M5e review 6.2).
        public void Flash(string msg, float seconds = 2.5f) { _flash = msg; _flashUntil = Time.unscaledTime + seconds; }

        /// Which tool and which camera, in the HUD's own words.
        ///
        /// ASCII only, and not by oversight: the runtime panel draws with UI Toolkit's default theme font,
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
            _info = new Label { pickingMode = PickingMode.Ignore };
            Style(_info.style, 12); _info.style.position = Position.Absolute; _info.style.left = 10; _info.style.bottom = 10; _info.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_info);
        }

        static string Extra(string line) => line == null ? "" : "\n" + line;

        static void Style(IStyle s, int fontSize)
        {
            s.backgroundColor = new Color(0, 0, 0, 0.55f); s.color = Color.white; s.fontSize = fontSize;
            s.paddingLeft = 6; s.paddingRight = 6; s.paddingTop = 3; s.paddingBottom = 3;
        }

        void LateUpdate()
        {
            if (_info == null) return;
            if (_flash != null && Time.unscaledTime > _flashUntil) _flash = null;
            _info.text = string.Join("\n", Lines(_deck, _selected, CursorShip(), _last, _truth, _frame))
                + Extra(_ramp) + Extra(StatusText) + Extra(SensorConfigText) + Extra(SensorText) + Extra(_flash);
            if (cam == null) return;
            var panel = _doc.rootVisualElement.panel;
            foreach (var (label, local) in _deckLabels)
            {
                var world = transform.TransformPoint(local);
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
            return ShipFrame.ToShip(transform.InverseTransformPoint(hit.point));
        }
    }
}
