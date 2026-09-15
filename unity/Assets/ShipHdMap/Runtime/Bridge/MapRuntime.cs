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
        public LocalizationHud Hud { get; private set; }
        public GameObject Ship { get; private set; }

        string _mode = "edit"; Pose2D? _prev; float _emitTimer; SeedData _seed;
        readonly Dictionary<string, LandmarkMarker> _markers = new();

        void Awake()
        {
            InitForTest();
            if (!Application.isPlaying) return;
            Ship = GameObject.Find("Ship");
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, shipParams); }
            Placer.decks = _seed?.decks ?? new List<Deck>();
            if (cam == null) cam = Camera.main; Placer.cam = cam;
            Placer.Created += lm => { _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(lm.ToModel());
                var (x, y, z) = ShipFrame.ToShip(lm.transform.position);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = x, y = y, z = z, deck = lm.deckId })); };
            Placer.Deleted += id => { _markers.Remove(id); MapRefs.Remove(id); };
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
            Hud = gameObject.AddComponent<LocalizationHud>();
        }

        // ---- incoming (React -> Unity) ----
        public void Load(string json)
        {
            CurrentMap = MapJson.Parse<VehicleMap>(json);
            foreach (var m in _markers.Values) if (m) DestroyImmediate(m.gameObject);
            _markers.Clear(); MapRefs.Clear(); Placer.All.Clear();
            foreach (var lm in CurrentMap.landmarks ?? new List<Landmark>())
            {
                var mk = LandmarkMarker.Spawn(LandmarksRoot, lm.id, lm.marker.code, ShipFrame.ToUnity(lm.position[0], lm.position[1], lm.position[2]),
                    ShipFrame.ToUnity(lm.normal[0], lm.normal[1], lm.normal[2]), (float)lm.size_m, lm.deck_id, lm.mounted_on);
                _markers[lm.id] = mk; MapRefs[lm.id] = RefOf(lm); Placer.All.Add(mk);
            }
            Placer.decks = CurrentMap.decks ?? Placer.decks;
            Placer.nextCode = _markers.Count + 1;
            Placer.nextId = _markers.Count + 1;
        }

        public void SetMode(string mode) { _mode = mode; Placer.enabledForInput = mode == "edit"; if (mode == "edit") Vehicle.running = false; }
        public void SetDeck(string deck) { if (Ship) ShipMeshBuilder.SetDeckVisibility(Ship, deck); }
        public void Select(string id) { Send(BridgeMessages.OnSelected, "{\"id\":\"" + id + "\"}"); }
        public void Confirm(string json) { var c = MapJson.Parse<ConfirmMsg>(json); if (_markers.TryGetValue(c.tempId, out var m)) { _markers.Remove(c.tempId); m.id = c.id; m.name = c.id; _markers[c.id] = m; MapRefs[c.id] = MapRefs[c.tempId]; MapRefs.Remove(c.tempId); } }
        public void SetPose(string json) { /* M5 */ }

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
            _markers.Remove(id); MapRefs.Remove(id); Placer.All.Remove(m);
            if (m) { if (Application.isPlaying) Destroy(m.gameObject); else DestroyImmediate(m.gameObject); }
        }

        public SeedData Seed => _seed;
        public IEnumerable<LandmarkMarker> Markers => _markers.Values;
    }
}
