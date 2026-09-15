using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public static class ShipSeedBuilder
    {
        public static SeedData Build(ShipParams p)
        {
            var seed = new SeedData { decks = new(), facilities = new(), lashing_points = new(), ramps = new(), lanes = new() };
            double hb = p.beamM / 2;
            for (int i = 0; i < p.deckCount; i++)
            {
                string deckId = $"D{i + 1}"; double z = p.DeckZ(i);
                seed.decks.Add(new Deck { id = deckId, name = $"Deck {i + 1}", z_surface = z, z_clear = p.deckClearM, movable = false,
                    outline = Rect(0, -hb, p.lengthM, hb, z) });

                var pillars = new List<Facility>(); int n = 0;
                foreach (double y in new[] { -(hb - p.pillarInsetM), hb - p.pillarInsetM })
                    for (double x = p.pillarPitchM; x < p.lengthM - 1e-9; x += p.pillarPitchM)
                    {
                        double h = p.pillarSizeM / 2;
                        pillars.Add(new Facility { id = $"C-PILLAR-{deckId}-{++n:000}", kind = "pillar", deck_id = deckId,
                            footprint = Rect(x - h, y - h, x + h, y + h, z), z_min = z, z_max = z + p.deckClearM });
                    }
                seed.facilities.AddRange(pillars);

                int m = 0;
                for (double x = 1; x <= p.lengthM - 1 + 1e-9; x += p.lashingPitchM)
                    for (double y = -hb + 1; y <= hb - 1 + 1e-9; y += p.lashingPitchM)
                    {
                        if (InsideAnyPillar(x, y, pillars)) continue;
                        seed.lashing_points.Add(new LashingPoint { id = $"LP-{deckId}-{++m:0000}", kind = "cloverleaf", deck_id = deckId,
                            position = new[] { Math.Round(x, 6), Math.Round(y, 6), z } });
                    }

                seed.lanes.Add(new Lane { id = $"A2-{deckId}-0001", deck_id = deckId, width_m = p.laneWidthM, direction = "forward", speed_limit_kmh = p.laneSpeedKmh, next = new(),
                    centerline = new[] { new[] { 2.0, 0, z }, new[] { p.lengthM / 2, 0, z }, new[] { p.lengthM - 2, 0, z } } });
            }
            double rz = p.DeckZ(p.rampDeckIndex); double hw = p.rampWidthM / 2;
            seed.ramps.Add(new Ramp { id = "RAMP-STERN", type = "stern_quarter", hinge = new[] { new[] { 0.0, -hw, rz }, new[] { 0.0, hw, rz } },
                length_m = p.rampLengthM, width_m = p.rampWidthM, angle_range_deg = new[] { p.rampAngleMin, p.rampAngleMax },
                connects_lane = $"A2-D{p.rampDeckIndex + 1}-0001", transition_landmarks = new() });
            return seed;
        }

        static double[][] Rect(double x0, double y0, double x1, double y1, double z) =>
            new[] { new[] { x0, y0, z }, new[] { x1, y0, z }, new[] { x1, y1, z }, new[] { x0, y1, z }, new[] { x0, y0, z } };

        static bool InsideAnyPillar(double x, double y, List<Facility> pillars)
        {
            foreach (var f in pillars)
                if (x >= f.footprint[0][0] - 1e-9 && x <= f.footprint[2][0] + 1e-9 && y >= f.footprint[0][1] - 1e-9 && y <= f.footprint[2][1] + 1e-9) return true;
            return false;
        }
    }
}
