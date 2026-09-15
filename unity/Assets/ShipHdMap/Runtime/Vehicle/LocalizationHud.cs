using System;
using UnityEngine;

namespace ShipHdMap
{
    public class LocalizationHud : MonoBehaviour
    {
        LocalizerResult _last; Pose2D _truth; string _frame = "SHIP_AP";
        public void Set(LocalizerResult r, Pose2D truth, string frame) { _last = r; _truth = truth; _frame = frame; }

        void OnGUI()
        {
            const double R2D = 180 / Math.PI;
            var box = new Rect(10, Screen.height - 130, 420, 120);
            GUI.Box(box, "Localization");
            if (_last == null) { GUI.Label(new Rect(20, box.y + 25, 400, 20), "no estimate yet"); return; }
            var e = _last.pose; var t = _truth;
            double err = Math.Sqrt((e.x - t.x) * (e.x - t.x) + (e.y - t.y) * (e.y - t.y));
            double herr = ShipFrame.WrapDeg((e.psiRad - t.psiRad) * R2D);
            string[] lines =
            {
                $"frame {_frame}   N {_last.nObs}   RMS {_last.residualRms:F3}   iter {_last.iterations}   {(_last.ok ? "" : "(holding previous)")}",
                $"est  x {e.x,7:F2}  y {e.y,7:F2}  psi {e.psiRad * R2D,7:F1}",
                $"true x {t.x,7:F2}  y {t.y,7:F2}  psi {t.psiRad * R2D,7:F1}",
                $"err  {err:F2} m   {herr:F1} deg",
            };
            for (int i = 0; i < lines.Length; i++) GUI.Label(new Rect(20, box.y + 25 + i * 22, 400, 20), lines[i]);
        }
    }
}
