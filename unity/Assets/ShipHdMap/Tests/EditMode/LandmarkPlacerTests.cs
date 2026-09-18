using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class LandmarkPlacerTests
    {
        /// The whole point of M5e: in Select mode a click on ship structure must NOT spawn a marker.
        /// Before this, "left click on ship structure spawns a marker" made the 3D view unusable for anything else.
        [Test]
        public void SelectToolNeverPlaces()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, false, true), Is.EqualTo(ClickAct.Clear));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, false, false), Is.EqualTo(ClickAct.Clear));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, true, true), Is.EqualTo(ClickAct.DragMarker));
        }

        [Test]
        public void PlaceToolPlacesOnStructureAndStillSelectsMarkers()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, false, true), Is.EqualTo(ClickAct.Place));
            // a marker under the cursor wins: it is the only way to grab one without leaving the tool
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, true, true), Is.EqualTo(ClickAct.SelectMarker));
            // empty space places nothing and clears nothing -- an accidental miss must not lose the selection
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, false, false), Is.EqualTo(ClickAct.None));
        }

        [Test]
        public void ProbeToolProbesTheSurfaceItHits()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, false, true), Is.EqualTo(ClickAct.Probe));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, true, true), Is.EqualTo(ClickAct.SelectMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, false, false), Is.EqualTo(ClickAct.None));
        }

        /// Only Select drags. Dragging in Place mode would move the marker you just put down while you
        /// were reaching for the next spot.
        [Test]
        public void OnlySelectDrags()
        {
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Select, true, false), Is.EqualTo(ClickAct.DragMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Place, true, false), Is.EqualTo(ClickAct.SelectMarker));
            Assert.That(LandmarkPlacer.Decide(PlacerTool.Probe, true, false), Is.EqualTo(ClickAct.SelectMarker));
        }
    }
}
