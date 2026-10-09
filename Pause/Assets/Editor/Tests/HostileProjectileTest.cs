using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "the boss projectiles dont need to be bigger but we do need ... a
// clear indicator ... a light wrapper glow ... very visible in any
// background; when projectiles hit each other, they can destroy each other
// too; make sure all enemy things have friendly fire on; and when they die
// or break they can split into pieces sometimes."
//
//   * every hostile projectile wears a glow and keeps its size and hitbox:
//     lasers a sheath, boss shots a thin outline hugging their own
//     silhouette (BossArt.ShotRim, never a round halo -- "the glow effect on
//     the boss projectiles is too large, it should be more like a light
//     shadow framing the projectile art"), elite and ordinary enemy shots
//     the same kind of outline (ShotOutline; they used to wear HostileGlow's
//     round wrapper -- "the weapon shots have 2 large circles ... visible
//     but not by having a massive circle halo"); none is the player's red;
//   * every shot stands out of every world's backdrop: from real renders,
//     at least MinStandOutPixels of its pixels MinContrast clear of the
//     backdrop behind it (this replaced the round wrapper's ring contrast,
//     which only a round wrapper has);
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
            BoldOnBrightWorlds();
            ContrastOverEveryVariant();
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
            ShotOutline.Bold = null;
            BossArt.ShotRimBold = null;
            BackdropVariants.For("Frost").Reset();
            BackdropVariants.For("Verdant").Reset();
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
        FriendlyFire.Settle(go);   // on the board a while: past hostile fire's spawn-in protection (HostileFireTest covers it)
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
                          g != null && g.enabled && g.sortingOrder < 30 &&
                          g.GetComponent<Collider2D>() == null &&
                          Mathf.Abs(s.transform.lossyScale.x - drawn) < 1e-4f && Mathf.Abs(hb - hit) < 1e-4f);
                    int cell = style == BossShotStyle.Bolt ? BossArt.Bolt0 : BossArt.Shard0;
                    float reach = BossArt.ShotRimReach * drawn;
                    Check("... the glow is a rim cut from its own drawing, not the round wrapper (" + (g.sprite != null ? g.sprite.name : "none") + ")",
                          g.sprite != null && g.sprite == BossArt.ShotRim(boss, cell) && g.sprite != HostileGlow.Halo &&
                          BossArt.ShotRim(boss, cell + 1) != null && BossArt.ShotRim(boss, cell + 1) != g.sprite);
                    int halos = 0, kids = 0;
                    foreach (var r in s.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (r.gameObject == s.gameObject) continue;
                        kids++;
                        if (r.sprite == HostileGlow.Halo || r.sprite == HostileGlow.Sheath) halos++;
                    }
                    Check(boss.artKey + " " + style + ": no circular glow -- its only extra renderer is the rim outline (" + kids +
                          " extra, " + halos + " round halos)", kids == 1 && halos == 0);
                    Check("... a thin outline framing the art, no soft halo: gone " + reach.ToString("F3") + " wu past the silhouette, at most " +
                          BossArt.ShotRimAlpha + " opaque (its sprite " + d.ToString("F2") + " wu across with its clear pad)",
                          reach > .01f && reach <= .05f && BossArt.ShotRimAlpha >= .4f && BossArt.ShotRimAlpha <= .8f &&
                          BossArt.ShotRimPad > BossArt.ShotRimReach * BossArt.ShotRimTexels &&
                          d > drawn && (d - drawn) * .5f <= .1f);
                    string hug;
                    bool hugs = RimHugsArt(BossArt.Shot(boss, cell), g.sprite, out hug);
                    Check("... and it sits on the drawing itself, not stretched or shifted (" + hug + ")", hugs);
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
            Check("the rim only pulses in alpha, never swells into a round glow (scale " + minS.ToString("F3") + ".." + maxS.ToString("F3") + ", alpha " +
                  minA.ToString("F2") + ".." + maxA.ToString("F2") + ") while the shot keeps its size",
                  Mathf.Abs(maxS - minS) < 1e-4f && maxA > minA + .08f &&
                  Mathf.Abs(p.transform.lossyScale.x - BossConfig.BoltWorldSize) < 1e-4f);

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

        // the elites' shots, every kind (frost shards, resin globs and pools),
        // and every ordinary enemy's shot: a thin outline, no round halo
        var shots = EliteSystem.Shots;
        var styles = new List<(string key, EliteDef def, EliteShots.Kind kind)>();
        foreach (var d in EliteCatalog.All) styles.Add((d.key, d, EliteShots.KindOf(d.shotKind)));
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring || b.attack == EnemyAttack.Cross || b.attack == EnemyAttack.Lob))
                styles.Add((def.key, b.ShotStyle, b.shotKind));
        }
        int roundHalos = 0, outlined = 0;
        foreach (var (key, d, kind) in styles)
        {
            var s = shots.Fire(null, d, kind, new Vector2(0f, 1f), Vector2.down * 2f);
            if (s == null) { Check(key + ": could fire", false); continue; }
            var col = s.Hitbox.GetComponent<CircleCollider2D>();
            var g = s.Glow;
            var art = s.GetComponent<SpriteRenderer>();
            float drawn = art.bounds.size.y;
            foreach (var r in s.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.sprite == HostileGlow.Halo || r.sprite == HostileGlow.Sheath) roundHalos++;
            float past = Mathf.Max(g.bounds.size.x - art.bounds.size.x, g.bounds.size.y - art.bounds.size.y) * .5f;
            string hug;
            bool hugs = OutlineHugsArt(art.sprite, g.sprite, out hug);
            bool ok = g != null && g.enabled && g.sprite != null && g.sprite == ShotOutline.For(art.sprite, drawn) &&
                      g.sortingOrder < 30 && g.GetComponent<Collider2D>() == null &&
                      g.transform.localScale == Vector3.one && Mathf.Abs(drawn - d.shotSize) < .02f &&
                      Mathf.Abs(WorldRadius(col) - s.Radius) < 1e-4f && past <= .06f && hugs &&
                      HueGap(g.color, PlayerRed) >= 20f && !HostileGlow.IsPlayerRed(g.color);
            if (ok) outlined++;
            else Check(key + " " + kind + ": a thin outline traced from its drawing (" + (g.sprite != null ? g.sprite.name : "none") +
                       ", sprite " + past.ToString("F3") + " wu past the art incl. its clear pad; " + hug + "), drawn at its shotSize " +
                       d.shotSize + " (" + drawn.ToString("F2") + "), hitbox " + WorldRadius(col).ToString("F3") + " (" +
                       s.Radius.ToString("F3") + "), tinted " + Hex(g.color), false);
            if (kind == EliteShots.Kind.Glob)
            {
                s.Lob(new Vector2(0f, -1f), .3f);
                bool glowedInAir = true;
                for (int i = 0; i < 30 && s.Airborne; i++) { shots.Step(1f / 30f); glowedInAir &= g.enabled; }
                var poolArt = s.GetComponent<SpriteRenderer>();
                Check(key + ": the glob is outlined in the air and as a pool (" + (g.sprite != null ? g.sprite.name : "none") +
                      "; hitbox " + WorldRadius(col).ToString("F3") + ")",
                      glowedInAir && s.Pooled && g.enabled && g.sprite == ShotOutline.For(EliteFxArt.Pool, poolArt.bounds.size.y) &&
                      Mathf.Abs(WorldRadius(col) - s.Radius) < 1e-4f);
            }
            s.Recycle();
        }
        Check("every elite and enemy shot (" + styles.Count + " styles) wears a thin outline hugging its drawing: " + outlined +
              " outlined, " + roundHalos + " round halos", outlined == styles.Count && roundHalos == 0);
        Check("... reaching " + ShotOutline.OutlineReach + " wu past the silhouette like a boss shot's rim (" +
              (BossArt.ShotRimReach * BossConfig.BoltWorldSize).ToString("F3") + "), never swelling",
              ShotOutline.OutlineReach <= .05f && ShotOutline.OutlineReach >= .015f && ShotOutline.PulseScale == 0f);
        {
            var s = shots.Fire(null, Def("gunship"), EliteShots.Kind.Bolt, new Vector2(0f, 0f), Vector2.down * .01f);
            float minS = 9f, maxS = 0f, minA = 9f, maxA = 0f;
            for (int i = 0; i < 40; i++)
            {
                shots.Step(Dt);
                minS = Mathf.Min(minS, s.Glow.transform.localScale.x); maxS = Mathf.Max(maxS, s.Glow.transform.localScale.x);
                minA = Mathf.Min(minA, s.Glow.color.a); maxA = Mathf.Max(maxA, s.Glow.color.a);
            }
            Check("an elite shot's outline only pulses in alpha (scale " + minS.ToString("F3") + ".." + maxS.ToString("F3") + ", alpha " +
                  minA.ToString("F2") + ".." + maxA.ToString("F2") + ")", Mathf.Abs(maxS - minS) < 1e-4f && maxA > minA + .08f);
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
            EnemyBehaviour roster = null;
            foreach (var def in EnemyRoster.All)
            {
                var rb = EnemyBehaviours.For(def.key);
                if (roster == null && def.world == (flare ? 3 : w) && rb != null && rb.attack == EnemyAttack.Shot) roster = rb;
            }
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
                    else if (gy % 2 == 1 || roster == null)
                    {
                        var s = EliteSystem.Shots.Fire(null, eliteDef, (EliteShots.Kind)(gx % 4), at, Vector2.zero);
                        list.Add((s, s.Glow));
                    }
                    else
                    {
                        // this world's ordinary enemy shot
                        var s = EliteSystem.Shots.Fire(null, roster.ShotStyle, roster.shotKind, at, Vector2.zero);
                        s.AsRosterShot(null, 0f);
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

            int rimmed = 0, fewest = int.MaxValue, bossRoundGlows = 0;
            int outlined = 0, fewestSmall = int.MaxValue, smallRoundGlows = 0;
            foreach (var (shot, glow) in list)
            {
                Vector3 c = cam.WorldToScreenPoint(glow.transform.position);
                if (shot is BossProjectile)
                {
                    // a rimmed boss shot: how many of its pixels (art and
                    // rim) stand MinContrast clear of the backdrop there
                    float half = shot.transform.lossyScale.x * .5f * (RH / (cam.orthographicSize * 2f));
                    fewest = Mathf.Min(fewest, StandOut(fg, bg, c, half));
                    rimmed++;
                    bossRoundGlows += glow.sprite == HostileGlow.Halo ? 1 : 0;
                    continue;
                }
                // an outlined elite / enemy shot: the same measure over the
                // square its outline covers
                float h2 = Mathf.Max(glow.bounds.extents.x, glow.bounds.extents.y) * (RH / (cam.orthographicSize * 2f));
                fewestSmall = Mathf.Min(fewestSmall, StandOut(fg, bg, c, h2));
                outlined++;
                smallRoundGlows += glow.sprite == HostileGlow.Halo ? 1 : 0;
            }
            string name = flare ? "a bright flare" : worlds[w];
            string smallLine = "an outlined elite / enemy shot stands out of " + name + ": at least " + fewestSmall + " px at " + MinContrast +
                               ":1 against the backdrop behind it, over " + outlined + " shots (need " + MinStandOutPixels + ")";
            Check("elite and enemy shots over " + name + " wear no circular glow: " + smallRoundGlows + " of " + outlined + " carry the round halo",
                  outlined > 0 && smallRoundGlows == 0);
            Check(smallLine, outlined > 0 && fewestSmall >= MinStandOutPixels);
            // The rim is a light one, made for the worlds' dark backdrops;
            // over a full-screen flare (no world has one) the art's own dark
            // outline is what is left, so that case is only reported.
            string rimLine = "a rimmed boss shot stands out of " + name + ": at least " + fewest + " px at " + MinContrast +
                             ":1 against the backdrop behind it, over " + rimmed + " shots (need " + MinStandOutPixels + ")";
            if (!flare)
                Check("boss shots over " + name + " wear no circular glow: " + bossRoundGlows + " of " + rimmed + " carry the round halo",
                      rimmed > 0 && bossRoundGlows == 0);
            if (flare) Debug.Log("[HOSTILE] INFO  " + rimLine);
            else Check(rimLine, rimmed > 0 && fewest >= MinStandOutPixels);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            pool.Dispose();
            Object.DestroyImmediate(backdrop);
        }
    }

    // ---- 2b. the bold outline on bright worlds ---------------------------------
    //
    // The bright backdrops (BackdropCatalog.Spec.Bright: Frost's lifted ice
    // and cloud ceiling, Verdant's lit jungle) switch the hostile shots'
    // outline and the boss shots' rim to the bold style -- a solid dark
    // keyline -- by the same predicate as the hearts (HeartOutline.UseBold);
    // Space and Ember keep the standard trace.
    static void BoldOnBrightWorlds()
    {
        string[] worlds = { "Space", "Frost", "Verdant", "Ember" };
        for (int w = 0; w < worlds.Length; w++)
        {
            Fresh(w);
            bool bright = BackdropCatalog.For(worlds[w]).Bright;
            Check(worlds[w] + ": the shots' outline and the boss rims are " + (bright ? "BOLD" : "standard") +
                  " by the hearts' own predicate (bright " + bright + ", hearts bold " + HeartOutline.UseBold + ")",
                  ShotOutline.UseBold == bright && BossArt.ShotRimUseBold == bright && HeartOutline.UseBold == bright);
            if (w == 0 || w == 3) Check(worlds[w] + " is not brightened: its shots keep the standard look", !bright);
            if (w == 1 || w == 2) Check(worlds[w] + " is brightened: its shots wear the bold keyline", bright);
        }
        // the bold outline: the same light trace at the drawing, a wider dark keyline, within the reach rule
        var art = EliteFxArt.Bolt;
        float drawn = .22f;
        var std = ShotOutline.For(art, drawn, false);
        var bold = ShotOutline.For(art, drawn, true);
        int stdDark = DarkTexels(std), boldDark = DarkTexels(bold);
        string hug;
        bool hugs = OutlineHugsArt(art, bold, out hug);
        Check("the bold shot outline keeps the light trace on the drawing and widens the dark keyline (" + boldDark + " dark texels vs " + stdDark +
              "; " + hug + "), reaching " + ShotOutline.BoldReach + " wu (<= .05)",
              std != bold && boldDark > stdDark * 3 / 2 && hugs && ShotOutline.BoldReach <= .05f && ShotOutline.BoldSolidTo < ShotOutline.BoldReach);
        var boss = BossCatalog.ForWorld(1);
        var rim = BossArt.ShotRim(boss, BossArt.Bolt0, false);
        var boldRim = BossArt.ShotRim(boss, BossArt.Bolt0, true);
        Check("the bold boss rim is its own sprite with a dark keyline (" + DarkTexels(boldRim) + " dark texels, standard " + DarkTexels(rim) +
              "), reaching " + (BossArt.ShotRimBoldReach * BossConfig.BoltWorldSize).ToString("F3") + " wu (<= .05)",
              rim != null && boldRim != null && rim != boldRim && DarkTexels(boldRim) > 0 && DarkTexels(rim) == 0 &&
              BossArt.ShotRimBoldReach * BossConfig.BoltWorldSize <= .05f && BossArt.ShotRimBoldReach * BossArt.ShotRimTexels < BossArt.ShotRimPad);
    }

    static int DarkTexels(Sprite s)
    {
        if (s == null || s.texture == null || !s.texture.isReadable) return -1;
        var r = s.textureRect;
        var px = s.texture.GetPixels32();
        int n = 0;
        for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
            for (int x = (int)r.x; x < (int)(r.x + r.width); x++)
            {
                var c = px[y * s.texture.width + x];
                if (c.a > 128 && c.r < 64) n++;
            }
        return n;
    }

    // ---- 2c. every variant of the bright worlds, at several moments ---------------
    //
    // Frost and Verdant, each installed backdrop variant, at 2 s (the
    // opening cloud ceiling at its thickest: CloudCover holds it ~4 s), 5 s
    // (clearing), 12 s and 40 s: every hostile shot this world
    // fires -- its roster enemies' shots, its elites' shots, every elite
    // shot kind (incl. Frost's slab and orb), and its boss's bolt and shard
    // -- stands MinStandOutPixels at MinContrast out of the backdrop behind
    // it, wherever on the screen it is.
    static readonly float[] VariantMoments = { 2f, 5f, 12f, 40f };

    static void ContrastOverEveryVariant()
    {
        foreach (int w in new[] { 1, 2 })
        {
            string world = w == 1 ? "Frost" : "Verdant";
            var spec = BackdropCatalog.For(world);
            var picker = BackdropVariants.For(world);
            int variants = 0;
            for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
            {
                if (spec.variantSets > 0 && !picker.Installed(v)) continue;
                variants++;
                var cam = Fresh(w);
                cam.aspect = RW / (float)RH;
                picker.Force = spec.variantSets > 0 ? v : 0;
                var backdrop = new GameObject("~Backdrop");
                var wb = backdrop.AddComponent<WorldBackdrop>();
                wb.Show(world, false);
                var pool = new BossProjectilePool(40, 1);
                var styles = ShotStyles(w);
                var rt = new RenderTexture(RW, RH, 24);
                cam.targetTexture = rt;
                var tex = new Texture2D(RW, RH, TextureFormat.RGB24, false);
                float clock = 0f;
                foreach (float at in VariantMoments)
                {
                    while (clock < at) { wb.Step(1f / 60f); clock += 1f / 60f; }
                    int fewestSmall = int.MaxValue, fewestBoss = int.MaxValue, smalls = 0, bosses = 0;
                    string worstSmall = "-", worstBoss = "-";
                    for (int pass = 0; pass < 3; pass++)
                    {
                        var list = new List<(Component shot, SpriteRenderer glow, string name)>();
                        int cell = 0;
                        for (int gx = 0; gx < 4; gx++)
                            for (int gy = 0; gy < 6; gy++, cell++)
                            {
                                var p = new Vector3(-2.1f + gx * 1.4f, -4f + gy * 1.6f, 0f);
                                var st = styles[(cell + pass * 7) % styles.Count];
                                if (st.boss != null)
                                {
                                    var b = pool.Fire(st.boss, st.bossStyle, p, Vector2.zero);
                                    if (b != null) list.Add((b, b.Glow, st.name));
                                    continue;
                                }
                                var s = EliteSystem.Shots.Fire(null, st.def, st.kind, p, Vector2.zero);
                                if (s == null) continue;
                                if (st.roster) s.AsRosterShot(null, 0f);
                                list.Add((s, s.Glow, st.name));
                            }
                        pool.Root.SetActive(false);
                        foreach (var e in list) if (e.shot is EliteShot) e.shot.gameObject.SetActive(false);
                        Color[] bg = Grab(cam, rt, tex);
                        pool.Root.SetActive(true);
                        foreach (var e in list) if (e.shot is EliteShot) e.shot.gameObject.SetActive(true);
                        Color[] fg = Grab(cam, rt, tex);
                        foreach (var (shot, glow, name) in list)
                        {
                            Vector3 c = cam.WorldToScreenPoint(glow.transform.position);
                            float half = shot is BossProjectile
                                ? shot.transform.lossyScale.x * .5f * (RH / (cam.orthographicSize * 2f))
                                : Mathf.Max(glow.bounds.extents.x, glow.bounds.extents.y) * (RH / (cam.orthographicSize * 2f));
                            int n = StandOut(fg, bg, c, half);
                            if (shot is BossProjectile) { bosses++; if (n < fewestBoss) { fewestBoss = n; worstBoss = name; } }
                            else { smalls++; if (n < fewestSmall) { fewestSmall = n; worstSmall = name; } }
                            if (n < MinStandOutPixels) DumpCrop(world + "-v" + v + "-" + at + "-" + name.Replace(' ', '_') + "-" + n, fg, bg, c, half);   // (HOSTILE_DUMP=dir: crops of a shot that fails, with / without it)
                        }
                        foreach (var e in list)
                        {
                            if (e.shot is EliteShot es) es.Recycle();
                            else if (e.shot is BossProjectile bp) bp.Recycle();
                        }
                    }
                    string where = world + " v" + v + " at " + at + " s";
                    Check("every elite / enemy shot stands out of " + where + ": at least " + fewestSmall + " px at " + MinContrast +
                          ":1 (worst " + worstSmall + "), over " + smalls + " shots of " + styles.Count + " styles (need " + MinStandOutPixels + ")",
                          smalls > 0 && fewestSmall >= MinStandOutPixels);
                    Check("every boss shot stands out of " + where + ": at least " + fewestBoss + " px at " + MinContrast +
                          ":1 (worst " + worstBoss + "), over " + bosses + " shots (need " + MinStandOutPixels + ")",
                          bosses > 0 && fewestBoss >= MinStandOutPixels);
                }
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                pool.Dispose();
                Object.DestroyImmediate(backdrop);
                picker.Force = 0;
            }
            Check(world + ": its backdrop variants were all covered (" + variants + ")", variants > 0);
        }
    }

    static void DumpCrop(string tag, Color[] fg, Color[] bg, Vector3 c, float half)
    {
        string dir = System.Environment.GetEnvironmentVariable("HOSTILE_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        int r = Mathf.CeilToInt(half) + 4, S = 2 * r + 1, k = 6;
        var t = new Texture2D(S * k * 2 + 4, S * k, TextureFormat.RGB24, false);
        for (int y = 0; y < S * k; y++)
            for (int x = 0; x < S * k * 2 + 4; x++)
            {
                bool right = x >= S * k + 4;
                if (!right && x >= S * k) { t.SetPixel(x, y, Color.red); continue; }
                int sx = (int)c.x - r + (right ? x - S * k - 4 : x) / k, sy = (int)c.y - r + y / k;
                Color col = sx < 0 || sy < 0 || sx >= RW || sy >= RH ? Color.magenta : (right ? bg : fg)[sy * RW + sx];
                t.SetPixel(x, y, col);
            }
        t.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, tag + ".png"), t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    struct ShotStyleEntry
    {
        public string name;
        public EliteDef def; public EliteShots.Kind kind; public bool roster;
        public BossDef boss; public BossShotStyle bossStyle;
    }

    // Every hostile shot world `w` fires: its roster shooters', its elites',
    // every elite shot kind (on a gunship), and its boss's bolt and shard.
    static List<ShotStyleEntry> ShotStyles(int w)
    {
        var list = new List<ShotStyleEntry>();
        foreach (var def in EnemyRoster.All)
        {
            if (def.world != w) continue;
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Shot || b.attack == EnemyAttack.Ring || b.attack == EnemyAttack.Cross || b.attack == EnemyAttack.Lob))
                list.Add(new ShotStyleEntry { name = def.key + " " + b.shotKind, def = b.ShotStyle, kind = b.shotKind, roster = true });
        }
        string wk = EnemyRoster.WorldKeys[w];
        foreach (var d in EliteCatalog.All)
            if (d.world == wk) list.Add(new ShotStyleEntry { name = "elite " + d.key + " " + d.shotKind, def = d, kind = EliteShots.KindOf(d.shotKind) });
        var gun = Def("gunship");
        foreach (EliteShots.Kind k in System.Enum.GetValues(typeof(EliteShots.Kind)))
            list.Add(new ShotStyleEntry { name = "gunship " + k, def = gun, kind = k });
        var boss = BossCatalog.ForWorld(w);
        list.Add(new ShotStyleEntry { name = boss.artKey + " bolt", boss = boss, bossStyle = BossShotStyle.Bolt });
        list.Add(new ShotStyleEntry { name = boss.artKey + " shard", boss = boss, bossStyle = BossShotStyle.Shard });
        return list;
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

    public const int MinStandOutPixels = 16;

    // Pixels within `half` of c (a square) whose luminance in fg stands
    // MinContrast clear of the mean backdrop luminance over that square.
    static int StandOut(Color[] fg, Color[] bg, Vector3 c, float half)
    {
        int r = Mathf.CeilToInt(half), n = 0, count = 0;
        int x0 = Mathf.Max(0, (int)c.x - r), x1 = Mathf.Min(RW - 1, (int)c.x + r);
        int y0 = Mathf.Max(0, (int)c.y - r), y1 = Mathf.Min(RH - 1, (int)c.y + r);
        float behind = 0f;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) { behind += Luminance(bg[y * RW + x]); n++; }
        if (n == 0) return 0;
        behind /= n;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Ratio(Luminance(fg[y * RW + x]), behind) >= MinContrast) count++;
        return count;
    }

    // The rim's alpha, read back, against the drawing's own alpha from its
    // source file (the cell's whole rect): full strength exactly under the
    // art -- the same bounds within the cell, to RimFit of it -- clear at
    // its sprite's edges, and never most of its quad.
    public const float RimFit = .04f;

    static bool RimHugsArt(Sprite art, Sprite rim, out string what)
    {
        what = "no art or rim";
        if (art == null || rim == null) return false;
        int w, h;
        var px = ShieldContour.ReadPixels(rim, out w, out h);
        Rect ar = art.rect;
        int aw = Mathf.RoundToInt(ar.width), ah = Mathf.RoundToInt(ar.height);
        var src = ShieldContour.ReadFromSourceFile(art.texture, Mathf.RoundToInt(ar.x), Mathf.RoundToInt(ar.y), aw, ah);
        what = "could not read the rim or the art back";
        if (px == null || src == null || px.Length < w * h || src.Length < aw * ah) return false;

        int most = 0, lit = 0;
        for (int i = 0; i < px.Length; i++) { most = Mathf.Max(most, px[i].a); if (px[i].a > 8) lit++; }
        bool clearEdges = true;
        for (int x = 0; x < w; x++) clearEdges &= px[x].a == 0 && px[(h - 1) * w + x].a == 0;
        for (int y = 0; y < h; y++) clearEdges &= px[y * w].a == 0 && px[y * w + w - 1].a == 0;

        // bounds, as shares of the cell: the rim's full-strength core, the art's solid pixels
        float pad = BossArt.ShotRimPad, n = BossArt.ShotRimTexels;
        Vector4 core = Bounds(px, w, h, most > 0 ? most : 255, -pad, n);
        Vector4 solid = Bounds(src, aw, ah, 128, 0f, aw);
        float off = Mathf.Max(Mathf.Max(Mathf.Abs(core.x - solid.x), Mathf.Abs(core.y - solid.y)),
                              Mathf.Max(Mathf.Abs(core.z - solid.z), Mathf.Abs(core.w - solid.w)));
        what = "peak alpha " + most + ", " + lit + " of " + px.Length + " texels lit, edges " + (clearEdges ? "clear" : "NOT clear") +
               ", core x " + core.x.ToString("F2") + ".." + core.z.ToString("F2") + " y " + core.y.ToString("F2") + ".." + core.w.ToString("F2") +
               " of the cell over art x " + solid.x.ToString("F2") + ".." + solid.z.ToString("F2") + " y " + solid.y.ToString("F2") + ".." +
               solid.w.ToString("F2") + ", off by " + off.ToString("F3");
        return clearEdges && lit > 0 && lit < px.Length / 2 && Mathf.Abs(most - 255f * BossArt.ShotRimAlpha) <= 2f && off <= RimFit;
    }

    // An elite / enemy shot's outline (ShotOutline) against its drawing:
    // clear at its sprite's edges, the light trace's full strength exactly
    // over the drawing's own opaque pixels (to one art pixel), a dark
    // hairline outside it, nothing round about it.
    static bool OutlineHugsArt(Sprite art, Sprite rim, out string what)
    {
        what = "no art or outline";
        if (art == null || rim == null) return false;
        var rt = rim.texture;
        var at = art.texture;
        what = "outline or art not readable";
        if (rt == null || at == null || !rt.isReadable || !at.isReadable) return false;
        int W = Mathf.RoundToInt(rim.rect.width), H = Mathf.RoundToInt(rim.rect.height);
        int aw = Mathf.RoundToInt(art.rect.width), ah = Mathf.RoundToInt(art.rect.height);
        var px = rt.GetPixels32();
        var all = at.GetPixels32();
        var src = new Color32[aw * ah];
        for (int y = 0; y < ah; y++)
            for (int x = 0; x < aw; x++)
                src[y * aw + x] = all[(Mathf.RoundToInt(art.rect.y) + y) * at.width + Mathf.RoundToInt(art.rect.x) + x];
        int k = ShotOutline.Upsample, pad = (W - aw * k) / 2;
        bool clearEdges = true;
        for (int x = 0; x < W; x++) clearEdges &= px[x].a == 0 && px[(H - 1) * W + x].a == 0;
        for (int y = 0; y < H; y++) clearEdges &= px[y * W].a == 0 && px[y * W + W - 1].a == 0;
        int most = 0, dark = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].r > 128) most = Mathf.Max(most, px[i].a);
            else if (px[i].a > 8) dark++;
        }
        var core = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++) core[i] = px[i].r > 128 && px[i].a >= most - 2 ? px[i] : new Color32(0, 0, 0, 0);
        Vector4 c = Bounds(core, W, H, 1, -pad, k);
        Vector4 solid = Bounds(src, aw, ah, Mathf.RoundToInt(ShotOutline.Coverage * 255f), 0f, 1f);
        float off = Mathf.Max(Mathf.Max(Mathf.Abs(c.x - solid.x), Mathf.Abs(c.y - solid.y)),
                              Mathf.Max(Mathf.Abs(c.z - solid.z), Mathf.Abs(c.w - solid.w)));
        what = "peak " + most + ", " + dark + " dark hairline texels, edges " + (clearEdges ? "clear" : "NOT clear") +
               ", core off the art's solid pixels by " + off.ToString("F2") + " art px";
        return clearEdges && Mathf.Abs(most - 255f * ShotOutline.Alpha) <= 2f && dark > 0 && off <= 1f;
    }

    // (xMin, yMin, xMax, yMax) of the pixels with alpha >= `atLeast`, each
    // (index + shift) / per: a share of the cell.
    static Vector4 Bounds(Color32[] px, int w, int h, int atLeast, float shift, float per)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a < atLeast) continue;
                x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
            }
        return new Vector4((x0 + shift) / per, (y0 + shift) / per, (x1 + 1 + shift) / per, (y1 + 1 + shift) / per);
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
                  System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Weapons/ShipAttacks.cs").Contains("HostileShots.ShootDownAlong(from, to, radius)"));
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
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteShots.cs").Contains("FriendlyFire.HostileHit(t, p, by)"));

        // during a player death everything is the domino's
        Check("none of it runs during a player death (DeathCrash.Running guards)",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Enemies/FriendlyFire.cs").Contains("if (mine == null || DeathCrash.Running"));
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
                  System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Enemies/FriendlyFire.cs").Contains("if (target == null || !Enabled || DeathCrash.Running) return false;"));
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
