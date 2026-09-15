using System.Collections.Generic;
using Newtonsoft.Json;

namespace ShipHdMap
{
    // Field names intentionally match the vehicle-map v1 JSON keys (spec §6). Do not rename.
    public class FrameInfo { public string name; public string origin; public Dictionary<string, string> axes; public string unit; }
    public class Marker { public string family; public int code; }
    public class Deck { public string id; public string name; public double z_surface; public double z_clear; public bool movable; public double[][] outline; }
    public class Landmark { public string id; public Marker marker; public double[] position; public double[] normal; public double size_m; public string deck_id; public string mounted_on; }
    public class Lane { public string id; public string deck_id; public double[][] centerline; public double width_m; public string direction; public double speed_limit_kmh; public List<string> next; }
    public class TargetPose { public double x; public double y; public double heading_deg; }
    public class Tolerance { public double lat_m; public double lon_m; public double heading_deg; }
    public class ParkingSlot
    {
        public string id; public string deck_id; public double[][] polygon; public TargetPose target_pose; public Tolerance tolerance;
        public string vehicle_class; public string access_lane_id; public List<string> lashing_points; public int sequence_no; public string status;
    }
    public class LashingPoint { public string id; public string kind; public double[] position; public string deck_id; }
    public class Marking { public string id; public string kind; public double[][] polygon; public string deck_id; }
    public class Facility { public string id; public string kind; public double[][] footprint; public double z_min; public double z_max; public string deck_id; }
    public class Ramp { public string id; public string type; public double[][] hinge; public double length_m; public double width_m; public double[] angle_range_deg; public string connects_lane; public List<string> transition_landmarks; }

    public class VehicleMap
    {
        public string schema; public string map_id; public int version; public string generated_at; public FrameInfo frame;
        public List<Deck> decks; public List<Landmark> landmarks; public List<Lane> lanes; public List<ParkingSlot> parking_slots;
        public List<LashingPoint> lashing_points; public List<Marking> markings; public List<Facility> facilities; public List<Ramp> ramps;
    }

    /// Output of the ship generator; body of POST /api/datasets/{id}/seed (spec §7).
    public class SeedData { public List<Deck> decks; public List<Facility> facilities; public List<LashingPoint> lashing_points; public List<Ramp> ramps; public List<Lane> lanes; }

    public static class MapJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };
        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Parse<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
