using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShipHdMap.Tests
{
    public class HudViewTests
    {
        [Test]
        public void LinesFormatContextCursorAndEstimate()
        {
            var idle = HudView.Lines("D3", "LM-0001", (48.094, 6.81, 11.67), null, default, "SHIP_AP");
            Assert.That(idle.Length, Is.EqualTo(2));
            Assert.That(idle[0], Is.EqualTo("deck D3   sel LM-0001"));
            Assert.That(idle[1], Does.StartWith("cursor x   48.09"));
            var r = new LocalizerResult { ok = true, pose = new Pose2D { x = 1.85, y = -0.14, psiRad = 0.8 * System.Math.PI / 180 }, nObs = 2, residualRms = 0.031, iterations = 3 };
            var drive = HudView.Lines("all", null, null, r, new Pose2D { x = 2, y = 0, psiRad = 0 }, "SHIP_AP");
            Assert.That(drive.Length, Is.EqualTo(6));
            Assert.That(drive[1], Is.EqualTo("cursor -"));
            Assert.That(drive[2], Does.Contain("N 2")); Assert.That(drive[2], Does.Contain("RMS 0.031"));
            Assert.That(drive[5], Does.StartWith("err  0.21 m"));
        }

        [Test]
        public void SensorLineCountsEveryVerdictAndTheTotal()
        {
            var why = new System.Collections.Generic.List<(string id, Miss miss)>
            {
                ("LM-0001", Miss.None), ("LM-0002", Miss.None), ("LM-0003", Miss.Fov), ("LM-0004", Miss.Fov),
                ("LM-0005", Miss.Fov), ("LM-0006", Miss.Range), ("LM-0007", Miss.Facing), ("LM-0008", Miss.Occluded),
                ("LM-0009", Miss.Blocked),
            };
            Assert.That(HudView.SensorLine(why), Is.EqualTo("seen 2 / 9   fov 3  range 1  facing 1  hidden 1  blocked 1"));
            // Nothing sensed is a different statement from "no eye is live", which is what null means.
            Assert.That(HudView.SensorLine(new System.Collections.Generic.List<(string, Miss)>()), Is.EqualTo("seen 0 / 0   fov 0  range 0  facing 0  hidden 0  blocked 0"));
            Assert.That(HudView.SensorLine(null), Is.Null);
        }

        [Test]
        public void StatusLineNamesTheProbeEyeEvenThoughTheCameraSaysDriver()
        {
            Assert.That(HudView.StatusLine(PlacerTool.Select, CamMode.Orbit, false), Is.EqualTo("tool select   cam orbit"));
            Assert.That(HudView.StatusLine(PlacerTool.Select, CamMode.Fly, false), Does.Contain("[WASD]"));
            Assert.That(HudView.StatusLine(PlacerTool.Probe, CamMode.Orbit, false), Does.Contain("click a deck"));
            // The probe parks the camera in Driver mode and deliberately does not tell the web, so the toolbar
            // still shows the orbit button lit. This line is the only thing on screen that says where the eye is.
            var probing = HudView.StatusLine(PlacerTool.Probe, CamMode.Driver, true);
            Assert.That(probing, Does.Contain("cam probe-eye"));
            Assert.That(probing, Does.Not.Contain("cam driver"));
        }

        [Test]
        public void HudAssetsExistInResources()
        {
            var ps = Resources.Load<PanelSettings>("HudPanelSettings");
            Assert.That(ps, Is.Not.Null, "run menu ShipHdMap/Create HUD Assets");
            Assert.That(ps.themeStyleSheet, Is.Not.Null);
            Assert.That(ps.scaleMode, Is.EqualTo(PanelScaleMode.ConstantPhysicalSize));
        }
    }
}
