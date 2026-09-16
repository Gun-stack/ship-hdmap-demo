using System.Linq;
using NUnit.Framework;
using ShipHdMap.Editor;

namespace ShipHdMap.Tests
{
    public class FixtureExporterTests
    {
        [Test]
        public void SeedLandmarksSitOnPillarInnerFacesAndHull()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var lms = FixtureExporter.SeedLandmarks(seed);
            Assert.That(lms.Count, Is.EqualTo(19));
            var lm1 = lms.First(l => l.id == "LM-0001");
            Assert.That(lm1.position, Is.EqualTo(new[] { 12.0, -6.2, 11.8 }).Within(1e-9));   // inner face of the first -y pillar, tag 1.2 m above Deck 3
            Assert.That(lm1.normal, Is.EqualTo(new[] { 0.0, 1.0, 0.0 }));
            Assert.That(lm1.mounted_on, Is.EqualTo("C-PILLAR-D3-001"));
            var lm2 = lms.First(l => l.id == "LM-0002");
            Assert.That(lm2.position, Is.EqualTo(new[] { 12.0, 6.2, 11.8 }).Within(1e-9));
            Assert.That(lm2.normal, Is.EqualTo(new[] { 0.0, -1.0, 0.0 }));
            var hull = lms.First(l => l.id == "LM-0019");
            Assert.That(hull.position, Is.EqualTo(new[] { 40.0, 11.9, 11.8 }).Within(1e-9));  // port hull (outline y max) minus 0.1
            Assert.That(hull.mounted_on, Is.EqualTo("HULL-PORT"));
        }

        [Test]
        public void SeedLandmarksFollowAChangedShip()
        {
            var p = new ShipParams { beamM = 30, firstDeckZ = 6.0 };  // D3 at 11.2, pillars at y = ±(15 − 5.5)
            var lms = FixtureExporter.SeedLandmarks(ShipSeedBuilder.Build(p));
            var lm1 = lms.First(l => l.id == "LM-0001");
            Assert.That(lm1.position[1], Is.EqualTo(-9.2).Within(1e-9));
            Assert.That(lm1.position[2], Is.EqualTo(11.2 + 1.2).Within(1e-9));
            Assert.That(lms.First(l => l.id == "LM-0019").position[1], Is.EqualTo(14.9).Within(1e-9));
        }

        [Test]
        public void BuildMapExportsTheWholeDeckThreeLashingGrid()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var map = FixtureExporter.BuildMap(seed, new VehicleMap(), FixtureExporter.SeedLandmarks(seed));
            Assert.That(map.lashing_points.Count, Is.EqualTo(seed.lashing_points.Count(l => l.deck_id == "D3")));
            Assert.That(map.lashing_points.Count, Is.GreaterThan(1000));
        }
    }
}
