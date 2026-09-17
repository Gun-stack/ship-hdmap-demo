namespace ShipHdMap
{
    /// One detected marker, in the vehicle frame. r metres; angles radians, CCW positive.
    public struct Observation { public string id; public double r; public double thetaRad; public double alphaRad; }

    /// Vehicle pose in Ship Frame (2.5D). psi CCW from +x.
    public struct Pose2D { public double x; public double y; public double psiRad; }

    /// Map entry for a marker: position and normal angle phi = atan2(ny, nx).
    public struct LandmarkRef { public string id; public double mx; public double my; public double phiRad; }

    /// sigmaXy/sigmaPsiDeg are the achievable precision at this solve: the diagonal of (J^T W J)^-1, the same
    /// quantity the API's CoverageAnalyzer predicts for every deck cell. Null when nothing was seen or A is singular.
    public class LocalizerResult
    {
        public bool ok; public Pose2D pose; public int nObs; public double residualRms; public int iterations;
        public double? sigmaXy, sigmaPsiDeg;
    }
}
