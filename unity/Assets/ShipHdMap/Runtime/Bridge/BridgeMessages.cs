namespace ShipHdMap
{
    public static class BridgeMessages
    {
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario", SetTimeScale = "SetTimeScale", SetPrediction = "SetPrediction", SetOccluded = "SetOccluded",
            SetBeliefParams = "SetBeliefParams", SetTool = "SetTool", SetCamMode = "SetCamMode", SetNormal = "SetNormal";
        public const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved",
            OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization", OnScenario = "onScenario",
            OnBelief = "onBelief";
    }
    public class FeatureCreatedEvt { public string tempId; public string layer; public double x, y, z; public string deck; public string mounted_on; public double[] normal; }
    public class FeatureMovedEvt { public string id; public double x, y, z; public double[] normal; public string deck; public string mounted_on; }
    public class LocalizationEvt { public double est_x, est_y, est_psi, true_x, true_y, true_psi, residual_rms; public int n_obs; public string frame; }
    public class SetNoiseMsg { public double sigma_r = 0.2, sigma_theta = 1, sigma_alpha = 2, sigma_gps = 0.5; } // angles in degrees on the wire
    public class SetOccludedMsg { public string[] ids; }
    public class SetToolMsg { public string tool; }          // select | place | probe
    public class SetCamModeMsg { public string mode; }       // orbit | fly | driver
    /// The web already wrote the new normal to the DB; this only turns the quad and the map reference.
    public class SetNormalMsg { public string id; public double[] normal; }
    public class ConfirmMsg { public string tempId; public string id; }
    public class StartScenarioMsg { public string mode; }   // the map is whatever was Loaded
    public class SetTimeScaleMsg { public double scale = 1; }
    /// Parking result (load) or an emptied slot (unload: status "empty", no errors — nulls are dropped from the JSON).
    public class SlotFilledEvt { public string slot_id; public string status; public double? err_lat, err_lon, err_heading; }
    /// Scenario log line. `event`: start | target | frame_switch | leave_lane | finished.
    /// `frame_switch` fires when the entrance landmark pair is seen and the vehicle leaves the Quay Frame for the
    /// Ship Frame; its `detail` carries the estimate at that instant. `finished`'s `detail` is one of
    /// no_empty_slot | no_filled_slot | ramp_blocked | no_frame_switch.
    public class ScenarioEvt { [Newtonsoft.Json.JsonProperty("event")] public string evt; public string mode; public string slot_id; public string detail; }
    public class RampMsg { public string id; public double angle_deg; public string state; }
    public class SetPoseMsg { public double draft_fwd_m = 8.1, draft_aft_m = 8.6, heel_deg, lpp_m = 120, tide_m = 0, quay_z_m = 3.5; public RampMsg ramp; }
    public class PredCellMsg { public double x, y; public double? s; }   // s null = the map says blind here
    public class SetPredictionMsg { public double grid_m = 1; public double[] bbox; public PredCellMsg[] cells; }
    public class SetBeliefParamsMsg
    {
        public double k = 2.0, drift_rate = 0.05, budget_m = 1.0, max_lost_m = 5.0, trail_m = 20.0;
        public int frames = 5;
    }
    /// `state`: ok | degraded | lost | backtracking | stopped. sigma fields are null when nothing was solved
    /// or the coverage map calls this cell blind.
    public class BeliefEvt
    {
        public string state; public int n_obs;
        public double? sigma_xy, sigma_psi, predicted_sigma_xy;
        public double lost_m, sigma_odo, trail_m;
    }
}
