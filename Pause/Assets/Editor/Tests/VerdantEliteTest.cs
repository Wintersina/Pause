using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Verdant's four new elites (Timber Hauler, Thornlash, Sporebloom, Leafblade; VerdantElites.cs) beside the Resin Warden: the roster
// (five), their strips (7 flight cells 1344 x 192, 3 death cells 576 x 192), anchors and muzzles inside the cell and on the hull, launching
// from their own Verdant pads, all five turning up in the director's rotation, the codex entries with their death strips, and each one's
// brain and attack in simulation. Every attack must show its footprint from the first frame of its tell (AttackPreview), for at least
// the FR1 lead, and clean up after itself; nothing allocates per frame.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites VerdantEliteTest
public static class VerdantEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VEL] PASS  " : "[VEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    static readonly (string key, string brain, string attack, string motion, LandingKind from)[] Expected =
    {
        ("verdant_elite_timber_hauler", "logger", "log_roll", "roll", LandingKind.RiverBay),
        ("verdant_elite_thornlash", "thorn", "vine_lash", "", LandingKind.TowerBay),
        ("verdant_elite_sporebloom", "drifter", "spore_burst", "burst", LandingKind.PodPad),
        ("verdant_elite_leafblade", "diver", "leaf_dive", "slash", LandingKind.RootHangar),
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
            Logger();
            Thorn();
            Drifter();
            Diver();
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
        Debug.Log("[VEL] failures: " + fails);
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
        for (int dy = -2; dy <= 2 && !hit; dy++)
            for (int dx = -2; dx <= 2 && !hit; dx++)
            {
                int x = cell * n + Mathf.RoundToInt(p.x) + dx, y = n - 1 - (Mathf.RoundToInt(p.y) + dy);
                hit = tex.GetPixel(x, y).a >= .5f;
            }
        return hit && d.PixelToLocal(p.x, p.y).magnitude < d.cellWorldSize * .42f;
    }

    // ---- roster ------------------------------------------------------------------------

    static void Roster()
    {
        var verdant = new List<EliteDef>();
        Check("Verdant has five elites (" + EliteCatalog.ForWorld(2, verdant) + ")", verdant.Count == 5 && EliteCatalog.Find("verdant_elite_resin_warden") != null);
        var brains = new HashSet<string> { "warden" };
        var attacks = new HashSet<string> { "resin_mortar" };
        var warden = EliteCatalog.Find("verdant_elite_resin_warden");
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Verdant with its own brain / attack (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 2 && d.brain == x.brain && d.attack == x.attack && d.shotMotion == x.motion && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            Check(x.key + ": launches from its own pad, never a Space body", LandingSite.Accepts(d.launchFrom, x.from) && !LandingSite.Accepts(d.launchFrom, LandingKind.Station));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the 1344 x 192 flight strip slices into 7 cells of 192 and the cell map is the flight layout",
                  frames != null && frames.Length == 7 && Mathf.Approximately(frames[0].rect.width, 192f) && Mathf.Approximately(frames[0].rect.height, 192f) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0 && d.cellPixels == 192);
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the 576 x 192 death strip slices into 3 cells of 192 (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length == 3 && Mathf.Approximately(death[0].rect.width, 192f) && Mathf.Approximately(death[0].rect.height, 192f));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Verdant/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            bool inCell = true;
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > 8 && p.x < 184 && p.y > 8 && p.y < 184; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > 8 && p.x < 184 && p.y > 8 && p.y < 184; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": two hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts == 2 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned beside the Resin Warden (speed " + d.speed + " vs " + warden.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .7f && d.avoidance >= .4f && d.avoidance <= .7f);
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
        // memory budget: the strips are imported uncompressed (pixel art): a flight strip and a death strip per elite stay small
        long bytes = 0;
        foreach (var x in Expected)
        {
            foreach (string suffix in new[] { "", "_death" })
            {
                var t = Resources.Load<Texture2D>("Elites/Verdant/" + x.key + suffix);
                if (t != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            }
        }
        Check("the four new elites' art holds " + (bytes / 1048576f).ToString("0.0") + " MB of textures, flight + death strips, uncompressed with the editor's readable copy (budget 14 MB, 3.5 MB each)", bytes > 0 && bytes <= 14L * 1048576);
        Check("Verdant is open to elites after the first 20 s", EliteDirector.Blocked(2, 60f) == null && EliteDirector.Blocked(2, 10f) == "too early");
    }

    // ---- the director: each from its own pad, and all five in rotation --------------------

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.RootHangar, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.RiverBay, 2),
            Pad(new Vector2(0f, 3f), LandingKind.PodPad, 3), Pad(new Vector2(.8f, 3.3f), LandingKind.TowerBay, 4),
            Pad(new Vector2(1.6f, 3f), LandingKind.Hatch, 5),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0, mine = 0;
        bool match = true;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(2, 1);
            foreach (var e in EliteShip.Live)
            {
                if (e.Def.key == "verdant_elite_resin_warden") continue;
                mine++;
                match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            }
            foreach (var e in EliteShip.Live) seen.Add(e.Def.key);
        }
        Check("each new Verdant elite launches from its own pad (" + mine + " of " + spawned + " launches)", spawned == 40 && mine > 20 && match);
        Check("all five Verdant elites turn up (" + seen.Count + ")", seen.Count == 5);
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(2, 1) == 1; }
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
            for (int i = 0; i < 5; i++) seen.Add(dir.NextDef(2).key);
            perDeck &= seen.Count == 5;
        }
        Check("the director deals Verdant's five elites as a shuffled deck: all five in every five spawns", perDeck);
        Check("the first elite still waits for the calm start (" + EliteDirector.FirstSeconds + " s)", EliteDirector.Blocked(2, EliteDirector.FirstSeconds - .5f) == "too early");
        Check("elites stay capped at " + EliteDirector.MaxAlive + " alive, whatever the world", EliteDirector.MaxAlive == 3);
        Object.DestroyImmediate(dir.gameObject);
    }

    static void Codex()
    {
        int n = 0;
        bool strips = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex != 2) continue;
            n++;
            var entry = global::Codex.Find(d.codexId);
            bool ok = entry != null && entry.category == CodexCategory.Enemies && (d.key == "verdant_elite_resin_warden" || EliteArt.HasExtra(d, EliteArt.Extra.Death));   // (the Warden has no painted death strip yet)
            if (!ok) Debug.Log("[VEL] codex " + d.key + ": entry " + (entry != null) + " category " + (entry != null ? entry.category.ToString() : "-") + " death " + EliteArt.HasExtra(d, EliteArt.Extra.Death));
            strips &= ok;
        }
        Check("the codex lists five Verdant elites, the four new ones with their death strip for the tap (" + n + ")", n == 5 && strips);
    }

    // ---- Timber Hauler: log_roll ----------------------------------------------------------

    static void Logger()
    {
        Fresh();
        var e = InPlay("verdant_elite_timber_hauler", new Vector2(0f, .6f));
        var lr = (LogRollAttack)e.Attack;
        StartTell(e);
        Check("Timber Hauler: the spots and the roll lanes are drawn on the first frame of the tell (" + lr.Telegraph.Loops + " outlines)",
              e.Telling && lr.Telegraph.Showing && lr.Telegraph.Loops >= 2 * e.Def.shotCount);
        Vector2 spot0 = lr.Spot(0);
        float tellSeconds = e.Attack.TellSeconds;
        Check("... for at least the whole wind-up (" + tellSeconds.ToString("0.00") + " s)", tellSeconds >= AttackPreview.MinLead + .3f);
        bool locked = true;
        StepUntil(() => { locked &= Mathf.Abs(lr.Spot(0).x - spot0.x) < 1e-3f; return e.Acting; }, 3f);
        Check("the spots are locked at the tell (they ride the board, never the pilot)", e.Acting && locked && AttackPreview.TooShort == 0);
        Check("the trunks are lobbed one after another out of the grabbers, all landing on their spots", StepUntil(() => lr.Lobbed >= e.Def.shotCount, 2f));
        int rolled = 0, bounced = 0, motionOk = 0;
        bool downhill = true, rightWay = true;
        float maxLife = 0f;
        for (float t = 0f; t < 8f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active) continue;
                if ((s.Motion & ShotMotion.Roll) != 0) motionOk++;
                if (s.Rolling)
                {
                    if (t < 3f) rolled++;
                    downhill &= s.Velocity.y < 0f;
                    bounced = Mathf.Max(bounced, s.Bounced);
                    maxLife = Mathf.Max(maxLife, s.Age);
                }
            }
        }
        Check("Timber Hauler: its trunks wear the Roll body and roll on after landing (" + motionOk + " frames, " + rolled + " rolling)", motionOk > 10 && rolled > 10);
        Check("... down the board, bouncing off a rail at most once, for no more than " + ShotMotions.RollSeconds + " s (bounces " + bounced + ", age " + maxLife.ToString("0.0") + ")",
              downhill && bounced <= 1 && maxLife <= ShotMotions.RollSeconds + .2f);
        // the drawn lane is the rule the trunk follows
        Check("the drawn roll direction is toward the nearer rail",
              LogRollAttack.RollDir(.8f).x > 0f && LogRollAttack.RollDir(-.8f).x < 0f && LogRollAttack.RollDir(.8f).y < 0f && rightWay);
        Check("the spots are never on the middle line (every roll is drawn)", Mathf.Abs(lr.Spot(0).x) >= LogRollAttack.MinSpotX - 1e-3f && Mathf.Abs(lr.Spot(1).x) >= LogRollAttack.MinSpotX - 1e-3f);
        // a tell cut short takes the drawing with it
        Fresh();
        e = InPlay("verdant_elite_timber_hauler", new Vector2(0f, .6f));
        StartTell(e);
        int shown = AttackPreview.ActiveCount;
        Shoot(e, 2);
        Check("a Timber Hauler killed in its tell takes its drawing with it (" + shown + " -> " + AttackPreview.ActiveCount + ")", shown >= 1 && AttackPreview.ActiveCount == 0);
    }

    // ---- Thornlash: vine_lash -------------------------------------------------------------

    static void Thorn()
    {
        Fresh();
        var e = InPlay("verdant_elite_thornlash", new Vector2(1.3f, -2.4f));
        var vl = (VineLashAttack)e.Attack;
        StartTell(e);
        var lash = vl.Lash;
        Check("Thornlash: the whip's whole swept area is drawn from the first frame of the tell",
              e.Telling && lash != null && lash.State == AttackHazard.Phase.Tell && lash.Preview != null && lash.Preview.Active && lash.Preview.DotCount > 20);
        float start = lash != null ? lash.StartRad : 0f;
        int dir = lash != null ? lash.Dir : 0;
        bool locked = true;
        StepUntil(() => { if (lash != null && lash.State == AttackHazard.Phase.Tell) locked &= Mathf.Approximately(lash.StartRad, start) && lash.Dir == dir; return e.Acting; }, 3f);
        Check("the sweep is aimed once, when the tell begins", locked && e.Acting && lash != null && lash.State == AttackHazard.Phase.Live && vl.Lit);
        Check("it showed for at least the FR1 lead (" + AttackPreview.MinShown.ToString("0.00") + " s) and never too short (" + AttackPreview.TooShort + ")",
              AttackPreview.MinShown >= .69f && AttackPreview.TooShort == 0);
        StepUntil(() => e.State == EliteState.Follow, 3f);
        Step(.5f);
        Check("the whip is gone when the attack is over (" + AttackHazard.ActiveCount + " hazards)", AttackHazard.ActiveCount == 0);
        Fresh();
        e = InPlay("verdant_elite_thornlash", new Vector2(1.3f, -2.4f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Thornlash killed in its tell takes the whip and its drawing with it", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0);
    }

    // ---- Sporebloom: spore_burst ----------------------------------------------------------

    static void Drifter()
    {
        Fresh();
        var e = InPlay("verdant_elite_sporebloom", new Vector2(0f, .4f));
        var sb = (SporeBurstAttack)e.Attack;
        StartTell(e);
        Check("Sporebloom: the burst rings and the pods' flight lines are drawn on the first frame of the tell (" + sb.Telegraph.Loops + " outlines)",
              e.Telling && sb.Telegraph.Showing && sb.Telegraph.Loops == 2 * e.Def.shotCount);
        Vector2 b0 = sb.Burst(0);
        StepUntil(() => e.Acting, 3f);
        Check("... and shown at least the FR1 lead, burst points locked", e.Acting && AttackPreview.TooShort == 0 && sb.Burst(0) == b0);
        int pods = 0, maxSpores = 0, bursts = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            int chips = 0, ps = 0;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active) continue;
                if (s.IsChip) chips++; else if ((s.Motion & ShotMotion.Burst) != 0) ps++;
            }
            pods = Mathf.Max(pods, ps);
            maxSpores = Mathf.Max(maxSpores, chips);
        }
        foreach (var s in EliteSystem.Shots.All) bursts += s.Splits;
        Check("its pods burst into rings of " + ShotMotions.BurstSpores + " spores (pods " + pods + ", most spores at once " + maxSpores + ", bursts " + bursts + ")",
              pods == e.Def.shotCount && maxSpores >= ShotMotions.BurstSpores && bursts == e.Def.shotCount);
        Check("the burst rings are as wide as the spores reach", Mathf.Approximately(SporeBurstAttack.Reach, ShotMotions.SporeSpeed * ShotMotions.SporeSeconds));
        Fresh();
        e = InPlay("verdant_elite_sporebloom", new Vector2(0f, .4f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Sporebloom killed in its tell takes its drawing with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Leafblade: leaf_dive -------------------------------------------------------------

    static void Diver()
    {
        Fresh();
        var e = InPlay("verdant_elite_leafblade", new Vector2(1.4f, -.2f));
        var ld = (LeafDiveAttack)e.Attack;
        StartTell(e);
        Check("Leafblade: the dive lane and the crescent's row are drawn on the first frame of the tell (" + ld.Telegraph.Loops + " outlines)",
              e.Telling && ld.Telegraph.Showing && ld.Telegraph.Loops == 2);
        Vector2 aim = ld.Aim;
        float row = ld.SlashRowY;
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead, then it dives committed down the lane at dashSpeed (" + e.Velocity.magnitude.ToString("0.0") + " u/s)",
              e.Acting && AttackPreview.TooShort == 0 && AttackPreview.MinShown >= .69f && e.Velocity.magnitude > e.Def.dashSpeed * .8f &&
              Vector2.Dot(e.Velocity.normalized, ld.Dir) > .98f);
        bool capsule = false, horizontal = false, onRow = false, heading = false;
        for (float t = 0f; t < 2f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active || !s.SlashCapsule) continue;
                capsule = true;
                horizontal |= Mathf.Abs(s.SlashAxis.y) < .02f;
                onRow |= Mathf.Abs(s.transform.position.y - (row - EliteSystem.Scroll * 0f)) < .6f;
                heading |= Mathf.Sign(s.Velocity.x) == Mathf.Sign(Mathf.Cos(ld.SlashDeg * Mathf.Deg2Rad));
            }
        }
        Check("a crescent leaves the blade along the drawn row, crossing the lane lengthwise (capsule " + capsule + ", level " + horizontal + ", on its row " + onRow + ", heading " + heading + ")",
              ld.Slashed && capsule && horizontal && onRow && heading);
        Fresh();
        e = InPlay("verdant_elite_leafblade", new Vector2(1.4f, -.2f));
        StartTell(e);
        Shoot(e, 2);
        Check("a Leafblade killed in its tell takes its drawing with it", AttackPreview.ActiveCount == 0);
    }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("verdant_elite_timber_hauler", new Vector2(-1.2f, .5f));
        var b = InPlay("verdant_elite_thornlash", new Vector2(1.4f, -2.2f));
        var c = InPlay("verdant_elite_sporebloom", new Vector2(0f, 1.2f));
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = 0f;
        Step(6f);   // warm up: every attack, preview, shot, spore and whip once
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = 0f;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
            EliteSystem.Step(Dt);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero per-frame allocations stepping the Timber Hauler, Thornlash and Sporebloom (" + allocated + " bytes over 90 frames)", allocated == 0);

        Fresh(.1f);
        var d = InPlay("verdant_elite_leafblade", new Vector2(1.2f, 0f));
        d.AttackCooldown = 0f;
        Step(5f);
        d.AttackCooldown = 0f;
        before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
            EliteSystem.Step(Dt);
        }
        allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero per-frame allocations stepping the Leafblade (" + allocated + " bytes over 90 frames)", allocated == 0);
    }
}
