namespace ShipHdMap
{
    public static class BridgeMessages
    {
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario";
        public const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved",
            OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization";
    }
    public class FeatureCreatedEvt { public string tempId; public string layer; public double x, y, z; public string deck; }
    public class LocalizationEvt { public double est_x, est_y, est_psi, true_x, true_y, true_psi, residual_rms; public int n_obs; public string frame; }
    public class SetNoiseMsg { public double sigma_r = 0.2, sigma_theta = 1, sigma_alpha = 2, sigma_gps = 0.5; } // angles in degrees on the wire
    public class ConfirmMsg { public string tempId; public string id; }
    public class StartScenarioMsg { public VehicleMap map; public string mode; }
}
