using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MapModelTests
    {
        static string FixturePath() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"));

        [Test]
        public void FixtureParses()
        {
            var map = MapJson.Parse<VehicleMap>(File.ReadAllText(FixturePath()));
            Assert.That(map.schema, Is.EqualTo("ship-hdmap/vehicle-map/1.0"));
            Assert.That(map.decks.Count, Is.EqualTo(3));
            Assert.That(map.decks[0].outline.Length, Is.EqualTo(5));
            Assert.That(map.landmarks.Count, Is.EqualTo(6));
            Assert.That(map.landmarks[0].marker.code, Is.EqualTo(1));
            Assert.That(map.landmarks[0].normal, Is.EqualTo(new double[] { 0, 1, 0 }));
            Assert.That(map.parking_slots[0].target_pose.heading_deg, Is.EqualTo(0));
            Assert.That(map.parking_slots[0].lashing_points.Count, Is.EqualTo(4));
            Assert.That(map.ramps[0].transition_landmarks, Is.EqualTo(new[] { "LM-0001", "LM-0002" }));
        }

        [Test]
        public void RoundTripKeepsKeys()
        {
            string original = File.ReadAllText(FixturePath());
            var map = MapJson.Parse<VehicleMap>(original);
            string again = MapJson.Serialize(map);
            var back = MapJson.Parse<VehicleMap>(again);
            Assert.That(MapJson.Serialize(back), Is.EqualTo(again));
            Assert.That(again, Does.Contain("\"map_id\""));
            Assert.That(again, Does.Contain("\"z_surface\""));
            Assert.That(again, Does.Not.Contain("\"MapId\""));
        }

        [Test]
        public void NullListsAreOmitted()
        {
            string json = MapJson.Serialize(new SeedData { decks = new System.Collections.Generic.List<Deck>() });
            Assert.That(json, Does.Contain("\"decks\""));
            Assert.That(json, Does.Not.Contain("\"facilities\""));
        }
    }
}
