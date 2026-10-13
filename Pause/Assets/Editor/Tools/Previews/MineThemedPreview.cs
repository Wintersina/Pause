using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One strip per world of the rail mine firing over that world's real backdrop: the windup (muzzle cells gathering), the aim line with
// its lock reticle, the live beam, the cool-down; plus a zoom on the muzzle and on the far-rail impact.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod MineThemedPreview.Run
//   (writes to $MINE_THEMED_DIR, else Builds/MineThemed; MINE_THEMED_BEFORE=1 also writes the old boss-cell beam as <world>-before)
public static class MineThemedPreview
{
    const int Width = 540, Height = 960, CropH = 330;
    static int World;
    static readonly string[] Keys = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    const float Dt = 1f / 60f;

    static Camera cam;
    static RenderTexture rt;
    static Texture2D tex;
    static Transform pilot;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("MINE_THEMED_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/MineThemed";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                bool both = System.Environment.GetEnvironmentVariable("MINE_THEMED_BEFORE") == "1";
                for (World = 0; World < Keys.Length; World++)
                {
                    string key = Keys[World].ToLowerInvariant();
                    var after = Capture(dir, key + "-after", false);
                    var before = both ? Capture(dir, key + "-before", true) : new List<Texture2D>();
                    Sheet(dir, key, before, after);
                    Debug.Log("[MINE-THEMED] " + key + ": " + after.Count + " frames");
                }
            }
            finally
            {
                MineLaserArt.NoFlame = false;
                MineLaserArt.NoKit = false;
                MineCue.LegacySpaceWhite = false;
                MineLaserArt.ResetCache();
                RailMineLaser.AngleOverride = null;
                EnemyThreat.ForceShooting = false;
                EnemyThreat.Reset();
                BossEncounter.ResetRun();
                EliteSystem.Clear();
                EliteSystem.PlayerOverride = null;
                SpawnSpace.ClockOverride = null;
            }
        }
        EditorApplication.Exit(0);
    }

    static void Scene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        EliteSystem.Clear();
        EnemyThreat.Reset();
        RailMineLasers.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, World);
        var bgGo = new GameObject("~Backdrop");
        var backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Keys[World], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (tex == null) tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static Texture2D Grab(Vector3 centre, float zoom)
    {
        float size = cam.orthographicSize;
        cam.orthographicSize = size / zoom;
        cam.transform.position = new Vector3(centre.x, centre.y, -10f);
        cam.Render();
        cam.orthographicSize = size;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        var copy = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        copy.SetPixels(tex.GetPixels());
        copy.Apply();
        return copy;
    }

    static List<Texture2D> Capture(string dir, string tag, bool old)
    {
        MineLaserArt.NoFlame = old;
        MineLaserArt.NoKit = old;
        MineCue.LegacySpaceWhite = System.Environment.GetEnvironmentVariable("MINE_THEMED_LEGACY") == "1";
        MineLaserArt.ResetCache();
        RailMineLaser.AngleOverride = 14f;
        Scene();
        EnemyThreat.ForceShooting = true;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        var def = EnemyRoster.One(World, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(false);
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, .5f, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, .5f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = pilot;
        pilot.position = new Vector3(-.5f, -1.5f, 0f);
        var fb = go.GetComponent<EnemyFlipbook>();
        float tell = Mathf.Max(EnemyBrain.TellFloorSeconds, brain.Behaviour.tell);
        // windup times, then seconds after the release
        float[] windup = { .15f, .5f, .85f, tell - .3f, tell - .08f };
        float[] after = { .05f, .17f, .3f, .47f };
        var frames = new List<Texture2D>();
        int wi = 0, ai = 0;
        bool fired = false;
        float sinceFire = 0f;
        for (int i = 0; i < 60 * 10 && ai < after.Length; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "RailMineLaser") mb.SendMessage("LateUpdate");
            var c = go.transform.position + new Vector3(-2f, -.4f, 0f);
            if (!fired && brain.State == EnemyBrain.Phase.Windup && wi < windup.Length && brain.StateTime >= windup[wi])
            {
                frames.Add(Grab(c, 1f));
                Save(dir, tag + "-w" + wi, frames[frames.Count - 1]);
                if (wi == 4) Save(dir, tag + "-w-zoom", Grab(go.transform.position + new Vector3(-.5f, -.2f, 0f), 3f));
                wi++;
            }
            if (!fired && brain.State == EnemyBrain.Phase.Release) fired = true;
            if (fired)
            {
                sinceFire += Dt;
                if (sinceFire >= after[ai])
                {
                    frames.Add(Grab(c, 1f));
                    Save(dir, tag + "-a" + ai, frames[frames.Count - 1]);
                    if (ai == 1) Save(dir, tag + "-a-zoom", Grab(go.transform.position + new Vector3(-1f, -.2f, 0f), 3f));
                    ai++;
                }
            }
        }
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
        EnemyThreat.ForceShooting = false;
        return frames;
    }

    static void Save(string dir, string name, Texture2D t) { File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG()); }

    // one horizontal strip per world (the after frames, then the before frames in a second row when asked), each cropped to the band round the mine
    static void Sheet(string dir, string key, List<Texture2D> before, List<Texture2D> after)
    {
        int cols = Mathf.Max(before.Count, after.Count);
        int rows = before.Count > 0 ? 2 : 1;
        int y0 = Mathf.RoundToInt(Height * .5f) - CropH / 2 + 30;
        var sheet = new Texture2D(Width * cols, CropH * rows, TextureFormat.RGB24, false);
        for (int c = 0; c < cols; c++)
        {
            Blit(sheet, after, c, 0, rows, y0, 0);
            Blit(sheet, before, c, 0, rows, y0, 1);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(dir, "strip-" + key + ".png"), sheet.EncodeToPNG());
    }

    static void Blit(Texture2D sheet, List<Texture2D> src, int col, int xOff, int rows, int y0, int row)
    {
        if (col >= src.Count) return;
        var px = src[col].GetPixels(0, y0, Width, CropH);
        sheet.SetPixels(col * Width, (rows - 1 - row) * CropH, Width, CropH, px);
    }
}
