using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tide's five elites (Riptide, Bathyscaphe, Trawler, Thunderfin, Pearl Warden; TideElites.cs): the roster, their strips (7 flight cells
// 1344 x 192, 3 death cells 576 x 192), muzzles on the hull, launching from Tide's own pads, the director's rotation, the codex entries, and
// each one's brain and attack in simulation. Every attack shows its footprint from the first frame of its tell for at least the FR1 lead
// and cleans up after itself; nothing allocates per frame.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites TideEliteTest
public static class TideEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TEL] PASS  " : "[TEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    static readonly (string key, string brain, string attack, LandingKind from)[] Expected =
    {
        ("tide_elite_riptide", "surfer", "surf_dash", LandingKind.TideTrenchHatch),
        ("tide_elite_bathyscaphe", "abyss", "depth_charge", LandingKind.TideRigDeck),
        ("tide_elite_trawler", "netter", "net_cast", LandingKind.TideReefDock),
        ("tide_elite_thunderfin", "stormray", "thunder_strike", LandingKind.TideVentStack),
        ("tide_elite_pearl_warden", "pearler", "pearl_curtain", LandingKind.TideWreckBay),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Roster();
            Sites();
            Rotation();
            Codex();
            Surf();
            Depth();
            Net();
            Thunder();
            Pearl();
            Allocations();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            AttackPools.ClearAll();
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[TEL] failures: " + fails);
        return fails;
    }

    // ---- fixtures ---------------------------------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        AttackPools.ClearAll();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        AttackPreview.ResetCounters();
        Random.InitState(1357);
    }

    static EliteShip InPlay(string key, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(EliteCatalog.Find(key), at);
        e.AttackCooldown = 99f;
        e.EscapeLeft = 0f;
        return e;
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    // Starts the attack and steps one frame: the first frame of its tell.
    static void StartTell(EliteShip e)
    {
        e.SetSeen(pilot.position);
        e.ForceAttack();
        EliteSystem.Step(Dt);
    }

    static bool StepUntil(System.Func<bool> done, float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            if (done()) return true;
            pilot.position = new Vector3(0f, -2.5f, 0f);   // (a pilot standing still: the attack's own geometry is under test)
            EliteSystem.Step(Dt);
        }
        return done();
    }

    // Hits it with a player weapon `n` times, a grace apart.
    static void Shoot(EliteShip e, int n)
    {
        for (int i = 0; i < n && e != null && e.State != EliteState.Dead; i++)
        {
            e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
            Step(EliteShip.GraceSeconds + .05f);
        }
    }

    static LandingSite Pad(Vector2 at, LandingKind kind, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind };
    }

    static bool OnHull(Texture2D tex, EliteDef d, ElitePoint p)
    {
        int cell = d.cells.Flight0, n = d.cellPixels;
        bool hit = false;
        int r = 2 * n / 192;   // a 2 px reach at the 192 px grid, scaled to HD cells
        for (int dy = -r; dy <= r && !hit; dy++)
            for (int dx = -r; dx <= r && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var tide = new List<EliteDef>();
        Check("Tide has five elites (" + EliteCatalog.ForWorld(4, tide) + ")", tide.Count == 5);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Tide with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 4 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            Check(x.key + ": launches from its own Tide pad, never a Space body", LandingSite.Accepts(d.launchFrom, x.from) && !LandingSite.Accepts(d.launchFrom, LandingKind.Station));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            float cell = d.cellPixels;   // 192, or 384 for the HD rebuilds (build_tide_hd.py)
            Check(x.key + ": the flight strip (7 x " + cell + " px cells) slices into 7 cells and the cell map is the flight layout",
                  EliteHdResolution.CellOk(d) &&
                  frames != null && frames.Length == 7 && Mathf.Approximately(frames[0].rect.width, cell) && Mathf.Approximately(frames[0].rect.height, cell) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0);
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the death strip slices into 3 cells of " + cell + " (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length == 3 && Mathf.Approximately(death[0].rect.width, cell));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Tide/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            bool inCell = true;
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > cell * .04f && p.x < cell * .96f && p.y > cell * .04f && p.y < cell * .96f; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > cell * .04f && p.x < cell * .96f && p.y > cell * .04f && p.y < cell * .96f; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": two hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts == 2 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned like its siblings (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .7f && d.avoidance >= .4f && d.avoidance <= .8f);
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
    }

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.TideTrenchHatch, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.TideRigDeck, 2),
            Pad(new Vector2(0f, 3f), LandingKind.TideReefDock, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.TideVentStack, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.TideWreckBay, 5),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(4, 1);
            foreach (var e in EliteShip.Live) { match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind); seen.Add(e.Def.key); }
        }
        Check("each Tide elite launches from its own pad (" + spawned + " launches)", spawned == 40 && match);
        Check("all five Tide elites turn up (" + seen.Count + ")", seen.Count == 5);
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(4, 1) == 1; }
        Check("... and any free pad when none of theirs is in view", fallback);
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
    }

    static void Rotation()
    {
        Fresh(.05f);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var seen = new HashSet<string>();
        bool perDeck = true;
        for (int deck = 0; deck < 4; deck++)
        {
            seen.Clear();
            for (int i = 0; i < 5; i++) seen.Add(dir.NextDef(4).key);
            perDeck &= seen.Count == 5;
        }
        Check("the director deals Tide's five elites as a shuffled deck", perDeck);
        Check("Tide's elites wait for the calm start", EliteDirector.Blocked(4, EliteDirector.FirstSeconds - .5f) == "too early");
        Object.DestroyImmediate(dir.gameObject);
    }

    static void Codex()
    {
        int n = 0;
        bool strips = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex != 4) continue;
            n++;
            var entry = global::Codex.Find(d.codexId);
            strips &= entry != null && entry.category == CodexCategory.Enemies && EliteArt.HasExtra(d, EliteArt.Extra.Death);
        }
        Check("the codex lists five Tide elites with their death strips (" + n + ")", n == 5 && strips);
    }

    // ---- Riptide: surf_dash ---------------------------------------------------------------

    static void Surf()
    {
        Fresh();
        var e = InPlay("tide_elite_riptide", new Vector2(0f, .6f));
        var sd = (SurfDashAttack)e.Attack;
        StartTell(e);
        Check("Riptide: it is telling and the lane is not drawn before the aim locks", e.Telling && !sd.Shown);
        bool shownInTell = StepUntil(() => sd.Shown, 3f);
        Check("... the dash lane is drawn once locked, while the tell still runs (" + sd.Telegraph.Loops + " outline)", shownInTell && e.Telling && sd.Telegraph.Showing && sd.Telegraph.Loops == 1);
        StepUntil(() => e.Acting, 3f);
        Check("it then dashes committed at dashSpeed (" + e.Velocity.magnitude.ToString("0.0") + " u/s) and the lane showed the FR1 lead",
              e.Acting && e.Velocity.magnitude > e.Def.dashSpeed * .7f && AttackPreview.TooShort == 0);
        int shots = 0;
        for (float t = 0f; t < 1.5f; t += Dt) { pilot.position = new Vector3(0f, -4.8f, 0f); EliteSystem.Step(Dt); foreach (var s in EliteSystem.Shots.All) if (s.Active) shots++; }
        Check("spout needles leave the lance as it brakes (" + sd.Fired + " fired)", sd.Fired == e.Def.shotCount && shots > 0);
        Fresh();
        e = InPlay("tide_elite_riptide", new Vector2(0f, .6f));
        StartTell(e);
        StepUntil(() => ((SurfDashAttack)e.Attack).Shown, 3f);
        Shoot(e, 2);
        Check("a Riptide killed in its tell takes its lane with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Bathyscaphe: depth_charge --------------------------------------------------------

    static void Depth()
    {
        Fresh();
        var e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, .4f));
        var dc = (DepthChargeAttack)e.Attack;
        StartTell(e);
        Check("Bathyscaphe: each charge's flight lane and burst ring are drawn on the first frame of the tell (" + dc.Telegraph.Loops + " outlines)",
              e.Telling && dc.Telegraph.Showing && dc.Telegraph.Loops == 2 * e.Def.shotCount);
        Vector2 b0 = dc.Burst(0);
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead, burst points locked", e.Acting && AttackPreview.TooShort == 0 && dc.Burst(0) == b0);
        int orbs = 0, bursts = 0, maxShards = 0;
        for (float t = 0f; t < 3.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            int o = 0, sh = 0;
            foreach (var s in EliteSystem.Shots.All) { if (!s.Active) continue; if (s.Kind == EliteShots.Kind.Orb) o++; else sh++; }
            orbs = Mathf.Max(orbs, o);
            maxShards = Mathf.Max(maxShards, sh);
        }
        foreach (var s in EliteSystem.Shots.All) bursts += s.Bursts;
        Check("fused orbs leave the claws and burst into rings of " + e.Def.hazardCount + " pearls (orbs " + orbs + ", bursts " + bursts + ", most pearls " + maxShards + ")",
              dc.Sent == e.Def.shotCount && orbs >= 1 && bursts == e.Def.shotCount && maxShards >= e.Def.hazardCount);
        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, .4f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Bathyscaphe killed in its tell takes its drawing with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Trawler: net_cast ----------------------------------------------------------------

    static void Net()
    {
        Fresh();
        var e = InPlay("tide_elite_trawler", new Vector2(0f, .6f));
        var nc = (NetCastAttack)e.Attack;
        StartTell(e);
        Check("Trawler: every net spot but the hole is ringed on the first frame of the tell (" + nc.Telegraph.Loops + " outlines)",
              e.Telling && nc.Telegraph.Showing && nc.Telegraph.Loops == nc.Count - 1);
        int gap = nc.Gap;
        Vector2 gapSpot = nc.Spot(gap);
        Check("the hole is beside the pilot toward the board's middle (spot " + gap + " of " + nc.Count + ")", gap == nc.Centre + (pilot.position.x > 0f ? -1 : 1));
        StepUntil(() => e.Acting, 3f);
        bool lobs = StepUntil(() => nc.Cast >= nc.Count - 1, 3f);
        Check("it lobs " + (nc.Count - 1) + " nets, shown the FR1 lead, none onto the hole", lobs && AttackPreview.TooShort == 0 && nc.Gap == gap);
        int pools = 0;
        bool onGap = false;
        for (float t = 0f; t < 2.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            int p = 0;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || s.Kind != EliteShots.Kind.Glob || s.Airborne) continue;
                p++;
                onGap |= Mathf.Abs(s.transform.position.x - gapSpot.x) < .25f;
            }
            pools = Mathf.Max(pools, p);
        }
        Check("the nets land as pools (" + pools + " at once) and the hole stays open (" + (!onGap) + ")", pools >= nc.Count - 2 && !onGap);
        Fresh();
        e = InPlay("tide_elite_trawler", new Vector2(0f, .6f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Trawler killed in its tell takes its rings with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Thunderfin: thunder_strike -------------------------------------------------------

    static void Thunder()
    {
        Fresh();
        var e = InPlay("tide_elite_thunderfin", new Vector2(0f, .6f));
        var ts = (ThunderStrikeAttack)e.Attack;
        StartTell(e);
        Check("Thunderfin: a glyph is armed under every column on the first frame of the tell (" + ts.Armed + ")",
              e.Telling && ts.Armed >= 1 && ts.Strike(0) != null && ts.Strike(0).State == AttackHazard.Phase.Tell);
        Check("... told at least " + AttackHazard.MinTellSeconds + " s (" + e.Attack.TellSeconds.ToString("0.00") + ")", e.Attack.TellSeconds >= AttackHazard.MinTellSeconds + .3f);
        bool live = false;
        float liveFor = 0f;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            var s0 = ts.Strike(0);
            if (s0 != null && s0.State == AttackHazard.Phase.Live) { live = true; liveFor += Dt; }
        }
        Check("the lightning goes live for no longer than " + AttackStrike.MaxLiveSeconds + " s (" + liveFor.ToString("0.00") + ")", live && liveFor <= AttackStrike.MaxLiveSeconds + Dt * 2);
        Step(1f);
        Check("nothing is left on the board afterwards (" + AttackHazard.ActiveCount + " hazards)", AttackHazard.ActiveCount == 0);
        Fresh();
        e = InPlay("tide_elite_thunderfin", new Vector2(0f, .6f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Thunderfin killed in its tell takes its glyphs with it", AttackHazard.ActiveCount == 0);
    }

    // ---- Pearl Warden: pearl_curtain ------------------------------------------------------

    static void Pearl()
    {
        Fresh();
        var e = InPlay("tide_elite_pearl_warden", new Vector2(0f, .6f));
        var pc = (PearlCurtainAttack)e.Attack;
        StartTell(e);
        StepUntil(() => e.Acting, 3f);
        int pearls = 0;
        for (float t = 0f; t < 2f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All) if (s.Active) pearls++;
        }
        Check("Pearl Warden: a curtain of " + (pc.Count - 1) + " slow pearls with one gap beside the pilot (fired " + pc.Fired + ", pearl frames " + pearls + ")",
              pc.Fired == pc.Count - 1 && pearls > 10 && pc.Gap == pc.Centre + (pilot.position.x > 0f ? -1 : 1));
    }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        string[][] groups =
        {
            new[] { "tide_elite_riptide", "tide_elite_bathyscaphe", "tide_elite_trawler" },
            new[] { "tide_elite_thunderfin", "tide_elite_pearl_warden" },
        };
        foreach (var g in groups)
        {
            Fresh(.1f);
            pilot.position = new Vector3(0f, -2.5f, 0f);
            var ships = new List<EliteShip>();
            for (int i = 0; i < g.Length; i++) ships.Add(InPlay(g[i], new Vector2(-1.4f + i * 1.4f, .5f + i * .4f)));
            foreach (var s in ships) s.AttackCooldown = 0f;
            Step(8f);   // warm up: every attack, preview, shot and pool once
            foreach (var s in ships) s.AttackCooldown = 0f;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 90; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                EliteSystem.Step(Dt);
            }
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("zero per-frame allocations stepping " + string.Join(", ", g) + " (" + allocated + " bytes over 90 frames)", allocated == 0);
        }
    }
}
