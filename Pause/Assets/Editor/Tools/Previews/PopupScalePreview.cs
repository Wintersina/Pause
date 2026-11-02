using System.IO;
using UnityEngine;

// Renders the "game-popups" screen (score popups, a fusion callout, the banners) at the phone and tablet sizes the
// popup rule is checked at, with the rule on ("after") or off ("before"), through the screen-fit rig:
//
//   POPUP_PREVIEW_DIR=.popup-frames POPUP_PREVIEW_LABEL=after \
//   Unity -batchmode -quit -projectPath Pause -executeMethod PopupScalePreview.Run
//   (POPUP_PREVIEW_LABEL=before turns ScoreHud.PopupScaling off; POPUP_PREVIEW_HEIGHT = frame height, default 1400)
public static class PopupScalePreview
{
    static FitDevice Dev(string id, int w, int h, bool ios, float pxPerPt)
    {
        return new FitDevice { id = id, name = id, w = w, h = h, ios = ios, pxPerPt = pxPerPt };
    }

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("POPUP_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PopupScalePreview";
        string label = System.Environment.GetEnvironmentVariable("POPUP_PREVIEW_LABEL");
        if (string.IsNullOrEmpty(label)) label = "after";
        string hv = System.Environment.GetEnvironmentVariable("POPUP_PREVIEW_HEIGHT");
        int height = string.IsNullOrEmpty(hv) ? 1400 : int.Parse(hv);
        ScoreHud.PopupScaling = label != "before";
        var devices = new[]
        {
            Dev("flip7-1080x2520", 1080, 2520, false, 2.625f),
            Dev("phone-1080x1920", 1080, 1920, false, 2.625f),
            Dev("ipad-1536x2048", 1536, 2048, true, 2f),
            Dev("tab-1668x2388", 1668, 2388, true, 2f),
            Dev("ipad-2048x2732", 2048, 2732, true, 2f),
        };
        FitScreen screen = null;
        foreach (var s in ScreenFitScreens.All) if (s.id == "game-popups") screen = s;
        Directory.CreateDirectory(Path.Combine(dir, label));
        try
        {
            foreach (var d in devices)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(screen, d, Path.Combine(dir, label, d.id + ".png"), height);
                    Debug.Log("[POPUPSCALE] " + label + " " + d.id + " failing checks " + shot.failures);
                    foreach (var f in shot.findings) Debug.Log("[POPUPSCALE]     " + f.kind + " " + f.element + ": " + f.detail);
                }
        }
        finally { ScoreHud.PopupScaling = true; }
        UnityEditor.EditorApplication.Exit(0);
    }
}
