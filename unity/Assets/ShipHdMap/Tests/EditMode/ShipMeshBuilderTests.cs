using System.Linq;
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
            Assert.That(ship.transform.Find("D3/MEP"), Is.Null, "M8: no MEP -- the pipes hid the lanes in the orbit view");
            Assert.That(ship.transform.Find("D3/Lashing").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(1000 * 7), "every socket, one mesh");
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
                Assert.That(b.GetComponent<Renderer>().bounds, Is.EqualTo(a.GetComponent<Renderer>().bounds));
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
            Assert.That(floor.GetComponent<Renderer>().bounds.center.x, Is.EqualTo(60f).Within(1e-3f), "(20 + 100) / 2, not the extent's own half");
            Assert.That(floor.GetComponent<Renderer>().bounds.center.z, Is.EqualTo(-2f).Within(1e-3f), "Unity z = -(ship y centre)");
            Assert.That(floor.GetComponent<Renderer>().bounds.size.x, Is.EqualTo(80f).Within(1e-3f));
            Assert.That(floor.GetComponent<Renderer>().bounds.size.z, Is.EqualTo(12f).Within(1e-3f));
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

        [Test]
        public void TheSternRampIsFoundByNameWhateverOrderTheMapListsRampsIn()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            seed.ramps.Reverse();                                   // the API lists them in id order: RAMP-D2-D1 first
            ship = ShipMeshBuilder.Build(seed);
            var stern = ship.transform.Find("Ramp");
            Assert.That(stern, Is.Not.Null);
            Assert.That(stern.localPosition.x, Is.EqualTo(0f).Within(1e-4f), "\"Ramp\" is the stern ramp at the AP");
            Assert.That(ship.transform.Cast<Transform>().Count(t => t.name == "Ramp"), Is.EqualTo(1));
            Assert.That(ship.transform.Find(ShipMeshBuilder.InnerRampName("RAMP-D3-D2")), Is.Not.Null);
        }

        [Test]
        public void AnInternalRampSwingsItsToeOntoTheLowerDeckAndStowsFlush()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            ship = ShipMeshBuilder.Build(seed);
            foreach (var r in seed.ramps.Where(r => r.type == "internal_hoistable"))
            {
                var t = ship.transform.Find(ShipMeshBuilder.InnerRampName(r.id));
                var plate = t.Find("Plate");
                // deployed: the plate's far end sits on the toe height
                float farEnd = plate.TransformPoint(new Vector3(0.5f * Mathf.Sign(plate.localPosition.x), 0.5f, 0)).y;
                Assert.That(farEnd, Is.EqualTo((float)r.toe[0][2]).Within(0.05f), r.id + " deployed reaches the lower deck");
                ShipMeshBuilder.SetInnerRamp(ship, r.id, deployed: false, animate: false);
                farEnd = plate.TransformPoint(new Vector3(0.5f * Mathf.Sign(plate.localPosition.x), 0.5f, 0)).y;
                Assert.That(farEnd, Is.EqualTo((float)r.hinge[0][2]).Within(0.01f), r.id + " stowed is flush with the upper deck");
            }
        }

        [Test]
        public void TheFloorHasAHoleWhereARampSwingsUpAndNowhereElse()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            ship = ShipMeshBuilder.Build(seed);
            Physics.SyncTransforms();
            bool Solid(string deck, double x, double y, double z)
            {
                var from = ShipFrame.ToUnity(x, y, z + 1); var to = ShipFrame.ToUnity(x, y, z - 0.5);
                return Physics.Linecast(from, to, out var hit) && hit.collider.transform.IsChildOf(ship.transform.Find(deck));
            }
            // RAMP-D3-D2 hangs from D3 at x 14.17..34, port strip y 7.8..11.8: open there on D3 (while deployed), floor on D2
            ShipMeshBuilder.SetInnerRamp(ship, "RAMP-D3-D2", deployed: true, animate: false);
            Physics.SyncTransforms();
            Assert.That(Solid("D3", 24, 9.8, 10.6), Is.False, "D3 is open over the ramp");
            Assert.That(Solid("D3", 24, 0, 10.6), Is.True);
            Assert.That(Solid("D3", 24, -9.8, 10.6), Is.True, "RAMP-D3-D4 opens D4, not D3");
            Assert.That(Solid("D4", 24, -9.8, 13.2), Is.False);
        }

        [Test]
        public void SlabPiecesCoverTheDeckButTheHoles()
        {
            var pieces = ShipMeshBuilder.SlabPieces(0, -12, 120, 12, new[] { new[] { 14.0, 7.8, 34.0, 11.8 } });
            double area = pieces.Sum(p => (p.x1 - p.x0) * (p.y1 - p.y0));
            Assert.That(area, Is.EqualTo(120 * 24 - 20 * 4).Within(1e-6));
            Assert.That(pieces.Any(p => p.x0 < 24 && p.x1 > 24 && p.y0 < 9.8 && p.y1 > 9.8), Is.False);
        }

        [Test]
        public void CutawayTakesThePortSideAndRoofOffAndSingleDeckViewHidesTheShell()
        {
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(new ShipParams { lashingPitchM = 4 }));
            var port = ship.transform.Find("Shell/ShellPort").GetComponentInChildren<Renderer>();
            var stbd = ship.transform.Find("Shell/Topside").GetComponent<Renderer>();
            Assert.That(port.enabled, Is.False, "cutaway is the default");
            Assert.That(stbd.enabled, Is.True);
            HullBuilder.SetCutaway(ship, false);
            Assert.That(port.enabled, Is.True);
            ShipMeshBuilder.SetDeckVisibility(ship, "D3");
            Assert.That(stbd.enabled, Is.False);
            Assert.That(ship.transform.Find("D4/Floor").GetComponent<Renderer>().enabled, Is.False, "a deck above the one in view is hidden, not faded");
            Assert.That(ship.transform.Find("D2/Floor").GetComponent<Renderer>().enabled, Is.True);
            ShipMeshBuilder.SetDeckVisibility(ship, "all");
            Assert.That(port.enabled, Is.True, "back to full shell, as it was left");
            Assert.That(ship.transform.Find("D4/Floor").GetComponent<Renderer>().enabled, Is.True);
        }

        /// A zero normal lights as NaN: black on screen, and bloom then spreads it over the whole frame. Every procedural
        /// mesh the ship builds (floors, sockets, shell) must carry unit normals.
        [Test]
        public void EveryGeneratedMeshHasRealNormals()
        {
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(new ShipParams { lashingPitchM = 4 }));
            int meshes = 0;
            foreach (var f in ship.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = f.sharedMesh; if (!m || !m.name.EndsWith("~gen")) continue;
                meshes++;
                foreach (var n in m.normals) Assert.That(n.magnitude, Is.GreaterThan(0.5f), $"{f.name}: a zero normal");
            }
            Assert.That(meshes, Is.GreaterThan(10), "the shell, floors and sockets are all procedural");
        }
    }
}
