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
            var existing = File.Exists(BridgeStubWindow.FixturePath()) ? MapJson.Parse<VehicleMap>(File.ReadAllText(BridgeStubWindow.FixturePath())) : new VehicleMap();

            var landmarks = SeedLandmarks(seed);

            var map = BuildMap(seed, existing, landmarks);
            File.WriteAllText(BridgeStubWindow.FixturePath(), MapJson.Serialize(map));
            Debug.Log($"Fixture written (from seed): {BridgeStubWindow.FixturePath()} (landmarks {map.landmarks.Count})");
        }

        /// Landmarks derived from seed geometry, so a changed ShipParams (beam, deck pitch, pillar layout) still puts every tag
        /// on a structure. Every deck gets the same kit: one pair per pillar station (tag on the pillar's centreline-side face,
        /// 1.2 m above the deck), one on the port hull, one bow pair. The stern-ramp deck comes first and keeps LM-0001..0021
        /// plus the ramp's frame-transition pair LM-0022/0023, exactly as before M8, so its slots, the ramp and every figure
        /// that names them still resolve. The other decks follow from LM-0024 in deck order.
        public static List<Landmark> SeedLandmarks(SeedData seed)
        {
            int rampDeck = Math.Min(new ShipParams().rampDeckIndex, seed.decks.Count - 1);
            var landmarks = new List<Landmark>();
            int n = 0;
            DeckKit(seed, seed.decks[rampDeck], landmarks, ref n, legacyCodes: true);

            // Frame-transition pair (spec §3.2): two tags on the stern ramp's hinge posts, facing astern so a vehicle
            // coming up the ramp sees both at once. 1.2 m above the hinge keeps the sight line clear of the ramp plate.
            var ramp = seed.ramps.FirstOrDefault(r => r.type == "stern_quarter");
            if (ramp != null)
            {
                var deck = seed.decks[rampDeck];
                double hy = (ramp.hinge[0][1] + ramp.hinge[1][1]) / 2, hz = ramp.hinge[0][2], half = ramp.width_m / 2 - 0.5;
                landmarks.Add(Lm(Id(++n), n % 20, 0.3, hy - half, hz + 1.2, -1, 0, 0, ramp.id, deck.id));
                landmarks.Add(Lm(Id(++n), n % 20, 0.3, hy + half, hz + 1.2, -1, 0, 0, ramp.id, deck.id));
                ramp.transition_landmarks = new List<string> { Id(n - 1), Id(n) };
            }
            for (int i = 0; i < seed.decks.Count; i++) if (i != rampDeck) DeckKit(seed, seed.decks[i], landmarks, ref n, legacyCodes: false);
            return landmarks;
        }

        /// One deck's tags. legacyCodes keeps the pre-M8 `id % 20` codes on the stern-ramp deck (fixtures and figures quote
        /// them); every newer tag gets its own code, `n % 587` -- the family has 587 -- so no two tags on one deck share one.
        static void DeckKit(SeedData seed, Deck deck, List<Landmark> landmarks, ref int n, bool legacyCodes)
        {
            int Code(int k) => legacyCodes ? k % 20 : k % 587;
            double zTag = deck.z_surface + 1.2;
            var pillars = seed.facilities.Where(f => f.deck_id == deck.id && f.kind == "pillar").ToList();
            var stbdRow = pillars.Where(f => PillarCenter(f).y < 0).OrderBy(f => PillarCenter(f).x).ToList();   // -y = starboard
            var portRow = pillars.Where(f => PillarCenter(f).y > 0).OrderBy(f => PillarCenter(f).x).ToList();   // +y = port
            int stationCount = Math.Min(9, Math.Min(stbdRow.Count, portRow.Count));
            for (int i = 0; i < stationCount; i++)
            {
                var s = stbdRow[i]; var p = portRow[i];
                ++n; landmarks.Add(Lm(Id(n), Code(n), PillarCenter(s).x, s.footprint.Max(pt => pt[1]), zTag, 0, 1, 0, s.id, deck.id));
                ++n; landmarks.Add(Lm(Id(n), Code(n), PillarCenter(p).x, p.footprint.Min(pt => pt[1]), zTag, 0, -1, 0, p.id, deck.id));
            }
            ++n; landmarks.Add(Lm(Id(n), Code(n), 40, deck.outline.Max(pt => pt[1]) - 0.1, zTag, 0, -1, 0, "HULL-PORT", deck.id));
            // Bow pair: the pillar rows stop at x = 108 and sit 6.2 m off the centreline, so past x ~ 101.8 nothing is
            // inside the 90 deg FOV from the lane. These two keep every lane exit point observable (spec 9.4).
            double bowX = deck.outline.Max(pt => pt[0]) - 0.3;
            ++n; landmarks.Add(Lm(Id(n), Code(n), bowX, -3, zTag, -1, 0, 0, "BOW-" + deck.id, deck.id));
            ++n; landmarks.Add(Lm(Id(n), Code(n), bowX, 3, zTag, -1, 0, 0, "BOW-" + deck.id, deck.id));
        }

        static string Id(int n) => $"LM-{n:0000}";

        /// Shared build for both exporters: schema/version/frame bump, seed decks/lanes/ramps/routes, every deck's facilities,
        /// and every deck's whole lashing grid, so slot.access_lane_id and slot.lashing_points always resolve inside
        /// the fixture instead of pointing at pre-regeneration ids (spec bug: was A2-0001 / LP-0001.. against seed
        /// A2-D3-0001 / LP-D3-....).
        public static VehicleMap BuildMap(SeedData seed, VehicleMap existing, List<Landmark> landmarks)
        {
            var slots = existing.parking_slots ?? new List<ParkingSlot>();
            foreach (var ps in slots)
            {
                var lane = seed.lanes.Find(l => l.deck_id == ps.deck_id);
                if (lane != null) ps.access_lane_id = lane.id;

                var deckLashing = seed.lashing_points.Where(l => l.deck_id == ps.deck_id).ToList();
                var chosen = new List<string>(); var used = new HashSet<string>();
                for (int i = 0; i < 4 && i < ps.polygon.Length; i++)
                {
                    var corner = ps.polygon[i];
                    var nearest = deckLashing.Where(l => !used.Contains(l.id))
                        .OrderBy(l => Sq(l.position[0] - corner[0]) + Sq(l.position[1] - corner[1]))
                        .FirstOrDefault();
                    if (nearest == null) continue;
                    used.Add(nearest.id); chosen.Add(nearest.id);
                }
                ps.lashing_points = chosen;
            }

            return new VehicleMap
            {
                schema = "ship-hdmap/vehicle-map/1.0", map_id = "roro-demo-01", version = existing.version + 1, generated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                frame = existing.frame ?? new FrameInfo { name = "SHIP_AP", origin = "AP x Baseline x Centerline", axes = new Dictionary<string, string> { ["x"] = "AP->bow (+)", ["y"] = "port (+)", ["z"] = "baseline->up (+)" }, unit = "m" },
                decks = seed.decks, lanes = seed.lanes, ramps = seed.ramps, routes = seed.routes,
                facilities = seed.facilities,   // every deck: Load rebuilds the hull from this map (M5b Task 7)
                lashing_points = seed.lashing_points,   // every deck's whole grid: slot generation maps each corner to a socket on any deck
                landmarks = landmarks,
                parking_slots = slots, markings = existing.markings ?? new List<Marking>(),
            };
        }

        static double Sq(double v) => v * v;

        static (double x, double y) PillarCenter(Facility f) => ((f.footprint[0][0] + f.footprint[2][0]) / 2, (f.footprint[0][1] + f.footprint[2][1]) / 2);

        static Landmark Lm(string id, int code, double x, double y, double z, double nx, double ny, double nz, string mountedOn, string deckId) =>
            new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { Math.Round(x, 6), Math.Round(y, 6), Math.Round(z, 6) }, normal = new[] { nx, ny, nz }, size_m = 0.30, deck_id = deckId, mounted_on = mountedOn };
    }
}
