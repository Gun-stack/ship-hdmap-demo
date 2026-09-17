namespace ShipHdMap
{
    public static class BridgeMessages
    {
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario", SetTimeScale = "SetTimeScale";
        public const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved",
            OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization", OnScenario = "onScenario";
    }
    public class FeatureCreatedEvt { public string tempId; public string layer; public double x, y, z; public string deck; public string mounted_on; public double[] normal; }
    public class FeatureMovedEvt { public string id; public double x, y, z; public double[] normal; public string deck; public string mounted_on; }
    public class LocalizationEvt { public double est_x, est_y, est_psi, true_x, true_y, true_psi, residual_rms; public int n_obs; public string frame; }
    public class SetNoiseMsg { public double sigma_r = 0.2, sigma_theta = 1, sigma_alpha = 2, sigma_gps = 0.5; } // angles in degrees on the wire
    public class ConfirmMsg { public string tempId; public string id; }
    public class StartScenarioMsg { public string mode; }   // the map is whatever was Loaded
    public class SetTimeScaleMsg { public double scale = 1; }
    /// Parking result (load) or an emptied slot (unload: status "empty", no errors — nulls are dropped from the JSON).
    public class SlotFilledEvt { public string slot_id; public string status; public double? err_lat, err_lon, err_heading; }
    /// Scenario log line. `event`: start | target | leave_lane | finished.
    public class ScenarioEvt { [Newtonsoft.Json.JsonProperty("event")] public string evt; public string mode; public string slot_id; public string detail; }
    public class RampMsg { public string id; public double angle_deg; public string state; }
    public class SetPoseMsg { public double draft_fwd_m = 8.1, draft_aft_m = 8.6, heel_deg, lpp_m = 120, tide_m = 0, quay_z_m = 3.5; public RampMsg ramp; }
}
