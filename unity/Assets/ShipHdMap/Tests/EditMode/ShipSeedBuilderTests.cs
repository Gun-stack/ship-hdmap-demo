using System.Linq;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class ShipSeedBuilderTests
    {
        [Test]
        public void DefaultParamsProduceThreeDecks()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            Assert.That(seed.decks.Select(d => d.id), Is.EqualTo(new[] { "D1", "D2", "D3" }));
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
            var ramp = seed.ramps.Single();
            Assert.That(ramp.id, Is.EqualTo("RAMP-STERN"));
            Assert.That(ramp.hinge, Is.EqualTo(new[] { new double[] { 0, -6, 10.6 }, new double[] { 0, 6, 10.6 } }));
            Assert.That(ramp.angle_range_deg, Is.EqualTo(new double[] { -7, 4 }));
            Assert.That(ramp.connects_lane, Is.EqualTo("A2-D3-0001"));
            Assert.That(seed.lanes.Count, Is.EqualTo(3));
            var lane = seed.lanes.Single(l => l.deck_id == "D3");
            Assert.That(lane.centerline, Is.EqualTo(new[] { new double[] { 2, 0, 10.6 }, new double[] { 60, 0, 10.6 }, new double[] { 118, 0, 10.6 } }));
            Assert.That(lane.width_m, Is.EqualTo(3.2));
        }

        [Test]
        public void SeedSerializesWithSpecKeys()
        {
            string json = MapJson.Serialize(ShipSeedBuilder.Build(new ShipParams()));
            Assert.That(json, Does.Contain("\"z_surface\"").And.Contain("\"lashing_points\"").And.Contain("\"footprint\""));
        }
    }
}
