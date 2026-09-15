using System;
using UnityEngine;

namespace ShipHdMap
{
    public static class LaneFollower
    {
        /// Position (ship x,y) and heading at arc length s along a [x,y,z] polyline. end=true once s exceeds the total length.
        public static (Vector2 pos, double headingRad, bool end) At(double[][] line, double s)
        {
            double acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1][0] - line[i][0], dy = line[i + 1][1] - line[i][1], len = Math.Sqrt(dx * dx + dy * dy);
                double heading = Math.Atan2(dy, dx);
                if (s <= acc + len || i + 2 == line.Length)
                {
                    double t = len < 1e-9 ? 0 : Math.Min(1, Math.Max(0, (s - acc) / len));
                    var p = new Vector2((float)(line[i][0] + dx * t), (float)(line[i][1] + dy * t));
                    return (p, heading, s > acc + len);
                }
                acc += len;
            }
            return (new Vector2((float)line[0][0], (float)line[0][1]), 0, true);
        }
    }
}
