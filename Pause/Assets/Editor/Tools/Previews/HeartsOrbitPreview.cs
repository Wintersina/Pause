using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames of a big boss and two big elites with their orange 3D-orbiting
// hearts, over the boss's world backdrop, for review (assemble the PNGs into a
// GIF with ffmpeg).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HeartsOrbitPreview.Run
//   (writes to $HEARTSORBIT_PREVIEW_DIR, else Builds/HeartsOrbitPreview;
//    $HEARTSORBIT_WORLD picks the boss's world index, default 4 = Tide;
//    $HEARTSORBIT_ELITES = two comma-separated elite keys)
public static class HeartsOrbitPreview
{
    const int Width = 1080, Height = 2340;
    const float Dt = 1f / 60f;
    const int Frames = 90, StepsPerFrame = 4;   // 6 s at 15 fps: more than a revolution

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HEARTSORBIT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HeartsOrbitPreview";
        Directory.CreateDirectory(dir);
        string wenv = System.Environment.GetEnvironmentVariable("HEARTSORBIT_WORLD");
        int w = string.IsNullOrEmpty(wenv) ? 4 : int.Parse(wenv);
        string[] keys = (System.Environment.GetEnvironmentVariable("HEARTSORBIT_ELITES") ?? "desert_elite_caravan_hauler,frost_elite_floe_harrower").Split(',');
        using (new TestHarness.Sandbox())
        {
            try { Capture(dir, w, keys); }
            finally
            {
                BossEncounter.ResetRun();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(0);
    }

    static void Capture(string dir, int w, string[] keys)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);

        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show(EnemyRoster.WorldKeys.Length > w ? char.ToUpper(EnemyRoster.WorldKeys[w][0]) + EnemyRoster.WorldKeys[w].Substring(1) : "Space", false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);

        BossEncounter.Begin(w, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = 0;
        for (int i = 0; i < 45; i++) e.Step(Dt, 1f);

        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var elites = new EliteHearts[keys.Length];
        for (int k = 0; k < keys.Length; k++)
        {
            EliteDef def = null;
            foreach (var d in EliteCatalog.All) if (d.key == keys[k]) def = d;
            if (def == null) { Debug.LogError("[HEARTSORBIT] no elite " + keys[k]); continue; }
            var ship = EliteShip.CreateInPlay(def, new Vector2(-1.5f + 3f * k, -1.9f));
            ship.AttackCooldown = 99f;
            elites[k] = ship.GetComponent<EliteHearts>();
            for (int i = 0; i < 40; i++) elites[k].Place(Dt, Dt);
        }

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        for (int f = 0; f < Frames; f++)
        {
            for (int s = 0; s < StepsPerFrame; s++)
            {
                e.Step(Dt, 1f);
                foreach (var eh in elites) if (eh != null) { eh.Place(Dt, Dt); eh.StepBreaks(Dt); }
            }
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(dir, "f" + f.ToString("D3") + ".png"), tex.EncodeToPNG());
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Debug.Log("[HEARTSORBIT-PREVIEW] " + dir);
    }
}
