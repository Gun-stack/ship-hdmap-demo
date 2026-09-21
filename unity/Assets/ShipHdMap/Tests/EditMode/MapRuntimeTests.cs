using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MapRuntimeTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }

        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));

        [Test]
        public void DestroyShipMaterialsDestroysEveryReferencedMaterialIncludingDeckFilterClones()
        {
            // isPlaying is false here, so DestroyShipMaterials takes its DestroyImmediate branch -- the only branch
            // an EditMode test can observe synchronously (Destroy() defers to end of frame and is refused outright
            // in edit mode), but it is the same collect-then-destroy sweep the play-mode branch runs.
            var p = new ShipParams { lashingPitchM = 4 };
            var ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p));
            ShipMeshBuilder.SetDeckVisibility(ship, "D1"); // clones every renderer's material to its own "~inst" instance
            var mats = new System.Collections.Generic.List<Material>();
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial) mats.Add(r.sharedMaterial);
            Assert.That(mats.Count, Is.GreaterThan(0));

            MapRuntime.DestroyShipMaterials(ship);

            // Unity overloads == so a destroyed Object compares equal to null even though the C# reference is not.
            foreach (var m in mats) Assert.That(m == null, Is.True, "material should have been destroyed");
            Object.DestroyImmediate(ship);
        }

        [Test]
        public void LoadSpawnsLandmarksAndBuildsMap()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(23));
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(System.Math.PI / 2).Within(1e-6)); // normal +y
            Assert.That(rt.CurrentMap.parking_slots.Count, Is.EqualTo(2));
        }

        /// A re-key that leaves the struct's own id stale is invisible here (MapRefs[c.id] exists either way) but
        /// fatal downstream: Localizer.Observe stamps Observation.id from that field, and Localizer.Solve looks the
        /// observation back up by id in this same map, so a stale id makes every future sighting of this landmark
        /// silently vanish from localization with no error anywhere.
        [Test]
        public void ConfirmRewritesTheLandmarkRefIdSoASightingCannotBeLookedUpUnderTheStaleOne()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture());
            rt.Confirm(MapJson.Serialize(new ConfirmMsg { tempId = "LM-0001", id = "LM-confirmed" }));

            Assert.That(rt.MapRefs.ContainsKey("LM-0001"), Is.False);
            Assert.That(rt.MapRefs.ContainsKey("LM-confirmed"), Is.True);
            Assert.That(rt.MapRefs["LM-confirmed"].id, Is.EqualTo("LM-confirmed"));
        }

        /// InitForTest attaches both View and Gizmo to this same GameObject, and Unity allows only one Renderer
        /// per GameObject -- so a LineRenderer added straight to this transform by either one would be racing
        /// the other for that single slot, and whichever lost would come back null and throw on the very next
        /// line. Both keep their LineRenderer on their own child instead (SensorCone, NormalRing), so neither
        /// ever competes for a slot on the root, and a third such component in the future has an established
        /// pattern to follow rather than a trap to rediscover.
        [Test]
        public void ProbeAndGizmoLineRenderersCoexistOnTheSameMapRoot()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture());
            Assert.DoesNotThrow(() =>
            {
                rt.Select("LM-0001");                                    // NormalGizmo.Attach -> DrawRing, onto its own child
                rt.Probe.PlaceAt(ShipFrame.ToUnity(5, 0, 11.8), 11.8);    // SensorView.Show -> EnsureLine, onto its own child
            });
            var cone = rt.transform.Find("SensorCone");
            Assert.That(cone, Is.Not.Null);
            Assert.That(cone.GetComponent<LineRenderer>(), Is.Not.Null);
            var ring = rt.transform.Find("NormalRing");
            Assert.That(ring, Is.Not.Null);
            Assert.That(ring.GetComponent<LineRenderer>(), Is.Not.Null);
            Assert.That(rt.GetComponent<LineRenderer>(), Is.Null);       // neither renderer sits on the root itself any more
        }

        [Test]
        public void LoadTwiceReplacesLandmarks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture()); rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(23));
        }

        [Test]
        public void SetNoiseParsesAndApplies()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.SetNoise("{\"sigma_r\":0.5,\"sigma_theta\":2,\"sigma_alpha\":3,\"sigma_gps\":0.5}");
            Assert.That(rt.Sensor.noise.sigmaR, Is.EqualTo(0.5));
            Assert.That(rt.Sensor.noise.sigmaThetaRad, Is.EqualTo(2 * System.Math.PI / 180).Within(1e-9));
        }

        [Test]
        public void DeleteRemovesMarkerAndMapRef()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture());
            int before = rt.LandmarksRoot.childCount;
            rt.Delete("LM-0003");
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"), Is.False);
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
            rt.Delete("LM-NOPE"); // no throw
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
        }

        [Test]
        public void SelectHighlightsWithoutEmitting()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            int emitted = 0; rt.Emit += (_, _) => emitted++;
            rt.Select("LM-0003");
            var halo3 = rt.LandmarksRoot.Find("LM-0003/Halo");
            Assert.That(halo3, Is.Not.Null); Assert.That(halo3.gameObject.activeSelf, Is.True);
            rt.Select("LM-0004");
            Assert.That(halo3.gameObject.activeSelf, Is.False);
            Assert.That(rt.LandmarksRoot.Find("LM-0004/Halo").gameObject.activeSelf, Is.True);
            rt.Select("LM-NOPE");
            Assert.That(rt.LandmarksRoot.Find("LM-0004/Halo").gameObject.activeSelf, Is.False);
            Assert.That(emitted, Is.EqualTo(0));
        }

        [Test]
        public void SelectFocusesOrbitOnlyFromTheWeb()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var camGo = new GameObject("Cam"); var orbit = camGo.AddComponent<OrbitCamera>(); rt.Orbit = orbit;
            var startTarget = orbit.target;

            rt.Highlight("LM-0003");
            Assert.That(orbit.target, Is.EqualTo(startTarget)); // scene-side highlight must not move the camera

            rt.Select("LM-0003");
            var markerPos = rt.LandmarksRoot.Find("LM-0003").position;
            Assert.That(Vector3.Distance(orbit.target, markerPos), Is.LessThan(1e-3f));

            rt.SetMode("drive");
            var driveTarget = orbit.target;
            rt.Select("LM-0004");
            Assert.That(orbit.target, Is.EqualTo(driveTarget)); // drive mode never refocuses on a selection

            Object.DestroyImmediate(camGo);
        }

        [Test]
        public void FeatureCreatedEventCarriesMountedOn()
        {
            var json = MapJson.Serialize(new FeatureCreatedEvt { tempId = "LM-0002", layer = "LM", x = 1, y = 2, z = 3, deck = "D3", mounted_on = "C-PILLAR-D3-001" });
            Assert.That(json, Does.Contain("mounted_on")); Assert.That(json, Does.Contain("C-PILLAR-D3-001"));
        }

        [Test]
        public void MarkerMovedUpdatesMapRefAndEmits()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            string name = null, json = null; rt.Emit += (n, j) => { name = n; json = j; };
            var lm = rt.LandmarksRoot.Find("LM-0001").GetComponent<LandmarkMarker>();
            lm.MoveTo(ShipFrame.ToUnity(30, 6, 11.8), ShipFrame.ToUnity(0, -1, 0), "D3", "C-PILLAR-D3-009");
            rt.OnMarkerMoved(lm);
            Assert.That(rt.MapRefs["LM-0001"].mx, Is.EqualTo(30).Within(1e-3));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(-System.Math.PI / 2).Within(1e-3));
            Assert.That(name, Is.EqualTo("onFeatureMoved"));
            Assert.That(json, Does.Contain("C-PILLAR-D3-009")); Assert.That(json, Does.Contain("normal"));
        }

        [Test]
        public void LoadBuildsOverlayLinesAndSetDeckFilters()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var overlay = rt.transform.Find("Overlay"); Assert.That(overlay, Is.Not.Null);
            var lines = overlay.GetComponentsInChildren<LineRenderer>(true);
            Assert.That(lines.Length, Is.EqualTo(5)); // 3 lanes + 2 slots
            rt.SetDeck("D3");
            Assert.That(overlay.Find("D1/A2-D1-0001").GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(overlay.Find("D3/A2-D3-0001").GetComponent<LineRenderer>().enabled, Is.True);
            int fillMeshesBefore = CountFillMeshes();
            rt.Load(Fixture()); // rebuild keeps the filter and does not duplicate
            Assert.That(rt.transform.Find("Overlay").GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(5));
            Assert.That(rt.transform.Find("Overlay/D1/A2-D1-0001").GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(CountFillMeshes(), Is.EqualTo(fillMeshesBefore)); // the old overlay's fill meshes were destroyed, not leaked
        }

        static int CountFillMeshes()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m && m.name.EndsWith("~fill")) n++;
            return n;
        }

        [Test]
        public void SlotFillsFollowStatusAndSelection()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var fill = rt.transform.Find("Overlay/D3/PS-D3-001/Fill");
            Assert.That(fill, Is.Not.Null);
            var mr = fill.GetComponent<MeshRenderer>();
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.35f).Within(1e-3));
            rt.Select("PS-D3-001");
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.75f).Within(1e-3));
            rt.Select("LM-0001");   // selecting a marker clears the slot highlight
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.35f).Within(1e-3));
            Assert.That(rt.LandmarksRoot.Find("LM-0001/Halo").gameObject.activeSelf, Is.True);
            rt.Select("");          // empty id clears everything
            Assert.That(rt.LandmarksRoot.Find("LM-0001/Halo").gameObject.activeSelf, Is.False);
            rt.SetDeck("D1");
            Assert.That(mr.enabled, Is.False);
        }

        [Test]
        public void SetDeckHidesMarkersOfOtherDecks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            rt.SetDeck("D1");
            Assert.That(rt.LandmarksRoot.Find("LM-0001").gameObject.activeSelf, Is.False); // fixture landmarks are all on D3
            rt.SetDeck("all");
            Assert.That(rt.LandmarksRoot.Find("LM-0001").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ShipSignatureRepeatsForTheSameMapAndChangesWithTheHull()
        {
            // The whole point of the signature is that a slot regeneration reloads the map without rebuilding
            // thousands of primitives -- and that a genuinely different hull still rebuilds. Its only call site
            // sits behind Application.isPlaying, which is false here, so nothing else reaches it.
            var a = MapJson.Parse<VehicleMap>(Fixture());
            var b = MapJson.Parse<VehicleMap>(Fixture());
            Assert.That(MapRuntime.ShipSignature(b), Is.EqualTo(MapRuntime.ShipSignature(a)), "a reload of the same map must not rebuild the hull");

            b.decks[0].outline[1][0] += 10; b.decks[0].outline[2][0] += 10;   // same deck heights, 10 m longer Deck 1
            Assert.That(MapRuntime.ShipSignature(b), Is.Not.EqualTo(MapRuntime.ShipSignature(a)), "a different deck outline must rebuild the hull");
        }

        /// 완료 기준 5: 편집 모드에서 3D 를 마음대로 클릭해도 마커가 생기지 않는다.
        /// 도구는 웹이 정하고, Load 가 그것을 지워서는 안 된다 — 지도를 다시 받았다고 배치 모드가 풀리면
        /// 마커를 줄지어 놓던 사람이 매번 다시 켜야 한다.
        [Test]
        public void SetToolReachesThePlacerAndSurvivesAReload()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Select));      // default: clicking must be safe
            rt.SetTool("{\"tool\":\"place\"}");
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Place));
            rt.Load(Fixture());
            Assert.That(rt.Placer.tool, Is.EqualTo(PlacerTool.Place));
        }

        /// The web saved the normal already; Unity just has to turn the quad and update the map reference,
        /// or the coverage the web is about to recompute will disagree with what the drive senses.
        [Test]
        public void SetNormalTurnsTheMarkerAndTheMapReference()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest(); rt.Load(Fixture());
            var before = rt.MapRefs["LM-0001"].phiRad;
            Assert.That(before, Is.EqualTo(System.Math.PI / 2).Within(1e-6));
            rt.SetNormal("{\"id\":\"LM-0001\",\"normal\":[1,0,0]}");
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(0).Within(1e-6));
            Assert.That(rt.MarkerOf("LM-0001").ToModel().normal[0], Is.EqualTo(1).Within(1e-3));
        }

        /// Two halves, because the early return is the whole risk: without an Orbit it must not throw, and
        /// WITH one it must actually set the mode. Asserting only the first half passes even if the body is dead.
        [Test]
        public void SetCamModeGuardsAMissingCameraAndOtherwiseSetsTheMode()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            Assert.DoesNotThrow(() => rt.SetCamMode("{\"mode\":\"fly\"}"));   // EditMode has no Orbit; must not NRE

            var camGo = new GameObject("cam"); camGo.AddComponent<Camera>();
            rt.Orbit = camGo.AddComponent<OrbitCamera>();
            rt.SetCamMode("{\"mode\":\"fly\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Fly));
            rt.SetCamMode("{\"mode\":\"driver\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Driver));
            Assert.That(rt.Orbit.driverTarget, Is.EqualTo(rt.Vehicle.transform));
            rt.SetCamMode("{\"mode\":\"orbit\"}");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Orbit));
            Assert.That(rt.Orbit.driverTarget, Is.Null);
            Object.DestroyImmediate(camGo);
        }

        /// Both editor handles are taken by Unity and given back by nothing else: leaving edit mode never passes
        /// through SetTool. A probe left holding the camera freezes the whole drive at its own eye (null
        /// driverTarget means ApplyDriver will not move it again) and a gizmo left attached turns normals -- and
        /// PUTs them -- mid-run.
        [Test]
        public void LeavingEditModeGivesBackTheCameraAndTheGizmo()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest(); rt.Load(Fixture());
            var camGo = new GameObject("cam"); camGo.AddComponent<Camera>();
            rt.Orbit = camGo.AddComponent<OrbitCamera>(); rt.Probe.orbit = rt.Orbit;

            rt.Highlight("LM-0001");
            Assert.That(rt.Gizmo.Target, Is.Not.Null, "edit mode: the selected marker carries a handle");
            rt.Probe.PlaceAt(Vector3.zero, 0);                       // the probe parks the camera at its own eye
            Assert.That(rt.Probe.Active, Is.True);
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Driver)); Assert.That(rt.Orbit.driverTarget, Is.Null);
            Assert.That(camGo.transform.position, Is.EqualTo(new Vector3(0, 1.2f, 0)).Using<Vector3>((a, b) => Vector3.Distance(a, b) < 1e-3f ? 0 : 1),
                "Aim parks the camera at the probe's eye: origin, one eye height up (no pose applied here)");
            // Heading 0 is ship +x, which ShipFrame maps to Unity +x. A yaw-only rotation would leave the
            // camera looking along +z -- 90 deg to starboard of the cone it just drew.
            Assert.That(Vector3.Dot(camGo.transform.forward, Vector3.right), Is.EqualTo(1).Within(1e-3), "the probe looks along its own heading");

            rt.SetMode("drive");
            Assert.That(rt.Probe.Active, Is.False, "a drive must not start from the probe's frozen eye");
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Orbit), "and the web believes it is orbiting");
            Assert.That(rt.Gizmo.Target, Is.Null, "a right-drag during a run must not turn a normal");

            rt.SetMode("edit");
            Assert.That(rt.Gizmo.Target, Is.Not.Null, "back in edit the still-selected marker gets its handle back");
            Object.DestroyImmediate(camGo);
        }

        /// The probe's Driver is a TOOL state, not one of the three cameras the toolbar offers, and reporting
        /// it made the probe cancel itself in one round trip -- see PollCamMode's comment for the loop. What a
        /// unit test can hold is the near end of it: while a probe stands, nothing about the camera mode goes
        /// out, and leaving the probe is silent too because the mode lands back where the web already had it.
        [Test]
        public void TheProbesCameraModeNeverReachesTheWebButOtherChangesDo()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            var sent = new List<(string name, string json)>();
            rt.Emit += (n, j) => sent.Add((n, j));
            rt.InitForTest(); rt.Load(Fixture());
            var camGo = new GameObject("cam"); camGo.AddComponent<Camera>();
            rt.Orbit = camGo.AddComponent<OrbitCamera>(); rt.Probe.orbit = rt.Orbit;
            System.Func<List<string>> reports = () => sent.Where(e => e.name == BridgeMessages.OnCamMode).Select(e => e.json).ToList();

            rt.Probe.PlaceAt(Vector3.zero, 0);
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Driver), "the probe does hold the camera in Driver");
            rt.PollCamMode();
            Assert.That(reports(), Is.Empty, "the toolbar would answer a 'driver' report by killing the probe");

            rt.SetTool("{\"tool\":\"select\"}");
            rt.PollCamMode();
            Assert.That(rt.Orbit.mode, Is.EqualTo(CamMode.Orbit));
            Assert.That(reports(), Is.Empty, "leaving it is silent too: the camera is back where the web still believed it was");

            // ...and the poll is not simply dead: a mode Unity changes for its own reasons still goes out.
            rt.Orbit.mode = CamMode.Fly;
            rt.PollCamMode();
            Assert.That(reports(), Is.EqualTo(new List<string> { "{\"mode\":\"fly\"}" }));
            Object.DestroyImmediate(camGo);
        }

        [Test]
        public void FeatureCreatedEventCarriesNormal()
        {
            var json = MapJson.Serialize(new FeatureCreatedEvt { tempId = "LM-0002", layer = "LM", x = 1, y = 2, z = 3, deck = "D3", mounted_on = "P", normal = new[] { 0.0, -1.0, 0.0 } });
            Assert.That(json, Does.Contain("normal"));
        }

        [Test]
        public void SetSensorMovesTheThreeGeometryFields()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            rt.SetSensor("{\"fov_deg\":55,\"max_dist_m\":12,\"max_view_angle_deg\":40}");
            Assert.That(rt.Sensor.fovDeg, Is.EqualTo(55f).Within(1e-4f));
            Assert.That(rt.Sensor.maxDist, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(rt.Sensor.maxViewAngleDeg, Is.EqualTo(40f).Within(1e-4f));
        }

        [Test]
        public void SetSensorReAimsALiveProbeInsteadOfWaitingForTheNextClick()
        {
            // The drive re-reads the three fields every frame through VisibleFrom, so it needs nothing. The probe
            // only recomputes in PlaceAt and on an arrow key -- without the re-aim its cone and its count keep the
            // old angle until the operator happens to touch it, which is exactly the disagreement M6 exists to end.
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var d3 = rt.CurrentMap.decks.Find(d => d.id == "D3");
            Assert.That(d3, Is.Not.Null, "fixture must carry Deck 3");
            rt.Probe.PlaceAt(rt.transform.TransformPoint(ShipFrame.ToUnity(60, 0, d3.z_surface)), d3.z_surface);
            string wide = rt.Hud.SensorText;
            Assert.That(wide, Does.StartWith("seen "), "placing the probe should already have produced a count");

            rt.SetSensor("{\"fov_deg\":10,\"max_dist_m\":25,\"max_view_angle_deg\":70}");

            Assert.That(rt.Hud.SensorText, Is.Not.EqualTo(wide), "narrowing the cone to 10 deg must re-aim the live probe");
        }
    }
}
