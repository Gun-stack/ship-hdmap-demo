using UnityEngine;

namespace ShipHdMap
{
    /// Swings an internal ramp about its hinge (Unity Z, the hinge line) between stowed (0 deg, flush with the upper deck)
    /// and deployed (deployedDeg, toe on the lower deck). Kinematic and purely visual: the scenario decides the state, the
    /// car drives the route polyline, and this only makes the ramp look like it is where the route says it is.
    public class RampHoist : MonoBehaviour
    {
        public float deployedDeg;
        public float lowerZ;              // the lower deck's height: SetDeckVisibility hides a ramp that is wholly above the deck in view
        public float degPerSecond = 3f;   // ~2.5 s for a 7.5 deg swing: slow enough to see, quick at a x20 time scale
        public bool Deployed { get; private set; }
        float _target, _angle;

        public void Set(bool deployed, bool animate)
        {
            Deployed = deployed; _target = deployed ? deployedDeg : 0f;
            if (!animate) { _angle = _target; Apply(); }
        }

        void Update()
        {
            if (Mathf.Approximately(_angle, _target)) return;
            _angle = Mathf.MoveTowards(_angle, _target, degPerSecond * Time.deltaTime);
            Apply();
        }

        void Apply() => transform.localRotation = Quaternion.Euler(0, 0, _angle);

        public float Angle => _angle;
    }
}
