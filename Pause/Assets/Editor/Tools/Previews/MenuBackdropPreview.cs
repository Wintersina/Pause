using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;

// Home-screen stills, one per unlocked world (and a second variant each): the
// real menu scene (laid out like a phone, see Run) with MenuBackdrop behind the title traffic and the menu.
//
//   MENU_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod MenuBackdropPreview.Run
public static class MenuBackdropPreview
{
    // Lays the screen out exactly like a device: the ScreenFit rig fakes the
    // device's pixel size / safe area / density (ScreenInfo), sets the
    // canvas the way CanvasScaler + UiScaleFloor do there and runs
    // startMenu.LayoutHome, so logo and buttons are where a phone puts them.
    //   MENU_PREVIEW_DEVICE  FitDevice id (default iphone-13, 1170x2532)
    //   MENU_PREVIEW_HEIGHT  frame height in px (default: the device's)
    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("MENU_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/MenuBackdropPreview";
        string deviceId = System.Environment.GetEnvironmentVariable("MENU_PREVIEW_DEVICE");
        var device = FitDevice.Find(string.IsNullOrEmpty(deviceId) ? "iphone-13" : deviceId);
        int height = 0;
        int.TryParse(System.Environment.GetEnvironmentVariable("MENU_PREVIEW_HEIGHT"), out height);
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
            EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
            var screen = ScreenFitScreens.Find("home");
            using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
            {
                UiScaleFloor.AttachScene();
                rig.Sync();
                screen.stage(rig);
                rig.Sync();
                var traffic = Object.FindFirstObjectByType<TitleScreenTraffic>();

                var bgo = new GameObject("~MenuBackdrop");
                var mb = bgo.AddComponent<MenuBackdrop>();
                var picks = new List<MenuBackdropSelection.Pick>();
                MenuBackdropSelection.Candidates(WorldManager.Worlds.Length - 1, picks);
                var shown = new Dictionary<string, int>();
                foreach (var p in picks)
                {
                    int n;
                    shown.TryGetValue(p.world, out n);
                    if (n >= 2) continue;
                    shown[p.world] = n + 1;
                    if (!mb.Build(p)) continue;
                    for (int i = 0; i < 30 * 6; i++)
                    {
                        mb.Step(1f / 30f);
                        traffic.Step(1f / 30f);
                        foreach (var book in traffic.GetComponentsInChildren<ShipFlameFlipbook>()) book.Step(1f / 30f);
                    }
                    var tex = rig.Capture(height, false);
                    File.WriteAllBytes(Path.Combine(dir, "home_" + p.world.ToLower() + (p.variant > 0 ? "_v" + p.variant : "") + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                traffic.Shutdown();
            }
        }
        EditorApplication.Exit(0);
    }
}
