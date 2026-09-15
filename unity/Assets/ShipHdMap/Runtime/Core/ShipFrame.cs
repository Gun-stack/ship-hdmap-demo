using System;
using UnityEngine;

namespace ShipHdMap
{
    /// Ship Frame: x fwd (from AP), y port (+), z up (from baseline). Heading CCW from +x, degrees.
    /// Unity: x fwd, y up, z starboard. Unity yaw (about +Y) rotates +X toward -Z = ship +Y, so yaw == heading.
    public static class ShipFrame
    {
        public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)z, (float)-y);

        public static (double x, double y, double z) ToShip(Vector3 u) => (u.x, -u.z, u.y);

        public static float UnityYawDeg(double headingDeg) => (float)headingDeg;

        public static Vector3 HeadingVector(double headingDeg)
        {
            double r = headingDeg * Math.PI / 180.0;
            return new Vector3((float)Math.Cos(r), 0f, (float)-Math.Sin(r));
        }

        /// (-180, 180]
        public static double WrapDeg(double a)
        {
            a %= 360.0;
            if (a <= -180.0) a += 360.0;
            else if (a > 180.0) a -= 360.0;
            return a;
        }

        /// (-pi, pi]
        public static double WrapRad(double a)
        {
            a %= 2 * Math.PI;
            if (a <= -Math.PI) a += 2 * Math.PI;
            else if (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }
    }
}
