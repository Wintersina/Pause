using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Desert's six elites as mini-boss fights (Dune Stalker, Caravan Hauler, Lens Matriarch, Glass Cannon, Scorpion Skiff, Mirror Bastion; DesertElites.cs): the
// roster, their strips (7 flight cells, 3 death cells), muzzles and nozzles on the hull, the flight cell map and launch / lift-off cells, the director's
// rotation, the codex entries, the fight rules (3 hearts, 5 armoured, hurtHide for the non-armoured chasers, the shrunk size band, no pink, no green) and each
// one's own brain and attack in simulation: a sand-burst trail, a quicksand row, a glass-shard fan, sun-lens beams, rolling boulders, a dust wall. Every attack
// shows its footprint from the first frame of its tell and cleans up after itself; nothing allocates per frame.
//
// The art is the ground-up redesign (Art/Enemies/src~/desert_elites_redesign: six flying hulls, 384 px cells, own palette each); DesertFramingTest
// guards the framing, ShipTierTest the sizes. Desert's backdrop has its own director and sites (DesertBackdrop.cs): each elite launches from its own kind
// (launchFrom), see DesertBackdropTest.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites DesertEliteTest
public static class DesertEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DEL] PASS  " : "[DEL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    static readonly (string key, string brain, string attack)[] Expected =
    {
        ("desert_elite_dune_stalker", "burrower", "worm_churn"),
        ("desert_elite_caravan_hauler", "caravan", "quicksand_row"),
        ("desert_elite_lens_matriarch", "glint", "glass_fan"),
        ("desert_elite_glass_cannon", "lenspost", "lens_beams"),
        ("desert_elite_scorpion_walker", "flanker", "boulder_roll"),
        ("desert_elite_mirror_bastion", "bulwark", "dust_wall"),
    };

    // The site kind each elite launches from (DesertBackdrop.cs; DesertBackdropTest flies them).
    static LandingKind LaunchKind(string key)
    {
        switch (key)
        {
            case "desert_elite_dune_stalker": return LandingKind.DesertCanyonDock;
            case "desert_elite_caravan_hauler": return LandingKind.DesertCrawlerHangar;
            case "desert_elite_lens_matriarch": return LandingKind.DesertMirrorSilo;
            case "desert_elite_glass_cannon": return LandingKind.DesertMirrorSilo;
            default: return LandingKind.DesertBuriedBunker;
        }
    }

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
            Stalker();
            Hauler();
            Matriarch();
            Cannon();
            Scorpion();
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
        Debug.Log("[DEL] failures: " + fails);
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
        var storm = new List<EliteDef>();
        Check("Desert has six elites (" + EliteCatalog.ForWorld(6, storm) + ")", storm.Count == 6);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        var shots = new HashSet<string>();
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": defined for Desert with its own brain / attack inside the world (" + (d != null ? d.brain + " / " + d.attack : "missing") + ")",
                  d != null && d.WorldIndex == 6 && d.brain == x.brain && d.attack == x.attack && brains.Add(d.brain) && attacks.Add(d.attack));
            if (d == null) continue;
            shots.Add(d.shotKind + "/" + d.shotCount + "/" + d.shotSpread + "/" + d.shotSpeed);
            Check(x.key + ": launches from its own site (" + d.launchFrom + "), codex id elite_desert_*", LandingSite.KindOf(d.launchFrom) == LaunchKind(x.key) && d.codexId.StartsWith("elite_desert_"));
            var frames = EliteArt.Frames(d);
            var c = d.cells;
            Check(x.key + ": the flight strip slices into 7 square cells of the def's cell size and the cell map is the flight layout",
                  frames != null && frames.Length >= 7 + c.flight.Length - 1 && Mathf.Approximately(frames[0].rect.width, d.cellPixels) && Mathf.Approximately(frames[0].rect.height, d.cellPixels) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.tell < 0 && c.action < 0 && c.hit < 0 && EliteHdResolution.CellOk(d));
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(x.key + ": the death strip slices into 3 cells of the def's cell size (" + (death != null ? death.Length.ToString() : "none") + ")",
                  death != null && death.Length >= 3 && Mathf.Approximately(death[0].rect.width, d.cellPixels));
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Desert/" + d.key + ".png"));
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
            Check(x.key + ": shots are pale bone gold (hue 35..55, S <= .33: never amber, red or violet); hearts are the player's heart colour " + ColorUtility.ToHtmlStringRGB(d.HeartColor),
                  d.HeartColor == ShipLivesIndicator.HeartTint && Hue(d.ShotColor) > 35f && Hue(d.ShotColor) < 55f && Sat(d.ShotColor) <= .33f);
            Check(x.key + ": lore is three sentences or fewer", d.lore.Split(new[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries).Length <= 3);
        }
        Check("each Desert elite shoots its own pattern (" + shots.Count + " distinct kind / count / spread / speed)", shots.Count == 6);
    }

    static float Sat(Color c) { float h, sa, v; Color.RGBToHSV(c, out h, out sa, out v); return sa; }
    static float Hue(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return h * 360f; }

    // Parked -> engines -> lift-off -> flight -> banks -> damaged: every Desert strip walks the flight layout.
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
            Check(x.key + ": the damaged cell replaces the flight cell after the first lost heart", e.Damaged && (def.hurtHide || e.CurrentFrame == def.cells.damaged || (def.cells.HasDamagedLoop && System.Array.IndexOf(def.cells.damagedLoop, e.CurrentFrame) >= 0)));
        }
    }

    static void Sites()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>
        {
            Pad(new Vector2(-2.0f, 3f), LandingKind.DesertWormBurrow, 1), Pad(new Vector2(-1.0f, 3f), LandingKind.DesertCrawlerBay, 2), Pad(new Vector2(0f, 3f), LandingKind.DesertGlassPad, 3),
            Pad(new Vector2(1.0f, 3f), LandingKind.DesertBuriedSilo, 4), Pad(new Vector2(2.0f, 3f), LandingKind.DesertRuinDock, 5), Pad(new Vector2(0f, 4.5f), LandingKind.DesertRuinDock, 6),
            Pad(new Vector2(-1.5f, 4.2f), LandingKind.DesertBuriedBunker, 7), Pad(new Vector2(-.5f, 4.2f), LandingKind.DesertCrawlerHangar, 8), Pad(new Vector2(.5f, 4.2f), LandingKind.DesertMirrorSilo, 9),
            Pad(new Vector2(1.5f, 4.2f), LandingKind.DesertCanyonDock, 10),
        };
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int spawned = 0, matched = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            EliteSystem.Clear();
            spawned += dir.SpawnGroup(6, 1);
            foreach (var e in EliteShip.Live) { seen.Add(e.Def.key); if (LandingSite.Accepts(e.Def.launchFrom, e.Site.kind)) matched++; }
        }
        Check("Desert's elites launch from Desert's own pads (" + spawned + " launches)", spawned == 40);
        Check("... each from the kind its def names (launchFrom) when one is free (" + matched + "/40)", matched == 40);
        Check("all six Desert elites turn up (" + seen.Count + ")", seen.Count == 6);
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
            for (int i = 0; i < 6; i++) seen.Add(dir.NextDef(6).key);
            perDeck &= seen.Count == 6;
        }
        Check("the director deals Desert's six elites as a shuffled deck", perDeck);
        Check("Desert's elites wait for the calm start", EliteDirector.Blocked(6, EliteDirector.FirstSeconds - .5f) == "too early");
        Object.DestroyImmediate(dir.gameObject);
    }

    static void Codex()
    {
        int n = 0;
        bool strips = true;
        foreach (var d in EliteCatalog.All)
        {
            if (d.WorldIndex != 6) continue;
            n++;
            var entry = global::Codex.Find(d.codexId);
            strips &= entry != null && entry.category == CodexCategory.Enemies && EliteArt.HasExtra(d, EliteArt.Extra.Death);
        }
        Check("the codex lists six Desert elites with their death strips (" + n + ")", n == 6 && strips);
    }

    // ---- the six attacks -------------------------------------------------------------------

    static int Shots(System.Func<EliteShot, bool> pick)
    {
        int n = 0;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && pick(s)) n++;
        return n;
    }

    // Kills it where it stands (a pause jump's damage), the way a player's pause would.
    static void Kill(EliteShip e) { e.TakeHit(EliteDamage.Teleport, e.transform.position, 9); }

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

    static Vector2 PilotAt => pilot.position;

    // ---- Dune Stalker: burrower / worm_churn ------------------------------------------------

    static void Stalker()
    {
        const string key = "desert_elite_dune_stalker";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var bb = (BurrowerBrain)e.Brain;
        var wc = (WormChurnAttack)e.Attack;
        Check("Dune Stalker: it roams (untouchable off the board) and leaves its burrow by the nearer side first (" + bb.Phase + ")", bb.Roams && bb.Phase == DesertPassBrain.Stage.Leave);
        bool passing = StepUntil(() => bb.Phase == DesertPassBrain.Stage.Pass, 8f);
        Check("... then makes a rippling PASS across the top (passes " + bb.Passes + ")", passing && bb.Passes == 1);
        bool window = StepUntil(() => bb.WantsAttack(PilotAt), 14f);
        Check("the cast window opens inside the rails, high over the board (x " + e.Position.x.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")", window && e.Position.y > pilot.position.y + 1.5f);
        StartTell(e);
        bool inside = true, spaced = true, below = true;
        for (int i = 0; i < wc.Count; i++)
        {
            inside &= Mathf.Abs(wc.Spot(i).x) <= EliteSystem.RailEdge;
            below &= wc.Spot(i).y < e.Position.y;
            if (i > 0) spaced &= (wc.Spot(i) - wc.Spot(i - 1)).magnitude >= .55f && wc.Spot(i).y <= wc.Spot(i - 1).y + 1e-3f;
        }
        Check("on the first frame of the tell a trail of " + WormChurnAttack.Bursts + " bursts is ringed leading down toward the pilot (" + wc.Marks.ShownCount + " shown)",
              e.Telling && wc.Count == WormChurnAttack.Bursts && wc.Marks.ShownCount == wc.Count && inside && spaced && below);
        Check("it keeps flying through the wind-up (not held, " + e.Velocity.magnitude.ToString("0.0") + " u/s)", !wc.HoldsDuringTell && e.Velocity.magnitude > .5f);
        StepUntil(() => e.Acting, 3f);
        int pools = 0, counted = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            pools = Mathf.Max(pools, Shots(s => s.Kind == EliteShots.Kind.Glob && !s.Airborne));
            counted = Mathf.Max(counted, bb.Casts);
        }
        Check("the bursts land one after another as pools (" + pools + " at once of " + wc.Count + ") and the FR1 lead was shown", wc.Laid == wc.Count && pools >= wc.Count - 1 && AttackPreview.TooShort == 0);
        Check("the cast is counted (" + counted + ")", counted == 1);
        bool back = StepUntil(() => bb.Passes >= 2, 24f);
        Check("it leaves by the far side and comes back for another pass (" + bb.Passes + ")", back);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        bb = (BurrowerBrain)e.Brain;
        wc = (WormChurnAttack)e.Attack;
        StepUntil(() => bb.WantsAttack(PilotAt), 20f);
        StartTell(e);
        Kill(e);
        Check("a Stalker killed in its tell takes its rings with it", (e == null || wc.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
        HurtFlees(key);
    }

    // ---- Caravan Hauler: caravan / quicksand_row --------------------------------------------

    static void Hauler()
    {
        const string key = "desert_elite_caravan_hauler";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var cb = (CaravanBrain)e.Brain;
        var qr = (QuicksandRowAttack)e.Attack;
        float minX = 9f, maxX = -9f;
        Step(12f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Caravan Hauler: it plods along the top in legs, never down to the pilot (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", legs " + cb.Legs + ", y " + e.Position.y.ToString("0.0") + ")",
              maxX - minX > 1.2f && cb.Legs >= 1 && e.Position.y > pilot.position.y + 1.5f);
        bool rests = StepUntil(() => cb.Resting, 20f);
        Check("it rests at the end of a leg, and only then wants to throw (" + cb.Resting + ")", rests && cb.WantsAttack(PilotAt));
        StartTell(e);
        bool above = true, open = true, distinct = true;
        for (int i = 0; i < qr.Count; i++)
        {
            above &= qr.Spot(i).y > pilot.position.y && qr.Spot(i).y < e.Position.y;
            open &= Mathf.Abs(qr.Spot(i).x - qr.SlotX(qr.GapSlot)) > .3f;
            for (int j = 0; j < i; j++) distinct &= (qr.Spot(i) - qr.Spot(j)).magnitude > .5f;
        }
        Check("on the first frame of the tell a ROW of " + (QuicksandRowAttack.Slots - 1) + " pools is ringed with slot " + qr.GapSlot + " left open (" + qr.Marks.ShownCount + " shown)",
              e.Telling && qr.Count == QuicksandRowAttack.Slots - 1 && qr.Marks.ShownCount == qr.Count && qr.Rows == 1 && above && open && distinct);
        Check("the open slot is beside the pilot's, not the pilot's own (gap x " + qr.SlotX(qr.GapSlot).ToString("0.00") + ", pilot x " + pilot.position.x.ToString("0.00") + ")",
              Mathf.Abs(qr.SlotX(qr.GapSlot) - pilot.position.x) > QuicksandRowAttack.Pitch * .5f);
        Check("it holds still through the wind-up", qr.HoldsDuringTell && qr.HoldsDuringAction);
        StepUntil(() => e.Acting, 3f);
        int pools = 0;
        for (float t = 0f; t < 5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            pools = Mathf.Max(pools, Shots(s => s.Kind == EliteShots.Kind.Glob && !s.Airborne));
        }
        Check("the canisters land as quicksand pools (" + pools + " at once of " + qr.Count + ") and the FR1 lead was shown", qr.Laid == qr.Count && pools >= qr.Count - 1 && AttackPreview.TooShort == 0);

        // the fight escalates with the hearts lost: a second row, its gap mirrored
        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        qr = (QuicksandRowAttack)e.Attack;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        StartTell(e);
        Check("a second row once two hearts are lost (" + qr.Rows + " rows, " + qr.Count + " pools at " + e.Hearts + " hearts)", qr.Rows == 2 && qr.Count == 2 * (QuicksandRowAttack.Slots - 1) && qr.Count <= DesertSpotAttack.MaxSpots);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        qr = (QuicksandRowAttack)e.Attack;
        StartTell(e);
        Shoot(e, 2);
        Kill(e);
        Check("a Hauler killed in its tell takes its rings with it", (e == null || qr.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Lens Matriarch: glint / glass_fan --------------------------------------------------

    static void Matriarch()
    {
        const string key = "desert_elite_lens_matriarch";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var gb = (GlintBrain)e.Brain;
        var gf = (GlassFanAttack)e.Attack;
        Check("Lens Matriarch: it roams and leaves its pad by the nearer side first (" + gb.Phase + ")", gb.Roams && gb.Phase == DesertPassBrain.Stage.Leave);
        bool passing = StepUntil(() => gb.Phase == DesertPassBrain.Stage.Pass, 8f);
        Check("... then zig-zags across the top (passes " + gb.Passes + ")", passing && gb.Passes == 1);
        float lo = 99f, hi = -99f;
        StepUntil(() => { lo = Mathf.Min(lo, e.Position.y); hi = Mathf.Max(hi, e.Position.y); return gb.Phase != DesertPassBrain.Stage.Pass; }, 14f);
        Check("the pass dips and climbs (y " + lo.ToString("0.0") + " .. " + hi.ToString("0.0") + ")", hi - lo > .3f);
        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        gb = (GlintBrain)e.Brain;
        gf = (GlassFanAttack)e.Attack;
        bool window = StepUntil(() => gb.WantsAttack(PilotAt), 20f);
        Check("the cast window opens inside the rails (x " + e.Position.x.ToString("0.0") + ")", window);
        StartTell(e);
        float centre = Mathf.Clamp(Mathf.Atan2(pilot.position.y - e.MuzzleWorld(0).y, pilot.position.x - e.MuzzleWorld(0).x) * Mathf.Rad2Deg, -150f, -30f);
        float nearest = 999f;
        for (int i = 0; i < gf.Count; i++) nearest = Mathf.Min(nearest, Mathf.Abs(gf.RayDeg(i) - centre));
        Check("on the first frame of the tell " + gf.Count + " rays are drawn as thin lines, none on the pilot's own line (nearest " + nearest.ToString("0.0") + " deg of a " + gf.Spread + " deg spread)",
              e.Telling && gf.Count == GlassFanAttack.Rays && gf.Telegraph.Showing && gf.Telegraph.Loops == gf.Count && nearest >= gf.Spread * .5f - 2f);
        Check("it halts for the wind-up", gf.HoldsDuringTell && gf.HoldsDuringAction);
        int before = EliteSystem.Shots.Launched;
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead", e.Acting && AttackPreview.TooShort == 0);
        Step(1f);
        Check("a shard goes down every ray (" + (EliteSystem.Shots.Launched - before) + " of " + gf.Count + ") and the lines are gone",
              EliteSystem.Shots.Launched - before >= gf.Count && AttackPreview.ActiveCount == 0 && gb.Casts == 1);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        Check("a fan of " + GlassFanAttack.MaxRays + " rays at its last heart (" + gf.Want + ")", gf.Want == GlassFanAttack.MaxRays);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        gb = (GlintBrain)e.Brain;
        StepUntil(() => gb.WantsAttack(PilotAt), 20f);
        StartTell(e);
        Kill(e);
        Check("a Matriarch killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);
        HurtFlees(key);
    }

    // ---- Glass Cannon: lenspost / lens_beams ------------------------------------------------

    static void Cannon()
    {
        const string key = "desert_elite_glass_cannon";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var lb = (LenspostBrain)e.Brain;
        var lbm = (LensBeamsAttack)e.Attack;
        Step(5f);
        Check("Glass Cannon: it sits on a perch along the top (perch " + lb.Perch + ", x " + e.Position.x.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")", lb.OnPerch && e.Position.y > pilot.position.y + 1.5f);
        int perch = lb.Perch;
        StartTell(e);
        float centre = Mathf.Clamp(Mathf.Atan2(pilot.position.y - e.MuzzleWorld(0).y, pilot.position.x - e.MuzzleWorld(0).x) * Mathf.Rad2Deg, -150f, -30f);
        float nearest = 999f;
        for (int i = 0; i < lbm.Count; i++) nearest = Mathf.Min(nearest, Mathf.Abs(lbm.BeamDeg(i) - centre));
        bool outsideIn = Mathf.Abs(lbm.BeamDeg(0) - centre) > Mathf.Abs(lbm.BeamDeg(lbm.Count - 1) - centre) + 1f;
        Check("on the first frame of the tell " + lbm.Count + " beam lines are drawn, none on the pilot's own line (nearest " + nearest.ToString("0.0") + " deg), the farthest first",
              e.Telling && lbm.Count == LensBeamsAttack.Beams && lbm.Telegraph.Showing && lbm.Telegraph.Loops == lbm.Count && nearest >= LensBeamsAttack.FanDeg - 2f && outsideIn);
        Check("it holds still through the wind-up (the beams are thin lines and bolts: nothing flashes the screen)", lbm.HoldsDuringTell && lbm.HoldsDuringAction);
        int before = EliteSystem.Shots.Launched;
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead", e.Acting && AttackPreview.TooShort == 0);
        Step(2f);
        int want = lbm.Count * lbm.Streak;
        Check("a stream of " + lbm.Streak + " bolts runs down each beam (" + (EliteSystem.Shots.Launched - before) + " of " + want + ") and the lines are gone",
              EliteSystem.Shots.Launched - before >= want && AttackPreview.ActiveCount == 0);
        Check("it then moves to ANOTHER perch (" + perch + " -> " + lb.Perch + ", moves " + lb.Moves + ")", lb.Moves == 1 && lb.Perch != perch);
        bool arrived = StepUntil(() => lb.OnPerch, 10f);
        Check("... and settles on it", arrived);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        Check("six beams at its last heart (" + lbm.Want + ")", lbm.Want == LensBeamsAttack.MaxBeams);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Cannon killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);
    }

    // ---- Scorpion Skiff: flanker / boulder_roll ---------------------------------------------

    static void Scorpion()
    {
        const string key = "desert_elite_scorpion_walker";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var fb = (FlankerBrain)e.Brain;
        var br = (BoulderRollAttack)e.Attack;
        Step(5f);
        Check("Scorpion Skiff: it holds a flank, high over the board (x " + e.Position.x.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")", Mathf.Abs(e.Position.x) > 1f && e.Position.y > pilot.position.y + 1.5f);
        float side = Mathf.Sign(e.Position.x);
        StartTell(e);
        bool far = Mathf.Sign(br.Bounce(0).x) == -side && Mathf.Abs(br.Bounce(0).x) <= EliteSystem.RailEdge && br.PathEnd(0).y < br.Bounce(0).y;
        Check("on the first frame of the tell the boulder's path is drawn (out to a ringed bounce on the FAR rail and on down: " + br.Lanes.Loops + " outlines)",
              e.Telling && br.Count == 1 && br.Lanes.Showing && br.Lanes.Loops == 3 && far);
        Check("it holds still through the wind-up", br.HoldsDuringTell && br.HoldsDuringAction);
        StepUntil(() => e.Acting, 3f);
        Check("... shown at least the FR1 lead", e.Acting && AttackPreview.TooShort == 0);
        bool skid = false;
        for (float t = 0f; t < 2f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            skid |= Shots(s => s.Kind == EliteShots.Kind.Slab && s.Skidding) > 0;
        }
        Check("a boulder skids out along the lane (" + br.Thrown + " thrown)", skid && br.Thrown == 1);
        Check("it then scuttles across to the other flank (side " + side + " -> " + fb.Side + ", crossings " + fb.Crossings + ")", fb.Crossings == 1 && fb.Side == -side);
        bool there = StepUntil(() => !fb.Crossing, 10f);
        Check("... and holds it (x " + e.Position.x.ToString("0.0") + ")", there && Mathf.Sign(e.Position.x) == fb.Side);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        br = (BoulderRollAttack)e.Attack;
        int one = br.Want;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check("a second boulder once it has lost a heart (" + one + " -> " + br.Want + ")", one == 1 && br.Want == 2);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        br = (BoulderRollAttack)e.Attack;
        StartTell(e);
        Kill(e);
        Check("a Skiff killed in its tell takes its lines with it", AttackPreview.ActiveCount == 0);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        StartTell(e);
        StepUntil(() => e.Acting, 3f);
        Step(.3f);
        Kill(e);
        Step(.2f);
        Check("... and a Skiff killed in its throw takes its boulders (" + Shots(s => s.Kind == EliteShots.Kind.Slab) + " left)", Shots(s => s.Kind == EliteShots.Kind.Slab) == 0);
        HurtFlees(key);
    }

    // ---- Mirror Bastion: bulwark / dust_wall ------------------------------------------------

    static void Bastion()
    {
        const string key = "desert_elite_mirror_bastion";
        Fresh();
        var e = InPlay(key, new Vector2(0f, 3f));
        var dw = (DustWallAttack)e.Attack;
        Step(4f);
        Check("Mirror Bastion: it holds the top of the view (y " + e.Position.y.ToString("0.0") + ")", e.Position.y > pilot.position.y + 1.5f);
        StartTell(e);
        Check("on the first frame of the tell the edges of every wave's gap are drawn across the board (" + dw.Telegraph.Loops + " lines for " + dw.Waves + " waves)",
              e.Telling && dw.Waves >= 2 && dw.Telegraph.Showing && dw.Telegraph.Loops == 2 * dw.Waves);
        float g0 = dw.GapCentre(0);
        Check("the gap is not at the pilot's height (gap y " + g0.ToString("0.00") + ", pilot y " + pilot.position.y.ToString("0.00") + ") and sits on the board",
              Mathf.Abs(g0 - pilot.position.y) > DustWallAttack.GapHalf && g0 - DustWallAttack.GapHalf >= EliteSystem.ViewBottom && g0 + DustWallAttack.GapHalf <= e.Position.y);
        bool slides = true;
        for (int w = 1; w < dw.Waves; w++) slides &= Mathf.Abs(dw.GapCentre(w) - dw.GapCentre(w - 1)) <= DustWallAttack.WaveSlide + .01f;
        Check("each wave's gap slides at most a hand from the last", slides);
        Check("the wall starts on the rail far from the pilot (from the " + (dw.FromLeft(0) ? "left" : "right") + ", pilot x " + pilot.position.x.ToString("0.0") + ")", dw.FromLeft(0) == (pilot.position.x >= 0f));
        StepUntil(() => e.Acting, 3f);
        int before = EliteSystem.Shots.Launched;
        bool clean = true, sideways = true;
        int waveSeen = 0, sentMax = 0;
        for (float t = 0f; t < 6f && sentMax < dw.Waves; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            if (dw.Sent > waveSeen)
            {
                // (the grains of a fresh wave are all at its rail: none inside its gap, all heading across)
                int w = dw.Sent - 1;
                foreach (var s in EliteSystem.Shots.All)
                {
                    if (!s.Active || Mathf.Abs(s.transform.position.x) < EliteSystem.RailEdge - .4f) continue;
                    if (Mathf.Abs(s.transform.position.y - dw.GapCentre(w)) < DustWallAttack.GapHalf - .02f) clean = false;
                    sideways &= Mathf.Abs(s.Velocity.y) < .01f && Mathf.Sign(s.Velocity.x) == (dw.FromLeft(w) ? 1f : -1f);
                }
                waveSeen = dw.Sent;
            }
            sentMax = Mathf.Max(sentMax, dw.Sent);
        }
        Check("the dust sweeps across in " + dw.Waves + " waves (" + sentMax + " sent, " + (EliteSystem.Shots.Launched - before) + " grains), none inside the gap, all sideways",
              sentMax == dw.Waves && EliteSystem.Shots.Launched - before >= dw.Waves * 6 && clean && sideways);
        Step(2f);
        Check("nothing is left behind (" + AttackPreview.ActiveCount + " drawings)", AttackPreview.ActiveCount == 0);

        // the fight escalates with the hearts lost
        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
        dw = (DustWallAttack)e.Attack;
        int full = dw.WaveCount;
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 2);
        Check("a third wave at three hearts (" + full + " -> " + dw.WaveCount + " at " + e.Hearts + " hearts)", full == 2 && dw.WaveCount == DustWallAttack.MaxWaves);

        Fresh();
        e = InPlay(key, new Vector2(0f, 3f));
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
            bool chaser = x.key.EndsWith("dune_stalker") || x.key.EndsWith("lens_matriarch") || x.key.EndsWith("scorpion_walker");
            Check(x.key + ": hearts " + d.hearts + " (5 armoured, 3 not), hurtHide only for the non-armoured chasers",
                  d.hearts == (d.armored ? 5 : 3) && d.hurtHide == chaser && (!d.hurtHide || !d.armored));
            bool heavy = x.key.EndsWith("caravan_hauler") || x.key.EndsWith("glass_cannon") || x.key.EndsWith("mirror_bastion");
            float drawn = ShipTierTest.DrawnLongSide(d);
            float target = heavy ? 1.6f : 1.55f;
            Check(x.key + ": drawn long side " + drawn.ToString("0.00") + " u in the shrunk band (about " + target.ToString("0.00") + " u)", Mathf.Abs(drawn - target) < .06f);
            Check(x.key + ": no pink, no green (shot " + ColorUtility.ToHtmlStringRGB(d.ShotColor) + ", core " + ColorUtility.ToHtmlStringRGB(d.ShotCore) + ")",
                  !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore) && !Green(d.ShotColor) && !Green(d.ShotCore));
            var atk = EliteAttacks.Create(d.attack);
            Check(x.key + ": an aimed attack winds up long enough for the lock (tell " + d.tellSeconds + " s)", !atk.Aimed || d.tellSeconds >= EliteDef.MinAimedTell);
            Check(x.key + ": a drawn tell of at least " + AttackHazard.MinTellSeconds + " s", d.tellSeconds >= AttackHazard.MinTellSeconds);
        }
        Check("Stalker and Matriarch roam; Hauler, Cannon, Skiff and Bastion stay on the board",
              EliteBrains.Create("burrower").Roams && EliteBrains.Create("glint").Roams &&
              !EliteBrains.Create("caravan").Roams && !EliteBrains.Create("lenspost").Roams && !EliteBrains.Create("flanker").Roams && !EliteBrains.Create("bulwark").Roams);
        Check("the base ids Desert used to fly are still registered",
              System.Array.IndexOf(EliteBrains.Ids, "interceptor") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "lance_dash") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "siege") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "siege_cannon") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "bastion") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "ward_curtain") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "striker") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "claw_dive") >= 0);
    }

    static bool Green(Color c) { float h = Hue(c); return h > 85f && h < 165f; }

    // ---- allocations ------------------------------------------------------------------------

    static void Allocations()
    {
        string[][] groups =
        {
            new[] { "desert_elite_dune_stalker", "desert_elite_lens_matriarch", "desert_elite_scorpion_walker" },
            new[] { "desert_elite_caravan_hauler", "desert_elite_glass_cannon", "desert_elite_mirror_bastion" },
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
