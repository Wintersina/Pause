using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each boss with damage art (all five) over its backdrop at 5,
// 4, 3, 2 and 1 hearts left
// (pristine, then battle damage stages 1..4: hull, smoke, arcs) side by side
// in one PNG strip per boss (bossdamage-<key>-strip.png), for review.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossDamagePreview.Run
//   (writes to $BOSSDAMAGE_PREVIEW_DIR, else Builds/BossDamagePreview)
public static class BossDamagePreview
{
    const int Width = 1080, Height = 2340;   // a 9:19.5 phone
    const float Dt = 1f / 60f;
    const float Half = 2.4f;                 // world units round the boss in each tile

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSDAMAGE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossDamagePreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < BossCatalog.All.Length; w++)
                    if (BossArt.HasDamageArt(BossCatalog.All[w]))
                        Render(w, Path.Combine(dir, "bossdamage-" + BossCatalog.All[w].damageKey.ToLowerInvariant() + "-strip.png"));
            }
            finally { BossEncounter.ResetRun(); }
        }
        EditorApplication.Exit(0);
    }

    static void Render(int world, string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);

        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show(WorldManager.Worlds[world].displayName, false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);

        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        float ppu = Height / (cam.orthographicSize * 2f);
        int side = Mathf.RoundToInt(Half * 2f * ppu);
        int tiles = Mathf.Max(1, BossConfig.Hearts);
        var strip = new Texture2D(side * tiles, side, TextureFormat.RGB24, false);

        for (int t = 0; t < tiles; t++)
        {
            int left = tiles - t;
            for (int i = 0; i < 20 && e.HeartsLeft > left; i++) e.OnShipAttackHit(BossConfig.HeartWeight * .5f);
            // settle (crumble and flash done), then wait for the idle pose
            for (int i = 0; i < 90; i++) e.Step(Dt, 1f);
            for (int i = 0; i < 600 && !BossArt.IsIdleFrame(e.Actor.BodyFrame); i++) e.Step(Dt, 1f);
            // stage 3's arcs come in bursts: show one lit
            for (int i = 0; i < 120 && e.Actor.DamageStage == 3 && !e.Actor.DamageArcs.enabled; i++) e.Step(Dt, 1f);

            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = old;

            Vector3 c = cam.WorldToScreenPoint(e.Actor.transform.position);
            int x0 = Mathf.Clamp((int)c.x - side / 2, 0, Width - side);
            int y0 = Mathf.Clamp((int)c.y - side / 2, 0, Height - side);
            strip.SetPixels(t * side, 0, side, side, shot.GetPixels(x0, y0, side, side));
            Debug.Log("[BOSSDAMAGE-PREVIEW] " + e.Actor.Boss.artKey + " hearts " + left + " stage " + e.Actor.DamageStage +
                      " frame " + e.Actor.BodyFrame + " smoke " + e.Actor.DamageSmoke.enabled + " arcs " + e.Actor.DamageArcs.enabled);
        }
        strip.Apply();
        File.WriteAllBytes(path, strip.EncodeToPNG());

        // a boss with a death strip: three frames of its death (bossdeath-<key>-strip.png)
        if (BossArt.HasDeathArt(e.Actor.Boss))
        {
            for (int i = 0; i < 20 && e.State == BossEncounter.Phase.Fight; i++) { e.OnShipAttackHit(1f); e.Step(Dt, 1f); }
            var dstrip = new Texture2D(side * 3, side, TextureFormat.RGB24, false);
            float[] at = { .1f, .3f, .55f };
            float t0 = 0f;
            for (int t = 0; t < 3; t++)
            {
                while (t0 < at[t]) { e.Step(Dt, 1f); t0 += Dt; }
                cam.Render();
                var old = RenderTexture.active;
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();
                RenderTexture.active = old;
                Vector3 c = cam.WorldToScreenPoint(e.Actor.transform.position);
                int x0 = Mathf.Clamp((int)c.x - side / 2, 0, Width - side);
                int y0 = Mathf.Clamp((int)c.y - side / 2, 0, Height - side);
                dstrip.SetPixels(t * side, 0, side, side, shot.GetPixels(x0, y0, side, side));
            }
            dstrip.Apply();
            File.WriteAllBytes(path.Replace("bossdamage-", "bossdeath-"), dstrip.EncodeToPNG());
            Object.DestroyImmediate(dstrip);
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(shot);
        Object.DestroyImmediate(strip);
        Debug.Log("[BOSSDAMAGE-PREVIEW] " + path);
    }
}
