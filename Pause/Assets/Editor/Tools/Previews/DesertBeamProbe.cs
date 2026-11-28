using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// The rail mine's beam over each Desert layout, as RailMineThemedTest fires it: runs that test's own Firing(6) with the layout
// forced (0: the layout the selection deals), logs its stand-out line, then saves the view right after (the backdrop the beam
// crossed) as beam-v<n>.png.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod DesertBeamProbe.Run   (writes to $SHOWCASE_DIR, else Builds/DesertBeamProbe)
public static class DesertBeamProbe
{
    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("SHOWCASE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/DesertBeamProbe";
        Directory.CreateDirectory(dir);
        var t = typeof(RailMineThemedTest);
        var firing = t.GetMethod("Firing", BindingFlags.NonPublic | BindingFlags.Static);
        var camField = t.GetField("cam", BindingFlags.NonPublic | BindingFlags.Static);
        var sel = BackdropVariants.For("Desert");
        using (new TestHarness.Sandbox())
        {
            try
            {
                AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache();
                for (int v = 0; v <= 4; v++)
                {
                    sel.Force = v;
                    firing.Invoke(null, new object[] { 6 });
                    var cam = (Camera)camField.GetValue(null);
                    var wb = Object.FindObjectOfType<WorldBackdrop>();
                    int shown = wb != null && wb.Current != null ? wb.Current.Variant : -1;
                    var rt = new RenderTexture(540, 960, 24);
                    var tex = new Texture2D(540, 960, TextureFormat.RGB24, false);
                    var old = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    var was = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 540, 960), 0, 0);
                    tex.Apply();
                    RenderTexture.active = was;
                    cam.targetTexture = old;
                    File.WriteAllBytes(Path.Combine(dir, "beam-v" + v + ".png"), tex.EncodeToPNG());
                    Debug.Log("[DESERT-BEAM] forced " + v + " -> shown v" + shown);
                }
            }
            finally
            {
                sel.Force = 0;
                MineLaserArt.ResetCache(); AttackArt.Clear();
                RailMineLaser.AngleOverride = null; EnemyThreat.ForceShooting = false; EnemyThreat.Reset();
                BossEncounter.ResetRun(); EliteSystem.Clear(); EliteSystem.PlayerOverride = null; SpawnSpace.ClockOverride = null;
            }
        }
        EditorApplication.Exit(0);
    }
}
