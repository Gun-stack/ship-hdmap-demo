using System.Linq;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class ShipSeedBuilderTests
    {
        [Test]
        public void DefaultParamsProduceFiveDecks()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            Assert.That(seed.decks.Select(d => d.id), Is.EqualTo(new[] { "D1", "D2", "D3", "D4", "D5" }));
            Assert.That(seed.decks[4].z_surface, Is.EqualTo(15.8).Within(1e-9));
            Assert.That(seed.decks[0].z_surface, Is.EqualTo(5.4).Within(1e-9));
            Assert.That(seed.decks[2].z_surface, Is.EqualTo(10.6).Within(1e-9));
            Assert.That(seed.decks[2].z_clear, Is.EqualTo(2.2).Within(1e-9));
            var o = seed.decks[2].outline;
            Assert.That(o.Length, Is.EqualTo(5));
            Assert.That(o[0], Is.EqualTo(new double[] { 0, -12, 10.6 }));
            Assert.That(o[2], Is.EqualTo(new double[] { 120, 12, 10.6 }));
            Assert.That(o[4], Is.EqualTo(o[0]));
        }

        [Test]
        public void PillarsTwoRowsAlongPitch()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var d3 = seed.facilities.Where(f => f.deck_id == "D3" && f.kind == "pillar").ToList();
            // x = 12, 24, ..., 108 -> 9 per row, 2 rows
            Assert.That(d3.Count, Is.EqualTo(18));
            Assert.That(d3.All(f => f.footprint.Length == 5));
            Assert.That(d3.Select(f => f.z_min).Distinct().Single(), Is.EqualTo(10.6).Within(1e-9));
            Assert.That(d3.Select(f => f.z_max).Distinct().Single(), Is.EqualTo(12.8).Within(1e-9));
            var ys = d3.Select(f => (f.footprint[0][1] + f.footprint[2][1]) / 2).Distinct().OrderBy(y => y).ToList();
            Assert.That(ys, Is.EqualTo(new[] { -6.5, 6.5 }).Within(1e-9));
            Assert.That(d3[0].id, Does.StartWith("C-PILLAR-D3-"));
        }

        [Test]
        public void LashingGridSkipsPillars()
        {
            var p = new ShipParams { lengthM = 20, beamM = 10, deckCount = 1, pillarPitchM = 10, pillarInsetM = 2, lashingPitchM = 1 };
            var seed = ShipSeedBuilder.Build(p);
            var lp = seed.lashing_points;
            Assert.That(lp.All(l => l.kind == "cloverleaf" && l.deck_id == "D1"));
            // grid x in [1..19] step 1 (19), y in [-4..4] step 1 (9) = 171, minus points inside the two 0.6 m pillars at (10, ±3) -> (10,3),(10,-3)
            Assert.That(lp.Count, Is.EqualTo(171 - 2));
            Assert.That(lp.Any(l => l.position[0] == 10 && l.position[1] == 3), Is.False);
            Assert.That(lp.Select(l => l.id).Distinct().Count(), Is.EqualTo(lp.Count));
        }

        [Test]
        public void RampAndLanes()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var ramp = seed.ramps.Single(r => r.type == "stern_quarter");
            Assert.That(ramp.id, Is.EqualTo("RAMP-STERN"));
            Assert.That(ramp.hinge, Is.EqualTo(new[] { new double[] { 0, -6, 10.6 }, new double[] { 0, 6, 10.6 } }));
            Assert.That(ramp.angle_range_deg, Is.EqualTo(new double[] { -7, 4 }));
            Assert.That(ramp.connects_lane, Is.EqualTo("A2-D3-0001"));
            Assert.That(seed.lanes.Count, Is.EqualTo(5));
            var lane = seed.lanes.Single(l => l.deck_id == "D3");
            Assert.That(lane.centerline, Is.EqualTo(new[] { new double[] { 2, 0, 10.6 }, new double[] { 60, 0, 10.6 }, new double[] { 118, 0, 10.6 } }));
            Assert.That(lane.width_m, Is.EqualTo(3.2));
            // every other deck is joined by a route that U-turns in at x 8, so its lane starts there
            foreach (var l in seed.lanes.Where(l => l.deck_id != "D3")) Assert.That(l.centerline[0][0], Is.EqualTo(8).Within(1e-9), l.id);
        }

        [Test]
        public void SeedSerializesWithSpecKeys()
        {
            string json = MapJson.Serialize(ShipSeedBuilder.Build(new ShipParams()));
            Assert.That(json, Does.Contain("\"z_surface\"").And.Contain("\"lashing_points\"").And.Contain("\"footprint\""));
        }

        [Test]
        public void NonPositiveParamsThrow()
        {
            Assert.Throws<System.ArgumentException>(() => ShipSeedBuilder.Build(new ShipParams { lengthM = 0 }));
            Assert.Throws<System.ArgumentException>(() => ShipSeedBuilder.Build(new ShipParams { beamM = -1 }));
            Assert.Throws<System.ArgumentException>(() => ShipSeedBuilder.Build(new ShipParams { pillarPitchM = 0 }));
            Assert.Throws<System.ArgumentException>(() => ShipSeedBuilder.Build(new ShipParams { lashingPitchM = -0.1 }));
        }

        // ── M8: internal ramps and routes ─────────────────────────────────────────

        [Test]
        public void InternalRampsHingeOnTheUpperDeckAndSpanOnePitch()
        {
            var p = new ShipParams(); var seed = ShipSeedBuilder.Build(p);
            var inner = seed.ramps.Where(r => r.type == "internal_hoistable").ToList();
            Assert.That(inner.Select(r => r.id), Is.EquivalentTo(new[] { "RAMP-D3-D2", "RAMP-D2-D1", "RAMP-D3-D4", "RAMP-D4-D5" }));
            foreach (var r in inner)
            {
                double zu = seed.decks.Single(d => d.id == r.upper_deck).z_surface, zl = seed.decks.Single(d => d.id == r.lower_deck).z_surface;
                Assert.That(r.hinge[0][2], Is.EqualTo(zu).Within(1e-9), r.id + " swings about its upper end");
                Assert.That(r.toe[0][2], Is.EqualTo(zl).Within(1e-9), r.id);
                double dx = r.hinge[0][0] - r.toe[0][0], dz = zu - zl;
                Assert.That(System.Math.Sqrt(dx * dx + dz * dz), Is.EqualTo(r.length_m).Within(1e-4), r.id);
                Assert.That(System.Math.Abs(r.hinge[1][1] - r.hinge[0][1]), Is.EqualTo(r.width_m).Within(1e-9), r.id);
                // clear of the pillar rows (y +-6.5, 0.6 m) and inside the hull (|y| 12)
                double yLo = System.Math.Min(r.hinge[0][1], r.hinge[1][1]), yHi = System.Math.Max(r.hinge[0][1], r.hinge[1][1]);
                Assert.That(System.Math.Min(System.Math.Abs(yLo), System.Math.Abs(yHi)), Is.GreaterThan(6.8), r.id);
                Assert.That(System.Math.Max(System.Math.Abs(yLo), System.Math.Abs(yHi)), Is.LessThanOrEqualTo(p.beamM / 2), r.id);
            }
        }

        [Test]
        public void RoutesJoinTheEntranceToEveryOtherDecksLaneStart()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            Assert.That(seed.routes.Select(r => r.deck_id), Is.EquivalentTo(new[] { "D1", "D2", "D4", "D5" }));
            var d3Lane = seed.lanes.Single(l => l.deck_id == "D3");
            foreach (var r in seed.routes)
            {
                Assert.That(r.path[0], Is.EqualTo(d3Lane.centerline[0]).Within(1e-9), r.id + " starts where the stern ramp lands");
                var lane = seed.lanes.Single(l => l.deck_id == r.deck_id);
                Assert.That(r.path[^1], Is.EqualTo(lane.centerline[0]).Within(1e-6), r.id + " ends on its deck's lane start: no jump onto the lane");
            }
        }

        [Test]
        public void RouteRampsAreExactlyTheRampsItDrivesOver()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            foreach (var r in seed.routes)
            {
                var crossed = new System.Collections.Generic.List<string>();
                for (int i = 0; i + 1 < r.path.Length; i++)
                {
                    var a = r.path[i]; var b = r.path[i + 1];
                    if (System.Math.Abs(a[2] - b[2]) < 1e-9) continue;
                    var ramp = seed.ramps.Single(q => q.type == "internal_hoistable" && Ends(q, a, b));
                    crossed.Add(ramp.id);
                }
                Assert.That(crossed, Is.EqualTo(r.ramps), r.id);
            }

            static bool Ends(Ramp q, double[] a, double[] b)
            {
                double hx = q.hinge[0][0], tx = q.toe[0][0], y = (q.hinge[0][1] + q.hinge[1][1]) / 2;
                bool On(double[] pt, double x, double z) => System.Math.Abs(pt[0] - x) < 1e-6 && System.Math.Abs(pt[1] - y) < 1e-6 && System.Math.Abs(pt[2] - z) < 1e-6;
                return (On(a, hx, q.hinge[0][2]) && On(b, tx, q.toe[0][2])) || (On(a, tx, q.toe[0][2]) && On(b, hx, q.hinge[0][2]));
            }
        }

        [Test]
        public void RoutesClimbNoSteeperThanEightDegrees()
        {
            foreach (var r in ShipSeedBuilder.Build(new ShipParams()).routes)
                for (int i = 0; i + 1 < r.path.Length; i++)
                {
                    double dx = r.path[i + 1][0] - r.path[i][0], dy = r.path[i + 1][1] - r.path[i][1], dz = r.path[i + 1][2] - r.path[i][2];
                    double h = System.Math.Sqrt(dx * dx + dy * dy);
                    if (h < 1e-9) continue;
                    Assert.That(System.Math.Atan2(System.Math.Abs(dz), h) * 180 / System.Math.PI, Is.LessThanOrEqualTo(8), $"{r.id} segment {i}");
                }
        }

        /// The car's whole body, not its centre, along every route: 4.8 x 1.85 m sampled every 0.5 m, against every
        /// pillar on the deck it is driving on (and the hull sides / stern), including through the U-turns.
        [Test]
        public void ACarOnAnyRouteClearsEveryPillarAndTheHull()
        {
            var p = new ShipParams(); var seed = ShipSeedBuilder.Build(p);
            const double halfL = 2.4, halfW = 0.925, pill = 0.3;
            foreach (var r in seed.routes)
            {
                double total = 0; for (int i = 0; i + 1 < r.path.Length; i++) total += System.Math.Sqrt(System.Math.Pow(r.path[i + 1][0] - r.path[i][0], 2) + System.Math.Pow(r.path[i + 1][1] - r.path[i][1], 2));
                for (double s = 0; s <= total; s += 0.5)
                {
                    var q = LaneFollower.At(r.path, s);
                    double c = System.Math.Cos(q.headingRad), sn = System.Math.Sin(q.headingRad);
                    // the stern-ramp deck is open astern (that is where the car comes in); every other deck is closed there
                    bool closedStern = System.Math.Abs(q.z - p.DeckZ(p.rampDeckIndex)) > 1e-6;
                    foreach (var (lx, ly) in new[] { (halfL, halfW), (halfL, -halfW), (-halfL, halfW), (-halfL, -halfW) })
                    {
                        double wx = q.x + lx * c - ly * sn, wy = q.y + lx * sn + ly * c;
                        Assert.That(System.Math.Abs(wy), Is.LessThan(p.beamM / 2 - 0.1), $"{r.id} s {s:F1}: body through the hull side");
                        if (closedStern) Assert.That(wx, Is.GreaterThan(0), $"{r.id} s {s:F1}: body out past the stern");
                    }
                    var deck = seed.decks.FirstOrDefault(d => System.Math.Abs(d.z_surface - q.z) < 1e-6);
                    if (deck == null) continue;   // on a ramp: its strip is clear of the pillar rows (tested above)
                    foreach (var f in seed.facilities.Where(f => f.deck_id == deck.id && f.kind == "pillar"))
                    {
                        double px = (f.footprint[0][0] + f.footprint[2][0]) / 2 - q.x, py = (f.footprint[0][1] + f.footprint[2][1]) / 2 - q.y;
                        double ax = px * c + py * sn, ay = -px * sn + py * c;   // pillar centre in the car's frame
                        bool hit = System.Math.Abs(ax) < halfL + pill * 1.4143 && System.Math.Abs(ay) < halfW + pill * 1.4143;
                        Assert.That(hit, Is.False, $"{r.id} s {s:F1} on {deck.id}: body sweeps {f.id}");
                    }
                }
            }
        }
    }
}
