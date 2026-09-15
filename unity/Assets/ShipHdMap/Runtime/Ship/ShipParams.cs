using System;

namespace ShipHdMap
{
    /// Generator parameters. Defaults produce the fixture geometry (L 120 m, B 24 m, Deck 3 at z 10.6).
    [Serializable]
    public class ShipParams
    {
        public double lengthM = 120, beamM = 24;
        public int deckCount = 3;
        public double firstDeckZ = 5.4, deckPitchM = 2.6, deckClearM = 2.2;
        public double pillarPitchM = 12, pillarSizeM = 0.6, pillarInsetM = 5.5;
        public double lashingPitchM = 0.75;
        public double rampLengthM = 30, rampWidthM = 12, rampAngleMin = -7, rampAngleMax = 4;
        public int rampDeckIndex = 2;
        public double laneWidthM = 3.2, laneSpeedKmh = 10;
        public double DeckZ(int i) => Math.Round(firstDeckZ + i * deckPitchM, 6);
    }
}
