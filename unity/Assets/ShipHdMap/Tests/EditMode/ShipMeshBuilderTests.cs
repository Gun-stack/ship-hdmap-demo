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
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p));
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
            var bow = ship.transform.Find("D3/Bow");
            Assert.That(bow, Is.Not.Null);
            Assert.That(bow.GetComponent<Collider>().bounds.center.x, Is.EqualTo(119.9f).Within(0.05f)); // 0.2 m thick wall centred at x = 119.9, inner face at x = 119.8
            Assert.That(ship.transform.Find("Ramp/Plate"), Is.Not.Null);
        }

        [Test]
        public void RampAngleRotatesAboutHinge()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p));
            var ramp = ship.transform.Find("Ramp");
            ShipMeshBuilder.SetRampAngle(ship, 0, 0);
            float minX0 = ramp.Find("Plate").GetComponent<Collider>().bounds.min.x; // far end of ramp is at x = -30
            Assert.That(minX0, Is.EqualTo(-30f).Within(0.05f));
            ShipMeshBuilder.SetRampAngle(ship, 4, 0);
            var b = ramp.Find("Plate").GetComponent<Collider>().bounds;
            Assert.That(b.min.x, Is.GreaterThan(-30f));      // shortened footprint
            Assert.That(ramp.position.y, Is.EqualTo(10.6f).Within(1e-3)); // hinge does not move
        }

        [Test]
        public void BuildFromMapMatchesBuildFromSeed()
        {
            // Coarser lashing pitch than the default, purely for speed: this test builds the hull twice and the
            // default pitch puts thousands of lashing sockets on every deck -- bearable once, slow twice.
            var p = new ShipParams { lashingPitchM = 4 };
            var seed = ShipSeedBuilder.Build(p);
            var map = new VehicleMap { decks = seed.decks, facilities = seed.facilities, ramps = seed.ramps, lashing_points = seed.lashing_points };
            var fromSeed = ShipMeshBuilder.Build(seed);
            var fromMap = ShipMeshBuilder.Build(map);
            foreach (var d in seed.decks)
            {
                var a = fromSeed.transform.Find($"{d.id}/Floor"); var b = fromMap.transform.Find($"{d.id}/Floor");
                Assert.That(b, Is.Not.Null, "deck " + d.id);
                Assert.That(b.localPosition, Is.EqualTo(a.localPosition));
                Assert.That(b.localScale, Is.EqualTo(a.localScale));
                Assert.That(fromMap.transform.Find($"{d.id}/Pillars").childCount, Is.EqualTo(fromSeed.transform.Find($"{d.id}/Pillars").childCount));
            }
            Assert.That(fromMap.transform.Find("Ramp").localPosition, Is.EqualTo(fromSeed.transform.Find("Ramp").localPosition));
            Object.DestroyImmediate(fromSeed); Object.DestroyImmediate(fromMap);
        }

        [Test]
        public void HullFollowsAnOutlineThatDoesNotStartAtTheOrigin()
        {
            // The builder read only the outline's EXTENT and then placed the floor at L/2 about y = 0, so a deck that
            // starts forward of the AP -- or does not straddle the centreline -- was drawn at the origin regardless of
            // what the map said. Extent 80 x 12, centred on ship (60, 2).
            var map = new VehicleMap { decks = new System.Collections.Generic.List<Deck> { new Deck { id = "D1", z_surface = 5.4, z_clear = 2.2,
                outline = new[] { new[] { 20.0, -4.0, 5.4 }, new[] { 100.0, -4.0, 5.4 }, new[] { 100.0, 8.0, 5.4 }, new[] { 20.0, 8.0, 5.4 }, new[] { 20.0, -4.0, 5.4 } } } } };
            ship = ShipMeshBuilder.Build(map);
            var floor = ship.transform.Find("D1/Floor");
            Assert.That(floor.localPosition.x, Is.EqualTo(60f).Within(1e-3f), "(20 + 100) / 2, not the extent's own half");
            Assert.That(floor.localPosition.z, Is.EqualTo(-2f).Within(1e-3f), "Unity z = -(ship y centre)");
            Assert.That(floor.localScale.x, Is.EqualTo(80f).Within(1e-3f));
            Assert.That(floor.localScale.z, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(ship.transform.Find("D1/Bow").localPosition.x, Is.EqualTo(100f - 0.1f).Within(1e-3f), "the bow bulkhead closes the deck at its own forward end");
            Assert.That(ship.transform.Find("D1/HullPort").localPosition.z, Is.EqualTo(-8f).Within(1e-3f), "port is ship +y, i.e. Unity -z");
        }

        [Test]
        public void ADeckWithoutAnOutlineIsSkippedInsteadOfThrowing()
        {
            // MapRuntime.ShipSignature already defends against a missing outline; the builder used to throw on the
            // same map, which would abort a Load with the scene half rebuilt.
            var map = new VehicleMap { decks = new System.Collections.Generic.List<Deck> {
                new Deck { id = "D1", z_surface = 5.4, z_clear = 2.2, outline = null },
                new Deck { id = "D2", z_surface = 8.0, z_clear = 2.2, outline = new double[0][] } } };
            Assert.That(() => ship = ShipMeshBuilder.Build(map), Throws.Nothing);
            Assert.That(ship.transform.Find("D1"), Is.Null);
            Assert.That(ship.transform.Find("D2"), Is.Null);
        }

        [Test]
        public void DeckVisibilityFadesOthers()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p));
            ShipMeshBuilder.SetDeckVisibility(ship, "D3");
            var d1FloorRenderer = ship.transform.Find("D1/Floor").GetComponent<Renderer>();
            float a1 = d1FloorRenderer.sharedMaterial.color.a;
            float a3 = ship.transform.Find("D3/Floor").GetComponent<Renderer>().sharedMaterial.color.a;
            Assert.That(a1, Is.LessThan(0.5f));
            Assert.That(a3, Is.EqualTo(1f).Within(1e-3));

            // Repeated deck switches must reuse each renderer's instanced material, not clone a fresh one every call.
            var firstMaterial = d1FloorRenderer.sharedMaterial;
            ShipMeshBuilder.SetDeckVisibility(ship, "D3");
            Assert.That(ReferenceEquals(d1FloorRenderer.sharedMaterial, firstMaterial), Is.True);
        }
    }
}
