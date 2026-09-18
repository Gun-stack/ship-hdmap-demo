using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ShipHdMap
{
    /// GameObject "Map". React calls these via react-unity-webgl sendMessage("Map", name, json). Outgoing events go through Emit.
    public class MapRuntime : MonoBehaviour
    {
        public ShipParams shipParams = new();
        public Camera cam;
        public event Action<string, string> Emit;

        public Transform LandmarksRoot { get; private set; }
        public Dictionary<string, LandmarkRef> MapRefs { get; } = new();
        public VehicleMap CurrentMap { get; private set; }
        public LandmarkSensor Sensor { get; private set; }
        public VehicleController Vehicle { get; private set; }
        public LandmarkPlacer Placer { get; private set; }
        public HudView Hud { get; private set; }
        public GameObject Ship { get; private set; }
        public GameObject Quay { get; private set; }
        public OrbitCamera Orbit { get; set; }
        public SensorView View { get; private set; }
        public NormalGizmo Gizmo { get; private set; }
        public ProbeView Probe { get; private set; }

        public LandmarkMarker MarkerOf(string id) => _markers.TryGetValue(id, out var m) ? m : null;
        public Vector3 MarkerPos(string id) => _markers[id].transform.position;

        string _mode = "edit"; string _selected; Pose2D? _prev; LocalizerResult _lastRes; float _emitTimer; SeedData _seed;
        GameObject _overlay; string _deck = "all"; SetPoseMsg _pose;
        string _shipSignature;
        readonly Dictionary<string, LandmarkMarker> _markers = new();
        readonly HashSet<string> _seen = new();

        public BeliefMonitor Belief { get; private set; } = new BeliefMonitor(new BeliefParams());
        double _lastS;   // arc length at the previous tick, to measure how far we moved
        string _retriedSlot;   // the slot we have already given one more go

        public enum Phase { Idle, OnQuay, OnRamp, OnLane, Parking, Departing, RampDown, QuayOut }
        public Phase ScenarioPhase { get; private set; } = Phase.Idle;
        public string TargetSlotId => _target?.id;
        string _scenarioMode = "load"; ParkingSlot _target; Lane _targetLane; Deck _targetDeck; double _exitS;
        const double R2D = 180 / Math.PI;
        double _trimDeg;   // set by ApplyPose; RampEndsInQuay needs it to match SetRampAngle's ship-local rotation

        void Awake()
        {
            InitForTest();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false; // let the React side panels receive keystrokes
#endif
            if (!Application.isPlaying) return;
            Ship = GameObject.Find("Ship");
            // Seed-built hull so the scene is not empty before the first Load; Load replaces it with the map's own hull.
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, transform); }
            AttachShip();
            Placer.decks = _seed?.decks ?? new List<Deck>();
            if (cam == null) cam = Camera.main;
            if (cam)
            {
                Placer.cam = cam; Hud.cam = cam; Gizmo.cam = cam;
                Orbit = cam.GetComponent<OrbitCamera>(); if (!Orbit) { Orbit = cam.gameObject.AddComponent<OrbitCamera>(); Orbit.AdoptCurrentPose(); }
                Probe.orbit = Orbit; Gizmo.orbit = Orbit;     // after Orbit exists, or both get null
            }
            Placer.Created += lm => { var m = lm.ToModel(); _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(m);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = m.position[0], y = m.position[1], z = m.position[2], deck = lm.deckId, mounted_on = lm.mountedOn, normal = m.normal })); };
            Placer.Selected += lm => { Highlight(lm.id); Send(BridgeMessages.OnSelected, "{\"id\":\"" + lm.id + "\"}"); };
            Placer.Moved += OnMarkerMoved;
            Placer.Cleared += () => { Highlight(null); Send(BridgeMessages.OnSelected, "{\"id\":null}"); };
            Placer.ProbeAt += hit => { var d = CurrentMap?.decks?.Find(x => x.id == Placer.DeckIdForHeight(LandmarksRoot.InverseTransformPoint(hit.point).y)); Probe.PlaceAt(hit.point, d?.z_surface ?? 0); };
            Gizmo.Rotated += OnMarkerMoved;   // the same path a drag-move takes: onFeatureMoved carries `normal`
            if (_seed != null && Emit != null) Send(BridgeMessages.OnSeedReady, MapJson.Serialize(_seed));
        }

        /// Serializes and emits the seed on demand; nothing subscribes to Emit yet when Awake runs
        /// (the bridge stub window subscribes after Play mode starts), so this lets a late subscriber ask for it.
        public void RequestSeed() { if (_seed != null) Send(BridgeMessages.OnSeedReady, MapJson.Serialize(_seed)); }

        /// Creates child objects without touching the scene ship or camera; used by Awake and by EditMode tests.
        public void InitForTest()
        {
            if (LandmarksRoot != null) return;
            LandmarksRoot = new GameObject("Landmarks").transform; LandmarksRoot.SetParent(transform, false);
            Sensor = new GameObject("Vehicle").AddComponent<LandmarkSensor>(); Sensor.transform.SetParent(transform, false);
            Sensor.occluders = LayerMask.GetMask("ShipStructure"); if (Sensor.occluders == 0) Sensor.occluders = ~LayerMask.GetMask("Landmark");
            Vehicle = Sensor.gameObject.AddComponent<VehicleController>();
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(Sensor.transform, false);
            body.transform.localScale = new Vector3(4.8f, 1.5f, 1.85f); body.transform.localPosition = new Vector3(0, 0.25f, 0);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            Placer = gameObject.AddComponent<LandmarkPlacer>(); Placer.landmarksRoot = LandmarksRoot;
            Hud = gameObject.AddComponent<HudView>();
            // On the MAP ROOT, not on Vehicle: EnsureLine sets useWorldSpace = false, so the cone's points are
            // read as the parent's local space. VehicleController.Apply() overwrites Vehicle's transform every
            // frame (and the quay leg unparents it entirely), which would apply the vehicle pose a second time.
            View = gameObject.AddComponent<SensorView>();
            Gizmo = gameObject.AddComponent<NormalGizmo>(); Gizmo.root = LandmarksRoot;
            Probe = gameObject.AddComponent<ProbeView>(); Probe.sensor = Sensor; Probe.view = View; Probe.root = transform;
            Quay = QuayBuilder.Build();   // world space: the Quay Frame, never a child of this root
        }

        /// Rebuild only when the map's ship-defining parts actually change: a slot regeneration reloads the whole map
        /// and rebuilding thousands of primitives every time would stall the browser.
        /// Includes each deck's own outline extent (not just z_surface/z_clear): two ships can share deck heights
        /// while differing only in length/beam, and the hull must still rebuild when the outline is what changed.
        /// Public only so an EditMode test can reach it: its one call site sits behind Application.isPlaying.
        public static string ShipSignature(VehicleMap m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in m.decks ?? new List<Deck>())
            {
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                foreach (var pt in d.outline ?? Array.Empty<double[]>())
                {
                    if (pt[0] < x0) x0 = pt[0]; if (pt[0] > x1) x1 = pt[0];
                    if (pt[1] < y0) y0 = pt[1]; if (pt[1] > y1) y1 = pt[1];
                }
                if (x1 < x0) { x0 = x1 = y0 = y1 = 0; } // no outline
                sb.Append(d.id).Append(':').Append(d.z_surface).Append(':').Append(d.z_clear).Append(':').Append(x1 - x0).Append(':').Append(y1 - y0).Append('|');
            }
            sb.Append('#').Append((m.facilities ?? new List<Facility>()).Count).Append('#').Append((m.lashing_points ?? new List<LashingPoint>()).Count);
            foreach (var r in m.ramps ?? new List<Ramp>()) sb.Append('#').Append(r.id).Append(':').Append(r.length_m).Append(':').Append(r.hinge[0][2]);
            return sb.ToString();
        }

        /// ShipMeshBuilder.Prim keeps its per-colour Materials only in a build-local dictionary, and SetDeckVisibility
        /// (called right after every rebuild) clones one more per renderer ("~inst") -- so after a rebuild every
        /// renderer under Ship, not just the ones on a filtered deck, owns its own Material instance. None of that
        /// is reachable once Ship is destroyed, and Materials are not reclaimed by GC until a domain reload -- costly
        /// on the WebGL target, where a single deck can carry thousands of lashing-socket renderers. Nothing outside
        /// the hull shares these: the quay, overlay fills, parked-car boxes and landmark markers each own their own
        /// Materials (QuayBuilder._mat, MapOverlay.LineMats/FillMats/_parkedMat, LandmarkMarker's per-code tag
        /// texture; its halo/seen rings are shared statics the same way MapOverlay's line and fill colours are), and every
        /// ShipMeshBuilder.Build call starts a fresh dictionary, so no two builds ever share a Material instance.
        public static void DestroyShipMaterials(GameObject ship)
        {
            var mats = new HashSet<Material>();
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial) mats.Add(r.sharedMaterial);
            foreach (var m in mats) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        }

        // ---- incoming (React -> Unity) ----
        public void Load(string json)
        {
            ScenarioPhase = Phase.Idle; Vehicle.running = false; _target = null; _retriedSlot = null; Vehicle.gameObject.SetActive(false);
            Gizmo.Detach(); Probe.Clear(); View.Hide();
            if (Vehicle.transform.parent != transform) Vehicle.transform.SetParent(transform, false);
            CurrentMap = MapJson.Parse<VehicleMap>(json);
            foreach (var m in _markers.Values) if (m) DestroyImmediate(m.gameObject);
            _selected = null; _markers.Clear(); MapRefs.Clear(); Placer.All.Clear();
            foreach (var lm in CurrentMap.landmarks ?? new List<Landmark>())
            {
                var mk = LandmarkMarker.Spawn(LandmarksRoot, lm.id, lm.marker.code, ShipFrame.ToUnity(lm.position[0], lm.position[1], lm.position[2]),
                    ShipFrame.ToUnity(lm.normal[0], lm.normal[1], lm.normal[2]), (float)lm.size_m, lm.deck_id, lm.mounted_on);
                _markers[lm.id] = mk; MapRefs[lm.id] = RefOf(lm); Placer.All.Add(mk);
            }
            Placer.decks = CurrentMap.decks ?? Placer.decks;
            Placer.nextCode = _markers.Count + 1;
            Placer.nextId = _markers.Count + 1;

            // free fill meshes before the overlay itself: SlotFill.OnDestroy does not run in EditMode (no [ExecuteAlways])
            if (_overlay) { foreach (var fill in _overlay.GetComponentsInChildren<SlotFill>(true)) { var m = fill.GetComponent<MeshFilter>()?.sharedMesh; if (m) DestroyImmediate(m); } DestroyImmediate(_overlay); }
            _overlay = MapOverlay.Build(CurrentMap, transform); MapOverlay.SetDeck(_overlay, _deck);
            foreach (var slot in CurrentMap.parking_slots ?? new List<ParkingSlot>())
            {
                if (!ScenarioPlanner.IsFilled(slot.status) || slot.target_pose == null) continue;
                var deck = CurrentMap.decks?.Find(d => d.id == slot.deck_id); if (deck == null) continue;
                MapOverlay.SpawnParked(_overlay, slot, new Pose2D { x = slot.target_pose.x, y = slot.target_pose.y, psiRad = slot.target_pose.heading_deg / R2D }, deck.z_surface);
            }
            foreach (var m in _markers.Values) if (m) m.gameObject.SetActive(_deck == "all" || m.deckId == _deck);

            var labels = new List<(string, Vector3)>();
            foreach (var d in CurrentMap.decks ?? new List<Deck>()) labels.Add(($"{d.id}  z {d.z_surface:F1} m", ShipFrame.ToUnity(4, 10, d.z_surface + 1.5)));
            Hud.SetDeckLabels(labels);
            Hud.SetContext(_deck, null);

            var sig = ShipSignature(CurrentMap);
            if (Application.isPlaying && sig != _shipSignature)
            {
                if (Ship) { DestroyShipMaterials(Ship); Destroy(Ship); }
                Ship = ShipMeshBuilder.Build(CurrentMap, transform);
                _shipSignature = sig;
                ShipMeshBuilder.SetDeckVisibility(Ship, _deck);
            }
            ApplyPose();
        }

        public void SetMode(string mode)
        {
            _mode = mode; Placer.enabledForInput = mode == "edit";
            if (mode == "edit")
            {
                ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false; Vehicle.gameObject.SetActive(false);
                if (Vehicle.transform.parent != transform) Vehicle.transform.SetParent(transform, false);
                Time.timeScale = 1f; if (Orbit) Orbit.follow = null;
            }
        }

        public void SetTimeScale(string json) => Time.timeScale = Mathf.Clamp((float)MapJson.Parse<SetTimeScaleMsg>(json).scale, 0.1f, 50f);
        public void SetDeck(string deck)
        {
            _deck = deck; if (Ship) ShipMeshBuilder.SetDeckVisibility(Ship, deck); if (_overlay) MapOverlay.SetDeck(_overlay, deck);
            foreach (var m in _markers.Values) if (m) m.gameObject.SetActive(deck == "all" || m.deckId == deck);
            Hud.SetContext(_deck, _selected);
        }
        /// Web-originated selection: highlight and bring the camera to it (edit mode only; drive keeps following the vehicle).
        public void Select(string id)
        {
            Highlight(id);
            if (_selected != null && Orbit && _mode != "drive") Orbit.Focus(_markers[_selected].transform.position, 12f);
        }

        /// Scene-side selection: at most one halo. Does not emit and does not move the camera —
        /// a scene click's own drag raycast runs in the same frame, so focusing here would move the marker under the cursor.
        public void Highlight(string id)
        {
            if (string.IsNullOrEmpty(id)) id = null;
            if (_selected != null && _markers.TryGetValue(_selected, out var prev) && prev) prev.SetHighlighted(false);
            _selected = id != null && _markers.ContainsKey(id) ? id : null;
            if (_selected != null) _markers[_selected].SetHighlighted(true);
            if (_overlay) MapOverlay.Highlight(_overlay, _selected == null ? id : null);   // a slot id highlights its fill; a marker id or null clears fills
            Hud.SetContext(_deck, _selected ?? id);
            if (_selected != null && _mode == "edit") Gizmo.Attach(_markers[_selected]); else Gizmo.Detach();
        }

        /// Re-keys both the marker and its LandmarkRef under the confirmed id. The LandmarkRef's own id field has
        /// to move with the key: Localizer.Observe stamps every Observation.id from that field, and Localizer.Solve
        /// looks the observation back up in this same map by id -- leaving the struct's old id behind after a
        /// re-key would make every future sighting of this landmark fail that lookup and get silently dropped.
        public void Confirm(string json) { var c = MapJson.Parse<ConfirmMsg>(json); if (_markers.TryGetValue(c.tempId, out var m)) { _markers.Remove(c.tempId); m.id = c.id; m.name = c.id; _markers[c.id] = m; if (MapRefs.TryGetValue(c.tempId, out var r)) { r.id = c.id; MapRefs[c.id] = r; MapRefs.Remove(c.tempId); } if (_selected == c.tempId) _selected = c.id; } }
        /// Ship Frame is the Map root's local space; pose rotates this root (M5a) and drops it by the aft draft (M5b).
        /// Children keep their local coordinates either way.
        public void SetPose(string json) { _pose = MapJson.Parse<SetPoseMsg>(json); ApplyPose(); }

        /// Trim > 0 (stern deeper) raises the bow (+x); heel > 0 lowers starboard (Unity +z). Rotation about the AP origin.
        public static Quaternion PoseRotation(double trimDeg, double heelDeg) => Quaternion.Euler((float)heelDeg, 0, (float)trimDeg);

        public void ApplyPose()
        {
            if (_pose == null) return;
            double trimDeg = _trimDeg = Math.Atan2(_pose.draft_aft_m - _pose.draft_fwd_m, _pose.lpp_m <= 0 ? 120 : _pose.lpp_m) * 180 / Math.PI;
            transform.localRotation = PoseRotation(trimDeg, _pose.heel_deg);
            transform.localPosition = new Vector3(0, (float)(-_pose.draft_aft_m), 0);   // waterline is world y = 0; the AP origin sits one aft draft below it
            // Stop the slab at the ramp's foot, not at the AP -- see QuayBuilder.Place. No ramp in the map means
            // nothing to bury and no quay leg to drive, so the historical edge (the AP) stands.
            QuayBuilder.Place(Quay, _pose.quay_z_m + _pose.tide_m, RampEndsInQuay().foot?[0] ?? 0);
            AttachShip();
            if (Ship && _pose.ramp != null) ShipMeshBuilder.SetRampAngle(Ship, _pose.ramp.angle_deg, trimDeg);
            Hud.SetRamp(_pose.ramp == null ? null : $"ramp {_pose.ramp.angle_deg:F1} deg  {_pose.ramp.state}");
            Physics.SyncTransforms();   // autoSyncTransforms is off; placement/drag raycasts must see the tilted colliders
        }

        /// The Demo scene may carry a pre-built "Ship" at the scene root; it must ride on the Map root to follow the pose.
        public void AttachShip()
        {
            if (!Ship) Ship = GameObject.Find("Ship");
            if (Ship && Ship.transform.parent != transform) Ship.transform.SetParent(transform, false);
        }

        /// Drag ended in the scene: refresh the localization map entry and tell the web, which persists it (PUT) — the scene never commits a move itself.
        public void OnMarkerMoved(LandmarkMarker lm)
        {
            var m = lm.ToModel(); MapRefs[lm.id] = RefOf(m);
            Send(BridgeMessages.OnFeatureMoved, MapJson.Serialize(new FeatureMovedEvt { id = lm.id, x = m.position[0], y = m.position[1], z = m.position[2], normal = m.normal, deck = lm.deckId, mounted_on = lm.mountedOn }));
        }

        public void SetNoise(string json)
        {
            var n = MapJson.Parse<SetNoiseMsg>(json);
            Sensor.noise.sigmaR = n.sigma_r; Sensor.noise.sigmaThetaRad = n.sigma_theta * Math.PI / 180; Sensor.noise.sigmaAlphaRad = n.sigma_alpha * Math.PI / 180;
            Sensor.noise.sigmaGps = n.sigma_gps;
        }

        /// Not persisted anywhere: damage is a fact about this moment, not about the map (spec §4).
        public void SetOccluded(string json)
        {
            var m = MapJson.Parse<SetOccludedMsg>(json);
            Sensor.occluded.Clear();
            if (m?.ids != null) foreach (var id in m.ids) Sensor.occluded.Add(id);
        }

        public void SetTool(string json)
        {
            var t = MapJson.Parse<SetToolMsg>(json)?.tool;
            Placer.tool = t == "place" ? PlacerTool.Place : t == "probe" ? PlacerTool.Probe : PlacerTool.Select;
            if (Placer.tool == PlacerTool.Probe) return;
            Probe.Clear();
            // The probe drove the camera to its own eye with no driverTarget; leaving the tool must give the
            // camera back, or the toolbar offers no way out of a first-person view of nothing.
            if (Orbit && Orbit.mode == CamMode.Driver && Orbit.driverTarget == null) Orbit.mode = CamMode.Orbit;
        }

        public void SetCamMode(string json)
        {
            var m = MapJson.Parse<SetCamModeMsg>(json)?.mode;
            if (Orbit == null) return;                 // EditMode and the pre-camera frames have no orbit yet
            Orbit.mode = m == "fly" ? CamMode.Fly : m == "driver" ? CamMode.Driver : CamMode.Orbit;
            Orbit.driverTarget = Orbit.mode == CamMode.Driver ? Vehicle.transform : null;
            Orbit.follow = Orbit.mode == CamMode.Orbit && _mode == "drive" ? Vehicle.transform : null;
            if (Orbit.mode != CamMode.Driver) View.Hide();
        }

        /// The web edited the normal (gizmo release or the property form) and already persisted it.
        /// Turning the quad here keeps MapRefs -- and therefore the next drive -- in step with the DB.
        public void SetNormal(string json)
        {
            var m = MapJson.Parse<SetNormalMsg>(json);
            if (m?.id == null || m.normal == null || m.normal.Length < 3 || !_markers.TryGetValue(m.id, out var mk)) return;
            var n = LandmarksRoot.TransformDirection(ShipFrame.ToUnity(m.normal[0], m.normal[1], m.normal[2]));
            mk.MoveTo(mk.transform.position - mk.NormalUnity * 0.01f, n, mk.deckId, mk.mountedOn);
            MapRefs[m.id] = RefOf(mk.ToModel());
        }

        public PredictionGrid Prediction { get; private set; }

        /// The coverage map for the deck being driven. The web fetches it (Unity does not do HTTP) and pushes it
        /// once per scenario; Ship Frame is invariant, so it only goes stale when a marker is added or occluded.
        public void SetPrediction(string json)
        {
            var m = MapJson.Parse<SetPredictionMsg>(json);
            var cells = new List<(double, double, double?)>();
            if (m?.cells != null) foreach (var c in m.cells) cells.Add((c.x, c.y, c.s));
            Prediction = new PredictionGrid(m?.bbox, m?.grid_m ?? 1, cells);
        }

        public void SetBeliefParams(string json)
        {
            var m = MapJson.Parse<SetBeliefParamsMsg>(json) ?? new SetBeliefParamsMsg();
            Belief = new BeliefMonitor(new BeliefParams { k = m.k, frames = m.frames, driftRate = m.drift_rate,
                budgetM = m.budget_m, maxLostM = m.max_lost_m, trailM = m.trail_m });
        }

        public void StartScenario(string json)
        {
            var s = MapJson.Parse<StartScenarioMsg>(json);
            _scenarioMode = s?.mode == "unload" ? "unload" : "load";
            if (_pose?.ramp != null && _pose.ramp.state == "blocked") { Finish("ramp_blocked"); return; }
            SetMode("drive"); Vehicle.gameObject.SetActive(true);
            if (Orbit) { Orbit.follow = Vehicle.transform; Orbit.distance = 25f; Orbit.pitchDeg = 35f;
                // free flight follows nothing; driver's eye is a valid way to watch a run, so keep that one
                if (Orbit.mode != CamMode.Driver) Orbit.mode = CamMode.Orbit; }
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "start", mode = _scenarioMode }));
            NextVehicle();
        }

        /// Picks the next slot (spec §3.2/§3.3) and puts a vehicle on the lane start (load) or in the slot (unload). Finishes when none is left.
        /// Only slots whose access_lane_id/deck_id both resolve are candidates, so one bad reference skips that slot instead of ending the run.
        void NextVehicle()
        {
            _prev = null;   // a fresh vehicle must not seed Gauss-Newton with the previous car's pose
            _seen.Clear();  // stated, not accidental: the next car must not inherit the previous car's entrance-pair sighting
            var candidates = new List<ParkingSlot>();
            if (CurrentMap != null)
                foreach (var s in CurrentMap.parking_slots ?? new List<ParkingSlot>())
                {
                    var lane = CurrentMap.lanes?.Find(l => l.id == s.access_lane_id);
                    if (lane != null && lane.centerline != null && lane.centerline.Length >= 2 && CurrentMap.decks?.Find(d => d.id == s.deck_id) != null)
                        candidates.Add(s);
                }
            _target = ScenarioPlanner.NextSlot(candidates, _scenarioMode);
            _targetLane = _target == null ? null : CurrentMap.lanes.Find(l => l.id == _target.access_lane_id);
            _targetDeck = _target == null ? null : CurrentMap.decks.Find(d => d.id == _target.deck_id);
            if (_target == null) { Finish(_scenarioMode == "unload" ? "no_filled_slot" : "no_empty_slot"); return; }
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "target", slot_id = _target.id }));
            double z = _targetDeck.z_surface;
            if (_scenarioMode == "unload")
            {
                // A previous car's QuayOut run may have left this Vehicle unparented (Quay Frame); the departure
                // path below is in Ship Frame, so it needs to be back on the Map root first.
                if (Vehicle.transform.parent != transform) Vehicle.transform.SetParent(transform, false);
                MapOverlay.RemoveParked(_overlay, _target.id);
                Vehicle.StartPath(ScenarioPlanner.DeparturePath(_target.target_pose, _targetLane, z), ScenarioPlanner.ParkSpeedMps);
                Belief.Reset(); _lastS = Vehicle.s;
                ScenarioPhase = Phase.Departing;
            }
            else
            {
                _exitS = ScenarioPlanner.ExitS(_targetLane, _target.target_pose);
                var (hinge, foot) = RampEndsInQuay();
                // no ramp in the map: start on the lane as in M5a. Reparent first -- a *previous* vehicle's quay run
                // may have left this Vehicle unparented (Quay Frame), and a Ship-Frame lane path needs it back on the Map root.
                if (hinge == null)
                {
                    Vehicle.transform.SetParent(transform, false); Vehicle.StartLane(_targetLane);
                    Belief.Reset(); _lastS = Vehicle.s;
                    ScenarioPhase = Phase.OnLane; return;
                }
                Vehicle.transform.SetParent(null, true);                                   // Quay Frame
                // psiRad 0 is a placeholder, not a claim about which way the car actually faces at spawn: Gps() copies
                // the heading exactly, so dPsi = truth.psi - est.psi is 0 regardless of this value and it never reaches
                // the executed path. It would matter the moment heading noise is added to the GPS fix.
                var truth = new Pose2D { x = ScenarioPlanner.QuaySpawn[0], y = ScenarioPlanner.QuaySpawn[1], psiRad = 0 };
                var est = Sensor.Gps(truth);
                Vehicle.StartPath(ScenarioPlanner.ToTruthFrame(ScenarioPlanner.QuayPath(est, foot, hinge), est, truth), ScenarioPlanner.QuaySpeedMps);
                Belief.Reset(); _lastS = Vehicle.s;
                ScenarioPhase = Phase.OnQuay;
            }
        }

        void Finish(string reason)
        {
            ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false; Vehicle.gameObject.SetActive(false);
            if (Vehicle.transform.parent != transform) Vehicle.transform.SetParent(transform, false);
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "finished", mode = _scenarioMode, detail = reason }));
        }

        // ---- per frame ----
        void Update() => Step(Time.deltaTime);

        /// One simulation tick: move the vehicle, localize, emit, then run the scenario transitions. Tests call this directly.
        public void Step(float dt)
        {
            if (_mode != "drive" || !Vehicle.running) return;
            // While backtracking (or given up) the retreat drive in StepScenario is the only thing allowed to move
            // the vehicle -- letting Advance keep pushing it forward along the lane it was already on would outrun
            // the retreat step every frame (lane speed exceeds it) and the vehicle would never actually retrace.
            if (Belief.State != BeliefState.Backtracking && Belief.State != BeliefState.Stopped) Vehicle.Advance(dt);
            Localize();
            var truth = ShipTruth();
            // The belief only has markers to judge on the ship-frame legs; on the quay and the ramp the vehicle
            // localises from GPS, so nObs is 0 there and an unguarded monitor would go Lost every normal run.
            if (ScenarioPhase == Phase.OnLane || ScenarioPhase == Phase.Parking || ScenarioPhase == Phase.Departing)
            {
                double moved = Math.Abs(Vehicle.s - _lastS); _lastS = Vehicle.s;
                Belief.Step(_lastRes.ok ? _lastRes.sigmaXy : null, Prediction?.SigmaAt(truth.x, truth.y), Vehicle.s, moved);
            }
            else { Belief.Reset(); _lastS = Vehicle.s; }
            _emitTimer += dt;
            if (_emitTimer >= 0.2f)
            {
                _emitTimer = 0;
                Send(BridgeMessages.OnLocalization, MapJson.Serialize(new LocalizationEvt { est_x = _lastRes.pose.x, est_y = _lastRes.pose.y, est_psi = _lastRes.pose.psiRad * R2D,
                    true_x = truth.x, true_y = truth.y, true_psi = truth.psiRad * R2D, residual_rms = _lastRes.residualRms, n_obs = _lastRes.nObs, frame = "SHIP_AP" }));
                Send(BridgeMessages.OnBelief, MapJson.Serialize(new BeliefEvt { state = Belief.State.ToString().ToLowerInvariant(), n_obs = _lastRes.nObs,
                    sigma_xy = _lastRes.sigmaXy, sigma_psi = _lastRes.sigmaPsiDeg, predicted_sigma_xy = Prediction?.SigmaAt(truth.x, truth.y),
                    lost_m = Belief.LostM, sigma_odo = Belief.SigmaOdo, trail_m = Belief.TrailM }));
            }
            StepScenario(dt);
        }

        /// Sense → solve → HUD, factored out so a mid-frame Rewind (StepScenario's OnLane exit-overshoot correction) can
        /// re-localize without re-triggering the 0.2s onLocalization emit, which stays in Step.
        void Localize()
        {
            var truth = ShipTruth();
            var obs = Sensor.Sense(truth, MapRefs, id => _markers[id].transform.position);
            _seen.Clear(); foreach (var o in obs) _seen.Add(o.id);
            _lastRes = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (_lastRes.ok && double.IsFinite(_lastRes.pose.x) && double.IsFinite(_lastRes.pose.y) && double.IsFinite(_lastRes.pose.psiRad)) _prev = _lastRes.pose;
            Hud.Set(_lastRes, truth, "SHIP_AP");
            // Vehicle.Z, not _targetDeck.z_surface: the target deck is where the car is GOING, and on the quay
            // and the ramp it is nowhere near that height. Vehicle.Z is the current path point, right every frame.
            // (_targetDeck would never be null here either -- Step returns early unless a run is under way.)
            if (Orbit != null && Orbit.mode == CamMode.Driver)
            {
                Vector3 eye = Sensor.transform.position + Vector3.up * Sensor.eyeHeight;
                View.Show(truth, Vehicle.Z, Sensor.VisibleFrom(truth, eye, MapRefs, MarkerPos), MarkerOf, Sensor.fovDeg, Sensor.maxDist);
            }
        }

        void StepScenario(float dt)
        {
            // Backtracking pre-empts every phase: retracing the path we drove needs no steering decision, and
            // none could be trusted anyway.
            if (Belief.State == BeliefState.Backtracking)
            {
                var target = Belief.BacktrackTargetS;
                if (target.HasValue)
                {
                    double back = Math.Max(target.Value, Vehicle.s - ScenarioPlanner.ParkSpeedMps * 0.5 * dt);
                    Vehicle.Rewind(back); _lastS = Vehicle.s;
                    if (Vehicle.s <= target.Value + 1e-6) Belief.ReachedBacktrackTarget();
                }
                return;
            }
            if (Belief.State == BeliefState.Stopped)
            {
                if (_target == null) return;
                if (_retriedSlot != _target.id)
                {
                    _retriedSlot = _target.id;            // spec §3.7: one retry before giving up
                    Belief.Reset(); _lastS = 0;
                    Vehicle.Rewind(0);
                    return;
                }
                _target.status = "unreachable";
                MapOverlay.SetStatus(_overlay, _target.id, "unreachable");
                Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = "unreachable" }));
                NextVehicle();
                return;
            }

            switch (ScenarioPhase)
            {
                case Phase.OnQuay when SawEntrancePair():
                {
                    // The estimate at this instant is everything the vehicle knows about where the ship is; it decides
                    // how squarely the car arrives at the top of the ramp. Lane keeping re-centres it after that.
                    // _prev is only ever set from a FINITE Gauss-Newton solve (Localize() guards on IsFinite; _lastRes.ok
                    // alone does not -- it is true even on a diverging solve), and falling back to ShipTruth() here
                    // would leak ground truth into the vehicle's belief at the one moment this demo is about estimation error.
                    var est = _prev ?? ShipTruth();
                    var r = CurrentMap.ramps[0];
                    var hingeShip = new[] { (r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2] };
                    // Captured BEFORE the reparent: SetParent(..., true) preserves world pose, but once the parent flips,
                    // ShipTruth()'s shortcut (parent == transform -> return Vehicle.Truth) would return the stale
                    // Quay-frame Truth from this frame's Advance(), not the projected Ship-frame pose. Also carries the
                    // vehicle's actual current height, so the ramp path climbs from where it really is (see RampTopPath).
                    var (truth, truthZ) = ShipTruthPose();
                    Vehicle.transform.SetParent(transform, true);                       // Ship Frame, same world pose
                    Vehicle.StartPath(ScenarioPlanner.ToTruthFrame(ScenarioPlanner.RampTopPath(est, truthZ, hingeShip, _targetLane.centerline[0]), est, truth), ScenarioPlanner.ParkSpeedMps);
                    Belief.Reset(); _lastS = Vehicle.s;
                    ScenarioPhase = Phase.OnRamp;
                    Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "frame_switch",
                        detail = $"est x {est.x:F2} y {est.y:F2} psi {est.psiRad * R2D:F1}" }));
                    break;
                }
                case Phase.OnQuay when Vehicle.AtEnd:
                    Finish("no_frame_switch");
                    break;
                case Phase.OnRamp when Vehicle.AtEnd:
                    Vehicle.StartLane(_targetLane);
                    Belief.Reset(); _lastS = Vehicle.s;
                    ScenarioPhase = Phase.OnLane;
                    break;
                case Phase.OnLane when Vehicle.s >= _exitS || Vehicle.AtEnd:
                {
                    // Plan in the belief frame at the moment of leaving the lane, then execute open-loop in the true frame:
                    // the estimation error at this instant becomes the parking error.
                    // ponytail: open-loop from one estimate; closed-loop pure pursuit on every frame's estimate is the upgrade path.
                    // Land exactly on the exit point: one frame of travel at a high time scale would otherwise
                    // put the plan's origin metres past it, and that offset lands straight in the parking error.
                    if (!Vehicle.AtEnd && Vehicle.s > _exitS) { Vehicle.Rewind(_exitS); _lastS = Vehicle.s; Localize(); }
                    var est = _prev ?? Vehicle.Truth;
                    double z = _targetDeck.z_surface;
                    var path = ScenarioPlanner.ToTruthFrame(ScenarioPlanner.ApproachPath(est, _target.target_pose, z), est, Vehicle.Truth);
                    Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "leave_lane", slot_id = _target.id, detail = $"est x {est.x:F2} y {est.y:F2} psi {est.psiRad * R2D:F1}" }));
                    Vehicle.StartPath(path, ScenarioPlanner.ParkSpeedMps);
                    Belief.Reset(); _lastS = Vehicle.s;
                    ScenarioPhase = Phase.Parking;
                    break;
                }
                case Phase.Parking when Vehicle.AtEnd:
                {
                    var (status, lat, lon, hdg) = ScenarioPlanner.Judge(Vehicle.Truth, _target.target_pose, _target.tolerance);
                    _target.status = status; MapOverlay.SetStatus(_overlay, _target.id, status);
                    MapOverlay.SpawnParked(_overlay, _target, Vehicle.Truth, _targetDeck.z_surface);
                    Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = status, err_lat = lat, err_lon = lon, err_heading = hdg }));
                    NextVehicle();
                    break;
                }
                case Phase.Departing when Vehicle.AtEnd:
                {
                    _target.status = "empty"; MapOverlay.SetStatus(_overlay, _target.id, "empty");
                    Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = "empty" }));
                    var (hingeQ, footQ) = RampEndsInQuay();
                    if (hingeQ == null) { NextVehicle(); break; }
                    // Where the departure leg actually left it (the lane's first point), read BEFORE the reparent while
                    // Vehicle.Truth/Z are still Ship Frame: the quay-out leg starts there instead of at the hinge,
                    // which sits 2 m astern of it and used to teleport the car backwards on the handover.
                    var hereQ = InQuay(Vehicle.Truth.x, Vehicle.Truth.y, Vehicle.Z);
                    Vehicle.transform.SetParent(null, true);
                    Vehicle.StartPath(ScenarioPlanner.QuayOutPath(hereQ, hingeQ, footQ), ScenarioPlanner.ParkSpeedMps);
                    Belief.Reset(); _lastS = Vehicle.s;
                    ScenarioPhase = Phase.RampDown;
                    break;
                }
                // Tide and quay height decide whether the ramp climbs or descends from the hinge to the quay, so the
                // height match has to work either way -- it is not always a descent despite the phase's name.
                case Phase.RampDown when Math.Abs(Vehicle.Z - QuayBuilder.SurfaceZ(Quay)) <= 0.05:
                    ScenarioPhase = Phase.QuayOut;   // wheels are on the quay: GPS is what the vehicle has again
                    break;
                // The height match is a belief-frame cue, not the only way through: a hinge whose y is off-centre
                // (stern_quarter ramps need not sit on the centreline) lets heel shift the foot height by
                // hy*sin(heel) -- far past the 5 cm threshold -- so it must never be the sole gate on progress.
                case Phase.RampDown when Vehicle.AtEnd:
                    NextVehicle();
                    break;
                case Phase.QuayOut when Vehicle.AtEnd:
                    NextVehicle();
                    break;
            }
        }

        static LandmarkRef RefOf(Landmark lm) => new LandmarkRef { id = lm.id, mx = lm.position[0], my = lm.position[1], phiRad = Math.Atan2(lm.normal[1], lm.normal[0]) };

        /// Both entrance markers of the stern ramp in one frame (spec §3.2): the trigger for the frame switch.
        bool SawEntrancePair()
        {
            var ids = CurrentMap?.ramps != null && CurrentMap.ramps.Count > 0 ? CurrentMap.ramps[0].transition_landmarks : null;
            if (ids == null || ids.Count < 2) return false;
            foreach (var id in ids) if (!_seen.Contains(id)) return false;
            return true;
        }

        /// The sensor and the map live in Ship Frame. While the vehicle drives in the Quay Frame its TRUE pose is
        /// projected through the Map root's inverse so observations stay meaningful; the vehicle's own belief is GPS
        /// until the entrance pair is seen.
        /// Last accepted estimate (finite solve only). Exposed so a test can measure what the belief cost at a handover.
        public Pose2D? LastEstimate => _prev;

        public Pose2D ShipTruth() => ShipTruthPose().pose;

        /// Same projection as ShipTruth(), plus the height (dropped from Pose2D) -- used at the frame switch, which
        /// needs both to carry the vehicle onto the ramp at its real current position AND height (see RampTopPath).
        (Pose2D pose, double z) ShipTruthPose()
        {
            if (Vehicle.transform.parent == transform) return (Vehicle.Truth, Vehicle.Z);
            // VehicleController.Apply() rides the body RideHeightM above the path point along WORLD up while the
            // vehicle is unparented (Quay Frame, no relation to the Map root's own tilt); subtract that lever arm
            // before projecting, or heel rotates part of it into ship y (and trim into ship z) as a spurious offset.
            var worldPos = Vehicle.transform.position - Vector3.up * (float)VehicleController.RideHeightM;
            var (x, y, z) = ShipFrame.ToShip(transform.InverseTransformPoint(worldPos));
            // The car's nose points along its LOCAL +X (VehicleController.Apply/the body box), not Unity's default
            // +Z "forward" -- transform.right is the vector that matches ShipFrame.HeadingVector's convention.
            var fwd = transform.InverseTransformDirection(Vehicle.transform.right);
            return (new Pose2D { x = x, y = y, psiRad = Math.Atan2(-fwd.z, fwd.x) }, z);
        }

        /// Ramp hinge midpoint and free-end midpoint in the Quay Frame, from the map's ramp geometry and the pose's angle.
        public (double[] hinge, double[] foot) RampEndsInQuay()
        {
            var r = CurrentMap?.ramps != null && CurrentMap.ramps.Count > 0 ? CurrentMap.ramps[0] : null;
            if (r == null) return (null, null);
            double hx = (r.hinge[0][0] + r.hinge[1][0]) / 2, hy = (r.hinge[0][1] + r.hinge[1][1]) / 2, hz = r.hinge[0][2];
            // SetRampAngle applies angleDeg + trimDeg as the ship-LOCAL rotation (angleDeg alone is measured
            // against the horizon), so the free end must be computed with that same local angle here.
            double a = ((_pose?.ramp?.angle_deg ?? 0) + _trimDeg) * Math.PI / 180;
            return (InQuay(hx, hy, hz), InQuay(hx - r.length_m * Math.Cos(a), hy, hz + r.length_m * Math.Sin(a)));
        }

        double[] InQuay(double x, double y, double z)
        {
            var (qx, qy, qz) = ShipFrame.ToShip(transform.TransformPoint(ShipFrame.ToUnity(x, y, z)));
            return new[] { qx, qy, qz };
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void EmitToWeb(string name, string json);
#endif

        void Send(string name, string json)
        {
            Emit?.Invoke(name, json);
#if UNITY_WEBGL && !UNITY_EDITOR
            EmitToWeb(name, json);
#endif
        }

        /// Web deleted the feature in the DB first; remove the scene marker without emitting.
        public void Delete(string id)
        {
            if (!_markers.TryGetValue(id, out var m)) return;
            if (_selected == id) _selected = null;
            _markers.Remove(id); MapRefs.Remove(id); Placer.All.Remove(m);
            if (m) { if (Application.isPlaying) Destroy(m.gameObject); else DestroyImmediate(m.gameObject); }
        }

        public SeedData Seed => _seed;
        public IEnumerable<LandmarkMarker> Markers => _markers.Values;
    }
}
