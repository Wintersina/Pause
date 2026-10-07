using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames for review of the hostile-projectile pass (HostileGlow,
// HostileShots, FriendlyFire, EnemySplit): a boss volley over each world's
// backdrop, shots breaking each other (and a laser burning through them),
// enemies splitting into pieces. PNG frames at 15 fps of world time, named
// proj-<clip>-NNN.png, for a GIF maker.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod HostileProjectilePreview.Run
//   (writes to $PROJGLOW_PREVIEW_DIR, else Builds/ProjGlowPreview)
public static class HostileProjectilePreview
{
    const int Width = 360, Height = 780;
    const float Dt = 1f / 30f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("PROJGLOW_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ProjGlowPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++) Volley(dir, w);
                Clash(dir);
                Splits(dir);
            }
            finally
            {
                BossEncounter.ResetRun();
                EliteSystem.Clear();
                EliteSystem.PlayerOverride = null;
                EnemySplit.ForceInEditor = false;
                EnemySplit.ChanceOverride = -1f;
            }
        }
        EditorApplication.Exit(0);
    }

    static Camera cam;
    static RenderTexture rt;
    static Texture2D png;
    static WorldBackdrop backdrop;

    static void Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var bgGo = new GameObject("~Backdrop");
        backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (png == null) png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static void Shoot(string stem, int frame)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(stem + "-" + frame.ToString("000") + ".png", png.EncodeToPNG());
    }

    static void TickFx(float dt)
    {
        foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) fx.Tick(dt);
        if (backdrop != null) backdrop.Step(dt);
        if (HazardRuntime.Instance != null) HazardRuntime.Instance.Step(dt);
    }

    // The world's boss firing its fans / volleys: every shot in its wrapper.
    static void Volley(string dir, int world)
    {
        Scene(world);
        var boss = BossCatalog.ForWorld(world);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        int pick = 0;
        for (int a = 0; a < boss.attacks.Length; a++)
            if (boss.attacks[a].kind == BossAttackKind.Fan) { pick = a; break; }
        e.Actor.ForcedAttack = pick;
        string stem = Path.Combine(dir, "proj-volley-" + Worlds[world].ToLower());
        int n = 0;
        for (int f = 0; f < 150; f++)
        {
            e.Step(Dt, 1f);
            TickFx(Dt);
            if (f >= 30 && f % 2 == 0) Shoot(stem, n++);
        }
        Debug.Log("[PROJGLOW-PREVIEW] " + stem + " " + n);
    }

    // Two elites trading fire across a boss's laser: shots meeting pop,
    // the laser burns whatever crosses it.
    static void Clash(string dir)
    {
        Scene(0);
        moveBackGround.speed = .05f;
        EliteDef gun = null, strike = null, siege = null;
        foreach (var d in EliteCatalog.All)
        {
            if (d.brain == "gunship") gun = d;
            if (d.brain == "striker") strike = d;
            if (d.brain == "siege") siege = d;
        }
        var a = EliteShip.CreateInPlay(gun, new Vector2(-1.8f, 1.5f));
        var b = EliteShip.CreateInPlay(strike, new Vector2(1.8f, -.5f));
        a.AttackCooldown = b.AttackCooldown = 99f;
        var pool = new BossProjectilePool(40, 2);
        var boss = BossCatalog.ForWorld(0);
        string stem = Path.Combine(dir, "proj-clash");
        int n = 0;
        try
        {
            for (int f = 0; f < 120; f++)
            {
                if (f % 9 == 0)
                {
                    float y = 1.5f - (f % 27) * .05f;
                    EliteSystem.Shots.Fire(a, gun, EliteShots.Kind.Bolt, new Vector2(-1.4f, y), new Vector2(3.2f, -1.2f));
                    EliteSystem.Shots.Fire(b, strike, EliteShots.Kind.Shard, new Vector2(1.4f, -.3f), new Vector2(-3.2f, .9f));
                }
                if (f % 14 == 0)
                    pool.Fire(boss, BossShotStyle.Shard, new Vector3(-.6f + (f % 28) * .04f, 3.6f, 0f), new Vector2(.2f, -3.2f));
                if (f == 40) EliteSystem.Shots.Fire(a, siege, EliteShots.Kind.Shell, new Vector2(-1.4f, -2.2f), new Vector2(3f, 0f));
                if (f == 60) pool.Beam(boss, null, -1, new Vector3(.4f, 4f, 0f), -90f, 0f, .3f, 1.4f, .3f);
                EliteSystem.Step(Dt);
                pool.Step(Dt);
                TickFx(Dt);
                if (f % 2 == 0) Shoot(stem, n++);
            }
        }
        finally { pool.Dispose(); }
        Debug.Log("[PROJGLOW-PREVIEW] " + stem + " " + n + " pops=" + HostileShots.Pops + " burns=" + HostileShots.BeamBurns);
    }

    // A wave of rocks, craft and mines broken by a boss's fire and their own
    // mine blasts: some burst, some split into pieces.
    static void Splits(string dir)
    {
        Scene(3);
        moveBackGround.speed = .05f;
        EnemySplit.ForceInEditor = true;
        EnemySplit.Seed(11u);
        var roles = new[] { EnemyRole.Rock, EnemyRole.Fighter, EnemyRole.Big, EnemyRole.Alien, EnemyRole.Mine, EnemyRole.Rock };
        var hazards = new GameObject[roles.Length * 2];
        for (int i = 0; i < hazards.Length; i++)
        {
            var def = EnemyRoster.One(3, roles[i % roles.Length]);
            var at = new Vector2(-2f + (i % 6) * .8f, i < 6 ? .6f : -1.6f);
            hazards[i] = EnemyFactory.Create(def, at, Quaternion.identity);
            ClearTarget.Ensure(hazards[i]);
        }
        var pool = new BossProjectilePool(40, 2);
        var boss = BossCatalog.ForWorld(3);
        string stem = Path.Combine(dir, "proj-split");
        int n = 0;
        try
        {
            for (int f = 0; f < 110; f++)
            {
                if (f == 10 || f == 34)
                    for (int i = 0; i < 6; i++)
                        pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-2f + i * .8f, 3.8f, 0f), Vector2.down * 5f);
                // every other kill splits in this clip, so both show
                EnemySplit.ChanceOverride = .5f;
                pool.Step(Dt);
                TickFx(Dt);
                if (f % 2 == 0) Shoot(stem, n++);
            }
        }
        finally { pool.Dispose(); EnemySplit.ChanceOverride = -1f; }
        Debug.Log("[PROJGLOW-PREVIEW] " + stem + " " + n + " splits=" + EnemySplit.Splits + " rolls=" + EnemySplit.Rolls);
    }
}
