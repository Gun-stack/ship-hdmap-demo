using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class AprilTagTests
    {
        [Test]
        public void HasTwentyDistinctCodes()
        {
            Assert.That(AprilTag36h11.Count, Is.EqualTo(20));
            var set = new System.Collections.Generic.HashSet<ulong>(AprilTag36h11.Codes);
            Assert.That(set.Count, Is.EqualTo(20));
            Assert.That(AprilTag36h11.Codes[0], Is.EqualTo(0xd5d628584UL)); // tag36h11 id 0
        }

        [Test]
        public void BitsHaveBlackBorderAndPayload()
        {
            var b = AprilTag36h11.Bits(0);
            Assert.That(b.GetLength(0), Is.EqualTo(8));
            for (int i = 0; i < 8; i++) { Assert.That(b[0, i], Is.False); Assert.That(b[7, i], Is.False); Assert.That(b[i, 0], Is.False); Assert.That(b[i, 7], Is.False); }
            int white = 0; for (int r = 1; r < 7; r++) for (int c = 1; c < 7; c++) if (b[r, c]) white++;
            Assert.That(white, Is.GreaterThan(0).And.LessThan(36));
        }

        [Test]
        public void TextureIsBlackAndWhiteOnly()
        {
            var t = AprilTag36h11.MakeTexture(3, 4);
            Assert.That(t.width, Is.EqualTo(32));
            foreach (var px in t.GetPixels()) Assert.That(px.r == 0f || px.r == 1f, px.ToString());
            Object.DestroyImmediate(t);
        }

        [Test]
        public void LandmarkSpawnAndModel()
        {
            var root = new GameObject("Landmarks");
            var lm = LandmarkMarker.Spawn(root.transform, "LM-0042", 42, ShipFrame.ToUnity(82.4, 3.1, 10.6), ShipFrame.ToUnity(0, -1, 0), 0.3f, "D3", "C-PILLAR-017");
            var m = lm.ToModel();
            Assert.That(m.id, Is.EqualTo("LM-0042"));
            Assert.That(m.position[0], Is.EqualTo(82.4).Within(1e-4));
            Assert.That(m.position[1], Is.EqualTo(3.1).Within(1e-4));
            Assert.That(m.normal, Is.EqualTo(new double[] { 0, -1, 0 }).Within(1e-4));
            Assert.That(m.marker.family, Is.EqualTo("apriltag-36h11"));
            Assert.That(lm.GetComponent<BoxCollider>(), Is.Not.Null);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void NextIdIsMonotonicRegardlessOfAllCount()
        {
            var go = new GameObject("Placer");
            var placer = go.AddComponent<LandmarkPlacer>();
            var root = new GameObject("Landmarks");

            Assert.That(placer.NextId(), Is.EqualTo("LM-0001"));
            Assert.That(placer.NextId(), Is.EqualTo("LM-0002"));
            var a = LandmarkMarker.Spawn(root.transform, "LM-0001", 1, Vector3.zero, Vector3.up, 0.3f, "D3", "C-PILLAR-001");
            var b = LandmarkMarker.Spawn(root.transform, "LM-0002", 2, Vector3.zero, Vector3.up, 0.3f, "D3", "C-PILLAR-002");
            placer.All.Add(a); placer.All.Add(b);

            // simulate a delete: All shrinks, but the id sequence must not depend on All.Count
            placer.All.Remove(a);
            Assert.That(placer.NextId(), Is.EqualTo("LM-0003"));

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(go);
        }
    }
}
