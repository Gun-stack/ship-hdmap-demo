using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    public static class FixtureExporter
    {
        [MenuItem("ShipHdMap/Export vehicle-map fixture")]
        static void Export()
        {
            var rt = UnityEngine.Object.FindFirstObjectByType<MapRuntime>();
            if (rt == null || !Application.isPlaying) { Debug.LogWarning("Enter Play mode with a Map in the scene first."); return; }
            var seed = rt.Seed ?? ShipSeedBuilder.Build(rt.shipParams);
            var existing = File.Exists(BridgeStubWindow.FixturePath()) ? MapJson.Parse<VehicleMap>(File.ReadAllText(BridgeStubWindow.FixturePath())) : new VehicleMap();
            var landmarks = rt.Markers.Select(m => m.ToModel()).OrderBy(l => l.id).ToList();
            var map = BuildMap(seed, existing, landmarks);
            File.WriteAllText(BridgeStubWindow.FixturePath(), MapJson.Serialize(map));
            Debug.Log($"Fixture written: {BridgeStubWindow.FixturePath()} (landmarks {map.landmarks.Count})");
        }

        /// Batch-mode alternative to Export(): rebuilds the fixture straight from the generator, without needing
        /// Play mode or hand-placed markers. Landmarks are re-seated onto real seed geometry (Deck 3 pillars / hull).
        [MenuItem("ShipHdMap/Export vehicle-map fixture (from seed)")]
        public static void ExportFromSeed()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            foreach (var r in seed.ramps) r.transition_landmarks = new List<string> { "LM-0001", "LM-0002" };
            var existing = File.Exists(BridgeStubWindow.FixturePath()) ? MapJson.Parse<VehicleMap>(File.ReadAllText(BridgeStubWindow.FixturePath())) : new VehicleMap();
            var landmarks = new List<Landmark>
            {
                Lm("LM-0001", 1, 12, -6.2, 11.8, 0, 1, 0, "C-PILLAR-D3-001"),
                Lm("LM-0002", 2, 12, 6.2, 11.8, 0, -1, 0, "C-PILLAR-D3-010"),
                Lm("LM-0003", 3, 40, 11.9, 11.8, 0, -1, 0, "HULL-PORT"),
                Lm("LM-0004", 4, 36, -6.2, 11.8, 0, 1, 0, "C-PILLAR-D3-003"),
                Lm("LM-0005", 5, 36, 6.2, 11.8, 0, -1, 0, "C-PILLAR-D3-012"),
                Lm("LM-0006", 6, 60, -6.2, 11.8, 0, 1, 0, "C-PILLAR-D3-005"),
            };
            var map = BuildMap(seed, existing, landmarks);
            File.WriteAllText(BridgeStubWindow.FixturePath(), MapJson.Serialize(map));
            Debug.Log($"Fixture written (from seed): {BridgeStubWindow.FixturePath()} (landmarks {map.landmarks.Count})");
        }

        /// Shared build for both exporters: schema/version/frame bump, seed decks/lanes/ramps, D3 facilities,
        /// and the D3 lashing-point window widened to also cover whatever each kept parking slot's corners
        /// actually snap to, so slot.access_lane_id and slot.lashing_points always resolve inside the fixture
        /// instead of pointing at pre-regeneration ids (spec bug: was A2-0001 / LP-0001.. against seed A2-D3-0001 / LP-D3-....).
        static VehicleMap BuildMap(SeedData seed, VehicleMap existing, List<Landmark> landmarks)
        {
            var d3Lashing = seed.lashing_points.Where(l => l.deck_id == "D3").ToList();
            var slots = existing.parking_slots ?? new List<ParkingSlot>();
            var needed = new HashSet<string>();
            foreach (var ps in slots)
            {
                var lane = seed.lanes.Find(l => l.deck_id == ps.deck_id);
                if (lane != null) ps.access_lane_id = lane.id;

                var chosen = new List<string>(); var used = new HashSet<string>();
                for (int i = 0; i < 4 && i < ps.polygon.Length; i++)
                {
                    var corner = ps.polygon[i];
                    var nearest = d3Lashing.Where(l => !used.Contains(l.id))
                        .OrderBy(l => Sq(l.position[0] - corner[0]) + Sq(l.position[1] - corner[1]))
                        .FirstOrDefault();
                    if (nearest == null) continue;
                    used.Add(nearest.id); chosen.Add(nearest.id); needed.Add(nearest.id);
                }
                ps.lashing_points = chosen;
            }

            var lashingExport = d3Lashing.Where(l =>
                (l.position[0] >= 94 && l.position[0] <= 105 && l.position[1] >= 2 && l.position[1] <= 4) || needed.Contains(l.id)).ToList();

            return new VehicleMap
            {
                schema = "ship-hdmap/vehicle-map/1.0", map_id = "roro-demo-01", version = existing.version + 1, generated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                frame = existing.frame ?? new FrameInfo { name = "SHIP_AP", origin = "AP x Baseline x Centerline", axes = new Dictionary<string, string> { ["x"] = "AP->bow (+)", ["y"] = "port (+)", ["z"] = "baseline->up (+)" }, unit = "m" },
                decks = seed.decks, lanes = seed.lanes, ramps = seed.ramps,
                facilities = seed.facilities.Where(f => f.deck_id == "D3").ToList(),            // keep the fixture small
                lashing_points = lashingExport,
                landmarks = landmarks,
                parking_slots = slots, markings = existing.markings ?? new List<Marking>(),
            };
        }

        static double Sq(double v) => v * v;

        static Landmark Lm(string id, int code, double x, double y, double z, double nx, double ny, double nz, string mountedOn) =>
            new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { x, y, z }, normal = new[] { nx, ny, nz }, size_m = 0.30, deck_id = "D3", mounted_on = mountedOn };
    }
}
