using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "the attack patterns don't make sense ... rework the attacks and
// lasers so they come from the bosses' arms and mouths, from the boss itself,
// not randomly on the screen; have some of the projectiles bounce off the
// rails vs just go through the rails."
//
//   * every attack fires from named body parts whose muzzle pixels (measured
//     from the art, per drawing) land on opaque boss pixels;
//   * every shot starts at its part in the drawing on screen when it fires,
//     on the boss -- never anywhere else on the screen;
//   * lasers are rooted at their part, follow it, and grow out of it;
//   * ricochets reflect off the rails' inner faces and splash after their
//     bounces; splashers stop at the rail; pass-through shots never reflect;
//   * a frozen world freezes all of it; a blink still erases shots and lasers;
//   * every pattern leaves the ship a way through (simulated).
//
// Drives BossEncounter / BossActor frame by frame in edit mode.
public static class BossAttackTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSATK] PASS  " : "[BOSSATK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EveryBossHasASensibleSet();
            EmittersLandOnTheArt();
            ShotsStartAtTheirPart();
            LasersGrowOutOfTheirPart();
            RicochetsReflectOffTheRails();
            SplashersAndPassersDontReflect();
            RailsAreTheWallsInnerFaces();
            FrozenWorldFreezesAttacks();
            BlinkStillErasesShotsAndLasers();
            EveryPatternLeavesAWayThrough();
            SteadyStepsDontAllocate();
        }
        finally
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
        }
        Debug.Log("[BOSSATK] failures: " + fails);
        return fails;
    }

    // Only the dodge simulation (every pattern leaves a way through), for
    // suites that change what shots do (HostileProjectileTest). Failures.
    public static int DodgeSimulation()
    {
        fails = 0;
        try { EveryPatternLeavesAWayThrough(); }
        finally { BossEncounter.ResetRun(); BossRails.Reset(); }
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void FreshScene(int world = 0)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static BossEncounter StartFight(int world, int forced)
    {
        FreshScene(world);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = forced;
        return e;
    }

    const float Dt = 1f / 60f;

    static readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>();

    static Texture2D Atlas(BossDef boss)
    {
        Texture2D t;
        if (atlases.TryGetValue(boss.artKey, out t) && t != null) return t;
        t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Bosses/" + boss.artKey + ".png"));
        atlases[boss.artKey] = t;
        return t;
    }

    // Alpha of the boss's drawing `frame` at cell pixel (x right, y down).
    static float Alpha(BossDef boss, int frame, int px, int py)
    {
        var t = Atlas(boss);
        int cell = BossEmitterTable.CellPixels;
        int col = frame % BossArt.BodyColumns, row = frame / BossArt.BodyColumns;
        int x = col * cell + px, y = t.height - 1 - (row * cell + py);
        if (px < 0 || py < 0 || px >= cell || py >= cell) return 0f;
        return t.GetPixel(x, y).a;
    }

    // A world point, as a cell pixel of the boss's drawing at `actor`.
    static Vector2Int ToPixel(BossActor actor, Vector3 p)
    {
        float k = BossEmitterTable.CellPixels / BossConfig.BossWorldSize;
        float half = BossEmitterTable.CellPixels * .5f;
        Vector3 d = p - actor.transform.position;
        return new Vector2Int(Mathf.RoundToInt(half + d.x * k), Mathf.RoundToInt(half - d.y * k));
    }

    static int FireFrameOf(BossAttack a) => a.fireFrame ? BossArt.Fire : BossArt.Tell(a.tell, 1);

    // ---- tests ---------------------------------------------------------

    static void EveryBossHasASensibleSet()
    {
        foreach (var boss in BossCatalog.All)
        {
            bool bounce = false, beam = false, parts = true;
            var tells = new HashSet<int>();
            foreach (var a in boss.attacks)
            {
                tells.Add(a.tell);
                bounce |= a.kind != BossAttackKind.Beam && a.rail == BossRailMode.Bounce && a.bounces > 0;
                beam |= a.kind == BossAttackKind.Beam;
                parts &= a.parts != null && a.parts.Length > 0 && a.parts.Length <= BossActor.MaxParts;
                if (a.parts != null) foreach (int p in a.parts) parts &= p >= 0;
                if (a.radialFrom != null) parts &= a.radialPart >= 0;
            }
            Check(boss.artKey + ": each tell pose (0, 1, 2) maps to its own attack",
                  tells.Count == boss.attacks.Length && tells.Contains(0) && tells.Contains(1) && tells.Contains(2));
            Check(boss.artKey + ": every attack names body parts the table knows", parts);
            Check(boss.artKey + ": at least one attack ricochets off the rails", bounce);
            Check(boss.artKey + ": its laser attack grows out of a part", beam);
            Check(boss.artKey + ": not every shot ricochets (some splash on the rails)",
                  System.Array.Exists(boss.attacks, a => a.kind != BossAttackKind.Beam && a.rail != BossRailMode.Bounce));
        }
        Check("the emitter table is generated from the art",
              File.ReadAllText("Assets/Scripts/Bosses/BossEmitterTable.cs").Contains("GENERATED by Art/BossAttacks/src~/measure_emitters.py") &&
              File.Exists("Assets/Art/BossAttacks/src~/measure_emitters.py"));
        Check("the attack FX sheet is generated and resolves",
              File.Exists("Assets/Art/BossAttacks/src~/build_boss_attack_fx.py") &&
              BossAttackFx.Get(BossCatalog.All[0], BossAttackFx.Spark0) != null &&
              BossAttackFx.Get(BossCatalog.All[3], BossAttackFx.Ring) != null);
    }

    // Every part's muzzle, in every drawing the boss can tell or fire from,
    // is an opaque pixel of that drawing; and each attack's parts in its
    // firing drawing in particular.
    static void EmittersLandOnTheArt()
    {
        foreach (var boss in BossCatalog.All)
        {
            int w = BossEmitters.World(boss);
            int bad = 0, total = 0;
            string first = null;
            for (int part = 0; part < BossEmitterTable.Parts[w].Length; part++)
                for (int f = 0; f < BossEmitterTable.Frames; f++)
                {
                    total++;
                    var px = BossEmitters.Pixel(boss, part, f);
                    if (Alpha(boss, f, px.x, px.y) < .5f)
                    {
                        bad++;
                        if (first == null) first = BossEmitterTable.Parts[w][part] + "@" + f;
                    }
                }
            Check(boss.artKey + ": every muzzle point is on an opaque boss pixel (" + (total - bad) + "/" + total +
                  (first != null ? ", first miss " + first : "") + ")", bad == 0);
            foreach (var a in boss.attacks)
            {
                bool on = true;
                foreach (int p in a.parts)
                {
                    var px = BossEmitters.Pixel(boss, p, FireFrameOf(a));
                    on &= Alpha(boss, FireFrameOf(a), px.x, px.y) >= .5f;
                    // a muzzle sits inside the drawn body, not out at the cell's edge
                    on &= px.x > 8 && px.x < 376 && px.y > 8 && px.y < 376;
                }
                Check(boss.artKey + " " + a.name + ": fires from " + string.Join("+", a.emitters) +
                      ", on the body in its firing drawing", on);
            }
        }
    }

    // The shots of each attack start at one of its parts, in the drawing on
    // screen as they fire, on an opaque pixel -- next to the boss.
    static void ShotsStartAtTheirPart()
    {
        for (int world = 0; world < BossCatalog.All.Length; world++)
        {
            var boss = BossCatalog.ForWorld(world);
            for (int ai = 0; ai < boss.attacks.Length; ai++)
            {
                var a = boss.attacks[ai];
                if (a.kind == BossAttackKind.Beam) continue;
                var e = StartFight(world, ai);
                var seen = new HashSet<BossProjectile>();
                int launched = 0, offPart = 0, offArt = 0, far = 0;
                bool frameOk = true;
                for (int i = 0; i < 600 && e.Actor.VolleysFired < a.volleys; i++)
                {
                    e.Step(Dt, 1f);
                    foreach (var s in e.Pool.Shots)
                    {
                        if (s == null || !s.Active || seen.Contains(s)) continue;
                        seen.Add(s);
                        launched++;
                        frameOk &= e.Actor.BodyFrame == FireFrameOf(a);
                        float best = float.MaxValue;
                        foreach (int p in a.parts) best = Mathf.Min(best, Vector3.Distance(s.LaunchedAt, e.Actor.Emitter(p)));
                        if (best > .01f) offPart++;
                        var px = ToPixel(e.Actor, s.LaunchedAt);
                        if (Alpha(boss, e.Actor.BodyFrame, px.x, px.y) < .5f) offArt++;
                        Vector3 d = s.LaunchedAt - e.Actor.transform.position;
                        if (Mathf.Abs(d.x) > BossConfig.BossWorldSize * .5f || Mathf.Abs(d.y) > BossConfig.BossWorldSize * .5f) far++;
                    }
                }
                string tag = boss.artKey + " " + a.name + ": ";
                Check(tag + "fired " + launched + " shots over its " + a.volleys + " volleys", launched > 0);
                Check(tag + "each shot starts at its part's muzzle (" + (launched - offPart) + "/" + launched + ")", offPart == 0);
                Check(tag + "... an opaque pixel of the drawing on screen (" + (launched - offArt) + "/" + launched + ")", offArt == 0);
                Check(tag + "... on the boss, not elsewhere on the screen", far == 0);
                Check(tag + "... while the boss shows its " + (a.fireFrame ? "fire" : "tell " + a.tell) + " pose", frameOk);
            }
        }
    }

    // Each laser's root is its part on the boss every frame; it grows out
    // of it (never pops in at full length) and ends on a rail or past the
    // bottom of the view.
    static void LasersGrowOutOfTheirPart()
    {
        for (int world = 0; world < BossCatalog.All.Length; world++)
        {
            var boss = BossCatalog.ForWorld(world);
            for (int ai = 0; ai < boss.attacks.Length; ai++)
            {
                var a = boss.attacks[ai];
                if (a.kind != BossAttackKind.Beam) continue;
                var e = StartFight(world, ai);
                string tag = boss.artKey + " " + a.name + ": ";
                for (int i = 0; i < 400 && e.Pool.ActiveBeams == 0; i++) e.Step(Dt, 1f);
                Check(tag + "one laser per part (" + e.Pool.ActiveBeams + ")", e.Pool.ActiveBeams == a.parts.Length);
                Check(tag + "the tell shows a sight line first, harmless",
                      AllBeams(e, b => b.Telegraphing && b.Hitbox == null));
                bool rooted = true, opaque = true, grew = false, popped = false, ends = true;
                var lastLen = new Dictionary<BossBeam, float>();
                for (int i = 0; i < 400 && e.Pool.ActiveBeams > 0; i++)
                {
                    e.Step(Dt, 1f);
                    foreach (var b in e.Pool.Beams)
                    {
                        if (b == null || !b.Active) continue;
                        rooted &= Vector3.Distance(b.Origin, e.Actor.Emitter(b.Part)) < .001f && b.Owner == e.Actor;
                        var px = ToPixel(e.Actor, b.Origin);
                        opaque &= Alpha(boss, e.Actor.BodyFrame, px.x, px.y) >= .5f;
                        if (!b.Live) continue;
                        float before;
                        if (!lastLen.TryGetValue(b, out before))
                        {
                            // its first live frame: a stub out of the part
                            popped |= b.Length > BossConfig.BeamGrowSpeed * Dt * 1.01f + .001f || b.Length >= b.Reach - .001f;
                        }
                        else if (b.Length > before + 1e-4f) grew = true;
                        lastLen[b] = b.Length;
                        ends &= b.Length <= b.Reach + 1e-4f;
                        // its end: on a rail's inner face, or past the view's bottom
                        if (b.Length >= b.Reach - 1e-3f)
                        {
                            Vector3 end = b.Origin + (Vector3)(b.Direction * b.Length);
                            ends &= b.EndsOnRail ? Mathf.Abs(Mathf.Abs(end.x) - BossRails.InnerEdge) < .01f : end.y < CameraFit.ViewBottom;
                        }
                    }
                }
                Check(tag + "each laser is rooted at its part on the moving boss, every frame", rooted);
                Check(tag + "... on an opaque pixel of the drawing on screen", opaque);
                Check(tag + "it grows out of the part (no full-length pop-in)", grew && !popped);
                Check(tag + "it ends on a rail or past the bottom of the view", ends);
            }
        }
    }

    static bool AllBeams(BossEncounter e, System.Func<BossBeam, bool> ok)
    {
        foreach (var b in e.Pool.Beams) if (b != null && b.Active && !ok(b)) return false;
        return true;
    }

    // A ricochet reflects off the rail's inner face (its edge touching it),
    // sparks, spends a bounce; after its last it splashes on the next rail.
    static void RicochetsReflectOffTheRails()
    {
        FreshScene();
        var boss = BossCatalog.ForWorld(1);
        var pool = new BossProjectilePool(4, 1);
        float edge = BossRails.InnerEdge;
        var s = pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.5f, 0f, 0f), new Vector2(6f, -.5f), BossRailMode.Bounce, 2, 0f, 0f);
        float limit = edge - s.Radius;
        float maxX = float.MinValue, minX = float.MaxValue;
        int sparks = pool.SparksPlayed;
        bool flipped = false;
        int i = 0;
        for (; i < 400 && s.Active && s.Bounces == 0; i++) { pool.Step(Dt); maxX = Mathf.Max(maxX, s.transform.position.x); }
        flipped = s.Velocity.x < 0f;
        Check("a ricochet reflects at the right rail's face (x <= " + limit.ToString("F3") + ", peak " + maxX.ToString("F3") + ")",
              s.Active && s.Bounces == 1 && flipped && maxX <= limit + 1e-4f && maxX > limit - .11f);
        Check("... with a spark on the rail", pool.SparksPlayed == sparks + 1);
        Check("... keeping its speed, only its x turned", Mathf.Abs(s.Velocity.x + 6f) < 1e-4f && Mathf.Abs(s.Velocity.y + .5f) < 1e-4f);
        for (; i < 800 && s.Active && s.Bounces == 1; i++) { pool.Step(Dt); minX = Mathf.Min(minX, s.transform.position.x); }
        Check("... and again off the left rail (min " + minX.ToString("F3") + ")",
              s.Active && s.Bounces == 2 && s.Velocity.x > 0f && minX >= -limit - 1e-4f);
        for (; i < 1200 && s.Active; i++) pool.Step(Dt);
        Check("after its bounces it splashes on the next rail, at the rail",
              !s.Active && s.EndReason == 2 && Mathf.Abs(s.transform.position.x - limit) < .01f && s.Bounces == 2);
        Check("... sparking there too", pool.SparksPlayed == sparks + 3);
        pool.Dispose();

        // Every bouncing attack in the catalogue really carries its bounces.
        foreach (var b in BossCatalog.All)
            foreach (var a in b.attacks)
                if (a.rail == BossRailMode.Bounce && a.kind != BossAttackKind.Beam)
                {
                    var e = StartFight(System.Array.IndexOf(BossCatalog.All, b), System.Array.IndexOf(b.attacks, a));
                    int most = 0;
                    bool spent = false;
                    for (int k = 0; k < 900; k++)
                    {
                        e.Step(Dt, 1f);
                        foreach (var p in e.Pool.Shots)
                            if (p != null && p.Active)
                            {
                                most = Mathf.Max(most, p.Bounces);
                                spent |= p.Bounces > a.bounces;
                            }
                    }
                    Check(b.artKey + " " + a.name + ": its shots ricochet in play (up to " + most + " of " + a.bounces + ")",
                          most >= 1 && !spent);
                }
    }

    static void SplashersAndPassersDontReflect()
    {
        FreshScene();
        var boss = BossCatalog.ForWorld(0);
        var pool = new BossProjectilePool(4, 1);
        var s = pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.8f, 0f, 0f), new Vector2(5f, -1f), BossRailMode.Absorb, 3, 0f, 0f);
        int sparks = pool.SparksPlayed;
        for (int i = 0; i < 300 && s.Active; i++) pool.Step(Dt);
        Check("a splasher stops at the rail, never reflecting (bounces asked of it are ignored)",
              !s.Active && s.EndReason == 2 && s.Bounces == 0 && s.transform.position.x <= BossRails.InnerEdge);
        Check("... with a spark", pool.SparksPlayed == sparks + 1);

        var q = pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.8f, 0f, 0f), new Vector2(5f, -1f), BossRailMode.Pass, 3, 0f, 0f);
        float maxX = 0f;
        bool reflected = false;
        for (int i = 0; i < 300 && q.Active; i++) { pool.Step(Dt); maxX = Mathf.Max(maxX, q.transform.position.x); reflected |= q.Velocity.x < 0f; }
        Check("a pass-through shot flies on past the rail and off the screen",
              !q.Active && q.EndReason == 1 && !reflected && maxX > BossRails.InnerEdge && q.Bounces == 0);
        pool.Dispose();
    }

    // The rails are the walls' inner faces, measured from the live walls
    // (world-fixed quads), the same on every screen.
    static void RailsAreTheWallsInnerFaces()
    {
        FreshScene();
        BossRails.Reset();
        Check("without walls the authored inner edge (" + BossRails.AuthoredInnerEdge + ") is used",
              Mathf.Approximately(BossRails.InnerEdge, ResumeFx.RailInnerEdge));
        foreach (float aspect in new[] { 9f / 16f, 9f / 21f, 3f / 4f })
        {
            var cam = Camera.main;
            cam.aspect = aspect;
            cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, 1080, Mathf.RoundToInt(1080 / aspect));
            var l = GameObject.CreatePrimitive(PrimitiveType.Quad);
            l.name = "leftPipe";
            l.transform.position = new Vector3(-3.21f, 0f, 1f);
            l.transform.localScale = new Vector3(1.43f, 10.75f, 1f);
            var r = GameObject.CreatePrimitive(PrimitiveType.Quad);
            r.name = "rightPipe";
            r.transform.position = new Vector3(3.21f, 0f, 1f);
            r.transform.localScale = new Vector3(1.43f, 10.75f, 1f);
            BossRails.Measure();
            float halfW = cam.orthographicSize * cam.aspect;
            Check("aspect " + aspect.ToString("F2") + ": the rail is the walls' inner face (" + BossRails.InnerEdge.ToString("F3") +
                  "), inside the view (" + halfW.ToString("F2") + ") and outside the ship's reach (2.4)",
                  Mathf.Abs(BossRails.InnerEdge - 2.495f) < .002f && BossRails.InnerEdge < halfW && BossRails.InnerEdge > 2.4f);
            Object.DestroyImmediate(l);
            Object.DestroyImmediate(r);
        }
        Check("the encounter measures the rails as the boss arrives",
              File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("BossRails.Measure();"));
        BossRails.Reset();
    }

    static void FrozenWorldFreezesAttacks()
    {
        // a laser mid-burn and Frost's ricochets in flight
        var e = StartFight(0, 2);
        for (int i = 0; i < 600 && !AnyLive(e); i++) e.Step(Dt, 1f);
        for (int i = 0; i < 10; i++) e.Step(Dt, 1f);
        var beams = Snapshot(e);
        for (int i = 0; i < 60; i++) e.Step(Dt, 0f);
        Check("a frozen world holds a burning laser exactly (root, angle, length)", beams == Snapshot(e) && AnyLive(e));
        e.Step(Dt, 1f);
        Check("... and it moves on with time", beams != Snapshot(e));

        e = StartFight(1, 0);
        for (int i = 0; i < 600 && e.Pool.ActiveShots == 0; i++) e.Step(Dt, 1f);
        e.Step(.2f, 1f);
        var shots = Snapshot(e);
        int sparks = e.Pool.SparksPlayed;
        for (int i = 0; i < 60; i++) e.Step(Dt, 0f);
        Check("frozen ricochets hang in place", shots == Snapshot(e) && e.Pool.SparksPlayed == sparks);
    }

    static bool AnyLive(BossEncounter e)
    {
        foreach (var b in e.Pool.Beams) if (b != null && b.Live) return true;
        return false;
    }

    static string Snapshot(BossEncounter e)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(e.Actor.transform.position.ToString("F5"));
        foreach (var b in e.Pool.Beams)
            if (b != null && b.Active) sb.Append(b.Origin.ToString("F5")).Append(b.Angle.ToString("F4")).Append(b.Length.ToString("F4"));
        foreach (var s in e.Pool.Shots)
            if (s != null && s.Active) sb.Append(s.transform.position.ToString("F5"));
        return sb.ToString();
    }

    // The pause-teleport's blast erases boss shots (paying BossShot) and
    // live lasers (paying nothing) -- and the laser's visual goes with it.
    static void BlinkStillErasesShotsAndLasers()
    {
        var e = StartFight(1, 1);   // Frost's glare beams
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        RunScore.BeginRun(true, true);
        for (int i = 0; i < 600 && !AnyLive(e); i++) e.Step(Dt, 1f);
        for (int i = 0; i < 20; i++) e.Step(Dt, 1f);
        BossBeam beam = null;
        foreach (var b in e.Pool.Beams) if (b != null && b.Live) { beam = b; break; }
        Vector3 mid = beam.Origin + (Vector3)(beam.Direction * Mathf.Min(beam.Length, 4f));
        var shot = e.Pool.Fire(e.Boss, BossShotStyle.Shard, mid + new Vector3(.25f, 0f, 0f), Vector2.zero);
        long before = RunScore.Total;
        int kills = TeleportFx.Strike(mid);
        e.Step(Dt, 1f);
        Check("a blink on a laser and a shot erases both (" + kills + ")", kills >= 2 && !beam.Active && !shot.Active);
        Check("... the shot pays BossShot, the laser nothing (" + (RunScore.Total - before) + ")",
              RunScore.Total - before == ScoreRules.BossShot);
        RunScore.EndRun(RunScore.RunId);
    }

    // For every attack of every boss, a ship at the bottom of the screen
    // (on the line it usually flies, and higher up) moving no faster than
    // MaxShipSpeed always has somewhere safe to be: some x on the line clear
    // of every live shot and laser, reachable from where it could have been
    // a frame earlier. The pattern repeats twice back to back.
    const float ShipRadius = .28f;
    const float MaxShipSpeed = 7f;

    static void EveryPatternLeavesAWayThrough()
    {
        for (int world = 0; world < BossCatalog.All.Length; world++)
        {
            var boss = BossCatalog.ForWorld(world);
            for (int ai = 0; ai < boss.attacks.Length; ai++)
            {
                foreach (float line in new[] { -3.5f, -2f })
                {
                    var e = StartFight(world, ai);
                    var a = boss.attacks[ai];
                    const int N = 97;
                    var reach = new bool[N];
                    var next = new bool[N];
                    for (int i = 0; i < N; i++) reach[i] = true;
                    int fewest = N, steps = 0;
                    float step = 4.8f / (N - 1);
                    int span = Mathf.Max(1, Mathf.FloorToInt(MaxShipSpeed * Dt / step));
                    bool alive = true;
                    for (; steps < 60 * 9 && alive && e.Actor.AttacksStarted < 3; steps++)
                    {
                        e.Step(Dt, 1f);
                        int count = 0;
                        for (int i = 0; i < N; i++)
                        {
                            next[i] = false;
                            float x = -2.4f + i * step;
                            if (!Safe(e, new Vector2(x, line))) continue;
                            for (int j = Mathf.Max(0, i - span); j <= Mathf.Min(N - 1, i + span); j++)
                                if (reach[j]) { next[i] = true; break; }
                            if (next[i]) count++;
                        }
                        fewest = Mathf.Min(fewest, count);
                        alive = count > 0;
                        var t = reach; reach = next; next = t;
                    }
                    Check(boss.artKey + " " + a.name + " (ship line y " + line + "): always a way through, " +
                          "narrowest " + (fewest * step).ToString("F2") + " wu of clear line",
                          alive && fewest * step >= .15f);
                }
            }
        }
    }

    static bool Safe(BossEncounter e, Vector2 p)
    {
        foreach (var s in e.Pool.Shots)
        {
            if (s == null || !s.Active || s.Hitbox == null) continue;
            if (((Vector2)s.transform.position - p).sqrMagnitude < (s.Radius + ShipRadius) * (s.Radius + ShipRadius)) return false;
        }
        foreach (var b in e.Pool.Beams)
        {
            if (b == null || !b.Live || b.Hitbox == null) continue;
            Vector2 o = b.Origin, d = b.Direction;
            float t = Mathf.Clamp(Vector2.Dot(p - o, d), 0f, b.Length);
            float half = b.Width * BossConfig.BeamHitFraction * .5f;
            if ((o + d * t - p).sqrMagnitude < (half + ShipRadius) * (half + ShipRadius)) return false;
        }
        return true;
    }

    // Steady flight -- shots ricocheting, a laser burning, sparks -- allocates
    // nothing per frame.
    static void SteadyStepsDontAllocate()
    {
        foreach (var pick in new[] { new Vector2Int(1, 0), new Vector2Int(3, 2) })
        {
            var e = StartFight(pick.x, pick.y);
            var a = e.Boss.attacks[pick.y];
            for (int i = 0; i < 900; i++)
            {
                e.Step(Dt, 1f);
                if (a.kind == BossAttackKind.Beam ? AnyLive(e) && i > 0 && e.Pool.Beams[0].Length > 3f : e.Pool.ActiveShots >= 4) break;
            }
            for (int i = 0; i < 3; i++) { e.Pool.Step(Dt); }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; i++) e.Pool.Step(Dt);
            long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(e.Boss.artKey + " " + a.name + ": steady pool steps allocate nothing (" + used + " bytes)", used == 0);
        }
    }
}
