using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ShipMeshBuilderTests
    {
        GameObject ship;
        [TearDown] public void Cleanup() { if (ship) Object.DestroyImmediate(ship); }

        [Test]
        public void BuildsDeckHierarchyInUnityCoordinates()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            Assert.That(ship.name, Is.EqualTo("Ship"));
            var floor = ship.transform.Find("D3/Floor");
            Assert.That(floor, Is.Not.Null);
            // Deck 3 floor top at Unity y = 10.6, centred at x = 60, z = 0
            Assert.That(floor.GetComponent<Collider>().bounds.max.y, Is.EqualTo(10.6f).Within(1e-3));
            Assert.That(floor.GetComponent<Collider>().bounds.center.x, Is.EqualTo(60f).Within(1e-3));
            var pillar = ship.transform.Find("D3/Pillars/C-PILLAR-D3-001");
            Assert.That(pillar, Is.Not.Null);
            // first pillar: ship (12, -6.5) -> Unity (12, *, +6.5)
            Assert.That(pillar.position.x, Is.EqualTo(12f).Within(1e-3));
            Assert.That(pillar.position.z, Is.EqualTo(6.5f).Within(1e-3));
            var mep = ship.transform.Find("D3/MEP");
            Assert.That(mep.childCount, Is.EqualTo(3));
            foreach (Transform pipe in mep)
            {
                Assert.That(pipe.GetComponent<BoxCollider>(), Is.Not.Null, $"{pipe.name} should have a BoxCollider");
                Assert.That(pipe.GetComponent<CapsuleCollider>(), Is.Null, $"{pipe.name} should not have a CapsuleCollider");
            }
            Assert.That(ship.transform.Find("Ramp/Plate"), Is.Not.Null);
        }

        [Test]
        public void RampAngleRotatesAboutHinge()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            var ramp = ship.transform.Find("Ramp");
            ShipMeshBuilder.SetRampAngle(ship, 0);
            float y0 = ramp.Find("Plate").GetComponent<Collider>().bounds.min.x; // far end of ramp is at x = -30
            Assert.That(y0, Is.EqualTo(-30f).Within(0.05f));
            ShipMeshBuilder.SetRampAngle(ship, 4);
            var b = ramp.Find("Plate").GetComponent<Collider>().bounds;
            Assert.That(b.min.x, Is.GreaterThan(-30f));      // shortened footprint
            Assert.That(ramp.position.y, Is.EqualTo(10.6f).Within(1e-3)); // hinge does not move
        }

        [Test]
        public void DeckVisibilityFadesOthers()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            ShipMeshBuilder.SetDeckVisibility(ship, "D3");
            float a1 = ship.transform.Find("D1/Floor").GetComponent<Renderer>().sharedMaterial.color.a;
            float a3 = ship.transform.Find("D3/Floor").GetComponent<Renderer>().sharedMaterial.color.a;
            Assert.That(a1, Is.LessThan(0.5f));
            Assert.That(a3, Is.EqualTo(1f).Within(1e-3));
        }
    }
}
