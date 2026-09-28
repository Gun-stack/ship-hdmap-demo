using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipHdMap
{
    /// Which slots a car can be sent to given the hoistable internal ramps (M8), and when a ramp goes up or down.
    /// Pure: the map, the slots and the current ramp states in, a decision out.
    ///
    /// Vocabulary, from the stern-ramp deck's point of view (traffic always comes from there):
    ///   the NEAR deck of a ramp is the one closer to the stern-ramp deck, the FAR deck the other one;
    ///   a ramp is "beyond" for a slot when the route to that slot's deck drives over it;
    ///   a slot is ON a ramp when it stands on the ramp's ground on the near deck -- ground that only exists as
    ///   floor once the ramp is stowed (going down, the ramp itself closes the opening; going up, it swings away).
    public static class RampPlanner
    {
        public const string Deployed = "deployed", Stowed = "stowed";

        public static Ramp Stern(VehicleMap m) => m?.ramps?.Find(r => (r.type ?? "stern_quarter") == "stern_quarter");
        public static IEnumerable<Ramp> Internal(VehicleMap m) => m?.ramps?.Where(r => r.type == "internal_hoistable") ?? Enumerable.Empty<Ramp>();
        public static Route RouteTo(VehicleMap m, string deckId) => m?.routes?.Find(r => r.deck_id == deckId);

        /// The deck the stern ramp lands on: the deck at its hinge height. Null without a stern ramp.
        public static string SternDeck(VehicleMap m)
        {
            var s = Stern(m); if (s == null || m.decks == null) return null;
            return m.decks.OrderBy(d => Math.Abs(d.z_surface - s.hinge[0][2])).FirstOrDefault()?.id;
        }

        public static string NearDeck(VehicleMap m, Ramp r)
        {
            var order = (m.decks ?? new List<Deck>()).OrderBy(d => d.z_surface).Select(d => d.id).ToList();
            int s = order.IndexOf(SternDeck(m) ?? ""), lo = order.IndexOf(r.lower_deck ?? ""), up = order.IndexOf(r.upper_deck ?? "");
            if (s < 0 || lo < 0 || up < 0) return null;
            return Math.Abs(lo - s) <= Math.Abs(up - s) ? r.lower_deck : r.upper_deck;
        }

        /// Is (x, y) on the ramp's plan footprint -- the rectangle between its hinge and its toe?
        public static bool OnFootprint(Ramp r, double x, double y)
        {
            if (r.hinge == null || r.toe == null) return false;
            double x0 = Math.Min(r.hinge[0][0], r.toe[0][0]), x1 = Math.Max(r.hinge[0][0], r.toe[0][0]);
            double y0 = Math.Min(r.hinge[0][1], r.hinge[1][1]), y1 = Math.Max(r.hinge[0][1], r.hinge[1][1]);
            return x >= x0 - 1e-9 && x <= x1 + 1e-9 && y >= y0 - 1e-9 && y <= y1 + 1e-9;
        }

        /// The ramp whose near-deck ground this slot stands on, or null.
        public static Ramp RampUnder(VehicleMap m, ParkingSlot s)
        {
            if (s?.target_pose == null) return null;
            return Internal(m).FirstOrDefault(r => NearDeck(m, r) == s.deck_id && OnFootprint(r, s.target_pose.x, s.target_pose.y));
        }

        /// Ramps the route to this slot's deck drives over, in travel order. Empty on the stern-ramp deck.
        public static List<string> RampsFor(VehicleMap m, ParkingSlot s) => RouteTo(m, s.deck_id)?.ramps ?? new List<string>();

        static bool Occupied(ParkingSlot s) => ScenarioPlanner.IsFilled(s.status) && s.status != "unreachable";

        /// A ramp can come down only onto clear ground: no car parked on its near-deck footprint.
        public static bool CanDeploy(VehicleMap m, Ramp r, IEnumerable<ParkingSlot> slots) =>
            !slots.Any(s => Occupied(s) && RampUnder(m, s)?.id == r.id);

        /// Loading is done beyond a ramp once no empty slot remains on a deck whose route drives over it.
        public static bool NothingLeftBeyond(VehicleMap m, Ramp r, IEnumerable<ParkingSlot> slots) =>
            !slots.Any(s => (s.status ?? "empty") == "empty" && RampsFor(m, s).Contains(r.id));

        /// Can a car be sent to this slot now, given the ramp states? A route's ramps must be down or able to come
        /// down; a slot on a ramp's ground needs that ramp up, or able to go up -- loading only (unloading drives off a
        /// stowed ramp as floor, and the far-decks-first order has emptied everything beyond it long before).
        public static bool Available(VehicleMap m, ParkingSlot s, string mode, IDictionary<string, string> state, IEnumerable<ParkingSlot> slots)
        {
            var all = slots as IList<ParkingSlot> ?? slots.ToList();
            foreach (var id in RampsFor(m, s))
            {
                var r = m.ramps.Find(q => q.id == id); if (r == null) return false;
                if (StateOf(state, id) != Deployed && !CanDeploy(m, r, all)) return false;
            }
            if (mode != "unload")
            {
                var under = RampUnder(m, s);
                if (under != null && StateOf(state, under.id) != Stowed && !NothingLeftBeyond(m, under, all.Where(q => q.id != s.id))) return false;
            }
            return true;
        }

        public static string StateOf(IDictionary<string, string> state, string id) => state != null && state.TryGetValue(id, out var v) ? v : Deployed;

        /// Where every internal ramp should be for a run that has just been told to go to `target`: its route's ramps
        /// down; while loading, every other ramp with nothing left beyond it up (that frees its near-deck ground, and
        /// nothing will need it again this run). Returns only the ramps whose state changes.
        public static List<(string id, string state)> Plan(VehicleMap m, ParkingSlot target, string mode, IDictionary<string, string> state, IEnumerable<ParkingSlot> slots)
        {
            var all = slots as IList<ParkingSlot> ?? slots.ToList();
            var need = target == null ? new List<string>() : RampsFor(m, target);
            var changes = new List<(string, string)>();
            foreach (var r in Internal(m))
            {
                string now = StateOf(state, r.id), want = now;
                if (need.Contains(r.id)) want = Deployed;
                else if (mode != "unload" && NothingLeftBeyond(m, r, all)) want = Stowed;
                if (want != now) changes.Add((r.id, want));
            }
            return changes;
        }
    }
}
