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
        public OrbitCamera Orbit { get; set; }

        string _mode = "edit"; string _selected; Pose2D? _prev; float _emitTimer; SeedData _seed;
        GameObject _overlay; string _deck = "all";
        readonly Dictionary<string, LandmarkMarker> _markers = new();

        void Awake()
        {
            InitForTest();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false; // let the React side panels receive keystrokes
#endif
            if (!Application.isPlaying) return;
            Ship = GameObject.Find("Ship");
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, shipParams); }
            Placer.decks = _seed?.decks ?? new List<Deck>();
            if (cam == null) cam = Camera.main;
            if (cam) { Placer.cam = cam; Hud.cam = cam; Orbit = cam.GetComponent<OrbitCamera>(); if (!Orbit) { Orbit = cam.gameObject.AddComponent<OrbitCamera>(); Orbit.AdoptCurrentPose(); } }
            Placer.Created += lm => { _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(lm.ToModel());
                var (x, y, z) = ShipFrame.ToShip(lm.transform.position);
                var (nx, ny, nz) = ShipFrame.ToShip(lm.NormalUnity);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = x, y = y, z = z, deck = lm.deckId, mounted_on = lm.mountedOn, normal = new[] { nx, ny, nz } })); };
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
        }

        // ---- incoming (React -> Unity) ----
        public void Load(string json)
        {
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

            if (_overlay) DestroyImmediate(_overlay);
            _overlay = MapOverlay.Build(CurrentMap, transform); MapOverlay.SetDeck(_overlay, _deck);
            foreach (var m in _markers.Values) if (m) m.gameObject.SetActive(_deck == "all" || m.deckId == _deck);

            var labels = new List<(string, Vector3)>();
            foreach (var d in CurrentMap.decks ?? new List<Deck>()) labels.Add(($"{d.id}  z {d.z_surface:F1} m", ShipFrame.ToUnity(4, 10, d.z_surface + 1.5)));
            Hud.SetDeckLabels(labels);
            Hud.SetContext(_deck, null);
        }

        public void SetMode(string mode)
        {
            _mode = mode; Placer.enabledForInput = mode == "edit";
            if (mode == "edit") { Vehicle.running = false; if (Orbit) Orbit.follow = null; }
        }
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
        public void SetPose(string json) { /* M5 */ }

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
            var s = MapJson.Parse<StartScenarioMsg>(json); if (s.map != null) Load(MapJson.Serialize(s.map));
            var (lane, deck) = ResolveScenarioLane();
            if (lane == null || deck == null) { Debug.LogWarning("StartScenario: no usable lane/deck in CurrentMap."); return; }
            _prev = null; SetMode("drive"); Vehicle.StartLane(lane, deck.z_surface);
            if (Orbit) { Orbit.follow = Vehicle.transform; Orbit.distance = 25f; Orbit.pitchDeg = 35f; }
        }

        /// Picks the scenario's starting lane: the lane the first ramp connects to (the vehicle drives on
        /// after disembarking), falling back to the first lane whose deck carries a landmark, then lanes[0].
        public (Lane lane, Deck deck) ResolveScenarioLane()
        {
            if (CurrentMap == null || CurrentMap.lanes == null || CurrentMap.lanes.Count == 0) return (null, null);

            Lane lane = null;
            var rampLaneId = CurrentMap.ramps != null && CurrentMap.ramps.Count > 0 ? CurrentMap.ramps[0].connects_lane : null;
            if (rampLaneId != null) lane = CurrentMap.lanes.Find(l => l.id == rampLaneId);

            if (lane == null && CurrentMap.landmarks != null)
            {
                var decksWithLandmarks = new HashSet<string>();
                foreach (var lm in CurrentMap.landmarks) decksWithLandmarks.Add(lm.deck_id);
                lane = CurrentMap.lanes.Find(l => decksWithLandmarks.Contains(l.deck_id));
            }

            lane ??= CurrentMap.lanes[0];
            var deck = CurrentMap.decks?.Find(d => d.id == lane.deck_id);
            return (lane, deck);
        }

        // ---- per frame ----
        void Update()
        {
            if (_mode != "drive" || !Vehicle.running) return;
            var obs = Sensor.Sense(Vehicle.Truth, MapRefs, id => _markers[id].transform.position);
            var res = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (res.ok && double.IsFinite(res.pose.x) && double.IsFinite(res.pose.y) && double.IsFinite(res.pose.psiRad)) _prev = res.pose;
            Hud.Set(res, Vehicle.Truth, "SHIP_AP");
            _emitTimer += Time.deltaTime;
            if (_emitTimer >= 0.2f)
            {
                _emitTimer = 0;
                Send(BridgeMessages.OnLocalization, MapJson.Serialize(new LocalizationEvt { est_x = res.pose.x, est_y = res.pose.y, est_psi = res.pose.psiRad * 180 / Math.PI,
                    true_x = Vehicle.Truth.x, true_y = Vehicle.Truth.y, true_psi = Vehicle.Truth.psiRad * 180 / Math.PI, residual_rms = res.residualRms, n_obs = res.nObs, frame = "SHIP_AP" }));
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
