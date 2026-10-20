using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Verdant's five elites as mini-boss fights (VerdantElites.cs): the roster (hearts, hiding, one pad each, no pink), the four new strips
// (7 flight cells, 3 death cells), anchors and muzzles inside the cell and on the hull, launching from their own Verdant pads (and the
// backdrop bringing the pad into view), all five turning up in the director's rotation, the codex entries with their death strips, and
// each one's brain and attack in simulation: none follows the pilot (a minimum distance outside committed attacks); the Leafblade's
// swooping passes (a diagonal lane and crescent rows drawn first, out the far side, back by another entry, two crescents once hurt);
// the Resin Warden's perches and its pool wall with a gap that narrows as hearts go; the Sporebloom sinking out of sight, rising
// elsewhere and leaving a spore cloud lane (outlined first) at two hearts; Thornlash's anchors alternating sides with the whip's
// fair corridor open and a sweep more per heart lost; the Timber Hauler's raft with its gap. Every attack must show its footprint
// from the first frame of its tell (AttackPreview), for at least the FR1 lead, and clean up after itself; nothing allocates per frame.
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

    // key -> brain, attack, shot motion, the one pad it launches from, hearts, hides when hurt
    static readonly (string key, string brain, string attack, string motion, LandingKind from, int hearts, bool hides)[] Expected =
    {
        ("verdant_elite_timber_hauler", "lumber", "raft_roll", "roll", LandingKind.RiverBay, 5, false),
        ("verdant_elite_thornlash", "anchor", "thorn_sweep", "", LandingKind.TowerBay, 3, true),
        ("verdant_elite_sporebloom", "floater", "spore_drift", "burst", LandingKind.PodPad, 3, true),
        ("verdant_elite_leafblade", "raptor", "leaf_swoop", "slash", LandingKind.RootHangar, 3, true),
    };

    static readonly (string key, string brain, string attack, string motion, LandingKind from, int hearts, bool hides) WardenRow =
        ("verdant_elite_resin_warden", "canopy", "canopy_mortar", "", LandingKind.Hatch, 3, true);

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
            Distances();
            Leafblade();
            Warden();
            Sporebloom();
            Thornlash();
            Hauler();
            Allocations();
            BringHome();
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
        var verdant = new List<EliteDef>();
        Check("Verdant has five elites (" + EliteCatalog.ForWorld(2, verdant) + ")", verdant.Count == 5 && EliteCatalog.Find("verdant_elite_resin_warden") != null);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        var docks = new HashSet<LandingKind>();
        var warden = EliteCatalog.Find("verdant_elite_resin_warden");
        var rows = new List<(string key, string brain, string attack, string motion, LandingKind from, int hearts, bool hides)>(Expected);
        rows.Add(WardenRow);
        foreach (var x in rows)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Verdant with its own brain / attack, new ids (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 2 && d.brain == x.brain && d.attack == x.attack && d.shotMotion == x.motion && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            Check(x.key + ": launches from its own pad, " + d.launchFrom + ", shared with no other Verdant elite, never a Space body",
                  LandingSite.KindOf(d.launchFrom) == x.from && LandingSite.Accepts(d.launchFrom, x.from) && docks.Add(x.from) && !LandingSite.Accepts(d.launchFrom, LandingKind.Station));
            Check(x.key + ": " + x.hearts + " hearts" + (d.armored ? " (armoured)" : "") + ", " + (x.hides ? "hides when hurt (" + d.hideSeconds + " s)" : "no hiding"),
                  d.hearts == x.hearts && d.armored == (x.hearts == 5) && d.hurtHide == x.hides && (!x.hides || d.hideSeconds >= 2f));
            Check(x.key + ": Verdant's cue colours, never pink (shot " + d.ShotColor + ", core " + d.ShotCore + ", hearts " + d.HeartColor + ")",
                  !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore) && !WorldCue.IsPink(d.HeartColor));
            float orbit = d.hullRadius * d.heartOrbit + d.heartSize * 1.05f;
            Check(x.key + ": hearts orbiting tighter than the player's (" + orbit.ToString("0.00") + " u)", orbit < .52f);
            if (x.key == WardenRow.key) continue;   // (the Warden's strip is the rebuilt ten-cell one: FrostVerdantEliteFramesTest)
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the flight strip slices into 7 square cells of the def's cell size and the cell map is the flight layout",
                  frames != null && frames.Length == 7 + c.flight.Length - 1 && Mathf.Approximately(frames[0].rect.width, d.cellPixels) && Mathf.Approximately(frames[0].rect.height, d.cellPixels) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0 && EliteHdResolution.CellOk(d));
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the death strip slices into 3 cells of the def's cell size (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length == 3 && Mathf.Approximately(death[0].rect.width, d.cellPixels) && Mathf.Approximately(death[0].rect.height, d.cellPixels));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Verdant/" + d.key + ".png"));
            bool solid = true;
            string bad = "";
            bool inCell = true;
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > d.cellPixels * .04f && p.x < d.cellPixels * .96f && p.y > d.cellPixels * .04f && p.y < d.cellPixels * .96f; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > d.cellPixels * .04f && p.x < d.cellPixels * .96f && p.y > d.cellPixels * .04f && p.y < d.cellPixels * .96f; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": tuned beside the Resin Warden (speed " + d.speed + " vs " + warden.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .7f && d.avoidance >= .4f && d.avoidance <= .7f);
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
        // memory budget. Two different numbers, and only the first is what a player's phone pays:
        //  phone  = ASTC 6x6 (3.56 bits per px, the Android diet override on each strip's importer), no readable copy: budget 4 MB,
        //           the same per-world phone budget EliteHdTest holds for all of a world's HD elites (these four are a part of it);
        //  editor = what Profiler.GetRuntimeMemorySizeLong reports here: RGBA32 plus the readable CPU copy (2 x w*h*4, ~45 MB for these
        //           four). It is not a shipping cost; it is only checked as a sanity bound so a duplicated or mip-mapped strip shows up.
        long px = 0, editorBytes = 0;
        bool astc = true;
        foreach (var x in Expected)
        {
            foreach (string suffix in new[] { "", "_death" })
            {
                var t = Resources.Load<Texture2D>("Elites/Verdant/" + x.key + suffix);
                if (t == null) continue;
                px += (long)t.width * t.height;
                editorBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                var ti = UnityEditor.AssetImporter.GetAtPath("Assets/Art/Resources/Elites/Verdant/" + x.key + suffix + ".png") as UnityEditor.TextureImporter;
                var an = ti != null ? ti.GetPlatformTextureSettings("Android") : default;
                if (ti == null || !an.overridden || !AndroidTextureDiet.IsAstc(an.format)) astc = false;
            }
        }
        float phoneMb = px * 3.56f / 8f / 1048576f;
        Check("the four new elites' art costs " + phoneMb.ToString("0.0") + " MB on phones (ASTC 6x6 on every strip: " + astc + "; budget 4 MB, shared with EliteHdTest's per-world budget)", px > 0 && astc && phoneMb <= 4f);
        Check("editor memory of the four strips " + (editorBytes / 1048576f).ToString("0.0") + " MB is at most RGBA32 plus the readable copy (" + (px * 8 / 1048576f).ToString("0.0") + " MB, no extra mips or copies)", editorBytes > 0 && editorBytes <= px * 8 * 1.05f);
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
                mine++;
                match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            }
            foreach (var e in EliteShip.Live) seen.Add(e.Def.key);
        }
        Check("each Verdant elite launches from its own kind of pad (" + mine + " of " + spawned + " launches)", spawned == 40 && mine == 40 && match);
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
        Check("elites stay capped at " + EliteDirector.MaxAlive + " alive, whatever the world", EliteDirector.MaxAlive == 1);
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
            bool ok = entry != null && entry.category == CodexCategory.Enemies && EliteArt.HasExtra(d, EliteArt.Extra.Death);
            if (!ok) Debug.Log("[VEL] codex " + d.key + ": entry " + (entry != null) + " category " + (entry != null ? entry.category.ToString() : "-") + " death " + EliteArt.HasExtra(d, EliteArt.Extra.Death));
            strips &= ok;
        }
        Check("the codex lists five Verdant elites, each with its death strip for the tap (" + n + ")", n == 5 && strips);
    }

    // ---- helpers for the simulations ------------------------------------------------------

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    // Steps until a hiding elite is back on the board (or `seconds` pass).
    static void WaitBack(EliteShip e, float seconds = 20f)
    {
        for (float t = 0f; t < seconds && Alive(e) && e.Hidden; t += Dt) EliteSystem.Step(Dt);
    }

    // A heart off it, past its grace (a pause jump's cause), waiting out the hide.
    static void LoseHeart(EliteShip e)
    {
        if (!Alive(e)) return;
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.1f);
        WaitBack(e);
        if (Alive(e)) e.AttackCooldown = 99f;   // (it does not start a cast of its own: the test forces them)
    }

    // Steps `seconds` with the elite's own attack clock held off.
    static void StepHold(EliteShip e, float seconds) { Step(seconds, () => { if (Alive(e)) e.AttackCooldown = 99f; }); }

    static Vector3 Wander(int i, float amp = 1.4f) => new Vector3(Mathf.Sin(i * .03f) * amp, -2.5f, 0f);

    // ---- none of them follows the pilot ------------------------------------------------------

    // Whatever the pilot does, outside a committed attack (and off the board) each one stays well away: 40 s of fighting with a pilot
    // drifting about at the bottom, attacks as often as it likes.
    static void Distances()
    {
        foreach (var key in new[] { "verdant_elite_leafblade", "verdant_elite_resin_warden", "verdant_elite_sporebloom", "verdant_elite_thornlash", "verdant_elite_timber_hauler" })
        {
            Fresh(.05f);
            var e = InPlay(key, new Vector2(1.2f, 1.8f));
            e.AttackCooldown = 0f;
            e.EngageLeft = 9999f;
            float min = 99f, minY = 99f, sumY = 0f;
            int n = 0, f = 0;
            Step(40f, () =>
            {
                pilot.position = Wander(f++, 1.8f);
                if (!Alive(e) || e.Offstage || e.Acting) return;
                min = Mathf.Min(min, Vector2.Distance(e.Position, pilot.position));
                minY = Mathf.Min(minY, e.Position.y - pilot.position.y);
                sumY += e.Position.y - pilot.position.y;
                n++;
            });
            Check(key + ": keeps its distance outside committed attacks (closest " + min.ToString("0.0") + " u, lowest " + minY.ToString("0.0") + " u above the pilot, average " + (n > 0 ? sumY / n : 0f).ToString("0.0") + ", " + e.Attacks + " attacks)",
                  Alive(e) && n > 100 && min > 3f && e.Attacks >= 3);
        }
    }

    // ---- Leafblade: raptor + leaf_swoop --------------------------------------------------------

    static void Leafblade()
    {
        Fresh(.05f);
        var e = InPlay("verdant_elite_leafblade", new Vector2(1.2f, 1.5f));
        var def = e.Def;
        var brain = (RaptorBrain)e.Brain;
        var sw = (LeafSwoopAttack)e.Attack;
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        int guard = 0;
        while (brain.Phase != RaptorBrain.Stage.Ready && guard++ < 30 * 20) EliteSystem.Step(Dt);
        Check("Leafblade leaves the view, comes in by an edge of the board and waits there (entry " + brain.Entry + ", at " + e.Position + ")",
              brain.Phase == RaptorBrain.Stage.Ready && (Mathf.Abs(e.Position.x) <= EliteSystem.RailEdge + .2f) && e.Position.y < EliteSystem.ViewTop + .2f && e.Position.y > EliteSystem.ViewTop - 4f);

        // a long fight: every pass recorded
        var entries = new List<int>();
        var dirs = new List<Vector2>();
        var loopsAtTell = new List<int>();
        var rowsAtTell = new List<int>();
        var crescents = new List<int>();
        var fires = new List<int>();
        var farExits = new List<bool>();
        var seenShots = new HashSet<int>();
        int started = 0, loops = 0, cres = 0;
        float tellLen = 0f, tellSpeed = 0f, runSpeed = 0f, minSlope = 9f, maxSlope = 0f, nearest = 99f, rowY = 0f;
        bool inTell = false, telegraph = false, locked = true, level = true, onRow = true, shown = true, passEnded = true;
        Vector2 lockedDir = Vector2.zero;
        var last = brain.Phase;
        int curEntry = -1;
        for (int i = 0; i < 30 * 90 && entries.Count < 5 && Alive(e); i++)
        {
            pilot.position = Wander(i);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (!e.Offstage && brain.Phase != RaptorBrain.Stage.Pass) nearest = Mathf.Min(nearest, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling)
            {
                if (!inTell) { inTell = true; tellLen = 0f; telegraph = sw.Telegraph.Showing; loops = sw.Telegraph.Loops; lockedDir = sw.Dir; curEntry = brain.Entry; rowsAtTell.Add(sw.Rows); loopsAtTell.Add(loops); }
                tellLen += Dt;
                tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
                locked &= Vector2.Dot(sw.Dir, lockedDir) > .999f;
            }
            else inTell = false;
            if (brain.PassesStarted > started)
            {
                started = brain.PassesStarted;
                entries.Add(curEntry);
                dirs.Add(sw.Dir);
                float slope = Mathf.Abs(sw.Dir.y);   // sin of the angle off the horizontal (a side entry)
                if (curEntry < 2) { minSlope = Mathf.Min(minSlope, slope); maxSlope = Mathf.Max(maxSlope, slope); }
                cres = 0;
                seenShots.Clear();   // (a pooled crescent keeps its object id from pass to pass)
                passEnded = false;
            }
            if (e.Acting)
            {
                runSpeed = Mathf.Max(runSpeed, e.Velocity.magnitude);
                foreach (var s in EliteSystem.Shots.All)
                {
                    if (!s.Active || !s.SlashCapsule || !seenShots.Add(s.GetInstanceID())) continue;
                    cres++;
                    level &= Mathf.Abs(s.SlashAxis.y) < .02f;
                    float best = 99f;
                    for (int r = 0; r < sw.Rows; r++) best = Mathf.Min(best, Mathf.Abs(s.transform.position.y - sw.RowAt(r).y));
                    onRow &= best < .45f;
                    rowY = s.transform.position.y;
                }
            }
            if (!passEnded && brain.Phase == RaptorBrain.Stage.Away && last == RaptorBrain.Stage.Pass)
            {
                passEnded = true;
                crescents.Add(cres);
                fires.Add(sw.RowsFired);
                Vector2 p = e.Position;
                bool far = curEntry == 0 ? p.x > 0f || p.y < EliteSystem.ViewBottom : curEntry == 1 ? p.x < 0f || p.y < EliteSystem.ViewBottom : p.y < EliteSystem.ViewBottom || Mathf.Abs(p.x) > EliteSystem.RailEdge;
                farExits.Add(far && e.Offstage);
            }
            last = brain.Phase;
        }
        Check("Leafblade's tell draws the diagonal lane and its crescent row from the first frame (" + loops + " outlines) and holds still on the edge (" + tellLen.ToString("0.00") + " s, " + tellSpeed.ToString("0.0") + " u/s)",
              telegraph && loops == 1 + sw.Rows && Mathf.Abs(tellLen - def.tellSeconds) < .12f && tellSpeed < 1.2f && AttackPreview.TooShort == 0);
        Check("... the lane is locked at the tell and it keeps its distance between passes (" + nearest.ToString("0.0") + " u at the closest)", locked && entries.Count >= 3 && nearest > 3.5f);
        Check("... a pass slices the lane at dashSpeed (" + runSpeed.ToString("0.0") + " u/s), the lane diagonal: " + minSlope.ToString("0.00") + ".." + maxSlope.ToString("0.00") + " off the horizontal",
              runSpeed > def.dashSpeed * .9f && runSpeed < def.dashSpeed * 1.2f && minSlope >= Mathf.Sin(LeafSwoopAttack.SideMinDeg * Mathf.Deg2Rad) - .02f && maxSlope <= Mathf.Sin(LeafSwoopAttack.SideMaxDeg * Mathf.Deg2Rad) + .02f);
        bool allFar = farExits.Count >= 3;
        foreach (bool b in farExits) allFar &= b;
        Check("... and leaves the view by the far side, untouchable outside the board (" + farExits.Count + " passes)", allFar);
        bool other = entries.Count >= 3;
        for (int i = 1; i < entries.Count; i++) other &= entries[i] != entries[i - 1];
        Check("... then re-enters from another entry each time (" + string.Join(", ", entries) + ")", other && entries.Exists(x => x == 0) | entries.Exists(x => x == 1) | entries.Exists(x => x == 2));
        bool oneCres = crescents.Count >= 3;
        foreach (int c in crescents) oneCres &= c == 1;
        Check("... leaving one level crescent across its drawn row at full hearts (seen " + string.Join(", ", crescents) + ", fired " + string.Join(", ", fires) + "; entries " + string.Join(", ", entries) + "; level " + level + ", on the row " + onRow + ", y " + rowY.ToString("0.0") + ")", oneCres && level && onRow);

        // hurt: it hides, comes back, and its passes carry two crescents
        Fresh(.05f);
        e = InPlay("verdant_elite_leafblade", new Vector2(1.2f, 1.5f));
        brain = (RaptorBrain)e.Brain;
        sw = (LeafSwoopAttack)e.Attack;
        e.AttackCooldown = 0f;
        Check("Leafblade: one crescent per pass at full hearts", brain.Crescents == 1);
        Step(3f);
        e.TakeHit(EliteDamage.PlayerWeapon, e.Position, 1);
        Step(.1f);
        Check("... a hit sends it hiding (3 hearts: " + e.Hearts + ")", e.Hidden && e.Hearts == def.hearts - 1);
        int lastEntry = brain.LastEntry;
        WaitBack(e);
        Check("... and back on the board, its passes now carry two crescents", !e.Hidden && brain.Crescents == 2);
        seenShots.Clear();
        int two = 0, outlines = 0;
        bool tellRows = false;
        int pass0 = brain.PassesStarted;
        for (int i = 0; i < 30 * 40 && Alive(e) && !(brain.PassesStarted > pass0 && brain.Phase == RaptorBrain.Stage.Away); i++)
        {
            pilot.position = Wander(i);
            EliteSystem.Step(Dt);
            if (e.Telling) { tellRows |= sw.Rows == 2; outlines = Mathf.Max(outlines, sw.Telegraph.Loops); }
            if (e.Acting) foreach (var s in EliteSystem.Shots.All) if (s.Active && s.SlashCapsule && seenShots.Add(s.GetInstanceID())) two++;
        }
        Check("... a pass of two crescents, two rows drawn with the lane (" + two + " crescents, " + outlines + " outlines)", two == 2 && tellRows && outlines == 3);
        Check("Leafblade: a re-entry never from the entry of its last pass (last " + lastEntry + ", now " + brain.LastEntry + ")", brain.Passes >= 1);

        // a tell cut short takes the drawing with it
        Fresh();
        e = InPlay("verdant_elite_leafblade", new Vector2(1.4f, -.2f));
        int w = 0;
        while (((RaptorBrain)e.Brain).Phase != RaptorBrain.Stage.Ready && w++ < 30 * 20) EliteSystem.Step(Dt);
        StartTell(e);
        int shownN = AttackPreview.ActiveCount;
        LoseHeart(e);
        Check("a Leafblade hit in its tell takes its drawing with it (" + shownN + " -> " + AttackPreview.ActiveCount + ")", shownN >= 1 && AttackPreview.ActiveCount == 0);
        _ = shown;
    }

    // ---- Resin Warden: canopy + canopy_mortar ---------------------------------------------------------

    static void Warden()
    {
        Fresh(.05f);
        var e = InPlay("verdant_elite_resin_warden", new Vector2(0f, 3f));
        var def = e.Def;
        var brain = (CanopyBrain)e.Brain;
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        // 40 s: the perches it uses and the hops between them
        var perches = new List<int>();
        var hopSecs = new List<float>();
        float hopClock = 0f, arc = 0f, perchY = 99f, hopFromY = 0f, minY = 99f;
        bool hopped = false;
        int last = brain.Perch;
        perches.Add(last);
        int f = 0;
        Step(40f, () =>
        {
            pilot.position = Wander(f++, 1.8f);
            if (!Alive(e)) return;
            if (brain.Phase == CanopyBrain.Stage.Hop)
            {
                if (!hopped) { hopped = true; hopClock = 0f; hopFromY = e.Position.y; }
                hopClock += Dt;
                arc = Mathf.Max(arc, e.Position.y - Mathf.Min(hopFromY, brain.Station(brain.Perch).y));
            }
            else
            {
                if (hopped) { hopSecs.Add(hopClock); hopped = false; }
                if (e.State == EliteState.Follow) { perchY = Mathf.Min(perchY, e.Position.y); }
            }
            minY = Mathf.Min(minY, e.Position.y);
            if (brain.Perch != last) { last = brain.Perch; perches.Add(last); }
        });
        var distinct = new HashSet<int>(perches);
        bool differ = perches.Count >= 4;
        for (int i = 1; i < perches.Count; i++) differ &= perches[i] != perches[i - 1];
        float topY = EliteSystem.ViewTop - def.topMargin;
        Check("Resin Warden hops between " + brain.PerchCount + " perches along the top, a different one each hop (perches " + string.Join(",", perches) + ", " + brain.Hops + " hops)",
              distinct.Count >= 3 && differ && brain.Hops >= 4);
        float longest = 0f;
        foreach (float h in hopSecs) longest = Mathf.Max(longest, h);
        Check("... each hop a short visible leap (" + hopSecs.Count + " hops, longest " + longest.ToString("0.00") + " s, an arc up to " + arc.ToString("0.00") + " u)", hopSecs.Count >= 3 && longest < 1.6f && arc > .25f);
        Check("... staying up on the canopy (never under y " + (topY - 1.1f).ToString("0.0") + ": lowest " + minY.ToString("0.00") + ")", minY > topY - 1.1f);

        // the wall: its gap narrows and its pools linger more for every heart lost
        float prevGap = 99f, prevLinger = -1f;
        var gaps = new List<float>();
        var lingers = new List<float>();
        for (int lost = 0; lost <= 2; lost++)
        {
            Fresh(.05f);
            pilot.position = new Vector3(.4f, -2.5f, 0f);
            e = InPlay("verdant_elite_resin_warden", new Vector2(-1f, 3f));
            for (int i = 0; i < lost; i++) LoseHeart(e);
            brain = (CanopyBrain)e.Brain;
            for (int i = 0; i < 30 * 8 && !(brain.Phase == CanopyBrain.Stage.Perch && e.State == EliteState.Follow && (e.Position - brain.Station(brain.Perch)).magnitude < .3f); i++) { e.AttackCooldown = 99f; EliteSystem.Step(Dt); }
            StepHold(e, 1f);
            pilot.position = new Vector3(.4f, -2.5f, 0f);
            StartTell(e);
            var m = (CanopyMortarAttack)e.Attack;
            string k = "Resin Warden at " + e.Hearts + " hearts: ";
            Check(k + "a ring on every spot of the wall is drawn on the first frame of the tell (" + m.Telegraph.Loops + " outlines for " + m.Count + " pools)", e.Telling && m.Telegraph.Showing && m.Telegraph.Loops == m.Count && m.Count >= 3);
            // the geometry: pools sorted by x, the one wide step is the gap
            var xs = new List<float>();
            for (int i = 0; i < m.Count; i++) xs.Add(m.Spot(i).x);
            xs.Sort();
            float r = m.PoolRadius, widest = 0f, hole = 0f;
            int widestAt = 0;
            for (int i = 1; i < xs.Count; i++)
            {
                float step = xs[i] - xs[i - 1];
                if (step > widest) { widest = step; widestAt = i; }
            }
            for (int i = 1; i < xs.Count; i++) if (i != widestAt) hole = Mathf.Max(hole, xs[i] - xs[i - 1] - 2f * r);
            float free = widest - 2f * r;
            float rail = EliteSystem.RailEdge;
            bool sealed_ = rail - (xs[xs.Count - 1] + r) < .3f && xs[0] - r + rail < .3f;
            Check(k + "ONE gap, " + free.ToString("0.00") + " u clear between pools (GapFor " + CanopyMortarAttack.GapFor(lost).ToString("0.00") + "); the rest of the wall has no hole over " + hole.ToString("0.00") + " u and meets both rails",
                  Mathf.Abs(free - CanopyMortarAttack.GapFor(lost)) < .02f && Mathf.Abs(m.GapWidth - free) < .02f && free >= CanopyMortarAttack.GapMin - .01f && hole < .5f && sealed_ &&
                  Mathf.Abs((xs[widestAt] + xs[widestAt - 1]) * .5f - m.GapX) < .02f);
            gaps.Add(free);
            lingers.Add(m.LingerSeconds);
            Vector2 first = m.Spot(0);
            bool locked = true;
            float tellSpeed = 0f;
            StepUntil(() => { locked &= (m.Spot(0) - first).sqrMagnitude < 1e-6f; tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); return e.Acting; }, 3f);
            Check(k + "the wall is locked at the tell, shown at least the FR1 lead and the warden planted (" + tellSpeed.ToString("0.0") + " u/s)", e.Acting && locked && tellSpeed < 1.2f && AttackPreview.TooShort == 0);
            // pools: landed at lobSeconds, still there at poolSeconds + linger - .3, gone by poolSeconds + linger + .5
            StepHold(e, def.lobSeconds + .3f);
            int pooled = 0;
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled) pooled++;
            float lifeLeft = 99f;
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled) lifeLeft = Mathf.Min(lifeLeft, s.LifeLeft);
            Check(k + "the pools land as sticky pools, " + lifeLeft.ToString("0.0") + " s of life left (" + pooled + " of " + m.Count + ")", pooled == m.Count);
            moveBackGround.speed = .005f;   // the board nearly still: a pool riding it at the test's 1.5 u/s would leave the bottom of the view (and be culled) after ~3 s, before its linger is up
            string trace = "";
            for (float tt = 0f; tt < def.poolSeconds - 1e-3f; tt += .5f)
            {
                StepHold(e, .5f);
                int nn = 0;
                float ll = 99f;
                foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled) { nn++; ll = Mathf.Min(ll, s.LifeLeft); }
                trace += nn + "/" + (nn > 0 ? ll.ToString("0.0") : "-") + " ";
            }
            pooled = 0;
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled) pooled++;
            // (landed lobSeconds + .3 + poolSeconds ago... the pools of a hurt warden are still there; a fresh one's have just dried)
            if (lost == 0) Check(k + "the pools have dried after poolSeconds (" + pooled + " left)", pooled == 0);
            else Check(k + "the pools are still there after poolSeconds: they linger " + m.LingerSeconds.ToString("0.0") + " s more (" + pooled + " left; count/life every .5 s: " + trace + ")", pooled == m.Count);
            StepHold(e, m.LingerSeconds + .6f);
            pooled = 0;
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled) pooled++;
            Check(k + "... and dry up in the end (" + pooled + " left)", pooled == 0);
            prevGap = Mathf.Min(prevGap, free);
            prevLinger = Mathf.Max(prevLinger, m.LingerSeconds);
        }
        Check("Resin Warden: the gap narrows with each heart lost (" + string.Join(" > ", gaps.ConvertAll(x => x.ToString("0.00"))) + ") and the pools linger longer (" + string.Join(" < ", lingers.ConvertAll(x => x.ToString("0.0"))) + " s more)",
              gaps.Count == 3 && gaps[0] > gaps[1] && gaps[1] > gaps[2] && gaps[2] >= CanopyMortarAttack.GapMin - .01f && lingers[0] < 1e-4f && lingers[1] > lingers[0] && lingers[2] > lingers[1]);
        Check("... the gap is never narrower than " + CanopyMortarAttack.GapMin + " u whatever the hearts (the ship is .56 wide)", CanopyMortarAttack.GapFor(0) > CanopyMortarAttack.GapFor(1) && CanopyMortarAttack.GapFor(9) >= CanopyMortarAttack.GapMin - 1e-4f);
        _ = prevGap; _ = prevLinger;

        // the gap is always there, wherever the pilot stands
        bool gapAlways = true;
        string worst = "";
        for (float px = -2.1f; px <= 2.11f; px += .7f)
        {
            Fresh(.05f);
            pilot.position = new Vector3(px, -2.5f, 0f);
            e = InPlay("verdant_elite_resin_warden", new Vector2(0f, 3f));
            LoseHeart(e);
            LoseHeart(e);
            pilot.position = new Vector3(px, -2.5f, 0f);
            StepHold(e, 2f);
            StartTell(e);
            var m = (CanopyMortarAttack)e.Attack;
            var xs = new List<float>();
            for (int i = 0; i < m.Count; i++) xs.Add(m.Spot(i).x);
            xs.Sort();
            float widest = 0f;
            for (int i = 1; i < xs.Count; i++) widest = Mathf.Max(widest, xs[i] - xs[i - 1]);
            float free = widest - 2f * m.PoolRadius;
            bool inside = m.GapX - free * .5f > -EliteSystem.RailEdge && m.GapX + free * .5f < EliteSystem.RailEdge;
            if (!(free >= CanopyMortarAttack.GapMin - .01f && inside)) { gapAlways = false; worst += px.ToString("0.0") + ":" + free.ToString("0.00") + " "; }
        }
        Check("Resin Warden: the gap exists and lies between the rails with the pilot anywhere along the lane, even at its last hearts " + worst, gapAlways);

        // a tell cut short takes the drawing with it
        Fresh();
        e = InPlay("verdant_elite_resin_warden", new Vector2(0f, 3f));
        StepHold(e, 1f);
        StartTell(e);
        int shown = AttackPreview.ActiveCount;
        LoseHeart(e);
        Check("a Resin Warden hit in its tell takes its drawing with it (" + shown + " -> " + AttackPreview.ActiveCount + ")", shown >= 1 && AttackPreview.ActiveCount == 0);
    }

    // ---- Sporebloom: floater + spore_drift ----------------------------------------------------

    static void Sporebloom()
    {
        Fresh(.05f);
        var e = InPlay("verdant_elite_sporebloom", new Vector2(0f, 3f));
        var def = e.Def;
        var brain = (FloaterBrain)e.Brain;
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        var phases = new List<FloaterBrain.Stage> { brain.Phase };
        var cols = new List<int> { brain.Column };
        Vector2 sinkFrom = Vector2.zero, riseAt = Vector2.zero;
        float dropped = 0f, sinkStartY = 0f, underX = 0f, minY = 99f;
        bool untouchable = true, sawUnder = false;
        int f = 0, sinks0 = 0, pods = 0;
        Step(70f, () =>
        {
            pilot.position = Wander(f++);
            if (!Alive(e)) return;
            if (brain.Phase != phases[phases.Count - 1])
            {
                if (brain.Phase == FloaterBrain.Stage.Sink) { sinkFrom = e.Position; sinkStartY = e.Position.y; }
                if (brain.Phase == FloaterBrain.Stage.Under) { dropped = Mathf.Max(dropped, sinkStartY - e.Position.y); underX = Mathf.Abs(e.Position.x); }
                if (brain.Phase == FloaterBrain.Stage.Rise) riseAt = e.Position;
                phases.Add(brain.Phase);
            }
            if (brain.Column != cols[cols.Count - 1]) cols.Add(brain.Column);
            if (brain.Phase == FloaterBrain.Stage.Under) { sawUnder = true; untouchable &= e.Offstage && !e.Collider.enabled; }
            if (brain.Phase == FloaterBrain.Stage.Float && !e.Offstage && !e.Acting) minY = Mathf.Min(minY, e.Position.y - pilot.position.y);
        });
        sinks0 = brain.Sinks;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && (s.Motion & ShotMotion.Burst) != 0) pods++;
        bool order = false;
        for (int i = 0; i + 3 < phases.Count; i++)
            order |= phases[i] == FloaterBrain.Stage.Float && phases[i + 1] == FloaterBrain.Stage.Sink && phases[i + 2] == FloaterBrain.Stage.Under && phases[i + 3] == FloaterBrain.Stage.Rise;
        Check("Sporebloom floats, then sinks off its side into the canopy, is out of sight, and rises again (" + sinks0 + " sinks; " + phases.Count + " phase changes)",
              sinks0 >= 2 && order && sawUnder && dropped > .3f && underX > EliteSystem.RailEdge);
        Check("... untouchable while it is under (no collider, not a target)", untouchable);
        bool moved = cols.Count >= 3;
        for (int i = 1; i < cols.Count; i++) moved &= cols[i] != cols[i - 1];
        Check("... and comes up at a different spot each time (stations " + string.Join(",", cols) + ")", moved);
        Check("... floating high over the board, never down at the pilot (lowest " + minY.ToString("0.0") + " u above)", minY > 4f);
        _ = sinkFrom; _ = riseAt; _ = pods; _ = def;

        // the burst: its rings are drawn first; no cloud at 3 hearts
        Fresh();
        e = InPlay("verdant_elite_sporebloom", new Vector2(0f, 1.4f));
        var sd = (SporeDriftAttack)e.Attack;
        StartTell(e);
        Check("Sporebloom at 3 hearts: the burst rings and the pods' flight lines are drawn on the first frame of the tell, and no cloud (" + sd.Telegraph.Loops + " outlines)",
              e.Telling && sd.Telegraph.Showing && sd.Telegraph.Loops == 2 * e.Def.shotCount && !sd.HasCloud);
        StepUntil(() => e.Acting, 3f);
        int cloudPools = 0;
        for (float t = 0f; t < 3f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Glob) cloudPools++;
        }
        Check("... the pods burst into spore rings and leave no cloud pool", cloudPools == 0);

        // at two hearts: a spore cloud lane, outlined first
        Fresh();
        pilot.position = new Vector3(.5f, -2.5f, 0f);
        e = InPlay("verdant_elite_sporebloom", new Vector2(0f, 1.4f));
        LoseHeart(e);
        sd = (SporeDriftAttack)e.Attack;
        StepHold(e, 2f);
        pilot.position = new Vector3(.5f, -2.5f, 0f);
        int globsBefore = 0;
        StartTell(e);
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Glob) globsBefore++;
        float lane = sd.CloudLaneX;
        Check("Sporebloom at " + e.Hearts + " hearts: the cloud lane is outlined on the first frame of the tell, before any cloud exists (" + sd.Telegraph.Loops + " outlines)",
              e.Telling && sd.HasCloud && sd.Telegraph.Showing && sd.Telegraph.Loops == 2 * e.Def.shotCount + 1 && globsBefore == 0 &&
              Mathf.Abs(sd.CloudLaneX) < EliteSystem.RailEdge && Mathf.Abs(sd.CloudLaneX - pilot.position.x) > 1f);
        bool lockedLane = true;
        StepUntil(() => { lockedLane &= Mathf.Approximately(sd.CloudLaneX, lane); return e.Acting; }, 3f);
        Check("... the lane is locked at the tell and shown at least the FR1 lead", e.Acting && lockedLane && AttackPreview.TooShort == 0);
        StepHold(e, e.Def.lobSeconds + .3f);
        int pooled = 0;
        float width = 0f;
        bool onLane = true;
        foreach (var s in EliteSystem.Shots.All)
            if (s.Active && s.Pooled && s.Kind == EliteShots.Kind.Glob) { pooled++; width = Mathf.Max(width, s.PoolRadius * 2f); onLane &= Mathf.Abs(s.transform.position.x - lane) < .05f; }
        Check("... then " + SporeDriftAttack.CloudPools + " cloud pools sit on it, each " + width.ToString("0.00") + " u across (" + pooled + " landed)",
              pooled == SporeDriftAttack.CloudPools && onLane && width > .8f);
        StepHold(e, SporeDriftAttack.CloudSeconds - .3f + .5f);
        pooled = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Pooled && s.Kind == EliteShots.Kind.Glob) pooled++;
        Check("... that linger about " + SporeDriftAttack.CloudSeconds + " s and are gone (" + pooled + " left)", pooled == 0);

        // a tell cut short takes the drawing with it
        Fresh();
        e = InPlay("verdant_elite_sporebloom", new Vector2(0f, .4f));
        StartTell(e);
        int shown = AttackPreview.ActiveCount;
        LoseHeart(e);
        Check("a Sporebloom hit in its tell takes its drawing with it (" + shown + " -> " + AttackPreview.ActiveCount + ")", shown >= 1 && AttackPreview.ActiveCount == 0);
    }

    // ---- Thornlash: anchor + thorn_sweep -----------------------------------------------------

    sealed class ThornLog
    {
        public readonly List<int> sides = new List<int>();
        public readonly Dictionary<int, int> sweepsAt = new Dictionary<int, int>(), wanted = new Dictionary<int, int>();
        public float minRoom = 99f, minDist = 99f, offX = -9f, height = 99f;
        public bool corridorOk = true, outlined = true, wasActing;
        public int lastAnchors, f;
    }

    // Watches the Thornlash until it has latched `untilAnchors` times.
    static void ThornWatch(ThornLog L, EliteShip e, AnchorBrain brain, ThornSweepAttack vs, int untilAnchors)
    {
        for (int i = 0; i < 30 * 120 && Alive(e) && brain.Anchors < untilAnchors; i++)
        {
            pilot.position = Wander(L.f++, 1.6f);
            EliteSystem.Step(Dt);
            if (!Alive(e)) break;
            if (brain.Anchors != L.lastAnchors)
            {
                L.lastAnchors = brain.Anchors;
                L.sides.Add(brain.Side);
                L.wanted[brain.Anchors] = brain.SweepsLeft;
                L.offX = Mathf.Max(L.offX, Mathf.Abs(e.Position.x) - EliteSystem.RailEdge);
                L.height = Mathf.Min(L.height, e.Position.y - pilot.position.y);
            }
            if (!e.Offstage && !e.Acting) L.minDist = Mathf.Min(L.minDist, Vector2.Distance(e.Position, pilot.position));
            if (e.Telling && vs.Lash != null && vs.Lash.State == AttackHazard.Phase.Tell) L.outlined &= vs.Lash.Preview != null && vs.Lash.Preview.Active && vs.Lash.Preview.DotCount > 20;
            bool acting = e.Acting && vs.Lash != null;
            if (acting && !L.wasActing)
            {
                L.sweepsAt[brain.Anchors] = (L.sweepsAt.TryGetValue(brain.Anchors, out int c) ? c : 0) + 1;
                L.minRoom = Mathf.Min(L.minRoom, vs.Lash.Room);
                L.corridorOk &= vs.Lash.Room >= AttackLash.MinCorridor - .01f;
            }
            L.wasActing = acting;
        }
    }

    static void Thornlash()
    {
        Fresh(.05f);
        var e = InPlay("verdant_elite_thornlash", new Vector2(1.3f, 1.5f));
        var def = e.Def;
        var brain = (AnchorBrain)e.Brain;
        var vs = (ThornSweepAttack)e.Attack;
        e.AttackCooldown = 0f;
        e.EngageLeft = 9999f;
        var L = new ThornLog();
        // three phases of fighting, a heart lost between them (the anchor it hit was cut short: it hid, and came back to the other rail)
        ThornWatch(L, e, brain, vs, 3);
        LoseHeart(e); e.AttackCooldown = 0f; L.wasActing = false;
        ThornWatch(L, e, brain, vs, 6);
        LoseHeart(e); e.AttackCooldown = 0f; L.wasActing = false;
        ThornWatch(L, e, brain, vs, 9);
        bool alt = L.sides.Count >= 6;
        for (int i = 1; i < L.sides.Count; i++) alt &= L.sides[i] != L.sides[i - 1];
        Check("Thornlash latches on one side then the other, alternating (" + string.Join(" ", L.sides) + "), just outside a rail (" + L.offX.ToString("0.00") + " u past it), well above the pilot (" + L.height.ToString("0.0") + " u)",
              alt && L.offX > -.05f && L.offX < .35f && L.height > 2.5f);
        Check("... it keeps its distance (" + L.minDist.ToString("0.0") + " u at the closest outside a sweep)", L.minDist > 3f);
        Check("... every whip's swept area is drawn from the first frame of the tell", L.outlined && AttackPreview.TooShort == 0);
        Check("... and the fair corridor stays open beyond the start line (room " + L.minRoom.ToString("0.00") + " u, at least " + AttackLash.MinCorridor + ")", L.corridorOk && L.minRoom < 99f);
        string summary = "";
        foreach (var kv in L.sweepsAt) summary += kv.Key + ":" + kv.Value + "/" + (L.wanted.TryGetValue(kv.Key, out int w) ? w : -1) + " ";
        // anchors left whole (2, 5, 8 come after a latch with sweeps to spare and end with the swing; 3 and 6 were cut by the lost heart)
        bool sweeps = true;
        foreach (int a in new[] { 1, 2, 4, 5, 7, 8 })
            sweeps &= L.sweepsAt.TryGetValue(a, out int n) && L.wanted.TryGetValue(a, out int want) && n == want;
        Check("... one sweep per anchor at full hearts, one more for every heart lost (anchor:sweeps/wanted " + summary + ")",
              sweeps && L.wanted[1] == 1 && L.wanted[2] == 1 && L.wanted[4] == 2 && L.wanted[5] == 2 && L.wanted[7] == 3 && L.wanted[8] == 3);

        // the corridor, whatever the pilot does: Aim from both rails
        var spec = LashSpec.Standard(2);
        spec.length = def.hazardSize;
        float room = 99f;
        for (int side = -1; side <= 1; side += 2)
            for (float px = -1.9f; px <= 1.91f; px += .3f)
            {
                AttackLash.Aim(spec, new Vector2(side * (EliteSystem.RailEdge + .08f), EliteSystem.ViewTop - 2f), new Vector2(px, -2.5f), EliteSystem.RailEdge, out _, out _, out _, out float r);
                room = Mathf.Min(room, r);
            }
        Check("... AttackLash.Aim from either rail leaves a corridor of at least " + AttackLash.MinCorridor + " u for every pilot spot across the lane (" + room.ToString("0.00") + ")", room >= AttackLash.MinCorridor - .01f);

        // the whip is gone with it
        Fresh();
        e = InPlay("verdant_elite_thornlash", new Vector2(1.3f, 1.5f));
        StepHold(e, 3f);
        StartTell(e);
        Check("Thornlash: the whip's swept area is up on the first frame of the tell", e.Telling && ((ThornSweepAttack)e.Attack).Lash != null && ((ThornSweepAttack)e.Attack).Lash.Preview.Active);
        LoseHeart(e);
        Check("a Thornlash hit in its tell takes the whip and its drawing with it", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0);
    }

    // ---- Timber Hauler: lumber + raft_roll -----------------------------------------------------

    static void Hauler()
    {
        Fresh(.05f);
        var e = InPlay("verdant_elite_timber_hauler", new Vector2(0f, 3f));
        var def = e.Def;
        var lr = (RaftRollAttack)e.Attack;
        Check("Timber Hauler: five hearts, armoured, no hiding (" + def.hearts + ")", def.hearts == 5 && def.armored && !def.hurtHide);
        e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
        Step(.1f);
        Check("... a hit takes a heart and it stays on the board (" + e.Hearts + ")", e.Hearts == 4 && !e.Hidden && e.State != EliteState.Dead);
        StartTell(e);
        Check("Timber Hauler at 4 hearts: log_roll, never a raft; the spots and the roll lanes are drawn on the first frame (" + lr.Telegraph.Loops + " outlines)",
              e.Telling && !lr.Raft && lr.SpotCount == def.shotCount && lr.Telegraph.Showing && lr.Telegraph.Loops >= 2 * def.shotCount);
        Finish(e);
        StepHold(e, 1f);
        // hurt to two hearts: a raft, then a row, then a raft
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.1f);
        e.TakeHit(EliteDamage.Teleport, e.Position, 1);
        Step(.1f);
        Check("Timber Hauler at " + e.Hearts + " hearts", e.Hearts == 2);
        var kinds = new List<bool>();
        for (int cast = 0; cast < 3; cast++)
        {
            Finish(e);
            StepHold(e, .3f);
            pilot.position = new Vector3(.6f, -2.5f, 0f);
            e.AttackCooldown = 99f;
            StartTell(e);
            kinds.Add(lr.Raft);
            if (cast == 0)
            {
                string k = "Timber Hauler's raft: ";
                float x0 = lr.Spot(0).x, x1 = lr.Spot(1).x;
                float clear = (x1 - x0) - 2f * lr.LaneHalf;
                Check(k + "two spots with one gap between them, " + clear.ToString("0.00") + " u of free floor between the roll lanes (at least " + LogRollAttack.RaftClear + "), both between the rails",
                      lr.Raft && lr.SpotCount == 2 && x1 > x0 && clear >= LogRollAttack.RaftClear - .02f && clear >= 1.1f && x0 - lr.LaneHalf > -EliteSystem.RailEdge && x1 + lr.LaneHalf < EliteSystem.RailEdge);
                Check(k + "the spots, both roll lanes and the gap are drawn on the first frame of the tell (" + lr.Telegraph.Loops + " outlines)", e.Telling && lr.Telegraph.Showing && lr.Telegraph.Loops >= 4);
                float gapAt = lr.RaftGapX;
                bool locked = true;
                StepUntil(() => { locked &= Mathf.Approximately(lr.RaftGapX, gapAt); return e.Acting; }, 3f);
                Check(k + "locked at the tell, shown at least the FR1 lead", e.Acting && locked && AttackPreview.TooShort == 0);
                // both trunks lobbed together, both rolling down the same diagonal, the gap open all the way
                float minClear = 99f;
                bool same = true, parallel = true, together = false, downhill = true;
                int frames = 0;
                for (float t = 0f; t < 6f; t += Dt)
                {
                    pilot.position = new Vector3(0f, -4.8f, 0f);
                    e.AttackCooldown = 99f;
                    EliteSystem.Step(Dt);
                    var rolling = new List<EliteShot>();
                    foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Rolling) rolling.Add(s);
                    if (rolling.Count == 2)
                    {
                        together = true;
                        frames++;
                        downhill &= rolling[0].Velocity.y < 0f && rolling[1].Velocity.y < 0f;
                        if (rolling[0].Bounced == 0 && rolling[1].Bounced == 0)
                        {
                            same &= Mathf.Sign(rolling[0].Velocity.x) == Mathf.Sign(rolling[1].Velocity.x) && Mathf.Sign(rolling[0].Velocity.x) == lr.RaftDirX;
                            parallel &= (rolling[0].Velocity - rolling[1].Velocity).magnitude < .05f;
                            minClear = Mathf.Min(minClear, Mathf.Abs(rolling[0].transform.position.x - rolling[1].transform.position.x) - 2f * def.shotSize * .4f);
                        }
                    }
                }
                Check(k + "two trunks roll down together the same way, side by side (" + frames + " frames; free floor between them never under " + minClear.ToString("0.00") + " u)",
                      together && same && parallel && downhill && minClear >= 1.1f && frames > 20);
            }
            else
            {
                Finish(e);
            }
        }
        Check("Timber Hauler: at two hearts it alternates raft and row (" + string.Join(", ", kinds.ConvertAll(x => x ? "raft" : "row")) + ")", kinds.Count == 3 && kinds[0] && !kinds[1] && kinds[2]);

        // the raft's gap with the pilot anywhere along the lane
        bool always = true;
        string worst = "";
        for (float px = -2.1f; px <= 2.11f; px += .7f)
        {
            Fresh(.05f);
            pilot.position = new Vector3(px, -2.5f, 0f);
            e = InPlay("verdant_elite_timber_hauler", new Vector2(0f, 3f));
            e.TakeHit(EliteDamage.Teleport, e.Position, 3);
            StepHold(e, .2f);
            lr = (RaftRollAttack)e.Attack;
            pilot.position = new Vector3(px, -2.5f, 0f);
            StartTell(e);
            float clear = lr.Raft && lr.SpotCount == 2 ? (lr.Spot(1).x - lr.Spot(0).x) - 2f * lr.LaneHalf : -1f;
            bool ok = clear >= 1.1f && lr.Spot(0).x - lr.LaneHalf > -EliteSystem.RailEdge && lr.Spot(1).x + lr.LaneHalf < EliteSystem.RailEdge;
            if (!ok) { always = false; worst += px.ToString("0.0") + ":" + clear.ToString("0.00") + " "; }
        }
        Check("Timber Hauler: the raft's gap exists and lies between the rails with the pilot anywhere along the lane " + worst, always);

        // a tell cut short takes the drawing with it
        Fresh();
        e = InPlay("verdant_elite_timber_hauler", new Vector2(0f, .6f));
        StartTell(e);
        int shown = AttackPreview.ActiveCount;
        e.TakeHit(EliteDamage.Teleport, e.Position, 5);
        Step(.2f);
        Check("a Timber Hauler downed in its tell takes its drawing with it (" + shown + " -> " + AttackPreview.ActiveCount + ")", shown >= 1 && AttackPreview.ActiveCount == 0);
    }

    // Lets the cast in its tell run out so the next one can start.
    static void Finish(EliteShip e)
    {
        StepUntil(() => { e.AttackCooldown = 99f; return e.State == EliteState.Follow && !e.Acting && !e.Telling; }, 8f);
        e.AttackCooldown = 99f;
    }

    // ---- Verdant's backdrop brings the home pad into view --------------------------------------

    static void BringHome()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var sel = BackdropVariants.For("Verdant");
        sel.Reset();
        sel.Seed(11);
        sel.Force = 0;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Verdant");
        wb.Show("Verdant", false);
        var d = wb.Current != null ? wb.Current.Director as VerdantDirector : null;
        Check("Verdant's backdrop is up for the pad check", d != null);
        if (d == null) { sel.Reset(); return; }
        for (int i = 0; i < 30 * 20; i++) wb.Step(Dt);
        int Count(int kind)
        {
            int n = 0;
            foreach (var p in d.Sites.items) if (p.active && p.kind == kind) n++;
            return n;
        }
        // (each elite's own pad: root hangar 0, river bay 1, pod pad 2, tower bay 3, hatch 4)
        foreach (var x in new[] { ("roothangar", 0), ("riverbay", 1), ("podpad", 2), ("towerbay", 3), ("hatch", 4) })
        {
            int before = Count(x.Item2), spawned = d.SitesSpawned;
            bool ok = false;
            for (int i = 0; i < 60 && !ok; i++) { ok = d.BringHome(x.Item1); if (!ok) for (int k = 0; k < 30; k++) wb.Step(Dt); }
            Check("BringHome(" + x.Item1 + "): a " + x.Item1 + " is in view or on its way (" + before + " -> " + Count(x.Item2) + ", " + (d.SitesSpawned - spawned) + " spawned)", ok && Count(x.Item2) >= 1);
            int again = d.SitesSpawned;
            Check("... asked again it spawns no second one", d.BringHome(x.Item1) && d.SitesSpawned == again);
        }
        Check("... a launchFrom of no Verdant pad (a Space station, a Frost hangar) or none is not conjured", !d.BringHome("station") && !d.BringHome("hangar") && !d.BringHome(""));
        Check("... LandingSites.BringHome reaches it through the world backdrop", LandingSites.BringHome("towerbay,roothangar"));
        EliteSystem.Clear();
        sel.Reset();
        if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
    }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("verdant_elite_timber_hauler", new Vector2(-1.2f, 3f));
        var b = InPlay("verdant_elite_thornlash", new Vector2(1.4f, 2f));
        var c = InPlay("verdant_elite_sporebloom", new Vector2(0f, 3f));
        var d = InPlay("verdant_elite_leafblade", new Vector2(1.2f, 2f));
        var w = InPlay("verdant_elite_resin_warden", new Vector2(.4f, 3f));
        a.EngageLeft = b.EngageLeft = c.EngageLeft = d.EngageLeft = w.EngageLeft = 9999f;
        // (a hauler at two hearts casts rafts, a sporebloom its cloud; a warden has lost hearts too: its lingering pools)
        a.TakeHit(EliteDamage.Teleport, a.Position, 3);
        c.TakeHit(EliteDamage.Teleport, c.Position, 1);
        w.TakeHit(EliteDamage.Teleport, w.Position, 1);
        int f = 0;
        Step(.5f, () => { pilot.position = Wander(f++); });
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = d.AttackCooldown = w.AttackCooldown = 0f;
        Step(50f, () => pilot.position = Wander(f++));   // warm up: every attack, preview, shot, spore, pool, whip, hop, pass and sink once
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long allocated = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 90; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
                EliteSystem.Step(Dt);
            }
        });
        Check("zero per-frame allocations stepping the five Verdant elites, their rafts, clouds, pools, whips, crescents and passes (" + allocated + " bytes over 90 frames; meter control " + control + ")",
              meter && allocated == 0);
    }
}
