using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames for review of the rail-mine laser and the hostile shots' outline:
// per world, a rail mine's attack (its telegraph, then the shot / beam), and
// an ordinary enemy shot, an elite shot and a boss shot side by side over the
// world's backdrop -- a full view and a 2x close-up of the shots. Also logs,
// per world, how many pixels of the smallest elite / enemy shot stand
// MinContrast clear of the backdrop (the same measure HostileProjectileTest
// uses for boss shots).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod MineLaserPreview.Run
//   (writes to $MINE_LASER_PREVIEW_DIR, else Builds/MineLaserPreview)
public static class MineLaserPreview
{
    const int Width = 540, Height = 960;
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("MINE_LASER_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/MineLaserPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++)
                {
                    Shots(dir, w);
                    Mine(dir, w);
                }
            }
            finally
            {
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

    static Camera cam;
    static RenderTexture rt;
    static Texture2D png;
    static Transform pilot;

    static void Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        EliteSystem.Clear();
        EnemyThreat.Reset();
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
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var bgGo = new GameObject("~Backdrop");
        var backdrop = bgGo.AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4f, 0f);
        EliteSystem.PlayerOverride = pilot;
        HazardRuntime.Ensure();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (png == null) png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static Color[] Grab()
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        return png.GetPixels();
    }

    static void Save(string path)
    {
        Grab();
        File.WriteAllBytes(path, png.EncodeToPNG());
    }

    // A row of shots: enemy (this world's roster shot), elite (bolt, shard,
    // slag, shell), boss (bolt, shard); and their stand-out numbers.
    static void Shots(string dir, int world)
    {
        Scene(world);
        var pool = new BossProjectilePool(16, 1);
        var boss = BossCatalog.ForWorld(world);
        EliteDef gun = null;
        foreach (var d in EliteCatalog.All) if (d.brain == "gunship") gun = d;
        // this world's ordinary enemy shot: its first roster shooter's style
        EnemyBehaviour rosterB = null;
        foreach (var def in EnemyRoster.All)
        {
            if (def.world != world || def.role == EnemyRole.Mine) continue;
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring) && b.shotKind != EliteShots.Kind.Glob) { rosterB = b; break; }
        }
        var small = new List<Component>();
        var bossShots = new List<Component>();
        float y = .6f;
        for (int row = 0; row < 3; row++, y -= 1.4f)
        {
            if (rosterB != null)
            {
                var s = EliteSystem.Shots.Fire(null, rosterB.ShotStyle, rosterB.shotKind, new Vector2(-2f, y), Vector2.zero);
                if (s != null) { s.AsRosterShot(null, 0f); small.Add(s); }
            }
            var kinds = new[] { EliteShots.Kind.Bolt, EliteShots.Kind.Shard, EliteShots.Kind.Slag };
            var e = EliteSystem.Shots.Fire(null, gun, kinds[row], new Vector2(-.7f, y), Vector2.zero);
            if (e != null) small.Add(e);
            var e2 = EliteSystem.Shots.Fire(null, gun, EliteShots.Kind.Shell, new Vector2(.5f, y), Vector2.zero);
            if (e2 != null) small.Add(e2);
            var b1 = pool.Fire(boss, row == 1 ? BossShotStyle.Shard : BossShotStyle.Bolt, new Vector3(1.8f, y, 0f), Vector2.zero);
            if (b1 != null) bossShots.Add(b1);
        }
        // a few running frames so the pulses are mid-breath
        for (int i = 0; i < 9; i++) { EliteSystem.Shots.Step(0f); pool.Step(Dt); }

        string stem = Path.Combine(dir, "shots-" + Worlds[world].ToLower());
        Save(stem + "-full.png");
        // a close-up on the shots
        float size = cam.orthographicSize;
        var pos = cam.transform.position;
        cam.orthographicSize = size / 2f;
        cam.transform.position = new Vector3(-.1f, -.8f, -10f);
        Save(stem + "-zoom.png");
        cam.orthographicSize = size;
        cam.transform.position = pos;

        // stand-out: pixels of each small shot >= 3:1 against the mean backdrop around it
        foreach (var c in small) c.gameObject.SetActive(false);
        pool.Root.SetActive(false);
        var bg = Grab();
        foreach (var c in small) c.gameObject.SetActive(true);
        pool.Root.SetActive(true);
        var fg = Grab();
        int fewestSmall = int.MaxValue, fewestBoss = int.MaxValue;
        float ppu = Height / (cam.orthographicSize * 2f);
        foreach (var c in small)
        {
            var sr = c.GetComponent<SpriteRenderer>();
            float half = Mathf.Max(sr.bounds.extents.x, sr.bounds.extents.y) * ppu + 2f;
            fewestSmall = Mathf.Min(fewestSmall, StandOut(fg, bg, cam.WorldToScreenPoint(c.transform.position), half));
        }
        foreach (var c in bossShots)
        {
            float half = c.transform.lossyScale.x * .5f * ppu;
            fewestBoss = Mathf.Min(fewestBoss, StandOut(fg, bg, cam.WorldToScreenPoint(c.transform.position), half));
        }
        int rounds = 0;
        foreach (var c in small)
            foreach (var r in c.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sprite == HostileGlow.Halo) rounds++;
        Debug.Log("[MINE-LASER-PREVIEW] " + Worlds[world] + ": smallest enemy/elite shot stands out by " + fewestSmall +
                  " px at 3:1 (" + small.Count + " shots, " + rounds + " round halos); boss shots " + fewestBoss + " px");
        pool.Dispose();
    }

    // A rail mine on the right rail, the pilot level with it: its windup
    // (telegraph) and its attack.
    static void Mine(string dir, int world)
    {
        Scene(world);
        EnemyThreat.ForceShooting = true;
        float clock = 100f;
        SpawnSpace.ClockOverride = clock;
        var def = EnemyRoster.One(world, EnemyRole.Mine);
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
        string stem = Path.Combine(dir, "mine-" + Worlds[world].ToLower());
        bool tele = false, fired = false;
        float sinceFire = 0f;
        for (int i = 0; i < 60 * 8; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            if (fb != null) fb.Advance(Dt);
            EliteSystem.Step(Dt);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "RailMineLaser") mb.SendMessage("LateUpdate");
            if (!tele && brain.State == EnemyBrain.Phase.Windup && brain.StateTime > .6f * Mathf.Max(EnemyBrain.TellFloorSeconds, brain.Behaviour.tell))
            {
                Save(stem + "-1-telegraph.png");
                tele = true;
            }
            if (tele && !fired && brain.State == EnemyBrain.Phase.Release) fired = true;
            if (fired)
            {
                sinceFire += Dt;
                if (sinceFire >= .15f) { Save(stem + "-2-attack.png"); break; }
            }
        }
        if (!tele) Save(stem + "-1-idle.png");
        Debug.Log("[MINE-LASER-PREVIEW] " + Worlds[world] + " mine: telegraph " + tele + ", fired " + fired + " (" + brain.Behaviour.attack + ")");
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(rail);
        EnemyThreat.ForceShooting = false;
    }

    static int StandOut(Color[] fg, Color[] bg, Vector3 c, float half)
    {
        int r = Mathf.CeilToInt(half), n = 0, count = 0;
        int x0 = Mathf.Max(0, (int)c.x - r), x1 = Mathf.Min(Width - 1, (int)c.x + r);
        int y0 = Mathf.Max(0, (int)c.y - r), y1 = Mathf.Min(Height - 1, (int)c.y + r);
        float behind = 0f;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { behind += Lum(bg[y * Width + x]); n++; }
        if (n == 0) return 0;
        behind /= n;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float f = Lum(fg[y * Width + x]);
                if ((Mathf.Max(f, behind) + .05f) / (Mathf.Min(f, behind) + .05f) >= 3f) count++;
            }
        return count;
    }

    static float Lin(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
    static float Lum(Color c) => .2126f * Lin(c.r) + .7152f * Lin(c.g) + .0722f * Lin(c.b);
}
