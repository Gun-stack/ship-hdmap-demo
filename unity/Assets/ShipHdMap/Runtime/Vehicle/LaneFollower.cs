using System;

namespace ShipHdMap
{
    /// Pose on a [x,y,z] polyline. Arc length s is measured on the horizontal projection, so speed is ground speed
    /// and a slope does not slow the vehicle down.
    public struct PathPose { public double x, y, z, headingRad, pitchRad; public bool end; }

    public static class LaneFollower
    {
        public static PathPose At(double[][] line, double s)
        {
            double acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1][0] - line[i][0], dy = line[i + 1][1] - line[i][1], dz = line[i + 1][2] - line[i][2];
                double len = Math.Sqrt(dx * dx + dy * dy);
                double heading = Math.Atan2(dy, dx), pitch = Math.Atan2(dz, len < 1e-9 ? 1e-9 : len);
                if (s <= acc + len || i + 2 == line.Length)
                {
                    double t = len < 1e-9 ? 0 : Math.Min(1, Math.Max(0, (s - acc) / len));
                    return new PathPose { x = line[i][0] + dx * t, y = line[i][1] + dy * t, z = line[i][2] + dz * t,
                        headingRad = heading, pitchRad = pitch, end = s > acc + len };
                }
                acc += len;
            }
            return new PathPose { x = line[0][0], y = line[0][1], z = line[0][2], end = true };
        }
    }
}
