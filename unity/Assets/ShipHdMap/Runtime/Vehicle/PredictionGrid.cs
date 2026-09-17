using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    /// What the coverage map promised, indexed for O(1) lookup while driving (spec M5d §3.2).
    /// Sparse on purpose: the API only evaluates cells inside the deck outline and outside the pillars.
    public class PredictionGrid
    {
        readonly Dictionary<long, double?> _byCell = new();
        readonly double _x0, _y0, _grid;

        public PredictionGrid(double[] bbox, double gridM, IEnumerable<(double x, double y, double? s)> cells)
        {
            _grid = gridM > 0 ? gridM : 1.0;
            _x0 = bbox != null && bbox.Length >= 2 ? bbox[0] : 0;
            _y0 = bbox != null && bbox.Length >= 2 ? bbox[1] : 0;
            if (cells != null) foreach (var c in cells) _byCell[Key(c.x, c.y)] = c.s;
        }

        /// The promised sigma_xy at (x, y), or null where the map says blind -- or where we have no cell at all.
        /// Those two are deliberately the same answer: both mean "no promise to hold this reading against".
        public double? SigmaAt(double x, double y) => _byCell.TryGetValue(Key(x, y), out var s) ? s : null;

        long Key(double x, double y)
        {
            long ix = (long)Math.Floor((x - _x0) / _grid);
            long iy = (long)Math.Floor((y - _y0) / _grid);
            return (ix << 32) ^ (iy & 0xffffffffL);
        }
    }
}
