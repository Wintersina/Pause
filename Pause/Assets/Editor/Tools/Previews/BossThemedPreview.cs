using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each boss's NEW themed attacks (BossThemed) at the moments that matter, one contact strip per attack:
// the boss in its tell pose with the footprint outline drawn (the first frames of the tell), the last instant of the tell,
// then the live hazard at a few moments and the end. Phone-shaped frames (9:19.5), the two walls and a marker where the ship is.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossThemedPreview.Run [-only frost]
//   (writes to $BOSSTHM_PREVIEW_DIR, else Builds/BossThemedPreview)
public static class BossThemedPreview
{
    const int Width = 360, Height = 780;
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSTHM_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossThemedPreview";
        Directory.CreateDirectory(dir);
        string only = null;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-only") only = args[i + 1].ToLower();
        using (new TestHarness.Sandbox())
        {
            try
            {
                foreach (int w in new[] { 1, 3, 0, 4, 2 })
                {
                    var boss = BossCatalog.ForWorld(w);
                    if (only != null && boss.artKey.ToLower() != only) continue;
                    bool was = boss.themedAttacks;
                    boss.themedAttacks = true;
                    for (int a = 0; a < boss.attacks.Length; a++)
                        if (System.Array.IndexOf(boss.DefaultAttacks, boss.attacks[a]) < 0) Strip(dir, w, a);
                    boss.themedAttacks = was;
                }
            }
            finally { BossEncounter.ResetRun(); }
        }
        EditorApplication.Exit(0);
    }

    static WorldBackdrop backdrop;
    static readonly string[] WorldNames = { "Space", "Frost", "Verdant", "Ember", "Tide" };

    static Camera Scene(int world, out Transform ship)
    {
        backdrop = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackHazardArt.Forget();
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = world == 3 ? new Color(.12f, .04f, .05f) : world == 1 ? new Color(.1f, .12f, .2f) : new Color(.05f, .05f, .1f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        if (world == 2)
        {
            // the Verdant attacks are judged over the world's own backdrop (the pink cue has to read against green)
            var bgGo = new GameObject("~Backdrop");
            backdrop = bgGo.AddComponent<WorldBackdrop>();
            backdrop.Show(WorldNames[world], false);
            for (int i = 0; i < 600; i++) backdrop.Step(Dt);
        }
        var white = Texture2D.whiteTexture;
        var sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(.5f, .5f), white.width);
        foreach (float side in new[] { -1f, 1f })
        {
            var wall = new GameObject(side < 0 ? "leftPipe" : "rightPipe");
            var sr = wall.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = world == 2 ? new Color(.12f, .14f, .12f, .55f) : new Color(.16f, .17f, .26f);
            sr.sortingOrder = -10;
            wall.transform.position = new Vector3(side * 3.15f, 0f, 1f);
            wall.transform.localScale = new Vector3(1.43f, 30f, 1f);
        }
        var go = new GameObject("ship");
        var s = go.AddComponent<SpriteRenderer>();
        s.sprite = sprite;
        s.color = new Color(.9f, .95f, 1f, .9f);
        s.sortingOrder = 30;
        go.transform.position = new Vector3(.6f, -3.2f, 0f);
        go.transform.localScale = new Vector3(.45f, .45f, 1f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
        ship = go.transform;
        return cam;
    }

    static readonly System.Reflection.FieldInfo PlayerField = typeof(BossEncounter).GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    static void Strip(string dir, int world, int index)
    {
        Transform ship;
        var cam = Scene(world, out ship);
        var boss = BossCatalog.ForWorld(world);
        var attack = boss.attacks[index];
        EliteSystem.PlayerOverride = ship;
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        PlayerField.SetValue(e, ship);
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = index;
        for (int i = 0; i < 600 && !e.Actor.Telegraphing; i++) e.Step(Dt, 1f);

        float tell = attack.tellSeconds;
        float[] at = { .08f, tell * .5f, tell - .06f, tell + .12f, tell + .5f, tell + 1.0f, tell + 1.7f };
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var sheet = new Texture2D(Width * at.Length, Height, TextureFormat.RGB24, false);
        float t = 0f;
        for (int k = 0; k < at.Length; k++)
        {
            while (t < at[k] && e.State == BossEncounter.Phase.Fight) { e.Step(Dt, 1f); t += Dt; if (backdrop != null) backdrop.Step(Dt); }
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            png.Apply();
            RenderTexture.active = old;
            sheet.SetPixels(Width * k, 0, Width, Height, png.GetPixels());
        }
        sheet.Apply();
        string stem = Path.Combine(dir, "bossthm-" + boss.artKey.ToLower() + "-" + attack.name.Replace(' ', '_'));
        File.WriteAllBytes(stem + ".png", sheet.EncodeToPNG());
        File.WriteAllText(stem + ".txt", boss.name + " -- " + attack.name + " (" + attack.kind + ", from " + string.Join("+", attack.emitters) + ", tell " + attack.tellSeconds + " s); frames at " + string.Join(", ", System.Array.ConvertAll(at, x => x.ToString("0.00"))) + " s from the tell");
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(sheet);
        Object.DestroyImmediate(rt);
        BossEncounter.ResetRun();
        Debug.Log("[BOSSTHM-PREVIEW] " + stem);
    }
}
