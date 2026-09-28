using System;

namespace ShipHdMap
{
    /// Generator parameters. Defaults produce the fixture geometry: L 120 m, B 24 m, five car decks D1..D5 (z 5.4 .. 15.8),
    /// the stern quarter ramp on the middle deck D3, and hoistable internal ramps chaining D3->D2->D1 (port) and
    /// D3->D4->D5 (starboard).
    [Serializable]
    public class ShipParams
    {
        public double lengthM = 120, beamM = 24;
        public int deckCount = 5;
        public double firstDeckZ = 5.4, deckPitchM = 2.6, deckClearM = 2.2;
        public double pillarPitchM = 12, pillarSizeM = 0.6, pillarInsetM = 5.5;
        public double lashingPitchM = 0.75;
        public double rampLengthM = 30, rampWidthM = 12, rampAngleMin = -7, rampAngleMax = 4;
        public int rampDeckIndex = 2;
        public double laneWidthM = 3.2, laneSpeedKmh = 10;

        // ── internal hoistable ramps (M8) ─────────────────────────────────────
        // 20 m over a 2.6 m deck pitch is 7.5 deg. Each ramp runs fore-aft in a 4 m strip against the hull (port for the
        // ramps going down from the stern-ramp deck, starboard for the ones going up), clear of the pillar rows at
        // y = +-6.5. The j-th ramp away from the stern-ramp deck is entered at innerRampX[j] on its near deck, after a
        // U-turn at innerTurnX[j] off the centreline; both sit between pillar stations so the turn sweeps no pillar.
        public double innerRampLengthM = 20, innerRampWidthM = 4, innerRampSideY = 9.8;
        public double[] innerRampX = { 34, 56, 78 }, innerTurnX = { 40, 62, 84 };
        /// Where a deck's main lane starts. The stern-ramp deck keeps 2 m (the car rolls straight off the stern ramp);
        /// every other deck is joined by a route that U-turns in at x 8, so its lane starts there.
        public double laneStartX = 2, routedLaneStartX = 8;
        /// U-turn radius: half the offset between the centreline and the ramp strip, so a turn lands exactly on it.
        public double UTurnRadius => innerRampSideY / 2;

        public double DeckZ(int i) => Math.Round(firstDeckZ + i * deckPitchM, 6);
    }
}
