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

        string _mode = "edit"; string _selected; Pose2D? _prev; LocalizerResult _lastRes; float _emitTimer; SeedData _seed;
        GameObject _overlay; string _deck = "all"; SetPoseMsg _pose;
        readonly Dictionary<string, LandmarkMarker> _markers = new();

        public enum Phase { Idle, OnLane, Parking, Departing }
        public Phase ScenarioPhase { get; private set; } = Phase.Idle;
        public string TargetSlotId => _target?.id;
        string _scenarioMode = "load"; ParkingSlot _target; Lane _targetLane; Deck _targetDeck; double _exitS;
        const double R2D = 180 / Math.PI;

        void Awake()
        {
            InitForTest();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false; // let the React side panels receive keystrokes
#endif
            if (!Application.isPlaying) return;
            Ship = GameObject.Find("Ship");
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, shipParams, transform); }
            AttachShip();
            Placer.decks = _seed?.decks ?? new List<Deck>();
            if (cam == null) cam = Camera.main;
            if (cam) { Placer.cam = cam; Hud.cam = cam; Orbit = cam.GetComponent<OrbitCamera>(); if (!Orbit) { Orbit = cam.gameObject.AddComponent<OrbitCamera>(); Orbit.AdoptCurrentPose(); } }
            Placer.Created += lm => { var m = lm.ToModel(); _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(m);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = m.position[0], y = m.position[1], z = m.position[2], deck = lm.deckId, mounted_on = lm.mountedOn, normal = m.normal })); };
            Placer.Selected += lm => { Highlight(lm.id); Send(BridgeMessages.OnSelected, "{\"id\":\"" + lm.id + "\"}"); };
            Placer.Moved += OnMarkerMoved;
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
            Quay = QuayBuilder.Build();   // world space: the Quay Frame, never a child of this root
        }

        // ---- incoming (React -> Unity) ----
        public void Load(string json)
        {
            ScenarioPhase = Phase.Idle; Vehicle.running = false; _target = null; Vehicle.gameObject.SetActive(false);
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
            ApplyPose();
        }

        public void SetMode(string mode)
        {
            _mode = mode; Placer.enabledForInput = mode == "edit";
            if (mode == "edit")
            {
                ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false; Vehicle.gameObject.SetActive(false);
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
        }

        public void Confirm(string json) { var c = MapJson.Parse<ConfirmMsg>(json); if (_markers.TryGetValue(c.tempId, out var m)) { _markers.Remove(c.tempId); m.id = c.id; m.name = c.id; _markers[c.id] = m; MapRefs[c.id] = MapRefs[c.tempId]; MapRefs.Remove(c.tempId); if (_selected == c.tempId) _selected = c.id; } }
        /// Ship Frame is the Map root's local space; pose rotates this root (M5a) and drops it by the aft draft (M5b).
        /// Children keep their local coordinates either way.
        public void SetPose(string json) { _pose = MapJson.Parse<SetPoseMsg>(json); ApplyPose(); }

        /// Trim > 0 (stern deeper) raises the bow (+x); heel > 0 lowers starboard (Unity +z). Rotation about the AP origin.
        public static Quaternion PoseRotation(double trimDeg, double heelDeg) => Quaternion.Euler((float)heelDeg, 0, (float)trimDeg);

        public void ApplyPose()
        {
            if (_pose == null) return;
            double trimDeg = Math.Atan2(_pose.draft_aft_m - _pose.draft_fwd_m, _pose.lpp_m <= 0 ? 120 : _pose.lpp_m) * 180 / Math.PI;
            transform.localRotation = PoseRotation(trimDeg, _pose.heel_deg);
            transform.localPosition = new Vector3(0, (float)(-_pose.draft_aft_m), 0);   // waterline is world y = 0; the AP origin sits one aft draft below it
            QuayBuilder.SetHeight(Quay, _pose.quay_z_m + _pose.tide_m);
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
        }

        public void StartScenario(string json)
        {
            var s = MapJson.Parse<StartScenarioMsg>(json);
            _scenarioMode = s?.mode == "unload" ? "unload" : "load";
            SetMode("drive"); Vehicle.gameObject.SetActive(true);
            if (Orbit) { Orbit.follow = Vehicle.transform; Orbit.distance = 25f; Orbit.pitchDeg = 35f; }
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "start", mode = _scenarioMode }));
            NextVehicle();
        }

        /// Picks the next slot (spec §3.2/§3.3) and puts a vehicle on the lane start (load) or in the slot (unload). Finishes when none is left.
        /// Only slots whose access_lane_id/deck_id both resolve are candidates, so one bad reference skips that slot instead of ending the run.
        void NextVehicle()
        {
            _prev = null;   // a fresh vehicle must not seed Gauss-Newton with the previous car's pose
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
                MapOverlay.RemoveParked(_overlay, _target.id);
                Vehicle.StartPath(ScenarioPlanner.DeparturePath(_target.target_pose, _targetLane, z), ScenarioPlanner.ParkSpeedMps);
                ScenarioPhase = Phase.Departing;
            }
            else
            {
                _exitS = ScenarioPlanner.ExitS(_targetLane, _target.target_pose);
                Vehicle.StartLane(_targetLane);
                ScenarioPhase = Phase.OnLane;
            }
        }

        void Finish(string reason)
        {
            ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false; Vehicle.gameObject.SetActive(false);
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "finished", mode = _scenarioMode, detail = reason }));
        }

        // ---- per frame ----
        void Update() => Step(Time.deltaTime);

        /// One simulation tick: move the vehicle, localize, emit, then run the scenario transitions. Tests call this directly.
        public void Step(float dt)
        {
            if (_mode != "drive" || !Vehicle.running) return;
            Vehicle.Advance(dt);
            Localize();
            _emitTimer += dt;
            if (_emitTimer >= 0.2f)
            {
                _emitTimer = 0;
                Send(BridgeMessages.OnLocalization, MapJson.Serialize(new LocalizationEvt { est_x = _lastRes.pose.x, est_y = _lastRes.pose.y, est_psi = _lastRes.pose.psiRad * R2D,
                    true_x = Vehicle.Truth.x, true_y = Vehicle.Truth.y, true_psi = Vehicle.Truth.psiRad * R2D, residual_rms = _lastRes.residualRms, n_obs = _lastRes.nObs, frame = "SHIP_AP" }));
            }
            StepScenario();
        }

        /// Sense → solve → HUD, factored out so a mid-frame Rewind (StepScenario's OnLane exit-overshoot correction) can
        /// re-localize without re-triggering the 0.2s onLocalization emit, which stays in Step.
        void Localize()
        {
            var obs = Sensor.Sense(Vehicle.Truth, MapRefs, id => _markers[id].transform.position);
            _lastRes = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (_lastRes.ok && double.IsFinite(_lastRes.pose.x) && double.IsFinite(_lastRes.pose.y) && double.IsFinite(_lastRes.pose.psiRad)) _prev = _lastRes.pose;
            Hud.Set(_lastRes, Vehicle.Truth, "SHIP_AP");
        }

        void StepScenario()
        {
            switch (ScenarioPhase)
            {
                case Phase.OnLane when Vehicle.s >= _exitS || Vehicle.AtEnd:
                {
                    // Plan in the belief frame at the moment of leaving the lane, then execute open-loop in the true frame:
                    // the estimation error at this instant becomes the parking error.
                    // ponytail: open-loop from one estimate; closed-loop pure pursuit on every frame's estimate is the upgrade path.
                    // Land exactly on the exit point: one frame of travel at a high time scale would otherwise
                    // put the plan's origin metres past it, and that offset lands straight in the parking error.
                    if (!Vehicle.AtEnd && Vehicle.s > _exitS) { Vehicle.Rewind(_exitS); Localize(); }
                    var est = _prev ?? Vehicle.Truth;
                    double z = _targetDeck.z_surface;
                    var path = ScenarioPlanner.ToTruthFrame(ScenarioPlanner.ApproachPath(est, _target.target_pose, z), est, Vehicle.Truth);
                    Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "leave_lane", slot_id = _target.id, detail = $"est x {est.x:F2} y {est.y:F2} psi {est.psiRad * R2D:F1}" }));
                    Vehicle.StartPath(path, ScenarioPlanner.ParkSpeedMps);
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
                    NextVehicle();
                    break;
                }
            }
        }

        static LandmarkRef RefOf(Landmark lm) => new LandmarkRef { id = lm.id, mx = lm.position[0], my = lm.position[1], phiRad = Math.Atan2(lm.normal[1], lm.normal[0]) };

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
