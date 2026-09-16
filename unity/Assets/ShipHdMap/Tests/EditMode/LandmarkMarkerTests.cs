using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class LandmarkMarkerTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); }

        [Test]
        public void MoveToRepositionsAndReorients()
        {
            go = new GameObject("root");
            var lm = LandmarkMarker.Spawn(go.transform, "LM-T", 1, ShipFrame.ToUnity(10, -6, 11), ShipFrame.ToUnity(0, 1, 0), 0.3f, "D3", "P1");
            lm.MoveTo(ShipFrame.ToUnity(20, 6, 11), ShipFrame.ToUnity(0, -1, 0), "D3", "P2");
            var m = lm.ToModel();
            Assert.That(m.position[0], Is.EqualTo(20).Within(1e-3));
            Assert.That(m.position[1], Is.EqualTo(6).Within(1e-3));
            Assert.That(m.position[2], Is.EqualTo(11).Within(1e-3));
            Assert.That(m.normal[1], Is.EqualTo(-1).Within(1e-3));
            Assert.That(m.mounted_on, Is.EqualTo("P2"));
        }
    }
}
