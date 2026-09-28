using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public static class ShipSeedBuilder
    {
        public static SeedData Build(ShipParams p)
        {
            if (p.lengthM <= 0) throw new ArgumentException("lengthM must be positive", nameof(p.lengthM));
            if (p.beamM <= 0) throw new ArgumentException("beamM must be positive", nameof(p.beamM));
            if (p.pillarPitchM <= 0) throw new ArgumentException("pillarPitchM must be positive", nameof(p.pillarPitchM));
            if (p.lashingPitchM <= 0) throw new ArgumentException("lashingPitchM must be positive", nameof(p.lashingPitchM));

            var seed = new SeedData { decks = new(), facilities = new(), lashing_points = new(), ramps = new(), lanes = new(), routes = new() };
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

                double x0 = i == p.rampDeckIndex ? p.laneStartX : p.routedLaneStartX;
                seed.lanes.Add(new Lane { id = $"A2-{deckId}-0001", deck_id = deckId, width_m = p.laneWidthM, direction = "forward", speed_limit_kmh = p.laneSpeedKmh, next = new(),
                    centerline = new[] { new[] { x0, 0, z }, new[] { p.lengthM / 2, 0, z }, new[] { p.lengthM - 2, 0, z } } });
            }
            double rz = p.DeckZ(p.rampDeckIndex); double hw = p.rampWidthM / 2;
            seed.ramps.Add(new Ramp { id = "RAMP-STERN", type = "stern_quarter", hinge = new[] { new[] { 0.0, -hw, rz }, new[] { 0.0, hw, rz } },
                length_m = p.rampLengthM, width_m = p.rampWidthM, angle_range_deg = new[] { p.rampAngleMin, p.rampAngleMax },
                connects_lane = $"A2-D{p.rampDeckIndex + 1}-0001", transition_landmarks = new() });
            InternalRamps(p, seed);
            return seed;
        }

        /// The hoistable ramps and the route to every deck but the stern-ramp one. Two chains leave the stern-ramp deck:
        /// down on the port side, up on starboard. The j-th ramp of a chain joins its NEAR deck (the one closer to the
        /// stern-ramp deck, where traffic comes from) to its FAR deck:
        ///   near deck: centreline -> U-turn at innerTurnX[j] onto the ramp strip, heading aft -> ramp at innerRampX[j]
        ///   ramp:      runs aft, dropping or climbing one deck pitch over its length
        ///   far deck:  on aft along the strip -> U-turn at routedLaneStartX back onto the centreline, heading forward
        /// The hinge is the ramp's UPPER end, so going down it is at the near end and going up at the far end.
        static void InternalRamps(ShipParams p, SeedData seed)
        {
            double run = Math.Sqrt(p.innerRampLengthM * p.innerRampLengthM - p.deckPitchM * p.deckPitchM);
            double deployDeg = Math.Round(Math.Asin(p.deckPitchM / p.innerRampLengthM) * 180 / Math.PI, 3), hw = p.innerRampWidthM / 2;
            foreach (int step in new[] { -1, +1 })
            {
                double side = step < 0 ? +1 : -1;                  // down chain on port (+y), up chain on starboard (-y)
                double y = side * p.innerRampSideY, r = p.UTurnRadius;
                var chainRamps = new List<string>();
                var path = new List<double[]> { new[] { p.laneStartX, 0, p.DeckZ(p.rampDeckIndex) } };
                for (int near = p.rampDeckIndex, j = 0; near + step >= 0 && near + step < p.deckCount; near += step, j++)
                {
                    if (j >= p.innerRampX.Length) throw new ArgumentException($"no ramp station for chain step {j}", nameof(p.innerRampX));
                    int far = near + step;
                    double zn = p.DeckZ(near), zf = p.DeckZ(far), xn = Math.Round(p.innerRampX[j], 6), xf = Math.Round(xn - run, 6);
                    string nearId = $"D{near + 1}", farId = $"D{far + 1}", id = $"RAMP-{nearId}-{farId}";
                    bool down = far < near;
                    double hx = down ? xn : xf, hz = down ? zn : zf, tx = down ? xf : xn, tz = down ? zf : zn;
                    seed.ramps.Add(new Ramp { id = id, type = "internal_hoistable", length_m = p.innerRampLengthM, width_m = p.innerRampWidthM,
                        angle_range_deg = new[] { 0, deployDeg }, transition_landmarks = new(),
                        lower_deck = down ? farId : nearId, upper_deck = down ? nearId : farId,
                        hinge = new[] { new[] { hx, R(y - hw), hz }, new[] { hx, R(y + hw), hz } }, toe = new[] { new[] { tx, R(y - hw), tz }, new[] { tx, R(y + hw), tz } } });
                    chainRamps.Add(id);

                    // near deck: along the centreline to the turn, U-turn (forward bulge) onto the strip, aft to the ramp
                    path.Add(new[] { p.innerTurnX[j], 0, zn });
                    Arc(path, p.innerTurnX[j], side * r, r, -90, 90, side, zn);
                    path.Add(new[] { xn, y, zn });
                    // the ramp itself, then on aft along the strip and U-turn (aft bulge) back onto the far deck's centreline
                    path.Add(new[] { xf, y, zf });
                    path.Add(new[] { p.routedLaneStartX, y, zf });
                    Arc(path, p.routedLaneStartX, side * r, r, 90, 270, side, zf);
                    // ends on (routedLaneStartX, 0, zf): the far deck's lane start, and the next leg's starting point
                    seed.routes.Add(new Route { id = $"ROUTE-{farId}", deck_id = farId, ramps = new List<string>(chainRamps), path = Dedup(path) });
                }
            }
        }

        /// Points of a half-turn around (cx, cy): theta runs thetaFrom..thetaTo (deg), x = cx + r cos, y = cy + side r sin.
        /// side flips the turn for the starboard chain, so the same theta range bulges the same way fore/aft on both sides.
        static void Arc(List<double[]> path, double cx, double cy, double r, double thetaFrom, double thetaTo, double side, double z)
        {
            const int n = 8;
            for (int k = 0; k <= n; k++)
            {
                double t = (thetaFrom + (thetaTo - thetaFrom) * k / n) * Math.PI / 180;
                path.Add(new[] { Math.Round(cx + r * Math.Cos(t), 6), Math.Round(cy + side * r * Math.Sin(t), 6), z });
            }
        }

        static double R(double v) => Math.Round(v, 6);   // keeps 9.8 - 2 from reaching the JSON as 7.800000000000001

        static double[][] Dedup(List<double[]> pts)
        {
            var o = new List<double[]>();
            foreach (var q in pts)
                if (o.Count == 0 || Math.Abs(o[^1][0] - q[0]) + Math.Abs(o[^1][1] - q[1]) + Math.Abs(o[^1][2] - q[2]) > 1e-6) o.Add(q);
            return o.ToArray();
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
