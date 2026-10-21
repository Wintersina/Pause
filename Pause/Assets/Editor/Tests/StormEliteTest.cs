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
        float minX = 9f, maxX = -9f;
        Step(7f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Anvil Cloud: it drifts along the top, never down to the pilot (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")",
              maxX - minX > .6f && e.Position.y > pilot.position.y + 1.5f);
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
        Check("a fifth field once two hearts are lost (" + full + " -> " + sf.PlanCount + " at " + e.Hearts + " hearts)", full == 4 && sf.PlanCount == 5 && sf.PlanCount < StormSpotAttack.MaxSpots);

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
        Check("Refinery Tender: it roams (untouchable off the board) and leaves its dock by the nearer side first (" + ab.Phase + ")", ab.Roams && ab.Phase == StormPassBrain.Stage.Leave);
        bool passing = StepUntil(() => ab.Phase == StormPassBrain.Stage.Pass, 8f);
        Check("... then makes a PASS across the top (passes " + ab.Passes + ")", passing && ab.Passes == 1);
        bool window = StepUntil(() => ab.WantsAttack(Vector2.zero), 14f);
        Check("the cast window opens inside the rails, high over the board (x " + e.Position.x.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")", window && e.Position.y > pilot.position.y + 1.5f);
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
        bool back = StepUntil(() => ab.Passes >= 2, 24f);
        Check("it leaves by the far side and comes back for another pass (" + ab.Passes + ")", back);

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
        Check("Skyhook Harrier: it roams and leaves its dock by the nearer side first (" + hb.Phase + ")", hb.Roams && hb.Phase == StormPassBrain.Stage.Leave);
        bool passing = StepUntil(() => hb.Phase == StormPassBrain.Stage.Pass, 8f);
        Check("... then swoops across the top on its arc (passes " + hb.Passes + ")", passing && hb.Passes == 1);
        bool window = StepUntil(() => hb.WantsAttack(Vector2.zero), 14f);
        Check("the rake window opens inside the rails (x " + e.Position.x.ToString("0.0") + ")", window);
        Vector2 at = e.Position;
        StartTell(e);
        bool along = true, inside = true;
        for (int i = 0; i < cr.Count; i++)
        {
            inside &= Mathf.Abs(cr.CableX(i)) <= EliteSystem.RailEdge && cr.CableY(i) < EliteSystem.ViewTop;
            along &= (cr.CableX(i) - (i == 0 ? at.x : cr.CableX(i - 1))) * hb.DirX > .4f;
        }
        Check("on the first frame of the tell the " + CableRakeAttack.Cables + " cables are drawn as thin lines along its heading (" + cr.Telegraph.Loops + " outlines)",
              e.Telling && cr.Telegraph.Showing && cr.Telegraph.Loops == CableRakeAttack.Cables && along && inside);
        Check("the hooks go in: it holds through the wind-up", cr.HoldsDuringTell);
        int before = EliteSystem.Shots.Launched;
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead", e.Acting && AttackPreview.TooShort == 0);
        Step(2.5f, () => e.AttackCooldown = 99f);
        Check("the cables drop one after another (" + cr.Dropped + ", launched " + (EliteSystem.Shots.Launched - before) + ")", cr.Dropped == cr.Count && EliteSystem.Shots.Launched - before >= cr.Count);
        Check("the cast is counted and the lines are gone (" + hb.Casts + ")", hb.Casts == 1 && AttackPreview.ActiveCount == 0);
        bool back = StepUntil(() => hb.Passes >= 2, 24f);
        Check("it leaves by the far side and comes back (" + hb.Passes + ")", back);

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
        Step(4f);
        Check("Static Bastion: it holds the top of the view (y " + e.Position.y.ToString("0.0") + ")", e.Position.y > pilot.position.y + 1.5f);
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
                    if (s.Active && s.transform.position.y > EliteSystem.ViewTop - .4f && Mathf.Abs(s.transform.position.x - hw.GapCentre(w)) < HailWallAttack.GapHalf - .02f) clean = false;
                waveSeen = hw.Sent;
            }
        }
        Check("the hail falls in " + hw.Waves + " waves (" + hw.Sent + " sent, " + (EliteSystem.Shots.Launched - before) + " stones), none inside the gap", hw.Sent == hw.Waves && EliteSystem.Shots.Launched - before >= hw.Waves * 4 && clean);
        Step(3f);
        Check("nothing is left behind (" + AttackPreview.ActiveCount + " drawings)", AttackPreview.ActiveCount == 0);

        Fresh();
        e = InPlay("storm_elite_static_bastion", new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Bastion killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);
    }

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
        Check("Tender and Harrier roam; Anvil, Dancer and Bastion stay on the board",
              EliteBrains.Create("airship").Roams && EliteBrains.Create("hooker").Roams &&
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
