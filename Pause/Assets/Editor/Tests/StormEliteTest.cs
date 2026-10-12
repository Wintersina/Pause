using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Storm's five elites (Skyhook Harrier, Refinery Tender, Rod Dancer, Anvil Cloud, Static Bastion): the roster, their strips (7 flight cells
// 1344 x 192, 3 death cells 576 x 192), muzzles and nozzles on the hull, the flight cell map and launch / lift-off cells, the director's
// rotation, the codex entries and each one's brain and attack in simulation (all composed from the older brains and attacks with Storm's
// own numbers and material: ion bolts, hail canisters, arc fans, a lightning shell, a curtain of arcs). Nothing allocates per frame.
//
// Storm's backdrop is still Tide's stand-in director (BackdropSet), so the elites carry no launchFrom: they take any free pad of the
// stand-in's. A real Storm director will name its own LandingKinds later.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites StormEliteTest
public static class StormEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SEL] PASS  " : "[SEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    static readonly (string key, string brain, string attack)[] Expected =
    {
        ("storm_elite_skyhook_harrier", "interceptor", "lance_dash"),
        ("storm_elite_refinery_tender", "hauler", "slag_drop"),
        ("storm_elite_rod_dancer", "skirmisher", "blink_shards"),
        ("storm_elite_anvil_cloud", "siege", "siege_cannon"),
        ("storm_elite_static_bastion", "bastion", "ward_curtain"),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Roster();
            FlightCells();
            Sites();
            Rotation();
            Codex();
            Harrier();
            Tender();
            Dancer();
            Anvil();
            Bastion();
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
        Debug.Log("[SEL] failures: " + fails);
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
        var storm = new List<EliteDef>();
        Check("Storm has five elites (" + EliteCatalog.ForWorld(5, storm) + ")", storm.Count == 5);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        var shots = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Storm with its own brain / attack inside the world (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 5 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            shots.Add(d.shotKind + "/" + d.shotCount + "/" + d.shotSpread + "/" + d.shotSpeed);
            Check(x.key + ": no launchFrom (any pad of the stand-in backdrop), codex id elite_storm_*", string.IsNullOrEmpty(d.launchFrom) && d.codexId.StartsWith("elite_storm_"));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the 1344 x 192 flight strip slices into 7 cells of 192 and the cell map is the flight layout",
                  frames != null && frames.Length == 7 && Mathf.Approximately(frames[0].rect.width, 192f) && Mathf.Approximately(frames[0].rect.height, 192f) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0 && d.cellPixels == 192);
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the 576 x 192 death strip slices into 3 cells of 192 (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length == 3 && Mathf.Approximately(death[0].rect.width, 192f));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Storm/" + d.key + ".png"));
            bool solid = true, inCell = true;
            string bad = "";
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > 8 && p.x < 184 && p.y > 8 && p.y < 184; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > 8 && p.x < 184 && p.y > 8 && p.y < 184; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": two hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts == 2 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned like its siblings (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .7f && d.avoidance >= .4f && d.avoidance <= .9f);
            Check(x.key + ": hearts and shots are in the Storm white / cold-blue family, never red or violet (hearts " + ColorUtility.ToHtmlStringRGB(d.HeartColor) + ")",
                  Hue(d.HeartColor) > 40f && Hue(d.HeartColor) < 240f && ((Hue(d.ShotColor) > 150f && Hue(d.ShotColor) < 240f) || (Hue(d.ShotColor) > 40f && Hue(d.ShotColor) < 70f)));
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
        Check("each Storm elite shoots its own pattern (" + shots.Count + " distinct kind / count / spread / speed)", shots.Count == 5);
    }

    static float Hue(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return h * 360f; }

    // Parked -> engines -> lift-off -> flight -> banks -> damaged: every Storm strip walks the flight layout.
    static void FlightCells()
    {
        foreach (var x in Expected)
        {
            Fresh(.05f);
            var def = EliteCatalog.Find(x.key);
            var site = Pad(new Vector2(.6f, 3f), LandingKind.Ground, 1);
            var e = EliteShip.Create(def, site, 2.5f, new Vector2(-1.5f, 1.5f));
            Check(x.key + ": parked on the landed cell", e.State == EliteState.Parked && e.CurrentFrame == def.cells.parked);
            var seen = new HashSet<int>();
            for (float t = 0f; t < 14f && e.State != EliteState.Follow; t += Dt) { EliteSystem.Step(Dt); seen.Add(e.CurrentFrame); }
            Check(x.key + ": the lift-off cell is shown on the way up (" + string.Join(",", seen) + ")", seen.Contains(def.cells.liftoff) || seen.Contains(def.cells.parkedIdle));
            e.AttackCooldown = 99f;
            Step(2f, () => seen.Add(e.CurrentFrame));
            Check(x.key + ": then it flies on the hover cell", seen.Contains(def.cells.Flight0));
            e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
            Step(.6f);
            Check(x.key + ": the damaged cell replaces the flight cell after the first lost heart", e.Damaged && e.CurrentFrame == def.cells.damaged);
        }
    }

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite> { Pad(new Vector2(-1.6f, 3f), LandingKind.TideTrenchHatch, 1), Pad(new Vector2(0f, 3f), LandingKind.TideWreckBay, 2), Pad(new Vector2(1.6f, 3f), LandingKind.TideVentStack, 3) };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(5, 1);
            foreach (var e in EliteShip.Live) { seen.Add(e.Def.key); }
        }
        Check("Storm's elites launch from the stand-in backdrop's pads, any kind (" + spawned + " launches)", spawned == 40);
        Check("all five Storm elites turn up (" + seen.Count + ")", seen.Count == 5);
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
            for (int i = 0; i < 5; i++) seen.Add(dir.NextDef(5).key);
            perDeck &= seen.Count == 5;
        }
        Check("the director deals Storm's five elites as a shuffled deck", perDeck);
        Check("Storm's elites wait for the calm start", EliteDirector.Blocked(5, EliteDirector.FirstSeconds - .5f) == "too early");
        Object.DestroyImmediate(dir.gameObject);
    }

    static void Codex()
    {
        int n = 0;
        bool strips = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex != 5) continue;
            n++;
            var entry = global::Codex.Find(d.codexId);
            strips &= entry != null && entry.category == CodexCategory.Enemies && EliteArt.HasExtra(d, EliteArt.Extra.Death);
        }
        Check("the codex lists five Storm elites with their death strips (" + n + ")", n == 5 && strips);
    }

    // ---- the five attacks -------------------------------------------------------------------

    static int Shots(System.Func<EliteShot, bool> pick)
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && pick(s)) n++;
        return n;
    }

    // Skyhook Harrier: a committed hooked dash, ion bolts off the lance as it brakes.
    static void Harrier()
    {
        Fresh();
        var e = InPlay("storm_elite_skyhook_harrier", new Vector2(0f, .6f));
        StartTell(e);
        Check("Skyhook Harrier: it holds still through the tell", e.Telling);
        StepUntil(() => e.Acting, 3f);
        Check("it then dashes committed at dashSpeed (" + e.Velocity.magnitude.ToString("0.0") + " u/s)", e.Acting && e.Velocity.magnitude > e.Def.dashSpeed * .7f);
        int before = EliteSystem.Shots.Launched;
        Step(1.5f);
        Check("ion bolts leave the lance as it brakes (" + (EliteSystem.Shots.Launched - before) + ")", EliteSystem.Shots.Launched - before >= e.Def.shotCount);
    }

    // Refinery Tender: armoured, canisters sinking down its lane.
    static void Tender()
    {
        Fresh(.05f);
        var e = InPlay("storm_elite_refinery_tender", new Vector2(0f, .6f));
        Check("Refinery Tender: armoured", e.Def.armored);
        e.ForceAttack();
        int before = EliteSystem.Shots.Launched;
        Step(2.5f);
        bool sinking = false;
        foreach (var s in EliteSystem.Shots.All) sinking |= s.Active && s.Kind == EliteShots.Kind.Slag && s.Velocity.y < 0f;
        Check("it drops " + e.Def.shotCount + " canisters that sink down the lane (" + (EliteSystem.Shots.Launched - before) + ")",
              EliteSystem.Shots.Launched - before >= e.Def.shotCount && sinking);
    }

    // Rod Dancer: blink, then an arc fan.
    static void Dancer()
    {
        Fresh(.05f);
        var e = InPlay("storm_elite_rod_dancer", new Vector2(0f, -.5f));
        int blinks = e.Blinks, before = EliteSystem.Shots.Launched;
        e.ForceAttack();
        Step(2f);
        Check("Rod Dancer: it blinks, then fans " + e.Def.shotCount + " arcs (" + (EliteSystem.Shots.Launched - before) + ")",
              e.Blinks > blinks && EliteSystem.Shots.Launched - before >= e.Def.shotCount);
    }

    // Anvil Cloud: a sight line down the lane, then one lightning shell straight down it.
    static void Anvil()
    {
        Fresh(.05f);
        var e = InPlay("storm_elite_anvil_cloud", new Vector2(0f, 1f));
        Step(5f);
        e.ForceAttack();
        bool sight = false;
        float tell = 0f;
        while (e.Telling && tell < 5f) { EliteSystem.Step(Dt); tell += Dt; sight |= e.transform.Find("Sight").GetComponent<SpriteRenderer>().enabled; }
        int before = EliteSystem.Shots.Launched;
        Step(.4f);
        EliteShot shell = null;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Shell) shell = s;
        Check("Anvil Cloud: the lane is drawn through the tell (" + tell.ToString("0.00") + " s)", sight && tell >= AttackHazard.MinTellSeconds);
        Check("... then a lightning shell goes straight down it",
              EliteSystem.Shots.Launched > before && shell != null && shell.Velocity.y < -5f && Mathf.Abs(shell.Velocity.x) < .05f);
    }

    // Static Bastion: a curtain of slow arcs across the pilot's row with one gap beside the pilot.
    static void Bastion()
    {
        Fresh();
        var e = InPlay("storm_elite_static_bastion", new Vector2(0f, .6f));
        var wc = (WardCurtainAttack)e.Attack;
        StartTell(e);
        StepUntil(() => e.Acting, 3f);
        int arcs = 0;
        for (float t = 0f; t < 2f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            arcs = Mathf.Max(arcs, Shots(s => true));
        }
        Check("Static Bastion: a curtain of " + (wc.Count - 1) + " arcs with one gap beside the pilot (fired " + wc.Fired + ", most on screen " + arcs + ")",
              wc.Fired == wc.Count - 1 && arcs >= 3 && wc.Gap == wc.Centre + (pilot.position.x > 0f ? -1 : 1));
    }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        string[][] groups =
        {
            new[] { "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer" },
            new[] { "storm_elite_anvil_cloud", "storm_elite_static_bastion" },
        };
        foreach (var g in groups)
        {
            Fresh(.1f);
            pilot.position = new Vector3(0f, -2.5f, 0f);
            var ships = new List<EliteShip>();
            for (int i = 0; i < g.Length; i++) ships.Add(InPlay(g[i], new Vector2(-1.4f + i * 1.4f, .5f + i * .4f)));
            foreach (var s in ships) s.AttackCooldown = 0f;
            Step(8f);
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
