using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Storm's five elites as mini-boss fights (Anvil Cloud, Refinery Tender, Rod Dancer, Skyhook Harrier, Static Bastion; StormElites.cs): the
// roster, their strips, muzzles and nozzles on the hull, the flight cell map, launching from Storm's launch sites, the director's rotation, the
// codex entries, the fight rules (3 hearts, 5 armoured, hurtHide for the non-armoured chasers, the shrunk size band, no pink) and each one's own
// brain and attack in simulation: static fields, flares, rod strikes, a cable rake, a hail wall. Every attack shows its footprint from the first
// frame of its tell and cleans up after itself; nothing allocates per frame. Desert still flies the base brains and attacks.
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

    static readonly (string key, string brain, string attack, LandingKind from)[] Expected =
    {
        ("storm_elite_anvil_cloud", "thunderhead", "static_field", LandingKind.Hangar),
        ("storm_elite_refinery_tender", "airship", "flare_drop", LandingKind.RigBay),
        ("storm_elite_rod_dancer", "dancer", "rod_strikes", LandingKind.TowerBay),
        ("storm_elite_skyhook_harrier", "hooker", "cable_rake", LandingKind.PadRing),
        ("storm_elite_static_bastion", "citadel", "hail_wall", LandingKind.Hatch),
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
            Anvil();
            Tender();
            Dancer();
            Harrier();
            Bastion();
            DancerOrders();
            Scripts();
            Determinism();
            Rules();
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
        e.EngageLeft = 999f;   // (the engagement never runs out mid-test: a retreat would pop its flares)
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
            Check(x.key + ": launches from its own Storm launch site (" + d.launchFrom + "), codex id elite_storm_*", LandingSite.Accepts(d.launchFrom, x.from) && !LandingSite.Accepts(d.launchFrom, LandingKind.Station) && d.codexId.StartsWith("elite_storm_"));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the flight strip slices into 7 square cells of the def's cell size and the cell map is the flight layout",
                  frames != null && frames.Length == 10 + 6 && Mathf.Approximately(frames[0].rect.width, d.cellPixels) && Mathf.Approximately(frames[0].rect.height, d.cellPixels) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == 10 && c.damagedLoop[5] == 15 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0 && EliteHdResolution.CellOk(d));
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the death strip slices into 3 cells of the def's cell size (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length >= 3 && Mathf.Approximately(death[0].rect.width, d.cellPixels));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Storm/" + d.key + ".png"));
            bool solid = true, inCell = true;
            string bad = "";
            foreach (var p in d.muzzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > d.cellPixels * .04f && p.x < d.cellPixels * .96f && p.y > d.cellPixels * .04f && p.y < d.cellPixels * .96f; }
            foreach (var p in d.nozzles) { if (!OnHull(tex, d, p)) { solid = false; bad += p.name + " "; } inCell &= p.x > d.cellPixels * .04f && p.x < d.cellPixels * .96f && p.y > d.cellPixels * .04f && p.y < d.cellPixels * .96f; }
            Object.DestroyImmediate(tex);
            Check(x.key + ": its " + d.muzzles.Length + " muzzles and " + d.nozzles.Length + " nozzles sit on the hull, well inside the cell " + bad, solid && inCell && d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(x.key + ": " + d.hearts + " hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts >= 3 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned like its siblings (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= 2.5f && d.attackGap <= 5.5f && d.tellSeconds >= .7f && d.avoidance >= .4f && d.avoidance <= .9f);
            Check(x.key + ": shots are in the Storm white / cold-blue family, never red or violet; hearts are the player's heart colour " + ColorUtility.ToHtmlStringRGB(d.HeartColor),
                  d.HeartColor == ShipLivesIndicator.HeartTint && ((Hue(d.ShotColor) > 150f && Hue(d.ShotColor) < 240f) || (Hue(d.ShotColor) > 40f && Hue(d.ShotColor) < 70f)));
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
            e.TakeHit(EliteDamage.Teleport, e.Position, 1);
            Step(.6f);
            Check(x.key + ": the damaged burn loop replaces the flight cell after the first lost heart", e.Damaged && (def.hurtHide || (e.CurrentFrame >= 10 && e.CurrentFrame <= 15)));
        }
    }

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-1.6f, 3f), LandingKind.Hangar, 1), Pad(new Vector2(-.8f, 3.3f), LandingKind.RigBay, 2), Pad(new Vector2(0f, 3f), LandingKind.TowerBay, 3),
            Pad(new Vector2(.8f, 3.3f), LandingKind.PadRing, 4), Pad(new Vector2(1.6f, 3f), LandingKind.Hatch, 5),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0;
        var seen = new HashSet<string>();
        bool match = true;
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(5, 1);
            foreach (var e in EliteShip.Live) { match &= LandingSite.Accepts(e.Def.launchFrom, e.Site.kind); seen.Add(e.Def.key); }
        }
        Check("each Storm elite launches from its own launch site (" + spawned + " launches)", spawned == 40 && match);
        Check("all five Storm elites turn up (" + seen.Count + ")", seen.Count == 5);
        pads.Clear();
        pads.Add(Pad(new Vector2(0f, 3f), LandingKind.Ground, 9));
        bool fallback = true;
        for (int i = 0; i < 12; i++) { EliteSystem.Clear(); fallback &= dir.SpawnGroup(5, 1) == 1; }
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

    // ---- the five fights --------------------------------------------------------------------

    static int Shots(System.Func<EliteShot, bool> pick)
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && pick(s)) n++;
        return n;
    }

    // Kills it where it stands (a pause jump's damage), the way a player's pause would.
    static void Kill(EliteShip e) { e.TakeHit(EliteDamage.Teleport, e.transform.position, 9); }

    // ---- Anvil Cloud: thunderhead / static_field ---------------------------------------------

    static void Anvil()
    {
        Fresh();
        var e = InPlay("storm_elite_anvil_cloud", new Vector2(0f, 3f));
        var sf = (StaticFieldAttack)e.Attack;
        var tb = (ThunderheadBrain)e.Brain;
        float minX = 9f, maxX = -9f;
        Step(7f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Anvil Cloud: it holds the first station the script names (left: " + tb.Station + ", x " + e.Position.x.ToString("0.00") + " of " + tb.StationX(0).ToString("0.00") + "), high over the board, never down to the pilot (y " + e.Position.y.ToString("0.0") + ")",
              tb.Station == 0 && tb.Settled && e.Position.y > pilot.position.y + 1.5f);
        float still0 = e.Position.x;
        float still1 = still0;
        Step(3f, () => still1 = Mathf.Max(still1, Mathf.Abs(e.Position.x - still0)));
        Check("... and it stays on that station (no pendulum: moved " + still1.ToString("0.00") + " u in 3 s)", still1 < .25f);
        tb.OnScriptMove("R");
        Step(7f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("a token hops it to the next station (right: " + tb.Station + ", x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ")", tb.Station == 2 && tb.Settled && maxX - minX > .6f);
        StartTell(e);
        bool below = true;
        for (int i = 0; i < sf.Count; i++) below &= sf.Spot(i).y < e.Position.y;
        Check("on the first frame of the tell every static field is ringed (" + sf.Marks.ShownCount + " of " + sf.PlanCount + "), all under the cloud",
              e.Telling && sf.Count == sf.PlanCount && sf.Marks.ShownCount == sf.Count && sf.Count >= 4 && below);
        Check("it holds still through the wind-up", sf.HoldsDuringTell && sf.HoldsDuringAction);
        StepUntil(() => e.Acting, 3f);
        int pools = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            e.AttackCooldown = 99f;   // (one cast: the counters below are its own)
            EliteSystem.Step(Dt);
            pools = Mathf.Max(pools, Shots(s => s.Kind == EliteShots.Kind.Glob && !s.Airborne));
        }
        Check("the fields land as pools (" + pools + " at once of " + sf.Count + ") and the FR1 lead was shown", sf.Laid == sf.Count && pools >= sf.Count - 1 && AttackPreview.TooShort == 0);

        // the fight escalates with the hearts lost
        Fresh();
        e = InPlay("storm_elite_anvil_cloud", new Vector2(0f, 3f));
        sf = (StaticFieldAttack)e.Attack;
        int full = sf.PlanCount;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        Check("a fifth field once it is in phase 3 (" + full + " -> " + sf.PlanCount + " at " + e.Hearts + " hearts)", full == 4 && sf.PlanCount == 5 && sf.PlanCount < StormSpotAttack.MaxSpots);

        Fresh();
        e = InPlay("storm_elite_anvil_cloud", new Vector2(0f, 3f));
        sf = (StaticFieldAttack)e.Attack;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check("... and from phase 2, the first lost heart (" + e.Script.Phase + ", " + sf.PlanCount + " fields)", e.Script.Phase == 2 && sf.PlanCount == 5);

        Fresh();
        e = InPlay("storm_elite_anvil_cloud", new Vector2(0f, 3f));
        sf = (StaticFieldAttack)e.Attack;
        StartTell(e);
        Shoot(e, 2);
        Kill(e);
        Check("an Anvil Cloud killed in its tell takes its rings with it", (e == null || sf.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Refinery Tender: airship / flare_drop ------------------------------------------------

    static void Tender()
    {
        Fresh(.1f);   // (the board scrolls 3 u/s: what a flare rides down is its own fuse long)
        var e = InPlay("storm_elite_refinery_tender", new Vector2(0f, 3f));
        var ab = (AirshipBrain)e.Brain;
        var fd = (FlareDropAttack)e.Attack;
        Check("Refinery Tender: it does not roam (always a target, between the rails)", !ab.Roams);
        // a racetrack: legs across the top and a U-turn at each rail, never out of the view
        float minX = 9f, maxX = -9f, minY = 99f, maxY = -99f;
        bool sawRight = false, sawLeft = false, off = false;
        Step(14f, () =>
        {
            minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
            minY = Mathf.Min(minY, e.Position.y); maxY = Mathf.Max(maxY, e.Position.y);
            sawRight |= e.Velocity.x > .3f; sawLeft |= e.Velocity.x < -.3f;
            off |= e.Offstage || e.Position.y > EliteSystem.ViewTop || Mathf.Abs(e.Position.x) > EliteSystem.RailEdge;
        });
        Check("... it flies legs across the top and TURNS ROUND at the rail instead of leaving (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + minY.ToString("0.0") + " .. " + maxY.ToString("0.0") + ", legs " + ab.Legs + ")",
              sawRight && sawLeft && maxX - minX > 1.2f && ab.Legs >= 3 && !off && e.Position.y > pilot.position.y + 1.5f);
        bool window = StepUntil(() => ab.WantsAttack(Vector2.zero), 14f);
        Check("the cast window opens on a straight leg inside the rails, high over the board (x " + e.Position.x.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")", window && ab.OnLeg && e.Position.y > pilot.position.y + 1.5f);
        StartTell(e);
        bool stairs = true, inside = true;
        for (int i = 0; i < fd.Count; i++)
        {
            inside &= Mathf.Abs(fd.Spot(i).x) <= EliteSystem.RailEdge;
            if (i > 0) stairs &= fd.Spot(i).y <= fd.Spot(i - 1).y + 1e-3f && (fd.Spot(i).x - fd.Spot(i - 1).x) * ab.DirX >= -1e-3f;
        }
        Check("on the first frame of the tell a staircase of " + FlareDropAttack.Flares + " flares is ringed down its heading (" + fd.Marks.ShownCount + " shown)",
              e.Telling && fd.Count == FlareDropAttack.Flares && fd.Marks.ShownCount == FlareDropAttack.Flares && stairs && inside);
        Check("it keeps drifting through the wind-up (not held, " + e.Velocity.magnitude.ToString("0.0") + " u/s)", !fd.HoldsDuringTell && e.Velocity.magnitude > .3f);
        StepUntil(() => e.Acting, 3f);
        int maxOrbs = 0, maxBolts = 0, maxLaid = 0, maxCasts = 0, bursts = 0;
        for (float t = 0f; t < 6f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            e.AttackCooldown = 99f;   // (one cast: the counters below are its own)
            EliteSystem.Step(Dt);
            maxLaid = Mathf.Max(maxLaid, fd.Laid);
            maxCasts = Mathf.Max(maxCasts, ab.Casts);
            maxOrbs = Mathf.Max(maxOrbs, Shots(s => s.Kind == EliteShots.Kind.Orb));
            maxBolts = Mathf.Max(maxBolts, Shots(s => s.Kind != EliteShots.Kind.Orb));
            int b = 0;
            foreach (var sh in EliteSystem.Shots.All) b += sh.Bursts;
            bursts = Mathf.Max(bursts, b);
        }
        Check("the flares are lobbed one after another (" + maxLaid + ", most at once " + maxOrbs + ") and each bursts into an X of bolts (most bolts " + maxBolts + ", bursts " + bursts + ")",
              maxLaid == FlareDropAttack.Flares && maxOrbs >= 2 && maxBolts >= 4 && AttackPreview.TooShort == 0);
        Check("the cast is counted (" + maxCasts + ")", maxCasts == 1);
        float dir0 = ab.DirX;
        bool turned = StepUntil(() => ab.DirX != dir0, 12f);
        Check("it turns round at the rail and flies the next leg back (heading " + dir0 + " -> " + ab.DirX + ")", turned);

        // the script names the tier of a leg: a token moves the next U-turn onto it, and phase 2 spaces the tiers wider
        Fresh();
        e = InPlay("storm_elite_refinery_tender", new Vector2(0f, 3f));
        ab = (AirshipBrain)e.Brain;
        int legs = ab.Legs;
        int want = ab.Tier == 2 ? 0 : 2;
        ab.OnScriptMove("tier:" + want);
        bool tiered = StepUntil(() => ab.Legs > legs && ab.Tier == want, 12f);
        Check("a 'tier:" + want + "' token puts a following leg on that tier (tier " + ab.Tier + ", legs " + legs + " -> " + ab.Legs + ")", tiered);
        float narrow = ab.TierStep;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        Check("a later phase spaces the tiers wider (" + narrow.ToString("0.00") + " -> " + ab.TierStep.ToString("0.00") + " u)", ab.TierStep > narrow + .05f);

        Fresh();
        e = InPlay("storm_elite_refinery_tender", new Vector2(0f, 3f));
        ab = (AirshipBrain)e.Brain;
        fd = (FlareDropAttack)e.Attack;
        StepUntil(() => ab.WantsAttack(Vector2.zero), 20f);
        StartTell(e);
        Kill(e);
        Check("a Tender killed in its tell takes its rings with it", (e == null || fd.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Rod Dancer: dancer / rod_strikes ----------------------------------------------------

    static void Dancer()
    {
        Fresh();
        var e = InPlay("storm_elite_rod_dancer", new Vector2(0f, 3f));
        var rs = (RodStrikesAttack)e.Attack;
        float minX = 9f, maxX = -9f;
        Step(7f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Rod Dancer: it weaves across the top, never to the pilot (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")",
              maxX - minX > 1.2f && e.Position.y > pilot.position.y + 1.5f);
        StartTell(e);
        bool allTell = rs.Armed >= 3 && rs.Armed <= RodStrikesAttack.MaxColumns;
        for (int i = 0; i < rs.Armed; i++) allTell &= rs.Strike(i) != null && rs.Strike(i).State == AttackHazard.Phase.Tell;
        Check("a glyph is armed under EVERY column on the first frame of the tell (" + rs.Armed + ")", e.Telling && allTell);
        bool outsideIn = true, spaced = true;
        for (int i = 1; i < rs.Armed; i++)
        {
            outsideIn &= Mathf.Abs(rs.Lane(i) - pilot.position.x) <= Mathf.Abs(rs.Lane(i - 1) - pilot.position.x) + 1e-3f;
            spaced &= Mathf.Abs(rs.Lane(i) - rs.Lane(i - 1)) >= AttackStrike.MinLaneSpacing - .01f;
        }
        Check("the columns strike OUTSIDE IN, the pilot's own lane last, a strike's corridor apart",
              outsideIn && spaced && Mathf.Abs(rs.Lane(rs.Armed - 1) - pilot.position.x) < .2f);
        Check("... told at least " + AttackHazard.MinTellSeconds + " s (" + e.Attack.TellSeconds.ToString("0.00") + ") and struck at least " + StormFlight.MinStrikeGap + " s apart (" + rs.Gap.ToString("0.00") + ")",
              e.Attack.TellSeconds >= AttackHazard.MinTellSeconds + .2f && rs.Gap >= StormFlight.MinStrikeGap);
        int n = rs.Armed;
        var litAt = new float[n];
        for (int i = 0; i < n; i++) litAt[i] = -1f;
        float clock = 0f, liveFor = 0f;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            e.AttackCooldown = 99f;   // (one cast: the strikes below are its own)
            EliteSystem.Step(Dt);
            clock += Dt;
            for (int i = 0; i < n; i++)
            {
                var st = rs.Strike(i);
                if (litAt[i] < 0f && st != null && st.State == AttackHazard.Phase.Live) litAt[i] = clock;
            }
            var s0 = rs.Strike(0);
            if (s0 != null && s0.State == AttackHazard.Phase.Live) liveFor += Dt;
        }
        bool sequence = true;
        for (int i = 0; i < n; i++) sequence &= litAt[i] >= 0f && (i == 0 || litAt[i] - litAt[i - 1] >= rs.Gap * .6f);
        Check("the columns go live ONE AFTER ANOTHER (first " + litAt[0].ToString("0.00") + " s, last " + litAt[n - 1].ToString("0.00") + " s) for no longer than " + AttackStrike.MaxLiveSeconds + " s each",
              sequence && liveFor <= AttackStrike.MaxLiveSeconds + Dt * 2);
        Step(1.5f);
        Check("nothing is left on the board afterwards (" + AttackHazard.ActiveCount + " hazards)", AttackHazard.ActiveCount == 0);

        Fresh();
        e = InPlay("storm_elite_rod_dancer", new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Rod Dancer killed in its tell takes its glyphs with it", AttackHazard.ActiveCount == 0);
        HurtFlees("storm_elite_rod_dancer");
    }

    // A hurt chaser breaks off, flies off-screen, waits and comes back into play.
    static void HurtFlees(string key)
    {
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check(key + ": a lost heart is a flinch, not a disappearance (" + e.State + ", hearts " + e.Hearts + ")", e.InPlay && !e.Hidden && e.Flinching);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check(key + ": its last heart sends it on its one scripted exit (" + e.State + ", hearts " + e.Hearts + ")", e.State == EliteState.Hide && e.Hidden);
        bool back = StepUntil(() => !e.Hidden && e.InPlay, 30f);
        Check(key + ": ... and it returns to the fight", back);
    }

    // ---- Skyhook Harrier: hooker / cable_rake -----------------------------------------------

    static void Harrier()
    {
        Fresh();
        var e = InPlay("storm_elite_skyhook_harrier", new Vector2(0f, 3f));
        var hb = (HookerBrain)e.Brain;
        var cr = (CableRakeAttack)e.Attack;
        Check("Skyhook Harrier: it does not roam (always a target, between the rails)", !hb.Roams);
        float minX = 9f, maxX = -9f, minY = 99f, maxY = -99f;
        bool sawRight = false, sawLeft = false, off = false;
        Step(12f, () =>
        {
            minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x);
            minY = Mathf.Min(minY, e.Position.y); maxY = Mathf.Max(maxY, e.Position.y);
            sawRight |= e.Velocity.x > .3f; sawLeft |= e.Velocity.x < -.3f;
            off |= e.Offstage || e.Position.y > EliteSystem.ViewTop || Mathf.Abs(e.Position.x) > EliteSystem.RailEdge;
        });
        Check("... it swoops in V shapes across the top, dips and hooks round at the rail instead of leaving (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + minY.ToString("0.0") + " .. " + maxY.ToString("0.0") + ", legs " + hb.Legs + ")",
              sawRight && sawLeft && maxX - minX > 1.2f && maxY - minY > .5f && hb.Legs >= 2 && !off && e.Position.y > pilot.position.y + 1.5f);
        bool window = StepUntil(() => hb.WantsAttack(Vector2.zero), 14f);
        Check("the rake window opens on the way down to the dip, on a straight leg (x " + e.Position.x.ToString("0.0") + ", heading " + hb.DirX + ")", window && hb.OnLeg && hb.DirX * e.Position.x < 0f);
        StartTell(e);
        bool along = true, inside = true, onV = true;
        for (int i = 0; i < cr.Count; i++)
        {
            inside &= Mathf.Abs(cr.CableX(i)) <= EliteSystem.RailEdge && cr.CableY(i) < EliteSystem.ViewTop;
            onV &= Mathf.Abs(cr.CableY(i) - hb.PathY(cr.CableX(i))) < .01f;
            if (i > 0) along &= (cr.CableX(i) - cr.CableX(i - 1)) * hb.DirX > .4f;
        }
        Check("on the first frame of the tell the " + CableRakeAttack.Cables + " cables are drawn as thin lines along its heading, centred under the dip (" + cr.Telegraph.Loops + " outlines)",
              e.Telling && cr.Telegraph.Showing && cr.Count == CableRakeAttack.Cables && cr.Telegraph.Loops == cr.Count && along && inside && onV &&
              Mathf.Abs(cr.CableX(0) + cr.CableX(cr.Count - 1)) < .05f);
        Check("the hooks go in: it holds through the wind-up", cr.HoldsDuringTell);
        int before = EliteSystem.Shots.Launched;
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead", e.Acting && AttackPreview.TooShort == 0);
        Step(2.5f, () => e.AttackCooldown = 99f);
        Check("the cables drop one after another (" + cr.Dropped + ", launched " + (EliteSystem.Shots.Launched - before) + ")", cr.Dropped == cr.Count && EliteSystem.Shots.Launched - before >= cr.Count);
        Check("the cast is counted and the lines are gone (" + hb.Casts + ")", hb.Casts == 1 && AttackPreview.ActiveCount == 0);
        float dir0 = hb.DirX;
        bool turned = StepUntil(() => hb.DirX != dir0, 12f);
        Check("it hooks round at the rail and swoops back the other way (heading " + dir0 + " -> " + hb.DirX + ")", turned);

        // the script sets the depth of the next V; the later phases rake wider
        Fresh();
        e = InPlay("storm_elite_skyhook_harrier", new Vector2(0f, 3f));
        hb = (HookerBrain)e.Brain;
        float shallow = hb.Dip;
        hb.OnScriptMove("dip:2");
        bool deeper = StepUntil(() => hb.Dip > shallow + .3f, 12f);
        Check("a 'dip:2' token makes the next V deeper (" + shallow.ToString("0.00") + " -> " + hb.Dip.ToString("0.00") + " u)", deeper);

        Fresh();
        e = InPlay("storm_elite_skyhook_harrier", new Vector2(0f, 3f));
        hb = (HookerBrain)e.Brain;
        StepUntil(() => hb.WantsAttack(Vector2.zero), 20f);
        StartTell(e);
        Kill(e);
        Check("a Harrier killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);
        HurtFlees("storm_elite_skyhook_harrier");
    }

    // ---- Static Bastion: citadel / hail_wall ------------------------------------------------

    static void Bastion()
    {
        Fresh();
        var e = InPlay("storm_elite_static_bastion", new Vector2(0f, 3f));
        var hw = (HailWallAttack)e.Attack;
        var cb = (CitadelBrain)e.Brain;
        Step(4f);
        Check("Static Bastion: it holds the top of the view (y " + e.Position.y.ToString("0.0") + ")", e.Position.y > pilot.position.y + 1.5f);
        bool planted = StepUntil(() => cb.Planted && cb.TargetX > 0f, 12f);
        Check("... crawls to the rail end the script names (right: x " + e.Position.x.ToString("0.00") + " of " + cb.TargetX.ToString("0.00") + ") and plants", planted);
        float x0 = e.Position.x;
        cb.OnScriptMove("L");
        Step(3f);
        Check("... a token sends it crawling back (x " + x0.ToString("0.00") + " -> " + e.Position.x.ToString("0.00") + ", at a crawl: under " + (e.Def.speed * 1.2f).ToString("0.0") + " u/s)",
              e.Position.x < x0 - .8f && e.Velocity.magnitude < e.Def.speed * 1.2f);
        StartTell(e);
        Check("on the first frame of the tell the edges of every wave's gap are drawn (" + hw.Telegraph.Loops + " lines for " + hw.Waves + " waves)",
              e.Telling && hw.Waves >= 2 && hw.Telegraph.Showing && hw.Telegraph.Loops == 2 * hw.Waves);
        float g0 = hw.GapCentre(0);
        Check("the gap is not where the pilot stands (gap x " + g0.ToString("0.00") + ", pilot x " + pilot.position.x.ToString("0.00") + ") and keeps inside the rails",
              Mathf.Abs(g0 - pilot.position.x) > .5f && Mathf.Abs(g0) + HailWallAttack.GapHalf <= EliteSystem.RailEdge);
        bool slides = true;
        for (int w = 1; w < hw.Waves; w++) slides &= Mathf.Abs(hw.GapCentre(w) - hw.GapCentre(w - 1)) <= HailWallAttack.WaveSlide + .01f;
        Check("each wave's gap slides at most a hand from the last", slides);
        int before = EliteSystem.Shots.Launched;
        StepUntil(() => e.Acting, 3f);
        bool clean = true;
        int waveSeen = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            e.AttackCooldown = 99f;   // (one cast)
            EliteSystem.Step(Dt);
            if (hw.Sent > waveSeen)
            {
                // (the stones of a fresh wave are all up at the top: none inside its gap)
                int w = hw.Sent - 1;
                foreach (var s in EliteSystem.Shots.All)
                    if (s.Active && s.transform.position.y > EliteSystem.ViewTop - .4f && Mathf.Abs(s.transform.position.x - hw.GapCentre(w)) < hw.GapHalfOf(w) - .02f) clean = false;
                waveSeen = hw.Sent;
            }
        }
        Check("the hail falls in " + hw.Waves + " waves (" + hw.Sent + " sent, " + (EliteSystem.Shots.Launched - before) + " stones), none inside the gap", hw.Sent == hw.Waves && EliteSystem.Shots.Launched - before >= hw.Waves * 4 && clean);
        Step(3f);
        Check("nothing is left behind (" + AttackPreview.ActiveCount + " drawings)", AttackPreview.ActiveCount == 0);

        // the phases: three waves from phase 2, a gap that closes in at the last heart
        Fresh();
        e = InPlay("storm_elite_static_bastion", new Vector2(0f, 3f));
        hw = (HailWallAttack)e.Attack;
        StartTell(e);
        int waves1 = hw.Waves;
        float open1 = hw.GapHalfOf(hw.Waves - 1);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Step(EliteShip.GraceSeconds + .1f);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Step(EliteShip.GraceSeconds + .1f);
        Check("two lost hearts take the armoured Bastion into phase 2 (" + e.Script.Phase + ", " + e.Hearts + " hearts)", e.Script.Phase == 2);
        e.AttackCooldown = 99f;
        StartTell(e);
        int waves2 = hw.Waves;
        float open2 = hw.GapHalfOf(hw.Waves - 1);
        Check("phase 2 drops three waves (" + waves1 + " -> " + waves2 + "), the gap still full width (" + open1.ToString("0.00") + " / " + open2.ToString("0.00") + ")", waves1 == 2 && waves2 == 3 && Mathf.Approximately(open2, HailWallAttack.GapHalf));
        for (int i = 0; i < 2; i++)
        {
            e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
            Step(EliteShip.GraceSeconds + .1f);
        }
        Check("... and the last heart the last phase (" + e.Script.Phase + ", " + e.Hearts + " hearts)", e.Script.Phase == 3 && e.Hearts == 1);
        e.AttackCooldown = 99f;
        StartTell(e);
        Check("the last heart's wall CLOSES IN: each wave's gap narrower than the last (" + hw.GapHalfOf(0).ToString("0.00") + " -> " + hw.GapHalfOf(hw.Waves - 1).ToString("0.00") + ")",
              hw.Closing && hw.Waves == 3 && hw.GapHalfOf(hw.Waves - 1) < hw.GapHalfOf(0) - .1f && hw.GapHalfOf(hw.Waves - 1) >= HailWallAttack.MinGapHalf - .001f);

        Fresh();
        e = InPlay("storm_elite_static_bastion", new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Bastion killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Rod Dancer: the order of the columns follows the script's variant --------------------

    static void DancerOrders()
    {
        Check("rod strike orders by variant name: out / pair -> outside in, in / in4 -> inside out, endsA / endsB -> from both ends",
              RodStrikesAttack.OrderFor("out") == RodStrikesAttack.Order.OutsideIn && RodStrikesAttack.OrderFor("pair") == RodStrikesAttack.Order.OutsideIn &&
              RodStrikesAttack.OrderFor("in") == RodStrikesAttack.Order.InsideOut && RodStrikesAttack.OrderFor("in4") == RodStrikesAttack.Order.InsideOut &&
              RodStrikesAttack.OrderFor("endsA") == RodStrikesAttack.Order.Ends && RodStrikesAttack.OrderFor("endsB") == RodStrikesAttack.Order.Ends &&
              RodStrikesAttack.OrderFor("") == RodStrikesAttack.Order.OutsideIn);
        Fresh();
        var e = InPlay("storm_elite_rod_dancer", new Vector2(0f, 3f));
        var rs = (RodStrikesAttack)e.Attack;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Step(EliteShip.GraceSeconds + .1f);
        Check("a lost heart is phase 2 (" + e.Script.Phase + ")", e.Script.Phase == 2);
        StartTell(e);
        bool inside = rs.Sequence == RodStrikesAttack.Order.InsideOut && Mathf.Abs(rs.Lane(0) - pilot.position.x) < .2f;
        for (int i = 1; i < rs.Armed; i++) inside &= Mathf.Abs(rs.Lane(i) - pilot.position.x) >= Mathf.Abs(rs.Lane(i - 1) - pilot.position.x) - 1e-3f;
        Check("phase 2 strikes INSIDE OUT: the pilot's own lane first, the others farther and farther (" + rs.Armed + " columns)", rs.Armed >= 3 && inside);

        // the last heart: the one scripted exit by the top, back in, then the columns from both ends in
        e.AttackCooldown = 99f;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        bool back = StepUntil(() => !e.Hidden && e.InPlay && e.State != EliteState.Hide, 30f);
        e.AttackCooldown = 99f;
        Step(1.5f);
        Check("the last heart's exit comes back (" + e.State + ", phase " + e.Script.Phase + ")", back && e.Script.Phase == 3);
        StartTell(e);
        bool ends = rs.Sequence == RodStrikesAttack.Order.Ends && rs.Armed >= 3;
        float lo = 99f, hi = -99f;
        for (int i = 0; i < rs.Armed; i++) { lo = Mathf.Min(lo, rs.Lane(i)); hi = Mathf.Max(hi, rs.Lane(i)); }
        ends &= Mathf.Approximately(rs.Lane(0), lo) && Mathf.Approximately(rs.Lane(1), hi);
        Check("the last phase strikes from BOTH ENDS in: left-most, right-most, then the next in (" + rs.Lane(0).ToString("0.0") + ", " + rs.Lane(1).ToString("0.0") + ")", ends);
    }

    // ---- the scripts: shapes, tokens, variants, phases ------------------------------------------

    static readonly Dictionary<string, string[]> Tokens = new Dictionary<string, string[]>
    {
        { "thunderhead", new[] { "L", "C", "R" } },
        { "airship", new[] { "tier:0", "tier:1", "tier:2" } },
        { "dancer", new[] { "cw", "ccw" } },
        { "hooker", new[] { "dip:1", "dip:2" } },
        { "citadel", new[] { "L", "C", "R" } },
    };

    static bool Differ(ElitePhaseDef a, ElitePhaseDef b)
    {
        if (a == null || b == null || a.loop == null || b.loop == null) return false;
        if (a.loop.Length != b.loop.Length || !Mathf.Approximately(a.speed, b.speed)) return true;
        for (int i = 0; i < a.loop.Length; i++)
            if (a.loop[i].attack != b.loop[i].attack || a.loop[i].move != b.loop[i].move) return true;
        return false;
    }

    static bool HasVariant(EliteScriptDef sd, string name)
    {
        if (name == "" || name == "-") return true;
        if (sd.variants != null) foreach (var v in sd.variants) if (v.name == name) return true;
        return false;
    }

    static void Scripts()
    {
        var kinds = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            var sd = d.script;
            bool tokensOk = true, variantsOk = true, loops = sd.phase1 != null && sd.phase2 != null && sd.last != null &&
                sd.phase1.loop.Length >= 2 && sd.phase2.loop.Length >= 2 && sd.last.loop.Length >= 2;
            var ph = new[] { sd.phase1, sd.phase2, sd.last };
            var known = Tokens[d.brain];
            foreach (var p in ph)
                foreach (var st in p.loop)
                {
                    tokensOk &= st.move == "" || System.Array.IndexOf(known, st.move) >= 0;
                    variantsOk &= HasVariant(sd, st.attack);
                }
            Check(x.key + ": a three-phase script (loops of " + sd.phase1.loop.Length + " / " + sd.phase2.loop.Length + " / " + sd.last.loop.Length + " steps), every move token one its brain reads, every attack variant defined", loops && tokensOk && variantsOk);
            Check(x.key + ": phase 2 and the last heart each loop differently from the phase before", Differ(sd.phase1, sd.phase2) && Differ(sd.phase2, sd.last));
            kinds.Add(sd.entrance.kind);
            bool exits = d.hurtHide ? (sd.lastExit == "top" || sd.lastExit == "left" || sd.lastExit == "right") : (sd.lastExit == "none" || !d.hurtHide);
            Check(x.key + ": leaves the view only on its one scripted exit (hurtHide " + d.hurtHide + ", lastExit '" + sd.lastExit + "')", exits);
            Check(x.key + ": tempo and engage kept (" + sd.tempo + " / " + d.engageSeconds + ")", sd.tempo > 0f && d.engageSeconds >= 42f);
        }
        Check("the five Storm entrances are five different signatures (" + string.Join(",", kinds) + ")", kinds.Count == 5);
    }

    // ---- no coin flips: the same sequence whatever the seed --------------------------------------

    static List<int> TellLog(string key, int seed, float seconds, System.Func<EliteShip, int> read)
    {
        Fresh();
        Random.InitState(seed);
        var e = EliteShip.CreateInPlay(EliteCatalog.Find(key), new Vector2(0f, 3f));
        e.EscapeLeft = 0f;
        e.EngageLeft = 9999f;
        var log = new List<int>();
        bool was = false;
        for (float t = 0f; t < seconds; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            if (e == null || e.State == EliteState.Dead) break;
            bool tell = e.Telling;
            if (tell && !was) log.Add(read(e));
            was = tell;
        }
        return log;
    }

    static void Determinism()
    {
        var anvilA = TellLog("storm_elite_anvil_cloud", 11, 60f, e => ((ThunderheadBrain)e.Brain).Station);
        var anvilB = TellLog("storm_elite_anvil_cloud", 987, 60f, e => ((ThunderheadBrain)e.Brain).Station);
        Check("Anvil Cloud: the same stations in the same order whatever the seed (" + string.Join(",", anvilA) + " / " + string.Join(",", anvilB) + ")",
              Same(anvilA, anvilB, 4) && Distinct(anvilA) >= 2);
        var basA = TellLog("storm_elite_static_bastion", 5, 60f, e => ((CitadelBrain)e.Brain).TargetX > 0f ? 1 : -1);
        var basB = TellLog("storm_elite_static_bastion", 4242, 60f, e => ((CitadelBrain)e.Brain).TargetX > 0f ? 1 : -1);
        Check("Static Bastion: the same rail ends in the same order whatever the seed (" + string.Join(",", basA) + " / " + string.Join(",", basB) + ")",
              Same(basA, basB, 3) && Distinct(basA) >= 2);
        var danA = TellLog("storm_elite_rod_dancer", 3, 45f, e => ((DancerBrain)e.Brain).Way > 0f ? 1 : -1);
        var danB = TellLog("storm_elite_rod_dancer", 777, 45f, e => ((DancerBrain)e.Brain).Way > 0f ? 1 : -1);
        Check("Rod Dancer: the same weave directions in the same order whatever the seed (" + string.Join(",", danA) + " / " + string.Join(",", danB) + ")",
              Same(danA, danB, 4) && Distinct(danA) >= 2);
        var tenA = TellLog("storm_elite_refinery_tender", 8, 60f, e => ((AirshipBrain)e.Brain).Tier);
        var tenB = TellLog("storm_elite_refinery_tender", 6001, 60f, e => ((AirshipBrain)e.Brain).Tier);
        Check("Refinery Tender: the same tiers in the same order whatever the seed (" + string.Join(",", tenA) + " / " + string.Join(",", tenB) + ")", Same(tenA, tenB, 3));
        var harA = TellLog("storm_elite_skyhook_harrier", 2, 45f, e => ((HookerBrain)e.Brain).DirX > 0f ? 1 : -1);
        var harB = TellLog("storm_elite_skyhook_harrier", 31337, 45f, e => ((HookerBrain)e.Brain).DirX > 0f ? 1 : -1);
        Check("Skyhook Harrier: the same swoop headings in the same order whatever the seed (" + string.Join(",", harA) + " / " + string.Join(",", harB) + ")", Same(harA, harB, 3));
    }

    static bool Same(List<int> a, List<int> b, int atLeast)
    {
        int n = Mathf.Min(a.Count, b.Count);
        if (n < atLeast) return false;
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static int Distinct(List<int> a) { return new HashSet<int>(a).Count; }

    // ---- the fight rules ---------------------------------------------------------------------

    static void Rules()
    {
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            if (d == null) continue;
            bool chaser = x.key.EndsWith("rod_dancer") || x.key.EndsWith("skyhook_harrier");
            Check(x.key + ": hearts " + d.hearts + " (5 armoured, 3 not), hurtHide only for the non-armoured chasers",
                  d.hearts == (d.armored ? 5 : 3) && d.hurtHide == chaser && (!d.hurtHide || !d.armored));
            bool heavy = x.key.EndsWith("anvil_cloud") || x.key.EndsWith("refinery_tender") || x.key.EndsWith("static_bastion");
            float drawn = ShipTierTest.DrawnLongSide(d);
            float target = heavy ? 1.6f : 1.55f;
            Check(x.key + ": drawn long side " + drawn.ToString("0.00") + " u in the shrunk band (about " + target.ToString("0.00") + " u)", Mathf.Abs(drawn - target) < .06f);
            Check(x.key + ": no pink, no green (shot " + ColorUtility.ToHtmlStringRGB(d.ShotColor) + ", core " + ColorUtility.ToHtmlStringRGB(d.ShotCore) + ")",
                  !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore) && !Green(d.ShotColor) && !Green(d.ShotCore));
            var atk = EliteAttacks.Create(d.attack);
            Check(x.key + ": an aimed attack winds up long enough for the lock (tell " + d.tellSeconds + " s)", !atk.Aimed || d.tellSeconds >= EliteDef.MinAimedTell);
        }
        Check("none of the five Storm elites roams: all of them keep to the board (Tender and Harrier turn round at the rail)",
              !EliteBrains.Create("airship").Roams && !EliteBrains.Create("hooker").Roams &&
              !EliteBrains.Create("thunderhead").Roams && !EliteBrains.Create("dancer").Roams && !EliteBrains.Create("citadel").Roams);
        Check("the base ids Desert flies are still there",
              System.Array.IndexOf(EliteBrains.Ids, "interceptor") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "lance_dash") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "siege") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "siege_cannon") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "bastion") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "ward_curtain") >= 0);
    }

    static bool Green(Color c) { float h = Hue(c); return h > 85f && h < 165f; }

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
