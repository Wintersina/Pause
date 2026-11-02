using System;
using System.IO;
using UnityEngine;

// The Shop and the dock as PNGs on a tall phone, a 16:9 phone and a tablet:
//
//   scripts/unity-batch.sh -executeMethod ShopCapture.Run
//   (env SHOP_CAPTURE_DIR, else /private/tmp/shop-capture; SHOP_CAPTURE_SCREENS = comma list)
public static class ShopCapture
{
    static readonly string[] Screens = { "shop", "shop-owned-gated", "shop-unavailable", "dock", "home" };

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SHOP_CAPTURE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp/shop-capture";
        Directory.CreateDirectory(dir);
        string only = Environment.GetEnvironmentVariable("SHOP_CAPTURE_SCREENS");
        var ids = string.IsNullOrEmpty(only) ? Screens : only.Split(',');
        var devices = new[]
        {
            FitDevice.Find("flip7-1080x2520"), FitDevice.Find("and-1080x1920"),
            new FitDevice { id = "tab-1536x2048", name = "Tablet 1536x2048", w = 1536, h = 2048, pxPerPt = 2f },
        };
        foreach (var id in ids)
            foreach (var dev in devices)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.Find(id), dev, Path.Combine(dir, id + "_" + dev.id + ".png"), 2520);
                    foreach (var f in shot.findings) Debug.Log("[SHOPCAP] " + id + " @ " + dev.id + " " + (f.waived ? "WAIVED " : "") + f.kind + " " + f.element + ": " + f.detail);
                }
        UnityEditor.EditorApplication.Exit(0);
    }
}
