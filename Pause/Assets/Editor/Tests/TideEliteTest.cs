using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tide's five elites as mini-boss fights (Riptide, Bathyscaphe, Trawler, Thunderfin, Pearl Warden; TideElites.cs): the roster, their strips
// (7 flight cells, 3 death cells), muzzles on the hull, launching from Tide's own pads, the director's rotation, the codex entries, the
// fight rules (3 hearts, 5 armoured, the shrunk size band, no pink) and each one's own brain and attack in simulation. Every attack shows its footprint from the first frame of its tell for at least the FR1 lead
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
        ("tide_elite_riptide", "breaker", "foam_wake", LandingKind.TideTrenchHatch),
        ("tide_elite_bathyscaphe", "deep", "mine_chain", LandingKind.TideRigDeck),
        ("tide_elite_trawler", "trawl", "drag_net", LandingKind.TideReefDock),
        ("tide_elite_thunderfin", "tempest", "thunder_sweep", LandingKind.TideVentStack),
        ("tide_elite_pearl_warden", "oyster", "pearl_spiral", LandingKind.TideWreckBay),
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
            Rules();
            Foam();
            Mines();
            Nets();
            Thunder();
            Pearl();
            EngineFlames();
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
                  frames != null && frames.Length == 10 + 6 && Mathf.Approximately(frames[0].rect.width, cell) && Mathf.Approximately(frames[0].rect.height, cell) &&
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.Flight0 == 3 && c.bankLeft == 4 && c.bankRight == 5 && c.damaged == 6 &&
                  c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == 10 && c.damagedLoop[5] == 15 &&
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
            Check(x.key + ": " + d.hearts + " hearts orbiting tighter than the player's (" + (d.hullRadius * d.heartOrbit + d.heartSize * 1.05f).ToString("0.00") + " u)",
                  d.hearts >= 3 && d.hullRadius * d.heartOrbit + d.heartSize * 1.05f < .52f);
            Check(x.key + ": tuned like its siblings (speed " + d.speed + ", gap " + d.attackGap + ", tell " + d.tellSeconds + ")",
                  d.speed >= 1.2f && d.speed <= 3.4f && d.attackGap >= .2f && d.attackGap <= 5.5f && d.tellSeconds >= .55f && d.avoidance >= .4f && d.avoidance <= .8f);
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

    // ---- shared helpers for the five fights ------------------------------------------------

    static bool Alive(EliteShip e) { return e != null && e.State != EliteState.Dead; }

    // Kills it where it stands (a pause jump's damage), the way a player's pause would.
    static void Kill(EliteShip e) { e.TakeHit(EliteDamage.Teleport, e.transform.position, 9); }

    // ---- Riptide: breaker / foam_wake ------------------------------------------------------

    static void Foam()
    {
        Fresh();
        var e = InPlay("tide_elite_riptide", new Vector2(0f, 3f));
        var bb = (BreakerBrain)e.Brain;
        var fa = (FoamWakeAttack)e.Attack;
        Check("Riptide: it roams (untouchable off the board), leaves its dock by the nearer side first (" + bb.Phase + ")", bb.Roams && bb.Phase == BreakerBrain.Stage.Leave);
        bool ran = StepUntil(() => bb.Phase == BreakerBrain.Stage.Run, 8f);
        Check("... then makes a RUN along its wave line (runs " + bb.Runs + ")", ran && bb.Runs == 1);
        float lo, hi;
        BreakerBrain.Band(e.Def, out lo, out hi);
        float minY = 99f, maxY = -99f;
        bool window = StepUntil(() =>
        {
            minY = Mathf.Min(minY, e.Position.y);
            maxY = Mathf.Max(maxY, e.Position.y);
            return bb.WantsAttack(Vector2.zero);
        }, 6f);
        Check("it rides the top third (y " + minY.ToString("0.00") + " .. " + maxY.ToString("0.00") + ") and the attack window opens inside the rails", window && minY > lo - BreakerBrain.Amplitude - .8f);
        StartTell(e);
        bool onLine = true;
        for (int k = 0; k < BreakerBrain.Spots; k++) onLine &= fa.Spot(k).y <= hi + BreakerBrain.Amplitude + .05f && fa.Spot(k).y >= lo - BreakerBrain.Amplitude - .05f;
        Check("on the first frame of the tell " + BreakerBrain.Spots + " rings are drawn on the water along its line (" + fa.Marks.ShownCount + " shown)",
              e.Telling && fa.Count == BreakerBrain.Spots && fa.Marks.ShownCount == BreakerBrain.Spots && onLine);
        Check("it keeps riding through the wind-up (not held still, " + e.Velocity.magnitude.ToString("0.0") + " u/s)", !fa.HoldsDuringTell && e.Velocity.magnitude > 1f);
        StepUntil(() => e.Acting, 3f);
        var best = new float[BreakerBrain.Spots];
        for (int k = 0; k < best.Length; k++) best[k] = 9f;
        int maxOrbs = 0, castsSeen = 0;
        for (float t = 0f; t < 4.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            castsSeen = Mathf.Max(castsSeen, bb.Casts);
            int o = 0;
            foreach (var sh in EliteSystem.Shots.All)
            {
                if (!sh.Active || sh.Kind != EliteShots.Kind.Orb) continue;
                o++;
                for (int k = 0; k < best.Length; k++) best[k] = Mathf.Min(best[k], Vector2.Distance((Vector2)sh.transform.position, fa.Spot(k)));
            }
            maxOrbs = Mathf.Max(maxOrbs, o);
        }
        bool landed = true;
        for (int k = 0; k < best.Length; k++) landed &= best[k] < .45f;
        Check("a foam bank is lobbed onto every ring (" + maxOrbs + " at once; closest " + best[0].ToString("0.00") + " / " + best[1].ToString("0.00") + " / " + best[2].ToString("0.00") + " u)",
              maxOrbs >= BreakerBrain.Spots && landed && fa.Laid == BreakerBrain.Spots);
        Check("the cast is counted (" + castsSeen + ") and the FR1 lead was shown", castsSeen == 1 && AttackPreview.TooShort == 0);

        Fresh();
        e = InPlay("tide_elite_riptide", new Vector2(0f, 3f));
        bb = (BreakerBrain)e.Brain;
        fa = (FoamWakeAttack)e.Attack;
        StepUntil(() => bb.Phase == BreakerBrain.Stage.Run && bb.WantsAttack(Vector2.zero), 12f);
        StartTell(e);
        Kill(e);
        Check("a Riptide killed in its tell takes its rings and foam with it", (e == null || fa.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Bathyscaphe: deep / mine_chain ----------------------------------------------------

    static void Mines()
    {
        Fresh();
        var e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        var db = (DeepBrain)e.Brain;
        var ma = (MineChainAttack)e.Attack;
        bool hold = StepUntil(() => db.Phase == DeepBrain.Stage.Hold, 8f);
        Check("Bathyscaphe: it surfaces at a column at the top and holds there (" + db.Phase + ", y " + e.Position.y.ToString("0.00") + ")", hold && e.Position.y > pilot.position.y + 1.5f);
        Step(DeepBrain.ReadyWait + .1f);
        StartTell(e);
        Check("on the first frame of the tell every mine of the chain is ringed (" + ma.Marks.ShownCount + " of " + ma.PlanCount + ")",
              e.Telling && ma.Count == ma.PlanCount && ma.Marks.ShownCount == ma.PlanCount);
        bool descends = true;
        for (int i = 1; i < ma.Count; i++) descends &= ma.Spot(i).y < ma.Spot(i - 1).y + 1e-3f;
        Check("the chain runs down from under the hull to the pilot's column (" + ma.Spot(0).y.ToString("0.0") + " .. " + ma.Spot(ma.Count - 1).y.ToString("0.0") + ", end x " + ma.Spot(ma.Count - 1).x.ToString("0.00") + ")",
              descends && ma.Spot(0).y < e.Position.y && Mathf.Abs(ma.Spot(ma.Count - 1).x - pilot.position.x) < .15f);
        StepUntil(() => e.Acting, 3f);
        int maxOrbs = 0, maxBolts = 0, castsSeen = 0, bursts = 0, minesAlive = 0;
        float lowestMine = 99f;
        bool sank = false;
        for (float t = 0f; t < 6f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            castsSeen = Mathf.Max(castsSeen, db.Casts);
            sank |= db.Phase == DeepBrain.Stage.Sink || db.Phase == DeepBrain.Stage.Away;
            int o = 0, b = 0;
            minesAlive = 0;
            foreach (var sh in EliteSystem.Shots.All)
            {
                if (!sh.Active) continue;
                if (sh.IsMine) { minesAlive++; lowestMine = Mathf.Min(lowestMine, sh.transform.position.y); }
                if (sh.Kind == EliteShots.Kind.Orb) o++; else b++;
            }
            maxOrbs = Mathf.Max(maxOrbs, o);
            maxBolts = Mathf.Max(maxBolts, b);
        }
        foreach (var sh in EliteSystem.Shots.All) bursts += sh.Bursts;
        // (the cross's bolts reuse the pooled shots, so the burst is read off the mines: all gone by the end, none of them before it fell out of the view)
        Check("the mines are laid one after another (" + ma.Laid + ", most at once " + maxOrbs + ") and burst, not fall away (" + minesAlive + " left, lowest y " + lowestMine.ToString("0.0") + " vs view bottom " + EliteSystem.ViewBottom.ToString("0.0") + ")",
              ma.Laid == ma.Count && maxOrbs >= 2 && minesAlive == 0 && lowestMine > EliteSystem.ViewBottom - .6f && AttackPreview.TooShort == 0);
        Check("the cast done, it dives off the top (casts " + castsSeen + ", dived " + sank + ")", castsSeen == 1 && (sank || db.Surfacings >= 1));
        bool back = StepUntil(() => db.Surfacings >= 1, 16f);
        Check("... waits out of sight and surfaces again (" + db.Surfacings + ")", back);

        // the fight escalates with the hearts lost
        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        ma = (MineChainAttack)e.Attack;
        int full = ma.PlanCount;
        e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left * .3f);
        e.TakeHit(EliteDamage.Teleport, e.transform.position, 1);
        Check("more mines for every two hearts lost (" + full + " -> " + ma.PlanCount + " at " + e.Hearts + " hearts)", ma.PlanCount >= full + 1 && ma.PlanCount < TideSpotAttack.MaxSpots);

        Fresh();
        e = InPlay("tide_elite_bathyscaphe", new Vector2(0f, 2f));
        db = (DeepBrain)e.Brain;
        ma = (MineChainAttack)e.Attack;
        StepUntil(() => db.Phase == DeepBrain.Stage.Hold, 8f);
        StartTell(e);
        Kill(e);
        Check("a Bathyscaphe killed in its tell takes its rings with it", (e == null || ma.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Trawler: trawl / drag_net ---------------------------------------------------------

    static void Nets()
    {
        Fresh();
        var e = InPlay("tide_elite_trawler", new Vector2(1.5f, 2.5f));
        var tb = (TrawlBrain)e.Brain;
        var dn = (DragNetAttack)e.Attack;
        bool flank = StepUntil(() => tb.Phase == TrawlBrain.Stage.Flank, 10f);
        int side = tb.Side;
        Check("Trawler: it stands on its flank against a rail (side " + side + ", x " + e.Position.x.ToString("0.00") + ")",
              flank && Mathf.Sign(e.Position.x) == side && Mathf.Abs(e.Position.x) > EliteSystem.RailEdge - e.Def.hullRadius - .7f);
        Step(TrawlBrain.ReadyWait + .1f);
        StartTell(e);
        int n = dn.Count;
        float lastX = dn.Spot(n - 1).x;
        float clear = 2f * dn.Edge - (n - 1) * e.Def.lobSpacing - DragNetAttack.Ring;
        bool inward = true;
        for (int i = 1; i < n; i++) inward &= Mathf.Abs(dn.Spot(i).x) < Mathf.Abs(dn.Spot(i - 1).x) && Mathf.Sign(dn.Spot(i).x) == side;
        Check("on the first frame of the tell every net is ringed (" + dn.Marks.ShownCount + " of " + n + "), flank first, running in toward the middle",
              e.Telling && n == dn.NetsFor() && dn.Marks.ShownCount == n && n >= 1 && Mathf.Sign(dn.Spot(0).x) == side && inward);
        Check("the far end leaves a gap of at least " + DragNetAttack.Gap + " u to the far rail (" + clear.ToString("0.00") + ", last net x " + lastX.ToString("0.00") + ")", clear >= DragNetAttack.Gap - .01f);
        StepUntil(() => e.Acting, 3f);
        int pools = 0;
        for (float t = 0f; t < 4f; t += Dt)
        {
            pilot.position = new Vector3(0f, -4.8f, 0f);
            EliteSystem.Step(Dt);
            int p = 0;
            foreach (var sh in EliteSystem.Shots.All) if (sh.Active && sh.Kind == EliteShots.Kind.Glob && !sh.Airborne) p++;
            pools = Mathf.Max(pools, p);
        }
        Check("the nets land as pools (" + pools + " at once of " + n + ") and the FR1 lead was shown", dn.Laid == n && pools >= n && AttackPreview.TooShort == 0);
        bool crossed = StepUntil(() => tb.Visits >= 1, 14f);
        Check("it leaves by its own side and comes back on the opposite flank (side " + side + " -> " + tb.Side + ", visits " + tb.Visits + ")", crossed && tb.Side == -side);

        Fresh();
        e = InPlay("tide_elite_trawler", new Vector2(1.5f, 2.5f));
        tb = (TrawlBrain)e.Brain;
        dn = (DragNetAttack)e.Attack;
        StepUntil(() => tb.Phase == TrawlBrain.Stage.Flank, 10f);
        StartTell(e);
        Kill(e);
        Check("a Trawler killed in its tell takes its rings with it", (e == null || dn.Marks.ShownCount == 0) && AttackPreview.ActiveCount == 0);
    }

    // ---- Thunderfin: tempest / thunder_sweep ------------------------------------------------

    static void Thunder()
    {
        Fresh();
        var e = InPlay("tide_elite_thunderfin", new Vector2(0f, 3f));
        var tb = (TempestBrain)e.Brain;
        var ts = (ThunderSweepAttack)e.Attack;
        float minX = 9f, maxX = -9f;
        Step(5f, () => { minX = Mathf.Min(minX, e.Position.x); maxX = Mathf.Max(maxX, e.Position.x); });
        Check("Thunderfin: it loiters on an ellipse high over the board, never to the pilot (x " + minX.ToString("0.0") + " .. " + maxX.ToString("0.0") + ", y " + e.Position.y.ToString("0.0") + ")",
              maxX - minX > 1f && e.Position.y > pilot.position.y + 1.5f);
        bool rightward = tb.SweepDir > 0;
        StartTell(e);
        bool allTell = ts.Armed >= 3 && ts.Armed <= ts.Want;
        for (int i = 0; i < ts.Armed; i++) allTell &= ts.Strike(i) != null && ts.Strike(i).State == AttackHazard.Phase.Tell;
        Check("a glyph is armed under EVERY column on the first frame of the tell (" + ts.Armed + ")", e.Telling && allTell);
        bool ordered = true, spaced = true, pilotLane = false;
        for (int i = 0; i < ts.Armed; i++)
        {
            pilotLane |= Mathf.Abs(ts.Lane(i) - pilot.position.x) < .2f;
            if (i == 0) continue;
            ordered &= rightward ? ts.Lane(i) > ts.Lane(i - 1) : ts.Lane(i) < ts.Lane(i - 1);
            spaced &= Mathf.Abs(ts.Lane(i) - ts.Lane(i - 1)) >= AttackStrike.MinLaneSpacing - .01f;
        }
        Check("the columns are ordered along the way it flies (" + (rightward ? "right" : "left") + "), at least a strike's corridor apart, the pilot's own lane among them", ordered && spaced && pilotLane);
        Check("... told at least " + AttackHazard.MinTellSeconds + " s (" + e.Attack.TellSeconds.ToString("0.00") + ")", e.Attack.TellSeconds >= AttackHazard.MinTellSeconds + .3f);
        int n = ts.Armed;
        var litAt = new float[n];
        for (int i = 0; i < n; i++) litAt[i] = -1f;
        float clock = 0f;
        for (float t = 0f; t < 6f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            clock += Dt;
            for (int i = 0; i < n; i++)
            {
                var st = ts.Strike(i);
                if (litAt[i] < 0f && st != null && st.State == AttackHazard.Phase.Live) litAt[i] = clock;
            }
        }
        bool sequence = true;
        for (int i = 0; i < n; i++) sequence &= litAt[i] >= 0f && (i == 0 || litAt[i] - litAt[i - 1] >= e.Def.shotInterval * .6f);
        Check("the columns strike ONE AFTER ANOTHER, " + e.Def.shotInterval + " s apart (first " + litAt[0].ToString("0.00") + " s, last " + litAt[n - 1].ToString("0.00") + " s)", sequence);
        e.AttackCooldown = 99f;
        Step(1.5f);
        Check("nothing is left on the board afterwards (" + AttackHazard.ActiveCount + " hazards)", AttackHazard.ActiveCount == 0);

        Fresh();
        e = InPlay("tide_elite_thunderfin", new Vector2(0f, 3f));
        StartTell(e);
        Kill(e);
        Check("a Thunderfin killed in its tell takes its glyphs with it", AttackHazard.ActiveCount == 0);
    }

    // ---- Pearl Warden: oyster / pearl_spiral ------------------------------------------------

    static void Pearl()
    {
        Fresh();
        var e = InPlay("tide_elite_pearl_warden", new Vector2(0f, 3f));
        var ob = (OysterBrain)e.Brain;
        var ps = (PearlSpiralAttack)e.Attack;
        bool settled = StepUntil(() => ob.Settled && e.Position.y > pilot.position.y + 1.5f, 10f);
        Check("Pearl Warden: it keeps one of three stations at the top (station " + ob.Station + ", y " + e.Position.y.ToString("0.00") + ")", settled);
        Step(OysterBrain.ReadyWait + .1f);
        int station = ob.Station;
        StartTell(e);
        Check("the two edge lanes of the arc are drawn on the first frame of the tell (" + ps.Telegraph.Loops + " outlines)", e.Telling && ps.Telegraph.Showing && ps.Telegraph.Loops == 2);
        StepUntil(() => e.Acting, 3f);
        int sweep = ps.Sweep;
        float gap = ps.GapAngle;
        bool clearGap = true, monotone = true;
        for (int i = 0; i < ps.Total; i++)
        {
            clearGap &= Mathf.Abs(Mathf.DeltaAngle(ps.Angle(i), gap)) >= PearlSpiralAttack.GapDeg - .01f;
            if (i > 0) monotone &= (ps.Angle(i) - ps.Angle(i - 1)) * sweep > 0f;
        }
        Check("the spiral sweeps " + ps.Total + " of " + ps.PearlCount + " pearls across the arc in order, none within " + PearlSpiralAttack.GapDeg + " deg of where the pilot stood (gap " + gap.ToString("0") + " deg)",
              ps.Total >= 3 && ps.Total < ps.PearlCount && ps.Total >= ps.PearlCount - 4 && clearGap && monotone);
        int pearls = 0;
        for (float t = 0f; t < 2.5f; t += Dt)
        {
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.Step(Dt);
            foreach (var sh in EliteSystem.Shots.All) if (sh.Active) pearls++;
        }
        Check("they are fired (" + ps.Flung + " of " + ps.Total + ", pearl frames " + pearls + ") and it hops to another station (" + station + " -> " + ob.Station + ")",
              ps.Flung == ps.Total && pearls > 10 && ob.Hops == 1 && ob.Station != station);
        Check("the next cast sweeps the other way", PearlSpiralAttack.AngleOf(0, 5, 1) < PearlSpiralAttack.AngleOf(4, 5, 1) && PearlSpiralAttack.AngleOf(0, 5, -1) > PearlSpiralAttack.AngleOf(4, 5, -1));

        Fresh();
        e = InPlay("tide_elite_pearl_warden", new Vector2(0f, 3f));
        ob = (OysterBrain)e.Brain;
        ps = (PearlSpiralAttack)e.Attack;
        StepUntil(() => ob.Settled, 10f);
        StartTell(e);
        Kill(e);
        Check("a Pearl Warden killed in its tell takes its lanes with it", AttackPreview.ActiveCount == 0);
    }

    // ---- the fight rules -------------------------------------------------------------------

    static void Rules()
    {
        foreach (var x in Expected)
        {
            var d = EliteCatalog.Find(x.key);
            if (d == null) continue;
            Check(x.key + ": hearts " + d.hearts + " (5 armoured, 3 not), hurtHide only for the non-armoured chasers",
                  d.hearts == (d.armored ? 5 : 3) && (!d.hurtHide || !d.armored));
            float drawn = ShipTierTest.DrawnLongSide(d);
            float target = d.Heavy || System.Array.IndexOf(EliteDef.TideHeavyBrains, d.brain) >= 0 ? 1.6f : 1.55f;
            Check(x.key + ": drawn long side " + drawn.ToString("0.00") + " u in the shrunk band (about " + target.ToString("0.00") + " u)", Mathf.Abs(drawn - target) < .06f);
            Check(x.key + ": no pink (shot " + ColorUtility.ToHtmlStringRGB(d.ShotColor) + ", core " + ColorUtility.ToHtmlStringRGB(d.ShotCore) + ")", !WorldCue.IsPink(d.ShotColor) && !WorldCue.IsPink(d.ShotCore));
            var atk = EliteAttacks.Create(d.attack);
            Check(x.key + ": an aimed attack winds up long enough for the lock (tell " + d.tellSeconds + " s)", !atk.Aimed || d.tellSeconds >= EliteDef.MinAimedTell);
        }
        Check("Riptide, Bathyscaphe and Trawler roam; Thunderfin and Pearl Warden stay on the board",
              EliteBrains.Create("breaker").Roams && EliteBrains.Create("deep").Roams && EliteBrains.Create("trawl").Roams &&
              !EliteBrains.Create("tempest").Roams && !EliteBrains.Create("oyster").Roams);
        Check("the base ids Storm and Desert fly are still there",
              System.Array.IndexOf(EliteBrains.Ids, "interceptor") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "lance_dash") >= 0 &&
              System.Array.IndexOf(EliteBrains.Ids, "surfer") >= 0 && System.Array.IndexOf(EliteAttacks.Ids, "surf_dash") >= 0);
    }

    // ---- engine flames: baked into the strips, leaving each engine's mouth, in front of the hull ----------------
    // The burn cells (2..6) must show every engine's flame OVER the hull (a flame under the hull reads as coming from behind the engine):
    // the nozzle point is the exit (the dark bell mouth in cell 0; Bathyscaphe's lamp pods are lenses, so just solid hull), the flame covers it
    // in every burn cell, runs out past the hull's bottom edge in the hover cell, each engine on its own phase, no runtime plume doubles it.
    static Color32[] LoadCell(string key, int cell, int size)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Tide/" + key + ".png"))) return null;
        var px = tex.GetPixels32(); int w = tex.width;
        Object.DestroyImmediate(tex);
        var c = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) c[y * size + x] = px[y * w + cell * size + x];   // rows run bottom-up
        return c;
    }

    static void EngineFlames()
    {
        foreach (var e in Expected)
        {
            var def = EliteCatalog.Find(e.key);
            int S = def.cellPixels;
            var hull = LoadCell(e.key, 0, S);
            Check(e.key + ": strip loads for the flame check", hull != null);
            if (hull == null) continue;
            Check(e.key + ": no runtime plume doubles the baked flames (exhaustScale 0)", def.exhaustScale <= 0f);
            int hullBottom = 0;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) if (hull[(S - 1 - y) * S + x].a > 128) hullBottom = Mathf.Max(hullBottom, y);
            var lengths = new HashSet<int>();
            bool anyBurnsDamaged = false;   // the damaged cell (6) is a wreck: some engines are out, at least one still burns
            foreach (var nz in def.nozzles)
            {
                int nx = Mathf.RoundToInt(nz.x), ny = Mathf.RoundToInt(nz.y);
                var h0 = hull[(S - 1 - ny) * S + nx];
                bool lens = e.key.EndsWith("bathyscaphe");
                Check(e.key + " " + nz.name + ": nozzle point is on the hull at the exit (" + (lens ? "lamp-pod lens" : "dark bell mouth") + ")",
                      h0.a == 255 && (lens || (h0.r * .3f + h0.g * .59f + h0.b * .11f) < 70f));
                for (int cell = 2; cell <= 6; cell++)
                {
                    var c = LoadCell(e.key, cell, S);
                    bool covered = false;
                    for (int dy = -2; dy <= 2 && !covered; dy++)
                        for (int dx = -5; dx <= 5 && !covered; dx++)
                        {
                            var p = c[(S - 1 - (ny + dy)) * S + nx + dx]; var q = hull[(S - 1 - (ny + dy)) * S + nx + dx];
                            covered = p.a == 255 && (p.r != q.r || p.g != q.g || p.b != q.b) && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 150;
                        }
                    if (cell == 6) anyBurnsDamaged |= covered;
                    else Check(e.key + " cell " + cell + " " + nz.name + ": flame covers the nozzle point (in front of the hull, not hidden by the bell)", covered);
                    if (cell == 3)
                    {
                        int tip = ny;
                        for (int y = ny; y < S; y++)
                            for (int dx = -3; dx <= 3; dx++)
                            {
                                var q = c[(S - 1 - y) * S + nx + dx]; var r0 = hull[(S - 1 - y) * S + nx + dx];
                                if (q.a > 0 && (q.r != r0.r || q.g != r0.g || q.b != r0.b || q.a != r0.a)) tip = Mathf.Max(tip, y);
                            }
                        Check(e.key + " " + nz.name + ": flame leaves the engine and runs past the hull's bottom edge (tip " + tip + " vs " + hullBottom + ")", tip >= hullBottom + 10);
                        lengths.Add(tip);
                    }
                }
            }
            Check(e.key + ": each engine keeps its own phase (flame lengths differ in the hover cell)", lengths.Count >= 2);
            Check(e.key + ": the damaged cell still has an engine burning (the others sputter out)", anyBurnsDamaged);
        }
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
