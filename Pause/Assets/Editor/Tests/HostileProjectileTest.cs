using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "the boss projectiles dont need to be bigger but we do need ... a
// clear indicator ... a light wrapper glow ... very visible in any
// background; when projectiles hit each other, they can destroy each other
// too; make sure all enemy things have friendly fire on; and when they die
// or break they can split into pieces sometimes."
//
//   * every hostile projectile wears the wrapper (HostileGlow) and keeps its
//     size and hitbox; lasers wear a sheath; none is the player's red;
//   * the wrapper reads over every world's backdrop (and a bright flare):
//     a contrast ratio, from real renders, above MinContrast;
//   * shots of different owners (or volleys) break each other; a laser
//     burns shots crossing it; a heavy shell survives a bolt; a resin pool
//     swallows shots; player shots shoot hostile shots down;
//   * friendly fire: boss shots and lasers, elite shots and mine blasts
//     destroy rocks / enemies / mines and hurt elites, paying the pilot
//     nothing (an elite's death still pays its reward); chasers crash;
//   * kills split into fragments at the configured rate, capped;
//   * the boss dodge simulation still passes; nothing allocates per step.
public static class HostileProjectileTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HOSTILE] PASS  " : "[HOSTILE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const float MinContrast = 3f;
    const float Dt = 1f / 60f;
    static readonly Color PlayerRed = new Color32(255, 62, 78, 255);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            GlowOnEveryProjectile();
            ContrastOverEveryWorld();
            ShotVersusShot();
            LaserBurnsCrossingShots();
            PlayerShotsShootDown();
            FriendlyFireMatrix();
            SplitsIntoPieces();
            NoAllocations();
            if (TestHarness.Slow("boss dodge simulation"))
            {
                int dodge = BossAttackTest.DodgeSimulation();
                Check("the boss dodge simulation still passes with shots breaking each other (" + dodge + " failures)", dodge == 0);
            }
        }
        finally
        {
            EnemySplit.ForceInEditor = false;
            FriendlyFire.ClearPending();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            BossEncounter.ResetRun();
            BossRails.Reset();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            Time.timeScale = 1f;
        }
        Debug.Log("[HOSTILE] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static Camera Fresh(int world = 0)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        FriendlyFire.ClearPending();
        FriendlyFire.ResetCounters();
        HostileShots.ResetCounters();
        EnemySplit.ResetCounters();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        Random.InitState(4321);
        HazardRuntime.Ensure().ClearAll();
        return cam;
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return null;
    }

    static GameObject Hazard(EnemyRole role, Vector2 at, int world = 3)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(world, role), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static float WorldRadius(CircleCollider2D c) => c.radius * Mathf.Abs(c.transform.lossyScale.x);

    // ---- 1. the wrapper -----------------------------------------------------

    static void GlowOnEveryProjectile()
    {
        Fresh();
        var pool = new BossProjectilePool(8, 2);
        try
        {
            foreach (var boss in BossCatalog.All)
                foreach (var style in new[] { BossShotStyle.Bolt, BossShotStyle.Shard })
                {
                    var s = pool.Fire(boss, style, new Vector3(0f, 1f, 0f), Vector2.down);
                    float drawn = style == BossShotStyle.Bolt ? BossConfig.BoltWorldSize : BossConfig.ShardWorldSize;
                    float hit = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
                    var g = s.Glow;
                    float d = g.bounds.size.x;
                    float hb = WorldRadius(s.Hitbox.GetComponent<CircleCollider2D>());
                    Check(boss.artKey + " " + style + ": wears the glow (behind it, no collider), drawn at " + drawn +
                          " as before, hitbox " + hb.ToString("F3") + " (" + hit + ")",
                          g != null && g.enabled && g.sprite == HostileGlow.Halo && g.sortingOrder < 30 &&
                          g.GetComponent<Collider2D>() == null &&
                          Mathf.Abs(s.transform.lossyScale.x - drawn) < 1e-4f && Mathf.Abs(hb - hit) < 1e-4f);
                    Check("... the glow reaches only a little past the art (" + d.ToString("F2") + " wu across, " +
                          ((d - drawn) * .5f).ToString("F3") + " past each side)", d > drawn && (d - drawn) * .5f <= .12f);
                    Check("... tinted " + Hex(g.color) + ", never the player's red (" + HueGap(g.color, PlayerRed).ToString("F0") + " deg)",
                          HueGap(g.color, PlayerRed) >= 20f && !HostileGlow.IsPlayerRed(g.color));
                    s.Recycle();
                }

            // pulse: the wrapper breathes, the art and hitbox do not
            var p = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, new Vector3(0f, 0f, 0f), Vector2.down * .01f);
            float minS = 9f, maxS = 0f, minA = 9f, maxA = 0f;
            for (int i = 0; i < 40; i++)
            {
                pool.Step(Dt);
                minS = Mathf.Min(minS, p.Glow.transform.localScale.x); maxS = Mathf.Max(maxS, p.Glow.transform.localScale.x);
                minA = Mathf.Min(minA, p.Glow.color.a); maxA = Mathf.Max(maxA, p.Glow.color.a);
            }
            Check("the wrapper pulses (scale " + minS.ToString("F2") + ".." + maxS.ToString("F2") + ", alpha " +
                  minA.ToString("F2") + ".." + maxA.ToString("F2") + ") while the shot keeps its size",
                  maxS > minS * 1.08f && maxA > minA + .08f && Mathf.Abs(p.transform.lossyScale.x - BossConfig.BoltWorldSize) < 1e-4f);

            // lasers: a sheath along their length
            var beam = pool.Beam(BossCatalog.ForWorld(1), null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, 0f, 1f, .3f);
            for (int i = 0; i < 20; i++) pool.Step(Dt);
            var sh = beam.Sheath;
            Check("a live laser wears a glow sheath down its whole length (" + sh.bounds.size.y.ToString("F2") + " of " +
                  beam.Length.ToString("F2") + " wu, " + sh.bounds.size.x.ToString("F2") + " wide over a " + beam.Width + " beam)",
                  beam.Live && sh.enabled && sh.sprite == HostileGlow.Sheath &&
                  Mathf.Abs(sh.bounds.size.y - beam.Length) < .05f && sh.bounds.size.x > beam.Width &&
                  sh.bounds.size.x <= beam.Width + .3f && sh.sortingOrder < 26);
            Check("... its hitbox is the beam's as before",
                  Mathf.Abs(beam.Hitbox.GetComponent<BoxCollider2D>().size.x - beam.Width * BossConfig.BeamHitFraction) < 1e-4f);
            for (int i = 0; i < 120 && beam.Active; i++) pool.Step(Dt);
            Check("... and it goes with the laser", !beam.Active && !sh.enabled);
        }
        finally { pool.Dispose(); }

        // the elites' shots, every kind (frost shards, resin globs and pools)
        var shots = EliteSystem.Shots;
        foreach (var d in EliteCatalog.All)
        {
            var kind = EliteShots.KindOf(d.shotKind);
            var s = shots.Fire(null, d, kind, new Vector2(0f, 1f), Vector2.down * 2f);
            if (s == null) { Check(d.key + ": could fire", false); continue; }
            var col = s.Hitbox.GetComponent<CircleCollider2D>();
            var g = s.Glow;
            float drawn = s.GetComponent<SpriteRenderer>().bounds.size.y;
            float across = g.bounds.size.x;
            Check(d.key + " " + kind + ": wears the glow behind it, drawn at its shotSize " + d.shotSize + " (" + drawn.ToString("F2") +
                  "), hitbox " + WorldRadius(col).ToString("F3") + " (" + s.Radius.ToString("F3") + ")",
                  g != null && g.enabled && g.sprite == HostileGlow.Halo && g.sortingOrder < 30 && g.GetComponent<Collider2D>() == null &&
                  Mathf.Abs(drawn - d.shotSize) < .02f && Mathf.Abs(WorldRadius(col) - s.Radius) < 1e-4f);
            Check("... reaching only a little past the art (" + ((across - drawn) * .5f).ToString("F3") + " wu each side), tinted " +
                  Hex(g.color) + " (" + HueGap(g.color, PlayerRed).ToString("F0") + " deg off the player's red)",
                  across > drawn && (across - drawn) * .5f <= Mathf.Max(.12f, .42f * drawn) && HueGap(g.color, PlayerRed) >= 20f);
            if (kind == EliteShots.Kind.Glob)
            {
                s.Lob(new Vector2(0f, -1f), .3f);
                bool glowedInAir = true;
                for (int i = 0; i < 30 && s.Airborne; i++) { shots.Step(1f / 30f); glowedInAir &= g.enabled; }
                float poolAcross = g.bounds.size.x, poolDrawn = s.GetComponent<SpriteRenderer>().bounds.size.x;
                Check(d.key + ": the glob glows in the air and as a pool (" + poolAcross.ToString("F2") + " over a " +
                      poolDrawn.ToString("F2") + " pool; hitbox " + WorldRadius(col).ToString("F3") + ")",
                      glowedInAir && s.Pooled && g.enabled && poolAcross > poolDrawn * .9f &&
                      (poolAcross - poolDrawn) * .5f <= .2f && Mathf.Abs(WorldRadius(col) - s.Radius) < 1e-4f);
            }
            s.Recycle();
        }
        Check("the tint keeps the player's red out (a pure red source turns to " + Hex(HostileGlow.Tint(Color.red)) + ")",
              HueGap(HostileGlow.Tint(Color.red), PlayerRed) >= 20f && HueGap(HostileGlow.Tint(PlayerRed), PlayerRed) >= 20f);
    }

    // ---- 2. contrast over every world --------------------------------------

    const int RW = 540, RH = 960;

    static void ContrastOverEveryWorld()
    {
        string[] worlds = { "Space", "Frost", "Verdant", "Ember" };
        for (int w = 0; w <= worlds.Length; w++)
        {
            bool flare = w == worlds.Length;   // a bright flare filling the view
            var cam = Fresh(flare ? 3 : w);
            cam.aspect = RW / (float)RH;
            GameObject backdrop;
            if (flare)
            {
                backdrop = new GameObject("~Flare");
                var sr = backdrop.AddComponent<SpriteRenderer>();
                var white = Texture2D.whiteTexture;
                sr.sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(.5f, .5f), white.width);
                sr.color = new Color(1f, .93f, .72f);
                sr.sortingOrder = -50;
                backdrop.transform.localScale = new Vector3(20f, 20f, 1f);
            }
            else
            {
                backdrop = new GameObject("~Backdrop");
                var wb = backdrop.AddComponent<WorldBackdrop>();
                wb.Show(worlds[w], false);
                for (int i = 0; i < 600; i++) wb.Step(1f / 60f);   // set pieces in
            }

            var pool = new BossProjectilePool(40, 1);
            var boss = BossCatalog.ForWorld(flare ? 3 : w);
            var eliteDef = Def("gunship");
            var list = new List<(Component shot, SpriteRenderer glow)>();
            for (int gx = 0; gx < 4; gx++)
                for (int gy = 0; gy < 6; gy++)
                {
                    var at = new Vector3(-2.1f + gx * 1.4f, -4f + gy * 1.6f, 0f);
                    if (((gx + gy) & 1) == 0)
                    {
                        var s = pool.Fire(boss, gy % 3 == 0 ? BossShotStyle.Shard : BossShotStyle.Bolt, at, Vector2.zero);
                        list.Add((s, s.Glow));
                    }
                    else
                    {
                        var s = EliteSystem.Shots.Fire(null, eliteDef, EliteShots.Kind.Bolt, at, Vector2.zero);
                        list.Add((s, s.Glow));
                    }
                }

            var rt = new RenderTexture(RW, RH, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(RW, RH, TextureFormat.RGB24, false);
            pool.Root.SetActive(false);
            SetShots(list, false);
            Color[] bg = Grab(cam, rt, tex);
            pool.Root.SetActive(true);
            SetShots(list, true);
            Color[] fg = Grab(cam, rt, tex);

            float worst = float.MaxValue, mean = 0f, worstLight = float.MaxValue, worstDark = float.MaxValue;
            foreach (var (shot, glow) in list)
            {
                Vector3 c = cam.WorldToScreenPoint(glow.transform.position);
                float R = glow.bounds.extents.x * (RH / (cam.orthographicSize * 2f));   // pixels
                float lightRim = Ring(fg, c, R * HostileGlow.DarkEdge, R * HostileGlow.LightEdge);
                float darkRim = Ring(fg, c, R * HostileGlow.BodyEdge, R * HostileGlow.DarkEdge);
                float behind = Ring(bg, c, R * HostileGlow.BodyEdge, R * HostileGlow.LightEdge);
                float cl = Ratio(lightRim, behind), cd = Ratio(darkRim, behind);
                float score = Mathf.Max(cl, cd);
                worst = Mathf.Min(worst, score);
                worstLight = Mathf.Min(worstLight, cl);
                worstDark = Mathf.Min(worstDark, cd);
                mean += score / list.Count;
            }
            string name = flare ? "a bright flare" : worlds[w];
            Check("the wrapper reads over " + name + ": worst contrast " + worst.ToString("F1") + ":1 (mean " + mean.ToString("F1") +
                  ":1; light rim worst " + worstLight.ToString("F1") + ", dark rim worst " + worstDark.ToString("F1") + ") over " +
                  list.Count + " shots, need " + MinContrast + ":1", worst >= MinContrast);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            pool.Dispose();
            Object.DestroyImmediate(backdrop);
        }
    }

    static void SetShots(List<(Component shot, SpriteRenderer glow)> list, bool on)
    {
        foreach (var (shot, _) in list) if (shot is EliteShot) shot.gameObject.SetActive(on);
    }

    static Color[] Grab(Camera cam, RenderTexture rt, Texture2D tex)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        return tex.GetPixels();
    }

    // Mean relative luminance of the pixels between radii a and b.
    static float Ring(Color[] px, Vector3 c, float a, float b)
    {
        float sum = 0f;
        int n = 0;
        int r = Mathf.CeilToInt(b) + 1;
        for (int y = (int)c.y - r; y <= (int)c.y + r; y++)
            for (int x = (int)c.x - r; x <= (int)c.x + r; x++)
            {
                if (x < 0 || y < 0 || x >= RW || y >= RH) continue;
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), c);
                if (d < a + .5f || d > b - .5f) continue;
                sum += Luminance(px[y * RW + x]);
                n++;
            }
        return n > 0 ? sum / n : 0f;
    }

    static float Lin(float v) => v <= .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
    static float Luminance(Color c) => .2126f * Lin(c.r) + .7152f * Lin(c.g) + .0722f * Lin(c.b);
    static float Ratio(float a, float b) => (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);

    // ---- 3. shot vs shot ------------------------------------------------------

    static void ShotVersusShot()
    {
        Fresh();
        var shots = EliteSystem.Shots;
        var a = Def("gunship");
        var ea = EliteShip.CreateInPlay(a, new Vector2(-1.5f, 4f));
        var eb = EliteShip.CreateInPlay(Def("striker"), new Vector2(1.5f, 4f));
        ea.AttackCooldown = eb.AttackCooldown = 99f;

        // two elites' bolts, head on
        var s1 = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(-1f, 0f), Vector2.right * 3f);
        var s2 = shots.Fire(eb, eb.Def, EliteShots.Kind.Bolt, new Vector2(1f, 0f), Vector2.left * 3f);
        int pops = HostileShots.Pops;
        for (int i = 0; i < 40 && (s1.Active || s2.Active); i++) shots.Step(Dt);
        Check("two elites' shots that meet break each other (" + (HostileShots.Pops - pops) + " pops)",
              !s1.Active && !s2.Active && s1.EndReason == 5 && s2.EndReason == 5 && HostileShots.Pops - pops == 2);
        Check("... with a pop where they met", HazardRuntime.Instance != null && HazardRuntime.Instance.ActivePops > 0);

        // one elite's fan: the same volley never breaks itself
        var f1 = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(0f, -1f), new Vector2(.4f, -1f));
        var f2 = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(0f, -1f), new Vector2(-.4f, -1f));
        shots.Step(Dt);
        Check("one volley's shots overlapping at the muzzle fly on", f1.Active && f2.Active);
        f1.Recycle(); f2.Recycle();

        // the same elite, a later volley crossing an earlier one
        var old = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(-2f, -2f), Vector2.right * 2f);
        for (int i = 0; i < 45; i++) shots.Step(Dt);   // .75 s
        var late = shots.Fire(ea, a, EliteShots.Kind.Bolt, (Vector2)old.transform.position + Vector2.right * .6f, Vector2.left * 2f);
        for (int i = 0; i < 20 && (old.Active || late.Active); i++) shots.Step(Dt);
        Check("... but its later volley breaks an earlier shot it crosses", !old.Active && !late.Active);

        // a heavy siege shell survives a bolt
        var shell = shots.Fire(eb, Def("siege"), EliteShots.Kind.Shell, new Vector2(0f, 2f), Vector2.down * 2f);
        var bolt = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.up * 2f);
        for (int i = 0; i < 30 && bolt.Active; i++) shots.Step(Dt);
        Check("a heavy shell breaks a bolt and flies on", !bolt.Active && bolt.EndReason == 5 && shell.Active);
        shell.Recycle();

        // a resin pool swallows a shot and stays (a still board: the pool rides it)
        moveBackGround.speed = 0f;
        var warden = Def("warden");
        var glob = shots.Fire(eb, warden, EliteShots.Kind.Glob, new Vector2(1f, 0f), Vector2.zero);
        glob.Lob(new Vector2(1f, -1f), .2f);
        for (int i = 0; i < 20 && glob.Airborne; i++) shots.Step(Dt);
        var into = shots.Fire(ea, a, EliteShots.Kind.Bolt, (Vector2)glob.transform.position + Vector2.left * .8f, Vector2.right * 3f);
        for (int i = 0; i < 30 && into.Active; i++) shots.Step(Dt);
        Check("a landed resin pool swallows a shot and stays", glob.Pooled && !into.Active && into.EndReason == 5);
        glob.Recycle();

        // a boss shot against an elite shot
        var pool = new BossProjectilePool(8, 2);
        try
        {
            var bs = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, new Vector3(0f, 1f, 0f), Vector2.down * 3f);
            var es = shots.Fire(ea, a, EliteShots.Kind.Bolt, new Vector2(0f, -1f), Vector2.up * 3f);
            for (int i = 0; i < 40 && (bs.Active || es.Active); i++) { pool.Step(Dt); shots.Step(Dt); }
            Check("a boss shot and an elite shot break each other", !bs.Active && !es.Active && bs.EndReason == 5 && es.EndReason == 5);

            // a boss's own volley is one pattern
            var v1 = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Shard, new Vector3(0f, 1f, 0f), new Vector2(1f, -3f));
            var v2 = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Shard, new Vector3(0f, 1f, 0f), new Vector2(-1f, -3f));
            pool.Step(Dt);
            Check("a boss's fan doesn't break itself at the muzzle", v1.Active && v2.Active);
        }
        finally { pool.Dispose(); }
        Check("pays nothing", RunScore.Total == 0);
    }

    // ---- 4. lasers burn shots -----------------------------------------------

    static void LaserBurnsCrossingShots()
    {
        Fresh();
        var pool = new BossProjectilePool(8, 2);
        try
        {
            var beam = pool.Beam(BossCatalog.ForWorld(0), null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, 0f, 2f, .3f);
            for (int i = 0; i < 30; i++) pool.Step(Dt);
            var elite = Def("gunship");
            var e = EliteShip.CreateInPlay(elite, new Vector2(-2f, 4f));
            e.AttackCooldown = 99f;
            var shell = EliteSystem.Shots.Fire(e, Def("siege"), EliteShots.Kind.Shell, new Vector2(-1f, 0f), Vector2.right * 3f);
            int burns = HostileShots.BeamBurns;
            for (int i = 0; i < 40 && shell.Active; i++) { EliteSystem.Shots.Step(Dt); pool.Step(Dt); }
            Check("a live laser burns a shot crossing it (even a heavy shell) and burns on",
                  !shell.Active && shell.EndReason == 5 && HostileShots.BeamBurns > burns && beam.Live);

            // its own boss's earlier shots too, but not the volley it fires with
            var older = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, new Vector3(-1.2f, 1f, 0f), Vector2.right * 1.5f);
            for (int i = 0; i < 50 && older.Active; i++) pool.Step(Dt);
            Check("... and a shot its boss fired well before it", !older.Active && older.EndReason == 5);
        }
        finally { pool.Dispose(); }
    }

    // ---- 5. player shots ------------------------------------------------------

    static void PlayerShotsShootDown()
    {
        Fresh();
        var pool = new BossProjectilePool(8, 2);
        try
        {
            var s = pool.Fire(BossCatalog.ForWorld(2), BossShotStyle.Bolt, new Vector3(.1f, 1f, 0f), Vector2.zero);
            var beam = pool.Beam(BossCatalog.ForWorld(2), null, -1, new Vector3(1.5f, 3f, 0f), -90f, 0f, 0f, 2f, .3f);
            for (int i = 0; i < 20; i++) pool.Step(Dt);
            long total = RunScore.Total;
            int n = HostileShots.ShootDownAlong(new Vector2(0f, -2f), new Vector2(0f, 3f), .15f);
            Check("a player projectile's path shoots down the hostile shot it crosses (" + n + "), paying nothing",
                  n == 1 && !s.Active && s.EndReason == 5 && RunScore.Total == total);
            HostileShots.ShootDownAlong(new Vector2(1.5f, -2f), new Vector2(1.5f, 3f), .15f);
            Check("... but never a laser", beam.Live);
            Check("every player projectile step runs it (AttackProjectile.Tick)",
                  System.IO.File.ReadAllText("Assets/Scripts/Gameplay/ShipAttacks.cs").Contains("HostileShots.ShootDownAlong(from, to, radius)"));
        }
        finally { pool.Dispose(); }
    }

    // ---- 6. friendly fire ---------------------------------------------------------

    static void FriendlyFireMatrix()
    {
        // boss shots
        Fresh();
        var pool = new BossProjectilePool(16, 2);
        var boss = BossCatalog.ForWorld(3);
        try
        {
            int dust = score.dustPickups;
            var rock = Hazard(EnemyRole.Rock, new Vector2(-1.5f, 0f));
            var fighter = Hazard(EnemyRole.Fighter, new Vector2(0f, 0f));
            var mine = Hazard(EnemyRole.Mine, new Vector2(1.5f, 0f));
            var e = EliteShip.CreateInPlay(Def("gunship"), new Vector2(-.5f, -2.5f));
            e.AttackCooldown = 99f;
            int hearts = e.Hearts;
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-1.5f, 1.5f, 0f), Vector2.down * 4f);
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(0f, 1.5f, 0f), Vector2.down * 4f);
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.5f, 1.5f, 0f), Vector2.down * 4f);
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-.5f, -1f, 0f), Vector2.down * 4f);
            for (int i = 0; i < 40; i++) pool.Step(Dt);
            Check("boss shots destroy a rock, an enemy and a mine they hit", rock == null && fighter == null && mine == null);
            Check("... and take a heart off an elite (" + hearts + " -> " + (e != null ? e.Hearts : -1) + ")",
                  e != null && e.Hearts == hearts - 1 && e.LastHitCause == EliteDamage.FriendlyFire);
            Check("... paying the pilot nothing (score " + RunScore.Total + ", dust awards " + (score.dustPickups - dust) + ")",
                  RunScore.Total == 0 && score.dustPickups == dust);

            // an elite killed by friendly fire still pays its reward (elite rules)
            int paid = EliteRewards.Paid;
            for (int k = 0; k < 6 && e != null && e.State != EliteState.Dead; k++)
            {
                pool.Fire(boss, BossShotStyle.Bolt, (Vector3)e.Position + Vector3.up * .8f, Vector2.down * 4f);
                for (int i = 0; i < 60; i++) { pool.Step(Dt); EliteSystem.Step(Dt); }
            }
            Check("an elite downed by boss fire still pays its fixed reward (" + (EliteRewards.Paid - paid) + ")",
                  EliteRewards.Paid - paid == 1);

            // boss lasers
            long paidScore = RunScore.Total;   // the elite's reward above
            var r2 = Hazard(EnemyRole.Rock, new Vector2(0f, -1f));
            var beam = pool.Beam(boss, null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, 0f, 1f, .3f);
            for (int i = 0; i < 30; i++) pool.Step(Dt);
            Check("a boss laser burns through a rock in its path, unpaid", r2 == null && beam.Live && RunScore.Total == paidScore);

            // the boss itself is immune
            var bodyGo = new GameObject("BossBody");
            bodyGo.tag = "Enimey";
            bodyGo.AddComponent<BossTarget>();
            ClearTarget.Ensure(bodyGo).SetRadius(1f);
            bodyGo.transform.position = new Vector3(-2f, 0f, 0f);
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-2f, 1f, 0f), Vector2.down * 4f);
            for (int i = 0; i < 30; i++) pool.Step(Dt);
            Check("the boss's own body is never hit by friendly fire", bodyGo != null);
        }
        finally { pool.Dispose(); }

        // mine blasts, chained
        Fresh();
        int blasts = FriendlyFire.MineBlasts;
        var m1 = Hazard(EnemyRole.Mine, new Vector2(0f, 0f));
        var m2 = Hazard(EnemyRole.Mine, new Vector2(.9f, 0f));
        var near = Hazard(EnemyRole.Rock, new Vector2(-.8f, .3f));
        var nearB = Hazard(EnemyRole.Fighter, new Vector2(1.8f, 0f));
        var far = Hazard(EnemyRole.Rock, new Vector2(-2f, -3f));
        var ee = EliteShip.CreateInPlay(Def("gunship"), new Vector2(0f, -.9f));
        ee.AttackCooldown = 99f;
        int eh = ee.Hearts;
        EliteShip.FriendlyKill(m1);   // the first mine goes up
        var rt = HazardRuntime.Ensure();
        for (int i = 0; i < 30; i++) rt.Step(Dt);
        Check("a bursting mine blasts what's near it -- a rock, the next mine, an elite (hearts " + eh + " -> " + (ee != null ? ee.Hearts : -1) + ")",
              near == null && m2 == null && ee != null && ee.Hearts == eh - 1);
        Check("... the next mine chains on to the enemy beyond (" + (FriendlyFire.MineBlasts - blasts) + " blasts)",
              nearB == null && FriendlyFire.MineBlasts - blasts == 2);
        Check("... leaving what's far, paying nothing", far != null && RunScore.Total == 0);
        Check("the mine blast hooks every mine burst (RailBombAnimator.Burst)",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/RailBombAnimator.cs").Contains("FriendlyFire.MineBlast(mine)"));

        // a chaser running into a rock
        Fresh();
        var chaser = Hazard(EnemyRole.Chaser, new Vector2(0f, 0f));
        var rock2 = Hazard(EnemyRole.Rock, new Vector2(.15f, .1f));
        var bystander = Hazard(EnemyRole.Rock, new Vector2(2f, 3f));
        bool hasChaser = chaser.GetComponent<ChaserEnemy>() != null || chaser.GetComponent<moveEnimes>() != null;
        FriendlyFire.StepCrashes(new Rect(-5f, -6f, 10f, 12f));
        Check("a chaser running into a rock breaks, and the rock with it (free mover: " + hasChaser + ")",
              hasChaser && chaser == null && rock2 == null && bystander != null && RunScore.Total == 0);
        Check("elite shots' friendly fire is kept (EliteShot)",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteShots.cs").Contains("EliteShip.FriendlyKill(t.gameObject)"));

        // during a player death everything is the domino's
        Check("none of it runs during a player death (DeathCrash.Running guards)",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/FriendlyFire.cs").Contains("if (mine == null || DeathCrash.Running"));
    }

    // ---- 7. split into pieces -------------------------------------------------------

    static void SplitsIntoPieces()
    {
        Fresh();
        EnemySplit.ForceInEditor = true;
        try
        {
            var rt = HazardRuntime.Ensure();
            foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Big })
            {
                EnemySplit.Seed(20261004u);
                EnemySplit.ResetCounters();
                const int N = 400;
                TargetExplosion.Size size = TargetExplosion.Size.Small;
                int fragsSeen = 0;
                for (int i = 0; i < N; i++)
                {
                    var go = Hazard(role, new Vector2(0f, 0f), 0);
                    size = TargetExplosion.SizeFor(go);
                    int before = rt.ActiveFragments;
                    TargetExplosion.Spawn(go, ShipId.None);
                    fragsSeen = Mathf.Max(fragsSeen, rt.ActiveFragments - before);
                    Object.DestroyImmediate(go);
                    for (int k = 0; k < 90; k++) rt.Step(Dt);   // the pieces fly and fade
                }
                float want = EnemySplit.ChanceFor(size);
                float rate = EnemySplit.Splits / (float)EnemySplit.Rolls;
                Check(role + " (" + size + "): splits " + EnemySplit.Splits + " of " + EnemySplit.Rolls + " kills = " +
                      (rate * 100f).ToString("F1") + "% (configured " + (want * 100f).ToString("F0") + "%), into up to " + fragsSeen + " pieces",
                      EnemySplit.Rolls == N && Mathf.Abs(rate - want) < .06f && fragsSeen >= 2 && fragsSeen <= 4);
                Check("... every piece gone after its flight", rt.ActiveFragments == 0);
            }
            Check("bigger things split more often (" + EnemySplit.SmallChance + " < " + EnemySplit.MediumChance + " < " +
                  EnemySplit.LargeChance + ", elites " + EnemySplit.EliteChance + ")",
                  EnemySplit.SmallChance < EnemySplit.MediumChance && EnemySplit.MediumChance < EnemySplit.LargeChance);

            // a burst of kills: never more than the cap on screen
            EnemySplit.Seed(7u);
            EnemySplit.ResetCounters();
            int peak = 0;
            for (int i = 0; i < 200; i++)
            {
                var go = Hazard(EnemyRole.Big, new Vector2((i % 5) - 2f, 0f), 0);
                TargetExplosion.Spawn(go, ShipId.None);
                Object.DestroyImmediate(go);
                if (i % 3 == 0) rt.Step(Dt);   // ~67 kills a frame-ish
                peak = Mathf.Max(peak, rt.ActiveFragments);
            }
            Check("a storm of kills never puts more than " + HazardRuntime.MaxFragments + " pieces on screen (peak " + peak +
                  ", " + EnemySplit.Capped + " capped to plain blasts, pool built " + rt.FragmentsBuilt + ")",
                  peak <= HazardRuntime.MaxFragments && rt.FragmentsBuilt <= HazardRuntime.MaxFragments && EnemySplit.Capped > 0);
            bool collider = false;
            foreach (var c in rt.GetComponentsInChildren<Collider2D>(true)) collider = true;
            Check("the pieces are cosmetic: no colliders", !collider);
            for (int k = 0; k < 120; k++) rt.Step(Dt);

            // not during a player death -- the domino owns that
            Check("no splitting while a player death runs (EnemySplit guards DeathCrash.Running)",
                  System.IO.File.ReadAllText("Assets/Scripts/Gameplay/FriendlyFire.cs").Contains("if (target == null || !Enabled || DeathCrash.Running) return false;"));
        }
        finally { EnemySplit.ForceInEditor = false; }
    }

    // ---- 8. allocations -----------------------------------------------------------------

    static void NoAllocations()
    {
        Fresh();
        EnemySplit.ForceInEditor = true;
        var pool = new BossProjectilePool(24, 2);
        try
        {
            var boss = BossCatalog.ForWorld(1);
            for (int i = 0; i < 12; i++)
                pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-2f + i * .35f, 2f, 0f), new Vector2(0f, -.2f), BossRailMode.Bounce, 2, 0f, 0f);
            var e = EliteShip.CreateInPlay(Def("gunship"), new Vector2(0f, 4.5f));
            e.AttackCooldown = 99f;
            for (int i = 0; i < 12; i++)
                EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(-2f + i * .35f, -1f), new Vector2(0f, .1f));
            pool.Beam(boss, null, -1, new Vector3(2.2f, 3f, 0f), -90f, 0f, 0f, 3f, .3f);
            for (int i = 0; i < 6; i++) Hazard(EnemyRole.Rock, new Vector2(-2f + i * .8f, 4.2f));
            var rt = HazardRuntime.Ensure();
            var victim = Hazard(EnemyRole.Big, new Vector2(0f, -3f), 0);
            EnemySplit.Seed(1u);
            for (int i = 0; i < 12 && rt.ActiveFragments == 0; i++)
            {
                TargetExplosion.Spawn(victim, ShipId.None);
                rt.Step(Dt);
            }
            Object.DestroyImmediate(victim);
            for (int i = 0; i < 5; i++) { pool.Step(Dt); EliteSystem.Shots.Step(Dt); rt.Step(Dt); HostileShots.ShootDownAlong(new Vector2(2.4f, -5f), new Vector2(2.4f, -4f), .1f); }
            int live = HostileShots.ActiveCount, frags = rt.ActiveFragments;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                pool.Step(Dt);
                EliteSystem.Shots.Step(Dt);
                HostileShots.Resolve();
                HostileShots.ShootDownAlong(new Vector2(2.4f, -5f), new Vector2(2.4f, -4f), .1f);
                rt.Step(Dt);
                FriendlyFire.StepCrashes(new Rect(-5f, -6f, 10f, 12f));
            }
            long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("steady steps -- " + live + " glowing shots pulsing, a laser and its sheath, shot-vs-shot checks, " + frags +
                  " fragments flying, friendly-fire scans -- allocate nothing (" + used + " bytes)", used == 0 && live >= 20 && frags > 0);
        }
        finally { pool.Dispose(); EnemySplit.ForceInEditor = false; }
    }

    // ---- helpers --------------------------------------------------------------------------

    static float Hue(Color c) { Color.RGBToHSV(c, out float h, out _, out _); return h * 360f; }
    static float HueGap(Color a, Color b) { float d = Mathf.Abs(Hue(a) - Hue(b)) % 360f; return Mathf.Min(d, 360f - d); }
    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}
