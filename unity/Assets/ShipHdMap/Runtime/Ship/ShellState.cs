using UnityEngine;

namespace ShipHdMap
{
    /// Remembers the outer shell's view mode on the shell itself, so HullBuilder.Show can put it back the way it was
    /// after a single-deck view hid it.
    public class ShellState : MonoBehaviour { public bool cutaway = true, shown = true; }
}
